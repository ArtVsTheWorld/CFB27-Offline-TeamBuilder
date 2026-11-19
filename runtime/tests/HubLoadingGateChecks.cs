using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using OfflineTeamBuilderRuntime;

internal sealed class HubLoadingGateChecks : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Hide(IntPtr loader);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Show(IntPtr loader,int type);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate ulong Value();
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAlloc(IntPtr p,UIntPtr n,uint type,uint protect);
    [DllImport("kernel32.dll")] private static extern bool VirtualFree(IntPtr p,UIntPtr n,uint type);
    private readonly List<IntPtr> blocks=new List<IntPtr>();private readonly List<Delegate> callbacks=new List<Delegate>();
    private readonly ulong state,status,service,career,loader,serviceGlobal,loaderGlobal,undefined,matchStatesGlobal,code;
    private int hides,shows,showType;private ulong clock=1000;
    private readonly Hide hide;private readonly Show show;private readonly Value value;
    private HubLoadingGateChecks()
    {
        state=Alloc(128);status=Alloc(0x188);service=Alloc(0x800);career=Alloc(0x300);loader=Alloc(8);
        serviceGlobal=Alloc(8);loaderGlobal=Alloc(8);undefined=Alloc(8);matchStatesGlobal=Alloc(8);code=Address(VirtualAlloc(IntPtr.Zero,(UIntPtr)0x3000,0x3000,0x40));
        Q(serviceGlobal,service);Q(loaderGlobal,loader);Q(loader,0x33333000);Q(undefined,0x12345678);Q(service,0x11111000);Q(career,0x22222000);
        Q(service+0x468,career);Byte(service+0x7B1,1);Q(status+0x18,career);Q(status+0x50,career);D(status+8,11);Q(status+0x90,2);D(state,1);
        ulong original=Keep(new Hide(p=>hides++)),originalShow=Keep(new Show((p,t)=>{shows++;showType=t;})),tick=Keep(new Value(()=>clock));
        Write(code,HubLoadingGateCode.Hide(state,status,serviceGlobal,0x11111000,0x22222000,original,tick,matchStatesGlobal));
        Write(code+0x800,HubLoadingGateCode.Show(state,status,serviceGlobal,originalShow));
        Write(code+0x1000,HubLoadingGateCode.Return(state,status,serviceGlobal,loaderGlobal,0x11111000,0x22222000,0x33333000,originalShow,original,tick,undefined,matchStatesGlobal));
        Write(code+0x1700,HubLoadingGateCode.Entry(code+0x1000));
        Write(code+0x2000,HubLoadingGateCode.Callback(code+0x1700));
        Write(code+0x2100,GameProfile.Hex("48b87856341200000000").Concat(GameProfile.Hex("ff2500000000")).Concat(BitConverter.GetBytes(code+0x2000)).ToArray());
        hide=(Hide)Marshal.GetDelegateForFunctionPointer(new IntPtr((long)code),typeof(Hide));
        show=(Show)Marshal.GetDelegateForFunctionPointer(new IntPtr((long)code+0x800),typeof(Show));
        value=(Value)Marshal.GetDelegateForFunctionPointer(new IntPtr((long)code+0x2100),typeof(Value));
        BeginLoad();
    }
    private ulong Alloc(int n){var p=Marshal.AllocHGlobal(n);blocks.Add(p);Write(Address(p),new byte[n]);return Address(p);}
    private ulong Keep(Delegate d){callbacks.Add(d);return Address(Marshal.GetFunctionPointerForDelegate(d));}
    private static ulong Address(IntPtr p){if(p==IntPtr.Zero)throw new Exception("Private allocation failed");return unchecked((ulong)p.ToInt64());}
    private static void Write(ulong p,byte[] b)=>Marshal.Copy(b,0,new IntPtr((long)p),b.Length);
    private static byte[] Read(ulong p,int n){var b=new byte[n];Marshal.Copy(new IntPtr((long)p),b,0,n);return b;}
    private static void Q(ulong p,ulong v)=>Write(p,BitConverter.GetBytes(v));
    private static void D(ulong p,uint v)=>Write(p,BitConverter.GetBytes(v));
    private static void Byte(ulong p,byte v)=>Marshal.WriteByte(new IntPtr((long)p),v);
    private static uint U32(ulong p)=>BitConverter.ToUInt32(Read(p,4),0);
    private void BeginLoad()
    {
        Q(matchStatesGlobal,0);Byte(service+0x7B1,0);Q(service+0x468,0);value();Q(service+0x468,career);Byte(service+0x7B1,1);
    }
    private void HideNow()=>hide(new IntPtr((long)loader));
    private static void Check(bool condition,string text)=>Program.Assert(condition,"hub load gate: "+text);
    internal static void CheckAll()
    {
        using(var f=new HubLoadingGateChecks()) {
            Check(f.value()==0x12345678,"idle helper preserves original undefined result");
            f.HideNow();Check(f.hides==0&&U32(f.state+4)==1&&U32(f.state+56)==1,"only hide is deferred while native jobs remain");
            f.HideNow();Check(U32(f.state+56)==1,"repeated hide does not reset deadline or duplicate request");
            f.value();Check(f.hides==0,"UI frame continues without calling hide while waiting");
            D(f.state+4,2);f.value();Check(f.hides==1&&U32(f.state+4)==0&&U32(f.state+48)==1,"readiness replays hide exactly once on UI caller");
            f.value();Check(f.hides==1,"later frames never replay completed hide");
            f.BeginLoad();f.HideNow();f.show(new IntPtr((long)f.loader),5);f.value();Check(f.shows==1&&f.showType==5&&f.hides==1&&U32(f.state+4)==0,"new loading screen cancels stale hide and preserves show argument");
            f.BeginLoad();f.HideNow();f.clock+=59999;f.value();Check(f.hides==1,"native timeout does not release early");
            f.clock++;f.value();Check(f.hides==2&&U32(f.state+48)==2,"native timeout releases even without managed monitoring");
            f.BeginLoad();f.HideNow();D(f.state+4,2);ulong result=0;var t=new Thread(()=>result=f.value());t.Start();t.Join();Check(f.hides==2&&result==0x12345678,"different thread cannot replay UI hide");
            f.value();Check(f.hides==3,"original UI thread consumes release");
            f.BeginLoad();f.HideNow();D(f.state+4,2);Q(f.status+0x90,3);f.value();Check(f.hides==3&&U32(f.state+48)==4,"retired epoch cannot hide a different transition");Q(f.status+0x90,2);
            f.BeginLoad();f.HideNow();D(f.state+4,2);Q(f.service+0x468,0);f.value();Check(f.hides==3&&U32(f.state+4)==0,"left career cancels pending hide");Q(f.service+0x468,f.career);
            D(f.status+8,16);f.HideNow();Check(f.hides==4,"already completed pass uses stock hide immediately");D(f.status+8,11);
            f.BeginLoad();Byte(f.service+0x7B3,1);f.HideNow();Check(f.hides==5,"stock importer screen untouched");Byte(f.service+0x7B3,0);
            f.BeginLoad();D(f.service+0x7D8,1);f.HideNow();Check(f.hides==6,"busy saved map uses stock behavior");D(f.service+0x7D8,0);
            Byte(f.service+0x7B1,0);f.HideNow();Check(f.hides==7,"non-career screens untouched");Byte(f.service+0x7B1,1);
            f.Registers();
            f.ReloadHandoff();
        }
        using(var f=new HubLoadingGateChecks())f.MatchEntry();
        PracticeCompatibility();
        Policy();
    }
    private void Registers()
    {
        ulong pattern=Alloc(16),result=Alloc(0xB0);byte[] p=Enumerable.Range(0,16).Select(i=>(byte)(i*11+1)).ToArray();Write(pattern,p);
        var b=new List<byte>();Action<string> h=s=>b.AddRange(GameProfile.Hex(s));Action<ulong> q=n=>b.AddRange(BitConverter.GetBytes(n));
        h("4883ec2848b8");q(pattern);
        for(int i=0;i<6;i++)h("f30f6f"+(i*8).ToString("x2"));
        ulong[] expected={0x1111222233334444,0x2222333344445555,0x3333444455556666,0x4444555566667777,0x5555666677778888,0x6666777788889999};
        foreach(var x in new[]{Tuple.Create("48b9",0),Tuple.Create("48ba",1),Tuple.Create("49b8",2),Tuple.Create("49b9",3),Tuple.Create("49ba",4),Tuple.Create("49bb",5)}){h(x.Item1);q(expected[x.Item2]);}
        h("31c0f948b8");q(0x777788889999AAAA);h("ff1502000000eb08");q(code+0x2000);
        h("50505248b8");q(result);h("488b542408488990a0000000488b542430488990a80000005a5858");
        h("48b8");q(result);h("488908488950084c8940104c8948184c8950204c8958289c5a48895030");
        for(int i=0;i<6;i++){h("f30f7f"+(0x80+i*8).ToString("x2"));b.AddRange(BitConverter.GetBytes(0x40+i*16));}
        h("4883c428c3");Write(code+0x1800,b.ToArray());var run=(Value)Marshal.GetDelegateForFunctionPointer(new IntPtr((long)code+0x1800),typeof(Value));
        foreach(int mode in new[]{0,1,2}) {
            BeginLoad();bool release=mode==1;
            if(mode==2)show(new IntPtr((long)loader),1);else HideNow();
            if(release)D(state+4,2);run();byte[] output=Read(result,0xB0);
            Check(BitConverter.ToUInt64(output,0xA0)==0x777788889999AAAA,"callback preserves incoming RAX; mode="+mode);
            Check(BitConverter.ToUInt64(output,0xA8)==expected[3],"callback retains native caller-home R9 write; mode="+mode);
            for(int i=0;i<6;i++)Check(BitConverter.ToUInt64(output,i*8)==expected[i],"volatile register "+i+" retained; release="+release);
            Check((BitConverter.ToUInt64(output,0x30)&0x8C5)==0x45,"original helper flags retained; release="+release);
            for(int i=0;i<6;i++)Check(output.Skip(0x40+i*16).Take(16).SequenceEqual(p),"XMM"+i+" retained; release="+release);
            D(state+4,0);
        }
    }
    private void ReloadHandoff()
    {
        D(state+4,0);BeginLoad();int initialHides=hides,initialShows=shows;uint initialHolds=U32(state+56),initialReassertions=U32(state+84);
        D(status+8,0);show(new IntPtr((long)loader),1);HideNow();value();
        Check(hides==initialHides+1&&shows==initialShows+1&&U32(state+4)==0,"early stock hide does not block career initialization");
        D(status+8,11);value();
        Check(U32(state+4)==1&&U32(state+56)==initialHolds+1&&U32(state+84)==initialReassertions+1,"reload entry arms on first safe UI callback after native work starts");
        Check(shows==initialShows+2&&hides==initialHides+1,"native loading view is reasserted once, without hide replay");
        Check(U32(state+104)==0,"first wait consumes the entry ticket");
        D(status+8,16);uint releases=U32(state+60),stock=U32(state+72);HideNow();
        Check(hides==initialHides+2&&U32(state+4)==0&&U32(state+48)==5&&U32(state+60)==releases+1&&U32(state+72)==stock+1,"stock hide after completed pass is counted as a successful release");
        uint reason=U32(state+48);show(new IntPtr((long)loader),1);D(status+8,12);value();
        Check(U32(state+48)==reason&&U32(state+4)==0&&U32(state+64)==0,"later match-type Show cannot restart a wait during background team work");
        BeginLoad();D(status+8,11);show(new IntPtr((long)loader),1);value();uint cancels=U32(state+76);uint holds=U32(state+56);
        show(new IntPtr((long)loader),1);value();
        Check(U32(state+4)==0&&U32(state+76)==cancels+1&&U32(state+56)==holds,"new stock Show cancels wait even when view type matches; no rearm");
        BeginLoad();D(status+8,16);show(new IntPtr((long)loader),1);Q(status+0x90,3);value();
        Check(U32(state+64)==0&&U32(state+4)==0,"pass that finishes before first UI callback does not reopen loading screen");
        Q(status+0x90,2);D(status+8,11);
    }
    private void MatchEntry()
    {
        HideNow();Check(U32(state+4)==1,"match test begins with a real outstanding Dynasty wait");
        int beforeHide=hides;uint holds=U32(state+56);D(state+4,2);Q(matchStatesGlobal,1);value();
        Check(hides==beforeHide&&U32(state+4)==0&&U32(state+48)==6&&U32(state+112)==1,"match-state publication discards pending release without stock Hide call");
        Check(U32(state+104)==0&&U32(state+64)==0,"match entry clears ticket and incoming marker");
        show(new IntPtr((long)loader),1);value();HideNow();
        Check(U32(state+56)==holds&&hides==beforeHide+1&&U32(state+4)==0,"match Show/Hide use stock controls without another gate");
        Q(matchStatesGlobal,0);show(new IntPtr((long)loader),1);value();
        Check(U32(state+56)==holds&&U32(state+4)==0,"leaving match state alone does not grant another Dynasty entry ticket");
        Byte(service+0x7B1,0);value();Byte(service+0x7B1,1);
        Check(U32(state+104)==0,"inactive flag with a still-bound career cannot rearm during match preparation");
        BeginLoad();show(new IntPtr((long)loader),1);value();
        Check(U32(state+56)==holds+1&&U32(state+4)==1,"observing main menu rearms normal Dynasty reload wait");
        Q(matchStatesGlobal,1);clock+=HubLoadingGateCode.TimeoutMs;value();
        Check(hides==beforeHide+1&&U32(state+48)==6,"match entry overrides even an expired native deadline without hiding match UI");
        Q(matchStatesGlobal,0);BeginLoad();HideNow();uint oldHolds=U32(state+56);show(new IntPtr((long)loader),1);value();
        Check(U32(state+56)==oldHolds&&U32(state+4)==0,"new match load before match-state allocation cancels rather than reasserts the view");
    }
    private static void PracticeCompatibility()
    {
        var p=GameProfile.Current;long at=p.Rva("UiArgumentMarker");
        Check(at==0x3F0B070,"gate targets the isolated AS argument marker");
        Check(p.hubGate.validations.Single(c=>c.target=="UiArgumentMarker").expected=="4c894c2420c3cccccccccccccccccccc","callback contract includes complete leaf and padding");
        // Expanded Practice 0.8.26 fingerprint spans, reviewed against its source.
        // No normalization/bypass of Practice integrity checks is required.
        long[,] spans={
            {0x7b83740,40},{0x35c2540,514},{0x5fe1c60,232},{0x2ad9200,5},
            {0x3487670,86},{0x35e33c0,445},{0x35e48e0,368},{0x35ff9d0,3356},
            {0x36012d0,1561},{0x36018f0,1487},{0x3601ec4,486},{0x36a14e0,1211},
            {0x377e3d0,136},{0x3f0a390,8},{0x4ff7050,1063},{0x4ff7490,262},
            {0x4ff7770,783},{0x4ff8ca0,89},{0x5d124b0,271},{0x5d4d990,516},
            {0x5d5d090,62},{0x5d5d0d0,79},{0x5d5d120,1156},{0x5d5e600,1053},
            {0x5d705e0,2019},{0x5d743b0,7044},{0x5f0d650,690},{0x7b82410,388},
            {0x7b82599,44},{0x7b9d9c0,561},{0x7baf760,168},{0x7baf9d0,103},
            {0xae3a1b0,16},{0xbab6fc0,16},{0xbab6fd0,16},{0xbab6fe0,16},
            {0xbab6ff0,16},{0x5d5da80,63},{0x7bce980,72},{0x7c1b950,8},
            {0x25a3e60,168},{0x5d4e1c0,596}};
        for(int i=0;i<spans.GetLength(0);i++)
            Check(at+16<=spans[i,0]||at>=spans[i,0]+spans[i,1],"gate does not overlap Expanded Practice fingerprint "+spans[i,0].ToString("x"));
    }
    private static void Policy()
    {
        var policy=new HubLoadingGatePolicy();Func<TeamReadinessSnapshot> good=()=>new TeamReadinessSnapshot{observation="StableSample",currentCareerOwned=true,careerActive=true,epoch=2,expectedTeams=2,runtimeRecordsPresent=2,teams=new List<TeamReadiness>{new TeamReadiness{slot=1801,runtimeRecord="Present"},new TeamReadiness{slot=1802,runtimeRecord="Present"}}};
        Func<TeamReadinessSnapshot,long,bool> poll=(s,t)=>policy.Observe(s,16,0,0,0,2,t);
        Check(!poll(good(),0)&&!poll(good(),149)&&poll(good(),150),"stable completion window uses real readiness, not a fixed loading delay");
        foreach(Action<TeamReadinessSnapshot> mutate in new Action<TeamReadinessSnapshot>[] {s=>s.observation="Unavailable",s=>s.currentCareerOwned=false,s=>s.careerActive=false,s=>s.stockImportActive=true,s=>s.savedMapBusy=true,s=>s.runtimeRecordsPresent=1,s=>s.nativeQueueCount=1,s=>s.jobs.Add(new NativeJobReadiness()),s=>s.olderEpochPluginJobs=1,s=>s.epoch=3,s=>s.teams[1].runtimeRecord="Missing",s=>s.expectedTeams=0}) {
            var sample=good();mutate(sample);Check(!poll(sample,200),"incomplete/stale sample cannot release hub");Check(!poll(good(),201),"invalid sample resets stability window");
        }
        Check(!policy.Observe(good(),11,0,0,0,2,500),"pass still running is not ready");
        Check(!policy.Observe(good(),16,1,0,0,2,500),"pending job is not ready");
        Check(!policy.Observe(good(),16,0,1,0,2,500),"deferred team is not ready");
        Check(!policy.Observe(good(),16,0,0,1,2,500),"failed team cannot report successful completion");
    }
    public void Dispose(){VirtualFree(new IntPtr((long)code),UIntPtr.Zero,0x8000);foreach(var p in blocks)Marshal.FreeHGlobal(p);GC.KeepAlive(callbacks);}
}
