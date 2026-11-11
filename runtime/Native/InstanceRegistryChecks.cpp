#include "Protocol.h"
#include "CapacityAdmission.h"
#include "InstanceRegistryGuard.h"
#include <stdio.h>
#include "RegistryFixtureBodies.h"
using namespace InstanceRegistry;
static HANDLE readStarted;
static volatile LONG reading,overlaps;
static uintptr_t __fastcall NativeRead(uintptr_t a,uintptr_t b,uintptr_t c) {
    if(a!=11 || b!=22 || c!=33 || GetLastError()!=12345)ExitProcess(90);
    InterlockedExchange(&reading,1);if(readStarted)SetEvent(readStarted);
    Sleep(25);InterlockedExchange(&reading,0);SetLastError(54321);return 0xF123456789ABCDEF;
}
static uintptr_t __fastcall NativeWrite(uintptr_t a,uintptr_t b,uintptr_t c) {
    if(a!=11 || b!=22 || c!=33 || GetLastError()!=12345)ExitProcess(91);
    if(reading)InterlockedIncrement(&overlaps);
    SetLastError(54321);return 0xF123456789ABCDEF;
}
static DWORD WINAPI ReadThread(void*) {
    SetLastError(12345);
    if(HookFindByName(11,22,33)!=0xF123456789ABCDEF || GetLastError()!=54321)ExitProcess(92);
    return 0;
}
static DWORD WINAPI WriteThread(void*) {
    WaitForSingleObject(readStarted,5000);SetLastError(12345);
    if(HookAdd(11,22,33)!=0xF123456789ABCDEF || GetLastError()!=54321)ExitProcess(93);
    return 0;
}
static uintptr_t __fastcall RecursiveWriter(uintptr_t a,uintptr_t b) {
    if(a!=11 || b!=22)ExitProcess(94);
    SetLastError(12345);return HookFindByName(11,22,33);
}
static uintptr_t __fastcall RaiseNative(uintptr_t,uintptr_t,uintptr_t) {RaiseException(0xE1234567,0,0,nullptr);return 0;}
static bool SehCheck() {
    originals[6]=reinterpret_cast<uintptr_t>(&RaiseNative);
    __try {HookFindByName(11,22,33);}
    __except(GetExceptionCode()==0xE1234567?EXCEPTION_EXECUTE_HANDLER:EXCEPTION_CONTINUE_SEARCH) {}
    return activeReads==0 && activeWrites==0 && TryEnterCriticalSection(&gate);
}
static uintptr_t Call(unsigned arity,uintptr_t at,uintptr_t first=11) {
    switch(arity) {
    case 1:return reinterpret_cast<F1>(at)(first);
    case 2:return reinterpret_cast<F2>(at)(first,22);
    case 3:return reinterpret_cast<F3>(at)(first,22,33);
    default:return reinterpret_cast<F4>(at)(first,22,33,44);
    }
}
int wmain() {
    ConfigureSpecs();if(!InitializeCriticalSectionEx(&gate,0,0))return 1;InterlockedExchange(&enabled,1);
    originals[0]=reinterpret_cast<uintptr_t>(&NativeWrite);originals[6]=reinterpret_cast<uintptr_t>(&NativeRead);
    readStarted=CreateEventW(nullptr,TRUE,FALSE,nullptr);if(!readStarted)return 2;
    HANDLE reader=CreateThread(nullptr,0,ReadThread,nullptr,0,nullptr),writer=CreateThread(nullptr,0,WriteThread,nullptr,0,nullptr);
    if(!reader || !writer)return 3;
    if(WaitForSingleObject(reader,5000)!=WAIT_OBJECT_0 || WaitForSingleObject(writer,5000)!=WAIT_OBJECT_0)return 4;
    CloseHandle(reader);CloseHandle(writer);CloseHandle(readStarted);readStarted=nullptr;
    if(overlaps || !contentions || activeReads || activeWrites)return 5;
    originals[11]=reinterpret_cast<uintptr_t>(&RecursiveWriter);SetLastError(12345);
    if(HookEvent(11,22)!=0xF123456789ABCDEF || GetLastError()!=54321 || writeDuringRead)return 6;
    if(!SehCheck())return 7;LeaveCriticalSection(&gate);
    // Each captured prologue is executed before and after publication. Four
    // RIP-relative global reads are relocated to private data in these buffers.
    for(unsigned id=0;id<HookCount;++id) {
        Spec spec=specs[id];auto target=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_EXECUTE_READWRITE));if(!target)return 8;
        unsigned char expected[32];memcpy(expected,spec.expected,spec.length);
        if(spec.ripOffset>=0) {
            *reinterpret_cast<DWORD*>(target+0x800)=0x1357;
            int displacement=static_cast<int>((target+0x800)-(target+spec.ripOffset+6));memcpy(expected+spec.ripOffset+2,&displacement,4);
        }
        spec.expected=expected;memcpy(target,expected,spec.length);
        memcpy(target+spec.length,FixtureBodies[id],FixtureBodySizes[id]);
        uintptr_t first=11;
        if(id==12){*reinterpret_cast<DWORD*>(target+0x800)=0x100;first=reinterpret_cast<uintptr_t>(target+0x900);*reinterpret_cast<uintptr_t*>(target+0xA00)=reinterpret_cast<uintptr_t>(target+0xB00);}
        uintptr_t baseline=Call(FixtureArities[id],reinterpret_cast<uintptr_t>(target),first);
        if(!BuildTrampoline(spec,target))return 9;originals[id]=reinterpret_cast<uintptr_t>(spec.trampoline);
        if(!Publish(spec))return 10;
        if(Call(FixtureArities[id],reinterpret_cast<uintptr_t>(target),first)!=baseline)return 11;
        if(!CapacityAdmission::PatchEntry(target,spec.patch,expected,spec.length))return 12;
        VirtualFree(target,0,MEM_RELEASE);
    }
    FILE* file=nullptr;if(tmpfile_s(&file))return 13;Stats(file);if(Start(file))return 14;CapacityAdmission::Stats(file);if(CapacityAdmission::Start(file))return 15;fclose(file);
    Disable();CapacityAdmission::Disable();DeleteCriticalSection(&gate);
    puts("PASS instance registry: concurrent lookup/mutation exclusion, exact arguments/results/LastError, recursive event-to-registry calls, SEH lock release, all14 native prologue/trampoline paths and all5 RIP-relative relocations. Private process only.");
    return 0;
}
