#pragma once
#include <windows.h>
#include <stdio.h>
constexpr DWORD ProtocolVersion = 1;
struct TraceRecord {
    volatile LONG busy, sequence;
    DWORD tid, code;
    ULONGLONG tick, rip, rsp, rax, rcx, rdx, access, target;
};
struct alignas(16) Mailbox {
    DWORD version, collectorPid, minIntervalMs, waitMs;
    volatile LONG state; // 0 idle, 1 copying, 2 published, 3 collecting, 4 completed
    volatile LONG seen, skipped, sequence, installed, enabled;
    ULONGLONG lastTick, tick;
    DWORD tid, reserved;
    EXCEPTION_RECORD record;
    CONTEXT context;
    TraceRecord recent[64];
};
inline void ObjectName(wchar_t* out, size_t capacity, DWORD pid, const wchar_t* kind) {
    swprintf_s(out, capacity, L"Local\\CFBCrashDiagnostics_%lu_%s", pid, kind);
}
