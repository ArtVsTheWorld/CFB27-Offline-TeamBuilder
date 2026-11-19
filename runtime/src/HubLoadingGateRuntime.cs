using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OfflineTeamBuilderRuntime;

namespace OfflineTeamBuilderReentry;

internal sealed partial class TeamBuilderReentryRepair
{
    private ulong hubGateState,hubGateShowSlot,hubGateHideSlot,hubGateShow,hubGateHide,hubGateReturn;
    private bool hubShowOwned,hubHideOwned,hubReturnOwned,hubGateInstalled;
    private byte[] hubReturnOriginal,hubReturnPatch;
    private readonly HubLoadingGatePolicy hubGatePolicy=new HubLoadingGatePolicy();
    private uint lastHubHolds,lastHubReleases,lastHubReason,lastHubCancels;
    private void InstallHubLoadingGate()
    {
        try {
            var p=GameProfile.Current;
            if(p.hubGate?.validations==null||p.hubGate.validations.Length!=3)throw new InvalidOperationException("Loading-screen profile unavailable.");
            foreach(var check in p.hubGate.validations)if(!BytesEqual(Read(_base+(ulong)p.Rva(check.target),GameProfile.Hex(check.expected).Length),GameProfile.Hex(check.expected)))
                throw new InvalidOperationException("Loading-screen contract mismatch: "+check.target);
            if(!BytesEqual(Read(_base+(ulong)p.Rva("UiArgumentMarker"),16),GameProfile.Hex("4c894c2420c3cccccccccccccccccccc")))
                throw new InvalidOperationException("UI callback boundary/padding changed.");
            ulong loader=ReadU64(_base+(ulong)p.Rva("HubLoadingAdapterGlobal"));
            if(ReadU64(loader)!=_base+(ulong)p.Rva("HubLoadingAdapterVtable"))throw new InvalidOperationException("Loading adapter type mismatch.");
            hubGateShowSlot=_base+(ulong)p.Rva("HubLoadingAdapterVtable")+0x18;
            hubGateHideSlot=hubGateShowSlot+8;
            if(ReadU64(hubGateShowSlot)!=_base+(ulong)p.Rva("HubLoadingShow")||ReadU64(hubGateHideSlot)!=_base+(ulong)p.Rva("HubLoadingHide"))
                throw new InvalidOperationException("Another plugin owns the loading-screen controls.");
            var modules=_game.Modules.Cast<ProcessModule>().Where(m=>new[]{"kernel32.dll","kernelbase.dll","ntdll.dll"}.Contains(m.ModuleName.ToLowerInvariant()))
                .Select(m=>new SystemExportModule{Name=m.ModuleName,Path=m.FileName,Base=unchecked((ulong)m.BaseAddress.ToInt64()),Size=m.ModuleMemorySize}).ToArray();
            ulong tick=NativeSystemExports.Resolve(Read,modules,Environment.SystemDirectory,"kernel32.dll","GetTickCount64").Address;
            hubGateState=_remoteBase+0xC800;hubGateHide=_remoteBase+0xC000;hubGateShow=_remoteBase+0xD000;hubGateReturn=_remoteBase+0xE800;
            ulong service=_base+(ulong)p.Rva("ServiceGlobal"),loading=_base+(ulong)p.Rva("HubLoadingAdapterGlobal");
            Write(hubGateState,new byte[128]);
            Write(hubGateHide,HubLoadingGateCode.Hide(hubGateState,_status,service,_base+(ulong)p.Rva("ServiceVtable"),_base+(ulong)p.Rva("CareerManagerVtable"),_base+(ulong)p.Rva("HubLoadingHide"),tick,_base+(ulong)p.Rva("GateMatchStatesGlobal")));
            Write(hubGateShow,HubLoadingGateCode.Show(hubGateState,_status,service,_base+(ulong)p.Rva("HubLoadingShow")));
            Write(_remoteBase+0xE000,HubLoadingGateCode.Return(hubGateState,_status,service,loading,_base+(ulong)p.Rva("ServiceVtable"),_base+(ulong)p.Rva("CareerManagerVtable"),_base+(ulong)p.Rva("HubLoadingAdapterVtable"),_base+(ulong)p.Rva("HubLoadingShow"),_base+(ulong)p.Rva("HubLoadingHide"),tick,_base+(ulong)p.Rva("UiUndefinedValue"),_base+(ulong)p.Rva("GateMatchStatesGlobal")));
            Write(hubGateReturn,HubLoadingGateCode.Callback(_remoteBase+0xE000));
            hubReturnOriginal=Read(_base+(ulong)p.Rva("UiArgumentMarker"),16);hubReturnPatch=HubLoadingGateCode.Entry(hubGateReturn);
            hubShowOwned=true;SetPointer(hubGateShowSlot,hubGateShow);
            hubHideOwned=true;SetPointer(hubGateHideSlot,hubGateHide);
            hubReturnOwned=true;SetCodePatch(_base+(ulong)p.Rva("UiArgumentMarker"),hubReturnPatch);
            Write(hubGateState,BitConverter.GetBytes(1u));hubGateInstalled=true;
            Log("HUB_LOAD_GATE_READY native loading screen retained during current TeamBuilder work; UI-thread release; 60s native fallback; one wait per Dynasty entry; match entry bypass; isolated AS callback; Expanded Practice helper untouched; no importer or save edits.");
        } catch(Exception ex) {
            RestoreHubLoadingGate();Log("HUB_LOAD_GATE_DISABLED "+ex.Message+" Existing TeamBuilder loading remains active.");
        }
    }
    private void TickHubLoadingGate()
    {
        if(!hubGateInstalled)return;
        byte[] state=Read(hubGateState,128);if(state==null)return;
        uint pending=BitConverter.ToUInt32(state,4),holds=BitConverter.ToUInt32(state,56),releases=BitConverter.ToUInt32(state,60),reason=BitConverter.ToUInt32(state,48);
        if(holds!=lastHubHolds){lastHubHolds=holds;hubGatePolicy.Reset();Log("HUB_LOAD_GATE_WAIT epoch="+BitConverter.ToUInt64(state,24));}
        if(releases!=lastHubReleases||reason!=lastHubReason&&reason==2){lastHubReleases=releases;lastHubReason=reason;Log("HUB_LOAD_GATE_RELEASE reason="+(reason==2?"native-timeout":reason==5?"stock-hide-after-completion":"team-packages-complete")+" holds="+holds+" releases="+releases);}
        uint cancels=BitConverter.ToUInt32(state,76);
        if(cancels!=lastHubCancels){lastHubCancels=cancels;Log("HUB_LOAD_GATE_CANCEL reason="+reason+" count="+cancels);}
        if(pending!=1){hubGatePolicy.Reset();return;}
        var snapshot=TeamReadinessReader.Capture(Read,_base,_status,GameProfile.Current);
        if(hubGatePolicy.Observe(snapshot,ReadU32(_status+8),ReadU32(_status+4),ReadU32(_status+0x84),ReadU32(_status+0x4C),BitConverter.ToUInt64(state,24),readinessClock.ElapsedMilliseconds)) {
            byte[] after=Read(hubGateState,128);
            if(after!=null&&BitConverter.ToUInt32(after,4)==1&&after.Skip(8).Take(40).SequenceEqual(state.Skip(8).Take(40)))
                Write(hubGateState+4,BitConverter.GetBytes(2u));
        }
    }
    private Dictionary<string,object> HubLoadingGateHealth()
    {
        var result=new Dictionary<string,object>{{"installed",hubGateInstalled},{"scope","NativeTeamPackageCompletion; not proof of GPU textures/audio playback"}};
        if(!hubGateInstalled)return result;
        byte[] b=Read(hubGateState,128);if(b==null)return result;
        uint pending=BitConverter.ToUInt32(b,4),reason=BitConverter.ToUInt32(b,48);
        result["outcome"]=pending==1?"Waiting":pending==2?"ReleaseQueued":reason==1?"ReadyReplay":reason==5?"ReadyStockHide":reason==2?"TimedOut":reason==3?"CanceledByNewView":reason==4?"CanceledStaleContext":reason==6?"CanceledForMatchEntry":"Idle";
        result["pending"]=BitConverter.ToUInt32(b,4);result["epoch"]=BitConverter.ToUInt64(b,24);result["reason"]=BitConverter.ToUInt32(b,48);
        result["entryTicket"]=BitConverter.ToUInt32(b,104);result["matchEntryCancellations"]=BitConverter.ToUInt32(b,112);
        result["incomingLoad"]=BitConverter.ToUInt32(b,64);result["hideCalls"]=BitConverter.ToUInt32(b,68);result["stockReadyReleases"]=BitConverter.ToUInt32(b,72);result["cancellations"]=BitConverter.ToUInt32(b,76);result["reassertions"]=BitConverter.ToUInt32(b,84);result["waitMilliseconds"]=(BitConverter.ToUInt32(b,4)==0&&(BitConverter.ToUInt32(b,48)==1||BitConverter.ToUInt32(b,48)==2))?(object)BitConverter.ToUInt64(b,96):null;
        result["shows"]=BitConverter.ToUInt32(b,52);result["holds"]=BitConverter.ToUInt32(b,56);result["releases"]=BitConverter.ToUInt32(b,60);result["timeoutMs"]=HubLoadingGateCode.TimeoutMs;
        return result;
    }
    private void RestoreHubLoadingGate()
    {
        hubGateInstalled=false;hubGatePolicy?.Reset();
        if(_processHandle==IntPtr.Zero)return;
        try {
            if(hubGateState!=0){Write(hubGateState,BitConverter.GetBytes(0u));if(ReadU32(hubGateState+4)!=0)Write(hubGateState+4,BitConverter.GetBytes(2u));}
            if(hubShowOwned&&ReadU64(hubGateShowSlot)==hubGateShow)SetPointer(hubGateShowSlot,_base+(ulong)GameProfile.Current.Rva("HubLoadingShow"));
            if(hubHideOwned&&ReadU64(hubGateHideSlot)==hubGateHide)SetPointer(hubGateHideSlot,_base+(ulong)GameProfile.Current.Rva("HubLoadingHide"));
            if(hubReturnOwned) {
                var timer=Stopwatch.StartNew();while(GameAlive()&&hubGateState!=0&&ReadU32(hubGateState+4)!=0&&timer.ElapsedMilliseconds<1000)Thread.Sleep(10);
                if(hubGateState==0||ReadU32(hubGateState+4)==0) {
                    ulong at=_base+(ulong)GameProfile.Current.Rva("UiArgumentMarker");
                    if(BytesEqual(Read(at,hubReturnPatch.Length),hubReturnPatch))SetCodePatch(at,hubReturnOriginal);
                    hubReturnOwned=false;
                } else Log("HUB_LOAD_GATE_CLEANUP deferred UI-thread release retained; private code remains allocated until process exit.");
            }
        } catch(Exception ex){Log("HUB_LOAD_GATE_CLEANUP_ERROR "+ex.Message);}
        hubShowOwned=false;hubHideOwned=false;
    }
}
