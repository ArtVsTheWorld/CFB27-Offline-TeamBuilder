using System;
using System.Linq;
using System.Runtime.InteropServices;
using OfflineTeamBuilderRuntime;

internal sealed partial class NativeFixture
{
    private void ColdReady(){Q(context,A("CareerContextVtable"));}
    private void ColdPass(bool partial=false,bool builtin=false,bool nullQueue=false)
    {
        ColdReady();if(builtin)D(selected+0xF78,1116);
        if(nullQueue){Q(manager+0x180,0);Q(manager+0x1A0,0);}
        int resident=partial?3:0;
        for(int i=0;i<resident;i++)D(records+(ulong)i*0x78+0x58,slots[i]);
        loaded=resident;Q(manager+0x1D0,records+(ulong)resident*0x78);
        byte[] descriptors=Read(vector,72),serviceBefore=Read(service,0x900),selectedBefore=Read(selected,0x1000);
        dispatch();
        Program.Assert(U32(status+ColdStartBootstrapScope.Starts)==1&&U32(status)==0&&hubCalls==0&&stockLoads==0,
            "late installation starts on normal dispatch without replaying hub/import callbacks");
        Program.Assert(builds.Count==1&&builds[0]==slots[resident]&&pending.Count==1,"cold catch-up begins at the first absent team");
        for(int i=resident;i<9;i++){Program.Assert(builds.Last()==slots[i],"cold startup preserves saved-team order: partial="+partial+" builtin="+builtin+" nullQueue="+nullQueue+" index="+i+" builds="+string.Join(",",builds)+" stage="+U32(status+8)+" pending="+U32(status+4)+" ready="+U32(status+0x84));Complete();dispatch();}
        Program.Assert(builds.SequenceEqual(slots.Skip(resident))&&U32(status+8)==16&&U32(status+0x48)==9&&U32(status+0x4C)==0,
            "cold startup completes all saved teams and skips resident uniforms");
        Program.Assert(U32(status+ResidencyRepairScope.ResidentSkips)==resident&&U32(status+ColdStartBootstrapScope.Active)==0,
            "cold missing-only scope ends after completion");
        for(int i=0;i<50;i++)dispatch();
        Program.Assert(U32(status+ColdStartBootstrapScope.Starts)==1&&builds.Count==9-resident,"idle frontend cannot duplicate a completed catch-up");
        Program.Assert(Read(vector,72).SequenceEqual(descriptors)&&Read(service,0x900).SequenceEqual(serviceBefore)&&Read(selected,0x1000).SequenceEqual(selectedBefore),
            "cold catch-up leaves native metadata, service state and selected-school data unchanged");
        // A save boundary retires the existing private owner. Re-entering an
        // active frontend without another hub event gets one fresh catch-up.
        Marshal.WriteByte(new IntPtr((long)service),0x7B1,0);Q(service+0x468,0);dispatch();
        loaded=0;Q(manager+0x1D0,records);Bind();dispatch();
        Program.Assert(U32(status+ColdStartBootstrapScope.Starts)==2&&builds.Last()==slots[0]&&pending.Count==1,
            "later save entry also catches up after a retired ownership boundary");
        for(int i=0;i<9;i++){Complete();dispatch();}
        Program.Assert(U32(status+8)==16&&U32(status+0x48)==9&&hubCalls==0&&stockLoads==0,"reload catch-up completes without any hub or broad import call");
    }
    private void ColdExcluded(string kind)
    {
        ColdReady();ulong target=0,saved=0;bool word=false,byteValue=false;
        switch(kind){
            case "main menu":target=service+0x7B1;byteValue=true;break;
            case "stock import":target=service+0x7B3;byteValue=true;break;
            case "saved map":target=service+0x7D8;word=true;break;
            case "hub in progress":target=status+ColdStartBootstrapScope.CareerDepth;word=true;break;
            case "match":target=A("GateMatchStatesGlobal");break;
            case "converter":target=career+0x1B0;break;
            case "service type":target=service;break;
            case "career type":target=career;break;
            case "career absent":target=service+0x468;break;
            case "career invalid pointer":target=service+0x468;break;
            case "context type":target=context;break;
            case "context absent":target=service+0x470;break;
            case "selected absent":target=context+0x1D0;break;
            case "selected unaligned":target=context+0x1D0;break;
            case "selected invalid":target=selected+0xF78;word=true;break;
            case "builder type":target=manager;break;
            case "builder entry":target=A("Rva_0B38B3B8")+0x20;break;
            case "busy queue":target=manager+0x1A0;break;
            case "empty metadata":target=manager+0x138;break;
            case "unaligned metadata":target=manager+0x130;break;
            case "oversized metadata":target=manager+0x138;break;
            case "record stride":target=manager+0x1D0;break;
            case "record absent":target=manager+0x1C8;break;
            case "bad descriptor":target=BitConverter.ToUInt64(Read(vector,8),0);break;
            default:throw new Exception(kind);
        }
        saved=byteValue?Read(target,1)[0]:word?U32(target):BitConverter.ToUInt64(Read(target,8),0);
        ulong bad=0;
        switch(kind){
            case "stock import":case "saved map":case "match":case "converter":case "hub in progress":bad=1;break;
            case "busy queue":bad=vector+8;break;
            case "career invalid pointer":bad=0xFFFF800000000000UL;break;
            case "selected unaligned":bad=selected+1;break;
            case "empty metadata":bad=vector;break;
            case "unaligned metadata":bad=vector+1;break;
            case "oversized metadata":bad=vector+0x108;break;
            case "record stride":bad=records+1;break;
            case "record absent":bad=0;Q(manager+0x1D0,records+0x78);break;
            case "selected invalid":bad=0;break;
            case "bad descriptor":Q(manager+0x138,vector+8);bad=0;break;
            case "service type":case "career type":case "context type":case "builder type":case "builder entry":bad=saved+1;break;
        }
        if(byteValue)Marshal.WriteByte(new IntPtr((long)target),(byte)bad);else if(word)D(target,(uint)bad);else Q(target,bad);
        byte[] before=Read(status,0x200);
        for(int i=0;i<20;i++)dispatch();
        Program.Assert(builds.Count==0&&pending.Count==0&&U32(status+ColdStartBootstrapScope.Starts)==0&&Read(status,0x200).SequenceEqual(before),
            "cold initialization refuses "+kind+" without touching private ownership or native work");
        if(byteValue)Marshal.WriteByte(new IntPtr((long)target),(byte)saved);else if(word)D(target,(uint)saved);else Q(target,saved);
        if(kind=="record absent")Q(manager+0x1D0,records);
        dispatch();Program.Assert(builds.Count==1&&U32(status+ColdStartBootstrapScope.Starts)==1,"cold retry starts only when "+kind+" becomes valid");
    }
    private void ColdAlreadyResident()
    {
        ColdReady();for(int i=0;i<9;i++)D(records+(ulong)i*0x78+0x58,slots[i]);Q(manager+0x1D0,records+9*0x78);
        for(int i=0;i<50;i++)dispatch();
        Program.Assert(builds.Count==0&&U32(status+ColdStartBootstrapScope.Starts)==0,"resident native teams are not rebuilt to initialize diagnostics");
    }
    private static void ColdStartChecks()
    {
        foreach(bool partial in new[]{false,true})foreach(bool builtin in new[]{false,true})
            using(var f=new NativeFixture())f.ColdPass(partial,builtin);
        using(var f=new NativeFixture())f.ColdPass(false,false,true);
        foreach(string reason in new[]{"main menu","stock import","saved map","hub in progress","match","converter","service type","career type","career absent","career invalid pointer","context type","context absent","selected absent","selected unaligned","selected invalid","builder type","builder entry","busy queue","empty metadata","unaligned metadata","oversized metadata","record stride","record absent","bad descriptor"})
            using(var f=new NativeFixture())f.ColdExcluded(reason);
        foreach(string reason in new[]{"service","career","context","selected","metadata begin","metadata end","records","match","converter","queue","stock import","saved map","service type","career type","context type","builder type","builder entry","hub in progress"})
            using(var f=new NativeFixture())f.ColdAdoptionRace(reason);
        using(var f=new NativeFixture())f.ColdAlreadyResident();
        using(var f=new NativeFixture())f.ColdRealHubReentry();
        ColdContracts();
    }
    private void ColdAdoptionRace(string kind)
    {
        ColdReady();ulong target=0,bad=0;
        switch(kind){
            case "service":target=A("ServiceGlobal");break;
            case "career":target=service+0x468;break;
            case "context":target=service+0x470;break;
            case "selected":target=context+0x1D0;break;
            case "metadata begin":target=manager+0x130;bad=vector+8;break;
            case "metadata end":target=manager+0x138;bad=vector+64;break;
            case "records":target=manager+0x1D0;bad=records+0x78;break;
            case "match":target=A("GateMatchStatesGlobal");bad=1;break;
            case "converter":target=career+0x1B0;bad=1;break;
            case "queue":target=manager+0x1A0;bad=vector+8;break;
            case "stock import":target=service+0x7B3;bad=1;break;
            case "saved map":target=service+0x7D8;bad=1;break;
            case "service type":target=service;bad=A("ServiceVtable")+1;break;
            case "career type":target=career;bad=A("CareerManagerVtable")+1;break;
            case "context type":target=context;bad=A("CareerContextVtable")+1;break;
            case "builder type":target=manager;bad=A("Rva_0B38B3B8")+1;break;
            case "builder entry":target=A("Rva_0B38B3B8")+0x20;bad=A("Rva_0317B260")+1;break;
            case "hub in progress":target=status+ColdStartBootstrapScope.CareerDepth;bad=1;break;
            default:throw new Exception(kind);
        }
        ulong saved=BitConverter.ToUInt64(Read(target,8),0);
        byte[] code=Read(remote+0x2000,0x2000);
        byte[] marker=GameProfile.Hex("48b8").Concat(BitConverter.GetBytes(A("GateMatchStatesGlobal"))).Concat(GameProfile.Hex("48833800")).ToArray();
        int[] hits=Enumerable.Range(0,code.Length-marker.Length+1).Where(n=>marker.Select((v,i)=>code[n+i]==v).All(v=>v)).ToArray();
        Program.Assert(hits.Length==2,"cold adoption has two match-state checks");
        ulong patch=remote+0x2000+(ulong)hits[1],trampoline=remote+0x1C000;
        byte[] original=Read(patch,10);
        byte[] mutation=GameProfile.Hex("9c5048b8").Concat(BitConverter.GetBytes(target)).Concat(GameProfile.Hex("c700")).Concat(BitConverter.GetBytes((uint)bad))
            .Concat(GameProfile.Hex("c74004")).Concat(BitConverter.GetBytes((uint)(bad>>32))).Concat(GameProfile.Hex("589d")).Concat(original)
            .Concat(GameProfile.Hex("ff2500000000")).Concat(BitConverter.GetBytes(patch+10)).ToArray();
        Write(trampoline,mutation);Write(patch,new byte[]{0xE9}.Concat(BitConverter.GetBytes(checked((int)((long)trampoline-(long)patch-5)))).ToArray());
        byte[] before=Read(status,0x200);dispatch();
        if(kind=="hub in progress")Buffer.BlockCopy(BitConverter.GetBytes(1U),0,before,ColdStartBootstrapScope.CareerDepth,4);
        Program.Assert(builds.Count==0&&Read(status,0x200).SequenceEqual(before),"cold snapshot race refuses changed "+kind+" before adopting ownership");
        Q(target,saved);Write(patch,original);dispatch();
        Program.Assert(builds.Count==1&&U32(status+ColdStartBootstrapScope.Starts)==1,"cold snapshot race retries valid "+kind+" without another hub notification");
    }
    private static void ColdContracts()
    {
        var p=GameProfile.Current;const ulong imageBase=0x140000000,statusAddress=0x200004000;
        ulong[] args={statusAddress,imageBase+(ulong)p.Rva("DispatcherContinuation")};
        byte[] raw=p.templates["Dispatcher"].Emit(p,args),emitted=ColdStartBootstrapScope.Apply("Dispatcher",raw,p,args);
        int at=Array.IndexOf(raw,(byte)0x49,0x81);
        Program.Assert(at==0x81&&emitted.Length<0x2000,"cold dispatcher remains inside its private allocation");
        Program.Assert(Enumerable.Range(0,raw.Length).Where(n=>n<at||n>=at+5).All(n=>raw[n]==emitted[n]),"cold scope changes exactly one complete five-byte instruction before appending its stub");
        Program.Assert(emitted[at]==0xE9&&at+5+BitConverter.ToInt32(emitted,at+1)==raw.Length,"cold scope detour targets appended private code");
        byte[] broken=(byte[])raw.Clone();broken[at]^=1;
        Program.Reject(()=>ColdStartBootstrapScope.Apply("Dispatcher",broken,p,args),"cold scope refuses unknown dispatcher prefix");
        byte[] careerRaw=p.templates["Career"].Emit(p,new[]{statusAddress,0x300000000UL,imageBase+(ulong)p.Rva("CareerManagerVtable"),imageBase+(ulong)p.Rva("Add"),imageBase+(ulong)p.Rva("Needs"),imageBase+(ulong)p.Rva("Enter")});
        byte[] careerCode=ColdStartBootstrapScope.Apply("Career",careerRaw,p,args);
        Program.Assert(careerCode.Length<0x2000&&careerCode.Skip(0x3A8).Take(careerRaw.Length-0x3A8).SequenceEqual(careerRaw.Skip(0x3A8)),"Career depth counter preserves the existing completion callback bytes and address");
        foreach(string name in p.templates.Keys.Where(n=>n!="Dispatcher"&&n!="Career")) {
            byte[] other=GameProfile.Hex(p.templates[name].code);
            Program.Assert(ColdStartBootstrapScope.Apply(name,other,p,args).SequenceEqual(other),"cold scope preserves "+name);
        }
    }
    private void ColdRealHubReentry()
    {
        ColdReady();dispatchDuringHub=true;
        uint expectedReturn;int expectedHubs,expectedLoads;
        using(var control=new NativeFixture()) {
            control.ColdReady();expectedReturn=control.enter(new IntPtr((long)control.service));
            expectedHubs=control.hubCalls;expectedLoads=control.stockLoads;
        }
        uint returned=enter(new IntPtr((long)service));
        Program.Assert(returned==expectedReturn&&hubCalls==expectedHubs&&stockLoads==expectedLoads,"real hub chain retains its existing calls and return ABI: returned="+returned+" calls="+hubCalls+" stockLoads="+stockLoads+" cold="+U32(status+ColdStartBootstrapScope.Starts)+" depth="+U32(status+ColdStartBootstrapScope.CareerDepth));
        Program.Assert(U32(status+ColdStartBootstrapScope.Starts)==0&&U32(status+ColdStartBootstrapScope.CareerDepth)==0&&builds.Count==1,
            "nested AS dispatch during the real hub callback cannot start a competing cold pass");
        for(int i=0;i<9;i++){Complete();dispatch();}
        Program.Assert(builds.SequenceEqual(slots)&&U32(status+8)==16&&U32(status+ColdStartBootstrapScope.Starts)==0,"normal hub initialization still completes all teams without catch-up");
    }
}
