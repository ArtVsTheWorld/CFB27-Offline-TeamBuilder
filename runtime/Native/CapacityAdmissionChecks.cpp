#include "Protocol.h"
#include "CapacityAdmission.h"
#include <stdio.h>
using namespace CapacityAdmission;
static uintptr_t table[Invalid];
static DWORD reservationCount;
static ULONGLONG tick;
static unsigned sleepStep = 1, clearAt = 3, forwarded, lookedUp;
static bool present;
static bool needRoom;
static void* wantedManager = reinterpret_cast<void*>(0x12345678);
static void* wantedDescriptor;
static unsigned __fastcall Forward(void* manager, void* descriptor) {
    if (manager != wantedManager || descriptor != wantedDescriptor || GetLastError() != 12345) ExitProcess(90);
    if (needRoom && !SpaceAvailable()) ExitProcess(95);
    ++forwarded; SetLastError(54321); return 0xF1234567;
}
static unsigned __fastcall Seed(unsigned mode) { if (mode != 2) ExitProcess(91); return 0x12345678; }
static bool __fastcall Find(void* manager, unsigned hash, WORD* index, void** arena) {
    if (manager != wantedManager || hash != (Hash("example/asset_bundle") ^ 0x12345678)) ExitProcess(92);
    ++lookedUp; *index = present ? 2010 : Invalid; *arena = nullptr; SetLastError(111);
    return false; // A pending existing record still has a valid output index.
}
static ULONGLONG WINAPI Now() { return tick; }
static void WINAPI Pause(DWORD ms) {
    if (ms != 1) ExitProcess(93);
    tick += sleepStep;
    SetLastError(222);
    if (clearAt && tick >= clearAt) for (unsigned i = First; i < First + 128; ++i) table[i] = 0;
}
static DWORD WINAPI ReleaseSlots(void*) {
    Sleep(20);
    for(unsigned i=First;i<First+128;++i)InterlockedExchangePointer(reinterpret_cast<void* volatile*>(&table[i]),nullptr);
    return 0;
}
static void Full() { for (unsigned i = 0; i < Invalid; ++i) table[i] = i + 1; }
int wmain() {
    alignas(8) unsigned char descriptor[0x40]{};
    const char* name = "Example/ASSET_Bundle";
    memcpy(descriptor, &name, 8);
    *reinterpret_cast<unsigned*>(descriptor+8) = 2;
    *reinterpret_cast<int*>(descriptor+0x30) = -1;
    unsigned char before[sizeof(descriptor)]; memcpy(before,descriptor,sizeof(before));
    wantedDescriptor = descriptor; original = Forward; lookup = Find; seed = Seed;
    slots = table; reserved = &reservationCount; clockNow = Now; sleepFor = Pause;
    InterlockedExchange(&enabled,1);
    auto invoke = [&]() {
        SetLastError(12345);
        unsigned result = Hook(wantedManager,descriptor);
        if (result != 0xF1234567 || GetLastError() != 54321) ExitProcess(94);
    };
    invoke(); if (forwarded != 1 || lookedUp || waits) return 1;
    Full(); present = true; invoke();
    if (forwarded != 2 || lookedUp != 1 || waits || existing != 1) return 2;
    present = false; invoke();
    if (forwarded != 3 || waits != 1 || tick != 3 || timeouts || memcmp(before,descriptor,sizeof(before))) return 3;
    Full(); clearAt = 0; sleepStep = 400; tick = 0; invoke();
    if (forwarded != 4 || timeouts != 1 || tick != 1200) return 4;
    LONG64 previousWait = waits;
    *reinterpret_cast<unsigned*>(descriptor+8) = 1; invoke();
    *reinterpret_cast<unsigned*>(descriptor+8) = 2; *reinterpret_cast<int*>(descriptor+0x30) = 2009; invoke();
    if (forwarded != 6 || waits != previousWait) return 5;
    *reinterpret_cast<int*>(descriptor+0x30) = -1;
    Full(); for(unsigned i=First;i<First+100;++i)table[i]=0;
    reservationCount=50; if(SpaceAvailable())return 6;
    reservationCount=0; if(!SpaceAvailable())return 7;
    Disable(); Full(); invoke(); if (forwarded != 7 || waits != previousWait) return 8;
    InterlockedExchange(&enabled,1); clockNow=GetTickCount64; sleepFor=Sleep; needRoom=true;
    LONG64 beforeTimeout=timeouts;
    HANDLE cleanup=CreateThread(nullptr,0,ReleaseSlots,nullptr,0,nullptr);if(!cleanup)return 16;
    invoke(); WaitForSingleObject(cleanup,5000);CloseHandle(cleanup);
    if(forwarded!=8 || timeouts!=beforeTimeout || waits!=previousWait+1)return 17;
    needRoom=false;Disable();
    FILE* file = nullptr; if (tmpfile_s(&file)) return 9;
    Stats(file); long firstPosition=ftell(file); Stats(file);if(ftell(file)!=firstPosition)return 18;
    InterlockedIncrement64(&timeouts);Stats(file);if(ftell(file)<=firstPosition)return 19;
    if (Start(file)) return 10; fclose(file);
    // Execute the actual native prologue/trampoline/detour against a private
    // buffer, including its full stack frame and nonvolatile register saves.
    auto target=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));
    if(!target)return 11;
    memcpy(target,Prefix,sizeof(Prefix));
    const unsigned char body[]={0xb8,0x67,0x45,0x23,0xf1,0x48,0x81,0xc4,0x00,0x01,0x00,0x00,0x5f,0x5b,0x5d,0xc3};
    memcpy(target+sizeof(Prefix),body,sizeof(body));
    if(!Install(target))return 12;
    auto function=reinterpret_cast<AcquireFn>(target);
    if(function(wantedManager,descriptor)!=0xF1234567)return 13;
    if(!PatchEntry(target,installedPatch,Prefix,sizeof(Prefix)))return 14;
    target[0]^=1;
    if(Install(target))return 15;
    target[0]^=1;
    VirtualFree(target,0,MEM_RELEASE);
    puts("PASS capacity admission: argument/return/LastError forwarding, pending-reference bypass, cleanup progress, bounded timeout, category/explicit-ID bypass, reservations, disable, native prologue/trampoline, owned patch restore and foreign-prefix rejection. Private process only.");
    return 0;
}
