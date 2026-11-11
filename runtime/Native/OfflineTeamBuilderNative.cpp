#include <windows.h>
#include <stdio.h>
#include <io.h>
#include <fcntl.h>
#include <share.h>
#include <sys/stat.h>
#include "CapacityAdmission.h"
#include "InstanceRegistryGuard.h"

struct InitContext {
    DWORD version, size;
    ULONGLONG gameBase, statusAddress;
    DWORD result, reserved;
    const wchar_t* logPath;
};
struct alignas(16) NativeStatus {
    DWORD magic, version, size, installed;
    DWORD ready, failure, headroom, deadlineMs;
    LONG64 capacityCalls, capacityPressure, capacityWaits, capacityTimeouts, capacityWaitMs;
    LONG64 registryCalls, registryContentions, registryWaitMs, activeReads, activeWrites, writeDuringRead;
    DWORD lastError, reserved;
};
static_assert(sizeof(InitContext)==40,"Native init ABI");
static_assert(sizeof(NativeStatus)==128,"Native status ABI");
static NativeStatus status = {0x4F544247,1,sizeof(NativeStatus),0,0,0,64,1000};
static volatile LONG initializing;
static FILE* logFile;

static void CopyCounters() {
    InterlockedExchange64(&status.capacityCalls,InterlockedCompareExchange64(&CapacityAdmission::calls,0,0));
    InterlockedExchange64(&status.capacityPressure,InterlockedCompareExchange64(&CapacityAdmission::pressure,0,0));
    InterlockedExchange64(&status.capacityWaits,InterlockedCompareExchange64(&CapacityAdmission::waits,0,0));
    InterlockedExchange64(&status.capacityTimeouts,InterlockedCompareExchange64(&CapacityAdmission::timeouts,0,0));
    InterlockedExchange64(&status.capacityWaitMs,InterlockedCompareExchange64(&CapacityAdmission::totalWaitMs,0,0));
    LONG64 total=0;for(unsigned i=0;i<InstanceRegistry::HookCount;++i)total+=InterlockedCompareExchange64(&InstanceRegistry::calls[i],0,0);
    InterlockedExchange64(&status.registryCalls,total);
    InterlockedExchange64(&status.registryContentions,InterlockedCompareExchange64(&InstanceRegistry::contentions,0,0));
    InterlockedExchange64(&status.registryWaitMs,InterlockedCompareExchange64(&InstanceRegistry::waitMs,0,0));
    InterlockedExchange64(&status.activeReads,InterlockedCompareExchange64(&InstanceRegistry::activeReads,0,0));
    InterlockedExchange64(&status.activeWrites,InterlockedCompareExchange64(&InstanceRegistry::activeWrites,0,0));
    InterlockedExchange64(&status.writeDuringRead,InterlockedCompareExchange64(&InstanceRegistry::writeDuringRead,0,0));
}
static DWORD WINAPI Monitor(void*) {
    // The helper and its native consistency guards live until game exit. They
    // do not depend on an external collector or the lifetime of MMC.
    ULONGLONG nextLog=0;
    for(;;) {
        CopyCounters();
        ULONGLONG now=GetTickCount64();
        if(now>=nextLog) {
            if(ftell(logFile)<8*1024*1024) {
                CapacityAdmission::Stats(logFile);InstanceRegistry::Stats(logFile);fflush(logFile);
            }
            nextLog=now+1000;
        }
        Sleep(100);
    }
}
extern "C" __declspec(dllexport) DWORD WINAPI Initialize(void* argument) {
    auto context=static_cast<InitContext*>(argument);
    if(!context || context->version!=1 || context->size!=sizeof(InitContext))return 8;
    context->statusAddress=reinterpret_cast<ULONGLONG>(&status);
    uintptr_t base=reinterpret_cast<uintptr_t>(GetModuleHandleW(L"CollegeFB27.exe"));
    if(!base || context->gameBase!=base)return context->result=7;
    if(status.ready)return context->result=0;
    if(status.failure)return context->result=status.failure;
    if(InterlockedCompareExchange(&initializing,1,0))return context->result=6;
    DWORD result=0;
    if(!CapacityAdmission::ContractsReady(base) || !InstanceRegistry::ContractsReady(base))result=1;
    else {
        HMODULE pinned=nullptr;
        if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&Initialize),&pinned))result=2;
        int descriptor=-1;
        if(!result && (!context->logPath || _wsopen_s(&descriptor,context->logPath,
            _O_WRONLY|_O_CREAT|_O_EXCL|_O_TEXT,_SH_DENYNO,_S_IREAD|_S_IWRITE)))result=3;
        if(!result) {
            logFile=_wfdopen(descriptor,L"w");
            if(!logFile){_close(descriptor);result=3;}
        }
        if(!result && !CapacityAdmission::Start(logFile))result=4;
        if(!result)status.installed|=1;
        if(!result && !InstanceRegistry::Start(logFile))result=5;
        if(!result)status.installed|=2;
        if(!result) {
            HANDLE worker=CreateThread(nullptr,0,Monitor,nullptr,0,nullptr);
            if(!worker)result=9;else{CloseHandle(worker);status.ready=1;}
        }
        if(result) {
            status.failure=result;status.lastError=GetLastError();CapacityAdmission::Disable();
            if(logFile){fprintf(logFile,"# NATIVE_GUARD_FAILED result=%lu lastError=%lu\n",result,status.lastError);fflush(logFile);}
        }
    }
    context->result=result;InterlockedExchange(&initializing,0);return result;
}
BOOL WINAPI DllMain(HINSTANCE,DWORD,LPVOID) {return TRUE;}
