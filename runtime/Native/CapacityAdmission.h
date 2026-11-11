#pragma once
#include <tlhelp32.h>
#include <stdint.h>
#include <string.h>

namespace CapacityAdmission {
using AcquireFn = unsigned (__fastcall*)(void*, void*);
using LookupFn = bool (__fastcall*)(void*, unsigned, WORD*, void**);
using SeedFn = unsigned (__fastcall*)(unsigned);
using ClockFn = ULONGLONG (WINAPI*)();
using SleepFn = void (WINAPI*)(DWORD);
constexpr unsigned First = 2007, Count = 3200, Invalid = 5207, Headroom = 64, DeadlineMs = 1000;
constexpr uintptr_t AcquireRva = 0x53F1980;
static const unsigned char Prefix[] = {0x48,0x8b,0xc4,0x55,0x53,0x57,0x48,0x8d,0x68,0xa1,0x48,0x81,0xec,0x00,0x01,0x00,0x00};
static AcquireFn original;
static LookupFn lookup;
static SeedFn seed;
static ClockFn clockNow = GetTickCount64;
static SleepFn sleepFor = Sleep;
static uintptr_t volatile* slots;
static DWORD volatile* reserved;
static volatile LONG enabled;
static volatile LONG64 calls, pressure, existing, waits, timeouts, totalWaitMs;
static LONG64 previousCalls = -1, previousWaits = -1, previousTimeouts = -1, previousTotalWait = -1;
static unsigned char* installedTarget;
static unsigned char installedPatch[sizeof(Prefix)];

static bool SpaceAvailable() {
    unsigned need = Headroom + *reserved;
    if (need > Count) return false;
    unsigned empty = 0;
    for (unsigned i = First; i < Invalid; ++i)
        if (!slots[i] && ++empty >= need) return true;
    return false;
}
static unsigned Hash(const char* text) {
    unsigned hash = 5381;
    for (; *text; ++text) {
        unsigned c = static_cast<unsigned char>(*text);
        if (c >= 'A' && c <= 'Z') c += 32;
        hash = (hash * 33) ^ c;
    }
    return hash;
}
static bool Existing(void* manager, unsigned hash) {
    WORD index = Invalid;
    void* arena = nullptr;
    // Native helper takes/releases its usual lock and writes index even when
    // the matching record is pending. It does not acquire a resource reference.
    lookup(manager, hash, &index, &arena);
    return index < Invalid;
}
static void Admit(void* manager, void* descriptor) {
    const auto bytes = static_cast<unsigned char*>(descriptor);
    if (*reinterpret_cast<unsigned*>(bytes + 8) != 2 ||
        *reinterpret_cast<int*>(bytes + 0x30) != -1 || SpaceAvailable()) return;
    InterlockedIncrement64(&pressure);
    const char* name = *reinterpret_cast<const char**>(bytes);
    unsigned hash = Hash(name) ^ seed(2);
    if (Existing(manager, hash)) { InterlockedIncrement64(&existing); return; }
    ULONGLONG start = clockNow();
    InterlockedIncrement64(&waits);
    while (InterlockedCompareExchange(&enabled, 0, 0) && !SpaceAvailable()) {
        if (clockNow() - start >= DeadlineMs) { InterlockedIncrement64(&timeouts); break; }
        sleepFor(1);
        if (Existing(manager, hash)) break;
    }
    InterlockedAdd64(&totalWaitMs, static_cast<LONG64>(clockNow() - start));
}
static unsigned __fastcall Hook(void* manager, void* descriptor) {
    DWORD error = GetLastError();
    InterlockedIncrement64(&calls);
    if (InterlockedCompareExchange(&enabled, 0, 0)) Admit(manager, descriptor);
    SetLastError(error);
    // Exactly one normal acquisition, with its normal return and ownership.
    // A timeout forwards normally: it is observable, not a fabricated success.
    return original(manager, descriptor);
}

// Publish the entry only with peer threads stopped outside the overwritten
// prologue. Allocate/open handles before suspension; no heap or file I/O while
// suspended. The DLL and trampoline remain pinned until process exit.
static bool PatchEntry(unsigned char* target, const unsigned char* expected, const unsigned char* replacement, size_t size) {
    struct Peer { HANDLE handle; bool suspended; } peers[1024]{};
    unsigned count = 0; bool okay = true;
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return false;
    THREADENTRY32 entry{}; entry.dwSize = sizeof(entry);
    if (Thread32First(snapshot, &entry)) do {
        if (entry.th32OwnerProcessID != GetCurrentProcessId() || entry.th32ThreadID == GetCurrentThreadId()) continue;
        if (count == _countof(peers)) { okay = false; break; }
        HANDLE thread = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION, FALSE, entry.th32ThreadID);
        if (!thread) { okay = false; break; }
        peers[count++].handle = thread;
    } while (Thread32Next(snapshot, &entry));
    CloseHandle(snapshot);
    DWORD old = 0;
    if (okay && !VirtualProtect(target, size, PAGE_EXECUTE_READWRITE, &old)) okay = false;
    if (okay) {
        for (unsigned i = 0; i < count; ++i) {
            if (SuspendThread(peers[i].handle) == static_cast<DWORD>(-1)) { okay = false; break; }
            peers[i].suspended = true;
            CONTEXT context{}; context.ContextFlags = CONTEXT_CONTROL;
            if (!GetThreadContext(peers[i].handle, &context) ||
                (context.Rip >= reinterpret_cast<uintptr_t>(target) && context.Rip < reinterpret_cast<uintptr_t>(target) + size)) {
                okay = false; break;
            }
        }
        if (okay && memcmp(target, expected, size)) okay = false;
        if (okay) { memcpy(target, replacement, size); FlushInstructionCache(GetCurrentProcess(), target, size); }
    }
    for (unsigned i = count; i > 0; --i) if (peers[i-1].suspended) ResumeThread(peers[i-1].handle);
    if (old) { DWORD ignored = 0; VirtualProtect(target, size, old, &ignored); }
    for (unsigned i = 0; i < count; ++i) CloseHandle(peers[i].handle);
    return okay;
}
static void Jump(unsigned char* out, void* address) {
    out[0] = 0xFF; out[1] = 0x25; memset(out + 2, 0, 4); memcpy(out + 6, &address, 8);
}
static bool Install(unsigned char* target) {
    if (memcmp(target, Prefix, sizeof(Prefix))) return false;
    auto trampoline = static_cast<unsigned char*>(VirtualAlloc(nullptr, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    if (!trampoline) return false;
    memcpy(trampoline, Prefix, sizeof(Prefix)); Jump(trampoline + sizeof(Prefix), target + sizeof(Prefix));
    DWORD old = 0;
    if (!VirtualProtect(trampoline, 4096, PAGE_EXECUTE_READ, &old)) { VirtualFree(trampoline, 0, MEM_RELEASE); return false; }
    FlushInstructionCache(GetCurrentProcess(), trampoline, sizeof(Prefix) + 14);
    unsigned char patch[sizeof(Prefix)]; memset(patch, 0x90, sizeof(patch)); Jump(patch, reinterpret_cast<void*>(&Hook));
    original = reinterpret_cast<AcquireFn>(trampoline);
    bool installed = false;
    for (unsigned attempt = 0; attempt < 16 && !installed; ++attempt) {
        installed = PatchEntry(target, Prefix, patch, sizeof(patch));
        if (!installed) Sleep(10);
    }
    if (!installed) { VirtualFree(trampoline, 0, MEM_RELEASE); return false; }
    memcpy(installedPatch, patch, sizeof(patch)); installedTarget = target;
    return true;
}
static void Disable() { InterlockedExchange(&enabled, 0); }
static void Stats(FILE* file) {
    LONG64 n = InterlockedCompareExchange64(&calls, 0, 0), w = InterlockedCompareExchange64(&waits, 0, 0);
    LONG64 t = InterlockedCompareExchange64(&timeouts,0,0), elapsed = InterlockedCompareExchange64(&totalWaitMs,0,0);
    if (n == previousCalls && w == previousWaits && t == previousTimeouts && elapsed == previousTotalWait) return;
    previousCalls = n; previousWaits = w; previousTimeouts = t; previousTotalWait = elapsed;
    fprintf(file, "# CAPACITY_STATS calls=%lld pressure=%lld existing=%lld waits=%lld timeouts=%lld totalWaitMs=%lld\n",
        n, InterlockedCompareExchange64(&pressure,0,0), InterlockedCompareExchange64(&existing,0,0), w,
        t, elapsed);
}
static const unsigned char seedPrefix[] = {0x48,0x63,0xc1,0x48,0x8d,0x0d,0x92,0xb5,0x38,0x0a,0x8b,0x04,0x81,0xc3};
static const unsigned char findPrefix[] = {0x48,0x89,0x5c,0x24,0x08,0x48,0x89,0x74,0x24,0x10,0x48,0x89,0x7c,0x24,0x18,0x41,0x56,0x48,0x83,0xec,0x20,0x66,0x41,0xc7,0x00,0x57,0x14,0x49,0x8b,0xf1,0x49,0xc7};
static bool ContractsReady(uintptr_t base) {
    return !memcmp(reinterpret_cast<void*>(base+AcquireRva),Prefix,sizeof(Prefix)) &&
        !memcmp(reinterpret_cast<void*>(base+0x53CEDE0),seedPrefix,sizeof(seedPrefix)) &&
        !memcmp(reinterpret_cast<void*>(base+0x53EF9A0),findPrefix,sizeof(findPrefix));
}
static bool Start(FILE* file) {
    uintptr_t base = reinterpret_cast<uintptr_t>(GetModuleHandleW(L"CollegeFB27.exe"));
    if (!base) return false;
    // These helpers and the acquisition prologue are checked against the same
    // live capture used for the successful/failing paired run.
    if (!ContractsReady(base)) return false;
    lookup = reinterpret_cast<LookupFn>(base + 0x53EF9A0); seed = reinterpret_cast<SeedFn>(base + 0x53CEDE0);
    slots = reinterpret_cast<uintptr_t volatile*>(base + 0xF718160);
    reserved = reinterpret_cast<DWORD volatile*>(base + 0xF718150 + 0x422BC);
    if (!Install(reinterpret_cast<unsigned char*>(base + AcquireRva))) return false;
    InterlockedExchange(&enabled, 1);
    fprintf(file, "# CAPACITY_GUARD installed=1 acquire=%llx headroom=%u deadlineMs=%u policy=wait-before-lock-existing-reference-bypass-forward-exactly-once\n",
        static_cast<ULONGLONG>(base + AcquireRva), Headroom, DeadlineMs);
    fflush(file);
    return true;
}
}
