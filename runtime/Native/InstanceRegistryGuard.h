#pragma once
#include "RegistryContracts.h"
namespace InstanceRegistry {
constexpr unsigned HookCount = 14;
struct Spec {
    const char* name; uintptr_t rva; const unsigned char* expected; size_t length;
    void* hook; int ripOffset; unsigned char ripRegister;
    unsigned char* target; unsigned char* trampoline; unsigned char patch[32]; bool published;
};
static CRITICAL_SECTION gate;
static volatile LONG enabled;
static uintptr_t originals[HookCount];
static volatile LONG64 calls[HookCount], contentions, waitMs, activeReads, activeWrites, writeDuringRead;
static LONG64 previousTotal = -1, previousActiveReads = -1, previousActiveWrites = -1;
static bool IsRead(unsigned id) { return id == 2 || (id >= 5 && id <= 7); }
static bool Enter(unsigned id) {
    if (!InterlockedCompareExchange(&enabled,0,0)) return false;
    if (!TryEnterCriticalSection(&gate)) {
        InterlockedIncrement64(&contentions); ULONGLONG start = GetTickCount64();
        EnterCriticalSection(&gate); InterlockedAdd64(&waitMs, static_cast<LONG64>(GetTickCount64()-start));
    }
    InterlockedIncrement64(&calls[id]);
    if (IsRead(id)) InterlockedIncrement64(&activeReads);
    else {
        if (activeReads && !activeWrites) InterlockedIncrement64(&writeDuringRead);
        InterlockedIncrement64(&activeWrites);
    }
    return true;
}
static void Leave(unsigned id, bool held) {
    if (!held) return;
    if (IsRead(id)) InterlockedDecrement64(&activeReads); else InterlockedDecrement64(&activeWrites);
    LeaveCriticalSection(&gate);
}
using F1 = uintptr_t (__fastcall*)(uintptr_t);
using F2 = uintptr_t (__fastcall*)(uintptr_t,uintptr_t);
using F3 = uintptr_t (__fastcall*)(uintptr_t,uintptr_t,uintptr_t);
using F4 = uintptr_t (__fastcall*)(uintptr_t,uintptr_t,uintptr_t,uintptr_t);
#define REGISTRY_WRAPPER1(Name,Id) static uintptr_t __fastcall Hook##Name(uintptr_t a) { \
    DWORD saved=GetLastError(),returnedError=0; bool held=Enter(Id); uintptr_t result=0; SetLastError(saved); \
    __try { result=reinterpret_cast<F1>(originals[Id])(a); returnedError=GetLastError(); } \
    __finally { Leave(Id,held); } SetLastError(returnedError); return result; }
#define REGISTRY_WRAPPER2(Name,Id) static uintptr_t __fastcall Hook##Name(uintptr_t a,uintptr_t b) { \
    DWORD saved=GetLastError(),returnedError=0; bool held=Enter(Id); uintptr_t result=0; SetLastError(saved); \
    __try { result=reinterpret_cast<F2>(originals[Id])(a,b); returnedError=GetLastError(); } \
    __finally { Leave(Id,held); } SetLastError(returnedError); return result; }
#define REGISTRY_WRAPPER3(Name,Id) static uintptr_t __fastcall Hook##Name(uintptr_t a,uintptr_t b,uintptr_t c) { \
    DWORD saved=GetLastError(),returnedError=0; bool held=Enter(Id); uintptr_t result=0; SetLastError(saved); \
    __try { result=reinterpret_cast<F3>(originals[Id])(a,b,c); returnedError=GetLastError(); } \
    __finally { Leave(Id,held); } SetLastError(returnedError); return result; }
#define REGISTRY_WRAPPER4(Name,Id) static uintptr_t __fastcall Hook##Name(uintptr_t a,uintptr_t b,uintptr_t c,uintptr_t d) { \
    DWORD saved=GetLastError(),returnedError=0; bool held=Enter(Id); uintptr_t result=0; SetLastError(saved); \
    __try { result=reinterpret_cast<F4>(originals[Id])(a,b,c,d); returnedError=GetLastError(); } \
    __finally { Leave(Id,held); } SetLastError(returnedError); return result; }
REGISTRY_WRAPPER3(Add,0)
REGISTRY_WRAPPER3(Alias,1)
REGISTRY_WRAPPER1(Ready,2)
REGISTRY_WRAPPER1(Initialize,3)
REGISTRY_WRAPPER1(Destroy,4)
REGISTRY_WRAPPER4(FindInGroup,5)
REGISTRY_WRAPPER3(FindByName,6)
REGISTRY_WRAPPER4(FindByGuid,7)
REGISTRY_WRAPPER2(ClearGroup,8)
REGISTRY_WRAPPER3(Remove,9)
REGISTRY_WRAPPER1(ClearAll,10)
REGISTRY_WRAPPER2(Event,11)
REGISTRY_WRAPPER1(ClearAliases,12)
REGISTRY_WRAPPER1(ResidentMaintenance,13)
#undef REGISTRY_WRAPPER1
#undef REGISTRY_WRAPPER2
#undef REGISTRY_WRAPPER3
#undef REGISTRY_WRAPPER4
// Generated from the captured live native instructions, including all RIP loads.
static Spec specs[HookCount];
static bool BuildTrampoline(Spec& spec, unsigned char* target) {
    if (spec.length > sizeof(spec.patch) || spec.length < 14 || memcmp(target,spec.expected,spec.length)) return false;
    spec.target=target;
    spec.trampoline=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
    if(!spec.trampoline)return false;
    size_t used=0;
    if(spec.ripOffset>=0) {
        size_t offset=static_cast<size_t>(spec.ripOffset);
        // Only the verified MOV r32,[RIP+disp32] form is expanded. Preserve R11
        // and flags, and keep the original stack unchanged after the expansion.
        if(offset+6>spec.length || target[offset]!=0x8b || (target[offset+1]&0xc7)!=5) return false;
        int displacement=0;memcpy(&displacement,target+offset+2,4);
        uintptr_t address=reinterpret_cast<uintptr_t>(target)+offset+6+displacement;
        memcpy(spec.trampoline,target,offset);used=offset;
        const unsigned char prefix[]={0x41,0x53,0x49,0xbb};memcpy(spec.trampoline+used,prefix,sizeof(prefix));used+=sizeof(prefix);
        memcpy(spec.trampoline+used,&address,8);used+=8;
        spec.trampoline[used++]=0x41;spec.trampoline[used++]=0x8b;spec.trampoline[used++]=spec.ripRegister;
        spec.trampoline[used++]=0x41;spec.trampoline[used++]=0x5b;
        memcpy(spec.trampoline+used,target+offset+6,spec.length-offset-6);used+=spec.length-offset-6;
    } else {memcpy(spec.trampoline,target,spec.length);used=spec.length;}
    CapacityAdmission::Jump(spec.trampoline+used,target+spec.length);used+=14;
    DWORD previous=0;if(!VirtualProtect(spec.trampoline,4096,PAGE_EXECUTE_READ,&previous))return false;
    FlushInstructionCache(GetCurrentProcess(),spec.trampoline,used);
    memset(spec.patch,0x90,spec.length);CapacityAdmission::Jump(spec.patch,spec.hook);
    return true;
}
static bool Publish(Spec& spec) {
    for(unsigned attempt=0;attempt<16;++attempt) {
        if(CapacityAdmission::PatchEntry(spec.target,spec.expected,spec.patch,spec.length)){spec.published=true;return true;}
        Sleep(10);
    }
    return false;
}
static void Disable() {InterlockedExchange(&enabled,0);}
static void Stats(FILE* file) {
    LONG64 total=0;for(unsigned i=0;i<HookCount;++i)total+=InterlockedCompareExchange64(&calls[i],0,0);
    LONG64 reads=InterlockedCompareExchange64(&activeReads,0,0),writes=InterlockedCompareExchange64(&activeWrites,0,0);
    if(total==previousTotal && reads==previousActiveReads && writes==previousActiveWrites)return;
    previousTotal=total;previousActiveReads=reads;previousActiveWrites=writes;
    fprintf(file,"# REGISTRY_STATS total=%lld contentions=%lld waitMs=%lld activeReads=%lld activeWrites=%lld writeDuringRead=%lld",total,
        InterlockedCompareExchange64(&contentions,0,0),InterlockedCompareExchange64(&waitMs,0,0),
        reads,writes,InterlockedCompareExchange64(&writeDuringRead,0,0));
    for(unsigned i=0;i<HookCount;++i)fprintf(file," %s=%lld",specs[i].name?specs[i].name:"private",InterlockedCompareExchange64(&calls[i],0,0));
    fputc('\n',file);
}
static void ConfigureSpecs() {
    specs[0] = {"Add",0x1275520,PrefixAdd,sizeof(PrefixAdd),reinterpret_cast<void*>(&HookAdd),-1,0x00,nullptr,nullptr,{},false};
    specs[1] = {"Alias",0x1275780,PrefixAlias,sizeof(PrefixAlias),reinterpret_cast<void*>(&HookAlias),-1,0x00,nullptr,nullptr,{},false};
    specs[2] = {"Ready",0x1275880,PrefixReady,sizeof(PrefixReady),reinterpret_cast<void*>(&HookReady),-1,0x00,nullptr,nullptr,{},false};
    specs[3] = {"Initialize",0x1275960,PrefixInitialize,sizeof(PrefixInitialize),reinterpret_cast<void*>(&HookInitialize),6,0x1b,nullptr,nullptr,{},false};
    specs[4] = {"Destroy",0x1275a10,PrefixDestroy,sizeof(PrefixDestroy),reinterpret_cast<void*>(&HookDestroy),6,0x3b,nullptr,nullptr,{},false};
    specs[5] = {"FindInGroup",0x1275ac0,PrefixFindInGroup,sizeof(PrefixFindInGroup),reinterpret_cast<void*>(&HookFindInGroup),-1,0x00,nullptr,nullptr,{},false};
    specs[6] = {"FindByName",0x1275c50,PrefixFindByName,sizeof(PrefixFindByName),reinterpret_cast<void*>(&HookFindByName),-1,0x00,nullptr,nullptr,{},false};
    specs[7] = {"FindByGuid",0x1275e50,PrefixFindByGuid,sizeof(PrefixFindByGuid),reinterpret_cast<void*>(&HookFindByGuid),-1,0x00,nullptr,nullptr,{},false};
    specs[8] = {"ClearGroup",0x1275fa0,PrefixClearGroup,sizeof(PrefixClearGroup),reinterpret_cast<void*>(&HookClearGroup),6,0x03,nullptr,nullptr,{},false};
    specs[9] = {"Remove",0x12760a0,PrefixRemove,sizeof(PrefixRemove),reinterpret_cast<void*>(&HookRemove),-1,0x00,nullptr,nullptr,{},false};
    specs[10] = {"ClearAll",0x12761d0,PrefixClearAll,sizeof(PrefixClearAll),reinterpret_cast<void*>(&HookClearAll),6,0x1b,nullptr,nullptr,{},false};
    specs[11] = {"Event",0x1276f70,PrefixEvent,sizeof(PrefixEvent),reinterpret_cast<void*>(&HookEvent),-1,0x00,nullptr,nullptr,{},false};
    specs[12] = {"ClearAliases",0x1275940,PrefixClearAliases,sizeof(PrefixClearAliases),reinterpret_cast<void*>(&HookClearAliases),0,0x03,nullptr,nullptr,{},false};
    specs[13] = {"ResidentMaintenance",0x3177c50,PrefixResidentMaintenance,sizeof(PrefixResidentMaintenance),reinterpret_cast<void*>(&HookResidentMaintenance),-1,0x00,nullptr,nullptr,{},false};
}
static bool ContractsReady(uintptr_t base) {
    ConfigureSpecs();
    for(unsigned i=0;i<HookCount;++i)
        if(memcmp(reinterpret_cast<void*>(base+specs[i].rva),specs[i].expected,specs[i].length))return false;
    return true;
}
static bool Start(FILE* file) {
    uintptr_t base=reinterpret_cast<uintptr_t>(GetModuleHandleW(L"CollegeFB27.exe"));if(!base)return false;
    ContractsReady(base);
    for(unsigned i=0;i<HookCount;++i)if(memcmp(reinterpret_cast<void*>(base+specs[i].rva),specs[i].expected,specs[i].length)) {
        fprintf(file,"# INSTANCE_REGISTRY_CONTRACT_FAILED name=%s rva=%llx\n",specs[i].name,static_cast<ULONGLONG>(specs[i].rva));fflush(file);return false;
    }
    if(!InitializeCriticalSectionEx(&gate,0,0))return false;
    for(unsigned i=0;i<HookCount;++i) {
        if(!BuildTrampoline(specs[i],reinterpret_cast<unsigned char*>(base+specs[i].rva))) {
            fprintf(file,"# INSTANCE_REGISTRY_TRAMPOLINE_FAILED name=%s\n",specs[i].name);fflush(file);return false;
        }
        originals[i]=reinterpret_cast<uintptr_t>(specs[i].trampoline);
    }
    for(unsigned i=0;i<HookCount;++i)if(!Publish(specs[i])) {
        fprintf(file,"# INSTANCE_REGISTRY_PUBLICATION_FAILED name=%s\n",specs[i].name);fflush(file);
        Disable();
        // Retain executable buffers until process exit even after restoration;
        // another thread may still be returning through a forwarding wrapper.
        for(unsigned n=i;n>0;--n)CapacityAdmission::PatchEntry(specs[n-1].target,specs[n-1].patch,specs[n-1].expected,specs[n-1].length);
        return false;
    }
    InterlockedExchange(&enabled,1);
    fprintf(file,"# INSTANCE_REGISTRY_GUARD installed=1 hooks=%u policy=recursive-serialization-native-operations-preserved\n",HookCount);fflush(file);
    return true;
}
}
