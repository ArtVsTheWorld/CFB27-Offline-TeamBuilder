using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using OfflineTeamBuilderRuntime;

internal sealed class ReadinessChecks
{
    private const ulong Image=0x140000000, Status=0x30004000, Service=0x40000000, Manager=0x50000000;
    private const ulong Metadata=0x51000000, Records=0x52000000, Queue=0x53000000, Context=0x54000000, Selected=0x55000000;
    private readonly Dictionary<ulong,byte> bytes=new Dictionary<ulong,byte>();
    private readonly GameProfile profile=GameProfile.Current;
    private int reads;
    private Action<ulong,int,int> duringRead;
    private ReadinessChecks()
    {
        Put(Service,new byte[0x800]);Put(Manager,new byte[0x1D8]);Put(Status,new byte[0x188]);
        Q(Image+(ulong)profile.Rva("ServiceGlobal"),Service);
        Q(Image+(ulong)profile.Rva("Rva_0E350900"),Manager);
        Q(Service,Image+(ulong)profile.Rva("ServiceVtable"));
        Q(Manager,Image+(ulong)profile.Rva("Rva_0B38B3B8"));
        Q(Image+(ulong)profile.Rva("Rva_0B38B3B8")+0x20,Image+(ulong)profile.Rva("Rva_0317B260"));
        Q(Service+0x468,0x56000000);Q(Service+0x470,Context);Q(Context+0x1D0,Selected);
        Put(Selected+0xF78,BitConverter.GetBytes((ushort)1801));Put(Service+0x7B1,new byte[]{1});
        Q(Status+0x10,Service);Q(Status+0x18,0x56000000);Q(Status+0x30,Manager);
        Q(Status+0x50,0x56000000);Q(Status+0x58,Selected);D(Status+0xD8,1801);Q(Status+0x90,5);
        uint[] slots={1801,1807,1811,1811};
        Q(Manager+0x130,Metadata);Q(Manager+0x138,Metadata+32);
        for(int n=0;n<slots.Length;n++) {
            ulong node=0x57000000+(ulong)n*0x100;Put(node,new byte[0x18]);
            Q(Metadata+(ulong)n*8,node);Q(node,Image+(ulong)profile.Rva("Rva_0B494B90"));D(node+0x10,slots[n]);
        }
        Put(Records,new byte[2*0x78]);D(Records+0x58,1801);D(Records+0x78+0x58,1807);
        Q(Manager+0x1C8,Records);Q(Manager+0x1D0,Records+2*0x78);
        Q(Manager+0x180,Queue);Q(Manager+0x1A0,Queue+24);
        for(int n=0;n<3;n++) {
            ulong job=0x58000000+(ulong)n*0x100;Put(job,new byte[0x48]);Q(Queue+(ulong)n*8,job);D(job+8,1);
            Q(job,Image+(ulong)profile.Rva(n==2?"Rva_0B38B380":"Rva_0B38CA08"));
            Q(job+0x18,Status);Q(job+0x20,(ulong)(n+4));D(job+0x40,n==0?1811U:1801U);
        }
    }
    private void Put(ulong at,byte[] value){for(int n=0;n<value.Length;n++)bytes[at+(ulong)n]=value[n];}
    private void Q(ulong at,ulong value)=>Put(at,BitConverter.GetBytes(value));
    private void D(ulong at,uint value)=>Put(at,BitConverter.GetBytes(value));
    private byte[] Read(ulong at,int size) {
        duringRead?.Invoke(at,size,++reads);
        byte[] result=new byte[size];
        for(int n=0;n<size;n++)if(!bytes.TryGetValue(at+(ulong)n,out result[n]))return null;
        return result;
    }
    private TeamReadinessSnapshot Capture()=>TeamReadinessReader.Capture(Read,Image,Status,profile);
    private static void Check(bool value,string message)=>Program.Assert(value,"readiness: "+message);
    internal static void CheckAll()
    {
        var fixture=new ReadinessChecks();var before=fixture.bytes.ToArray();var s=fixture.Capture();
        Check(s.observation=="StableSample"&&s.currentCareerOwned,"validated current-career sample");
        Check(s.expectedTeams==3&&s.teams.Count==3&&s.runtimeRecordsPresent==2,"distinct team count, not callback count");
        Check(s.teams.Single(t=>t.slot==1811).runtimeRecord=="Missing"&&s.teams.Single(t=>t.slot==1801).activeTeam,"missing record and selected team are explicit");
        Check(s.nativeQueueCount==3&&s.olderEpochPluginJobs==1,"old native ownership survives epoch retirement");
        Check(s.jobs[0].slot==1811&&s.jobs[0].callbackEpoch==4&&s.jobs[0].olderEpoch,"old queued slot is identified");
        Check(!s.jobs[2].pluginOwned&&s.jobs[2].slot==null&&s.jobs[2].callbackEpoch==null,"unrelated native class not misread as a team job");
        Check(s.matchEntryReady==null&&s.teams.All(t=>t.stadiumWorldAndField=="Unverified"&&t.uniformTextures=="Unverified"&&t.audioPlayback=="Unverified"),"record presence never claims consumer readiness");
        Check(before.SequenceEqual(fixture.bytes.ToArray()),"reader never writes native state");
        string json=new JavaScriptSerializer().Serialize(s);
        Check(json.Contains("1811")&&json.Contains("Unverified")&&!json.Contains("HeadIdentity")&&!json.Contains("Address"),"support snapshot serializes evidence without raw job pointers");
        fixture.D(Status+0x48,99);s=fixture.Capture();
        Check(s.runtimeRecordsPresent==2&&s.matchEntryReady==null,"success counters cannot turn missing teams into ready teams");
        fixture.Q(Manager+0x138,Metadata+0x108);s=fixture.Capture();
        Check(s.observation=="Unavailable"&&s.teams.Count==0&&s.reason.Contains("bounds"),"oversized team vector rejected without partial result");
        fixture=new ReadinessChecks();fixture.Q(Manager+0x1D0,Records+1);s=fixture.Capture();
        Check(s.observation=="Unavailable"&&s.teams.Count==0,"misaligned records rejected");
        fixture=new ReadinessChecks();fixture.Q(Manager+0x180,1);s=fixture.Capture();
        Check(s.observation=="Unavailable","invalid native queue bounds rejected");
        fixture=new ReadinessChecks();fixture.Q(Manager,Image+0x2000);s=fixture.Capture();
        Check(s.observation=="NativeContractMismatch"&&s.jobs.Count==0,"unknown native owner never traversed");
        fixture=new ReadinessChecks();fixture.Q(Service+0x468,0x56001000);s=fixture.Capture();
        Check(!s.currentCareerOwned&&s.observation=="StableSample","another career's records not labeled current-save ownership");
        fixture=new ReadinessChecks();int statusReads=0;
        fixture.duringRead=(at,n,count)=>{if(at==Status&&++statusReads==2)fixture.Q(Status+0x90,6);};s=fixture.Capture();
        Check(s.observation=="ChangedDuringRead"&&s.teams.Count==0&&s.jobs.Count==0,"mid-read epoch transition discards mixed-save results");
        fixture=new ReadinessChecks();int managerReads=0;
        fixture.duringRead=(at,n,count)=>{if(at==Manager&&++managerReads==2)fixture.Q(Manager+0x1D0,Records);};s=fixture.Capture();
        Check(s.observation=="ChangedDuringRead"&&s.teams.Count==0,"mid-read residency transition discarded");
        fixture=new ReadinessChecks();statusReads=0;
        fixture.duringRead=(at,n,count)=>{if(at==Status&&++statusReads==2)fixture.D(Status+0x184,200);};s=fixture.Capture();
        Check(s.observation=="StableSample","diagnostic-only deferrals do not invalidate native ownership observation");
        Progress();
    }
    private static void Progress()
    {
        var tracker=new NativeQueueProgress();
        Func<string,TeamReadinessSnapshot> snapshot=head=>new TeamReadinessSnapshot {observation="StableSample",HeadIdentity=head};
        TeamReadinessSnapshot s=null;
        for(int t=0;t<=30000;t+=1000){s=snapshot("old-job");tracker.Observe(s,t);}
        Check(s.queueStallSuspected&&s.unchangedHeadMilliseconds==30000,"unchanged native head produces suspected-stall evidence");
        s=snapshot("new-job");tracker.Observe(s,31000);Check(!s.queueStallSuspected&&s.unchangedHeadMilliseconds==0,"next native head resets timer");
        s=snapshot("new-job");tracker.Observe(s,70000);Check(!s.queueStallSuspected&&s.unchangedHeadMilliseconds==0,"observation gap is not continuous stall evidence");
        tracker.Observe(new TeamReadinessSnapshot(),71000);s=snapshot("new-job");tracker.Observe(s,72000);
        Check(s.unchangedHeadMilliseconds==0,"unavailable sample breaks continuous timing");
        s=snapshot(null);s.queueStallSuspected=true;tracker.Observe(s,73000);
        Check(!s.queueStallSuspected&&s.unchangedHeadMilliseconds==0,"empty queue clears old stall flag");
    }
}
