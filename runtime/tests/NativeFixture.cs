using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using OfflineTeamBuilderRuntime;
using OfflineTeamBuilderReentry;

// ALL executable buffers and mocked native objects belong to this test process.
// No Process.GetProcessesByName, remote thread, game PID, save or cache access.
internal sealed class NativeFixture : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint Hub(IntPtr service);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Dispatch();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Builder(IntPtr manager,uint slot,ulong unused,IntPtr capture);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Callback(IntPtr result,IntPtr capture);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Destroy(IntPtr payload);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Load(IntPtr service,uint slot,ulong unused);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Reload(IntPtr service);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void LogoRegister(IntPtr registry,uint group,uint style,uint slot,IntPtr data,uint length,uint variant);
    private readonly List<IntPtr> objects=new List<IntPtr>();
    private readonly List<Delegate> delegates=new List<Delegate>();
    private readonly ulong image,remote,status,service,career,context,selected,manager,vector,records;
    private readonly List<uint> slots=new List<uint>{1807,1811,1810,1808,1806,1805,1804,1803,1801};
    private readonly List<uint> builds=new List<uint>();
    private readonly Queue<byte[]> pending=new Queue<byte[]>();
    private int destroyed,loaded;
    private readonly Hub enter;
    private readonly Dispatch dispatch;
    private NativeFixture()
    {
        image=Address(VirtualAlloc(IntPtr.Zero,(UIntPtr)0x10000000,0x2000,4));
        remote=Address(VirtualAlloc(IntPtr.Zero,(UIntPtr)0x20000,0x3000,0x40));
        status=remote+0x4000;
        service=Obj(0x900);career=Obj(0x300);context=Obj(0x300);selected=Obj(0x1000);manager=Obj(0x300);
        vector=Obj(9*8);records=Obj(9*0x78);
        Q(service,image+(ulong)GameProfile.Current.Rva("ServiceVtable"));Q(career,image+(ulong)GameProfile.Current.Rva("CareerManagerVtable"));
        Commit(image+(ulong)GameProfile.Current.Rva("ServiceGlobal"),8);
        Q(image+(ulong)GameProfile.Current.Rva("ServiceGlobal"),service);
        Bind();Q(context+0x1D0,selected);D(selected+0xF78,1801);
        Q(manager,image+0xB38B3B8);Q(manager+0x130,vector);Q(manager+0x138,vector+9*8);
        Q(manager+0x180,vector);Q(manager+0x1A0,vector);Q(manager+0x1C8,records);Q(manager+0x1D0,records);
        for(int n=0;n<9;n++){ulong node=Obj(0x200);Q(node,image+0xB494B90);D(node+0x10,slots[n]);Q(vector+(ulong)n*8,node);}
        Commit(image+0xE350900,8);Q(image+0xE350900,manager);
        Commit(image+0xB38B3B8,0x28);Q(image+0xB38B3B8+0x20,image+0x317B260);
        Jump(image+0x317B260,Keep(new Builder((owner,slot,unused,capture)=> {
            Program.Assert(Address(owner)==manager&&unused==0&&pending.Count==0,"native builder ABI and one job at a time");
            byte[] bytes=Read(Address(capture),32);pending.Enqueue(bytes);builds.Add(slot);Q(manager+0x1A0,vector+8);
        })));
        Jump(image+0x2AE3150,Keep(new Destroy(payload=>destroyed++)));
        ulong hub=Keep(new Hub(p=> { Marshal.WriteByte(p,0x7B1,1);Marshal.WriteByte(p,0x7B4,0);return 7; }));
        ulong key=Keep(new Hub(p=>1801));
        ulong load=Keep(new Load((p,slot,unused)=>Program.Assert(slot==1801&&unused==0,"stock load slot ABI")));
        ulong reload=Keep(new Reload(p=>Marshal.WriteByte(p,0x7B4,1)));
        Write(remote+0x10000,GameProfile.Current.Emit("MainCareer",remote+0x16000,hub,key,load,reload));
        Write(remote,GameProfile.Current.Emit("Career",status,remote+0x10000,image+(ulong)GameProfile.Current.Rva("CareerManagerVtable"),
            image+(ulong)GameProfile.Current.Rva("Add"),image+(ulong)GameProfile.Current.Rva("Needs"),image+(ulong)GameProfile.Current.Rva("Enter")));
        Write(remote+0x2000,GameProfile.Current.Emit("Dispatcher",status,image+(ulong)GameProfile.Current.Rva("DispatcherContinuation")));
        Commit(image+(ulong)GameProfile.Current.Rva("DispatcherContinuation"),20);
        Write(image+(ulong)GameProfile.Current.Rva("DispatcherContinuation"),GameProfile.Hex("5d488b5c2408488b742410488b7c2418c3"));
        enter=(Hub)Marshal.GetDelegateForFunctionPointer(new IntPtr((long)remote),typeof(Hub));
        dispatch=(Dispatch)Marshal.GetDelegateForFunctionPointer(new IntPtr((long)remote+0x2000),typeof(Dispatch));
    }
    private void Bind(){Q(service+0x468,career);Q(service+0x470,context);Marshal.WriteByte(new IntPtr((long)service),0x7B1,1);}
    private ulong Obj(int size){IntPtr p=Marshal.AllocHGlobal(size);objects.Add(p);Write(Address(p),new byte[size]);return Address(p);}
    private ulong Keep(Delegate d){delegates.Add(d);return Address(Marshal.GetFunctionPointerForDelegate(d));}
    private static ulong Address(IntPtr p){if(p==IntPtr.Zero)throw new Exception("Private allocation failed");return unchecked((ulong)p.ToInt64());}
    private static void Write(ulong at,byte[] bytes){Marshal.Copy(bytes,0,new IntPtr((long)at),bytes.Length);}
    private static byte[] Read(ulong at,int length){byte[] bytes=new byte[length];Marshal.Copy(new IntPtr((long)at),bytes,0,length);return bytes;}
    private static void Q(ulong at,ulong value){Write(at,BitConverter.GetBytes(value));}
    private static void D(ulong at,uint value){Write(at,BitConverter.GetBytes(value));}
    private static uint U32(ulong at){return unchecked((uint)Marshal.ReadInt32(new IntPtr((long)at)));}
    private static void Commit(ulong at,int size){ulong lo=at&~0xFFFUL,hi=(at+(ulong)size+0xFFF)&~0xFFFUL;if(VirtualAlloc(new IntPtr((long)lo),(UIntPtr)(hi-lo),0x1000,0x40)==IntPtr.Zero)throw new Exception("Private commit failed");}
    private static void Jump(ulong at,ulong target){Commit(at,12);Write(at,new byte[]{0x48,0xB8}.Concat(BitConverter.GetBytes(target)).Concat(new byte[]{0xFF,0xE0}).ToArray());}
    private void Deliver(byte[] bytes)
    {
        ulong cap=Obj(32),result=Obj(0x88);Write(cap,bytes);Marshal.WriteByte(new IntPtr((long)result),1);
        var cb=(Callback)Marshal.GetDelegateForFunctionPointer(new IntPtr((long)BitConverter.ToUInt64(bytes,24)),typeof(Callback));
        int before=destroyed,submissions=builds.Count;cb(new IntPtr((long)result),new IntPtr((long)cap));
        Program.Assert(destroyed==before+1&&builds.Count==submissions,"result destroyed once, no recursive submission");
    }
    private void Complete()
    {
        Program.Assert(pending.Count==1,"one native completion");Deliver(pending.Dequeue());Q(manager+0x1A0,vector);
        D(records+(ulong)loaded*0x78+0x58,builds.Last());loaded++;Q(manager+0x1D0,records+(ulong)loaded*0x78);
    }
    private void Sequence()
    {
        Program.Assert(enter(new IntPtr((long)service))==7,"stock career return ABI");
        for(int n=0;n<9;n++) {
            Program.Assert(builds.Count==n+1,"all nine team slots in order");
            enter(new IntPtr((long)service));dispatch();Program.Assert(builds.Count==n+1,"pending job not duplicated");
            Complete();if(n<8)enter(new IntPtr((long)service));dispatch();
        }
        Program.Assert(builds.SequenceEqual(slots)&&U32(status+8)==16&&U32(status+0x48)==9&&U32(status+0x4C)==0,"completed multi-team pass");
        byte[] first=new byte[32];
        // Week invalidation: same Career identity, a fresh completed pass.
        loaded=0;Q(manager+0x1D0,records);enter(new IntPtr((long)service));
        Program.Assert(builds.Count==10,"same-career week refresh");first=pending.Dequeue();
        // Retire the pass while the old native job remains queued. Its old
        // capture is NOT permission to enqueue a replacement behind it.
        Marshal.WriteByte(new IntPtr((long)service),0x7B1,0);Q(service+0x468,0);dispatch();
        Bind();int submitted=builds.Count;
        for(int i=0;i<5;i++){enter(new IntPtr((long)service));dispatch();}
        Program.Assert(builds.Count==submitted&&U32(status+4)==0&&U32(status+NativeWorkAdmission.CareerDeferrals)>=1,"new save waits for native job retirement, no replacement appended");
        Deliver(first);Q(manager+0x1A0,vector);dispatch();uint count=U32(status+0x48);Deliver(first);
        Program.Assert(U32(status+4)==1&&U32(status+0x48)==count&&U32(status+0x98)==2,"stale callback excluded after save reentry");
    }

    private void Admission()
    {
        // A stock loader/update already owns the shared native queue.
        Q(manager+0x1A0,vector+8);
        for(int i=0;i<4;i++)enter(new IntPtr((long)service));
        Program.Assert(builds.Count==0&&U32(status+4)==0&&U32(status+0x84)==1&&U32(status+8)==12,"busy initial queue publishes deferred request without faking pending job");
        Q(manager+0x1A0,vector);dispatch();
        Program.Assert(builds.Count==1&&builds[0]==slots[0]&&U32(status+4)==1,"initial pass resumes without a second Career notification after queue drains");
        Complete();ulong cursor=BitConverter.ToUInt64(Read(status+0x38,8),0);
        Q(manager+0x1A0,vector+8);dispatch();dispatch();
        Program.Assert(builds.Count==1&&U32(status+4)==0&&U32(status+0x84)==1&&BitConverter.ToUInt64(Read(status+0x38,8),0)==cursor,"deferred queue admission preserves next slot and callback barrier");
        Q(manager+0x1A0,vector);Marshal.WriteByte(new IntPtr((long)service),0x7B3,1);dispatch();
        Program.Assert(builds.Count==1&&U32(status+0x84)==1,"deferred work waits for stock importer");
        Marshal.WriteByte(new IntPtr((long)service),0x7B3,0);D(service+0x7D8,1);dispatch();
        Program.Assert(builds.Count==1&&U32(status+0x84)==1,"deferred work waits for stock saved-map processing");
        D(service+0x7D8,0);dispatch();
        Program.Assert(builds.Count==2&&builds[1]==slots[1]&&U32(status+4)==1,"deferred work resumes exact next slot without loss");
    }
    private void EmptyQueue()
    {
        Q(manager+0x180,0);Q(manager+0x1A0,0);enter(new IntPtr((long)service));
        Program.Assert(builds.Count==1&&U32(status+4)==1,"unallocated empty native queue does not prevent initial loading");
    }
    private void AdmissionRace(bool initial,string change)
    {
        if(!initial){enter(new IntPtr((long)service));Complete();}
        ulong cursor=BitConverter.ToUInt64(Read(status+0x38,8),0);
        ulong target,value;
        switch(change) {
            case "selected pointer":target=context+0x1D0;value=0;break;
            case "Career owner":target=service+0x468;value=0;break;
            case "selected slot":target=selected+0xF78;value=1807;break;
            case "converter":target=career+0x1B0;value=1;break;
            case "vector end":target=manager+0x138;value=vector+8*8;break;
            case "vector begin":target=manager+0x130;value=vector+2*8;break;
            case "cursor alignment":target=status+0x38;value=vector+9;break;
            default:throw new Exception("Unknown private mutation");
        }
        ulong saved=BitConverter.ToUInt64(Read(target,8),0);
        if(initial&&change=="cursor alignment")saved=vector; // initialized by Career before interception
        var p=GameProfile.Current;string name=initial?"Career":"Dispatcher";
        ulong[] args=initial?new[]{status,remote+0x10000,image+(ulong)p.Rva("CareerManagerVtable"),image+(ulong)p.Rva("Add"),image+(ulong)p.Rva("Needs"),image+(ulong)p.Rva("Enter")}:
            new[]{status,image+(ulong)p.Rva("DispatcherContinuation")};
        byte[] raw=p.templates[name].Emit(p,args);
        byte[] marker=GameProfile.Hex(initial?"488b46384c8b46404c39c0":"41c783840000000000000041c7430401000000");
        int at=Enumerable.Range(0,raw.Length-marker.Length+1).Single(n=>marker.Select((b,i)=>raw[n+i]==b).All(x=>x));
        ulong relay=initial?remote:remote+0x2000,patch=relay+(ulong)at,trampoline=remote+0x1C000;
        byte[] original=Read(patch,5);
        // This test process alone changes context AFTER earlier relay checks,
        // exactly at the last admission boundary. Preserve flags and RAX; no
        // managed callback, game process or native cancellation is involved.
        byte[] mutation=GameProfile.Hex("9c5048b8").Concat(BitConverter.GetBytes(target))
            .Concat(GameProfile.Hex("c700")).Concat(BitConverter.GetBytes((uint)value))
            .Concat(GameProfile.Hex("c74004")).Concat(BitConverter.GetBytes((uint)(value>>32)))
            .Concat(GameProfile.Hex("589dff2500000000")).Concat(BitConverter.GetBytes(relay+(ulong)raw.Length)).ToArray();
        Write(trampoline,mutation);
        Write(patch,new byte[]{0xE9}.Concat(BitConverter.GetBytes(checked((int)((long)trampoline-(long)patch-5)))).ToArray());
        int before=builds.Count;
        if(initial)enter(new IntPtr((long)service));else dispatch();
        Program.Assert(builds.Count==before&&U32(status+4)==0&&U32(status+0x84)==1,
            "last-moment "+name+" admission rejects changed "+change);
        if(!initial&&change!="cursor alignment")Program.Assert(BitConverter.ToUInt64(Read(status+0x38,8),0)==cursor,"declined race preserves next slot: "+change);
        Q(target,saved);Write(patch,original);dispatch();
        Program.Assert(builds.Count==before+1&&builds.Last()==slots[initial?0:1],"race resumes exact slot after valid context returns: "+name+" "+change);
    }
    private void LoadPass()
    {
        int start=builds.Count;
        if(pending.Count==0)enter(new IntPtr((long)service));
        for(int n=0;n<9;n++) {
            Program.Assert(builds.Count==start+n+1&&builds.Last()==slots[n],"regression pass retains exact team order");
            Complete();dispatch();
        }
        Program.Assert(U32(status+8)==16&&U32(status+0x48)==9&&U32(status+0x4C)==0,"regression pass fully completes");
    }
    private void SelectionLogos(bool reloaded=false,bool selectedHub=false)
    {
        LoadPass();
        Q(manager+0x20,image+0xB36D3D0);Q(manager+0x118,image+0xB38B380);Q(manager+0x128,manager+0x20);
        ulong[] libraries=new ulong[9];
        for(int i=0;i<9;i++){libraries[i]=Obj(0x50);Q(libraries[i],image+0xB36D3E0);Q(manager+0x38+(ulong)i*8,libraries[i]);}
        for(int i=0;i<9;i++) {
            ulong node=BitConverter.ToUInt64(Read(vector+(ulong)i*8,8),0),blobs=Obj(16);
            Marshal.WriteByte(new IntPtr((long)node),0x70,(byte)(slots[i]-1801));
            Q(node+0xD8,blobs);Q(node+0xE0,blobs+16);
            for(int n=0;n<2;n++) {
                ulong blob=Obj(0x30),data=Obj(128);Q(blobs+(ulong)n*8,blob);
                Q(blob,image+0xB27EE08);D(blob+0x14,128);D(blob+0x20,128);Q(blob+0x18,data);
                Write(data,GameProfile.Hex("444453207c000000"));
            }
        }
        Commit(image+0x3186240,16);Write(image+0x3186240,GameProfile.Hex("488bc4554154415641574883ec484c8b"));
        int registrations=0;bool publish=true;
        var entries=new Dictionary<string,ulong>();
        ulong register=Keep(new LogoRegister((registry,group,style,slot,data,length,variant)=> {
            Program.Assert(Address(registry)==manager+0x20&&group==0&&style<9&&slot<99&&variant<2&&length==128,"logo registrar seven-argument ABI");
            registrations++;
            string key=style+":"+slot+":"+variant;
            if(publish&&!entries.ContainsKey(key)){entries.Add(key,Address(data));D(libraries[style]+0x2C,U32(libraries[style]+0x2C)+1);}
            if(registrations==1)dispatch(); // nested dispatch must respect native logo-busy guard
        }));
        Commit(image+0x3052B80,40);
        Write(image+0x3052B80,GameProfile.Hex("48895c240848896c2410488974241848897c242048b8")
            .Concat(BitConverter.GetBytes(register)).Concat(GameProfile.Hex("ffe0")).ToArray());
        // The real selection menu has a current Career and valid cached DDS
        // images, but NO selected school and no new coach event.
        Q(context+0x1D0,0);D(status+0xCC,0);D(status+0x100,0);
        ulong jobs=0,head=0;byte[] savedQueue=null,savedHead=null;
        if(reloaded) {
            Q(context,image+0xBD73D78);
            jobs=Obj(56);head=Obj(0x60);Q(jobs,head);Q(head,image+0xB38CA08);D(head+8,1);
            Q(head+0x18,status);Q(status+0x90,30);Q(head+0x20,19);
            Q(head+0x50,BitConverter.ToUInt64(Read(vector+16,8),0));D(head+0x40,1810);
            for(int i=1;i<7;i++){ulong job=Obj(0x10);Q(job,image+0xB38D050);Q(jobs+(ulong)i*8,job);}
            Q(manager+0x180,jobs);Q(manager+0x1A0,jobs+56);
            D(status+8,12);D(status+0x48,0);D(status+0x84,1);
            savedQueue=Read(jobs,56);savedHead=Read(head,0x60);
        }
        Action<string> excluded=reason=>{
            D(status+LogoRegistryRepair.ConsecutiveAttempts,0);D(status+LogoRegistryRepair.Cooldown,0);
            dispatch();Program.Assert(registrations==0,"logo repair excludes "+reason);
        };
        Marshal.WriteByte(new IntPtr((long)service),0x7B3,1);excluded("stock importer");Marshal.WriteByte(new IntPtr((long)service),0x7B3,0);
        D(service+0x7D8,1);excluded("saved-map processing");D(service+0x7D8,0);
        Q(career+0x1B0,1);excluded("active converter");Q(career+0x1B0,0);
        if(!reloaded){Q(manager+0x1A0,vector+8);excluded("occupied native queue");Q(manager+0x1A0,vector);}
        Q(manager+0x138,vector+8);excluded("changed metadata vector");Q(manager+0x138,vector+72);
        D(status+0x4C,1);excluded("failed asset pass");D(status+0x4C,0);
        if(reloaded) {
            Q(context,image+0xBD73D79);excluded("unrecognized deferred Career context");Q(context,image+0xBD73D78);
            Q(context+0x1D0,selected);D(selected+0xF78,1807);excluded("changed deferred selected school");D(selected+0xF78,1801);Q(context+0x1D0,0);
            Q(head,image+0xB38D050);excluded("non-plugin queue head");Q(head,image+0xB38CA08);
            D(head+8,0);excluded("unstarted queue head");D(head+8,2);excluded("completing queue head");D(head+8,1);
            Q(head+0x18,status+8);excluded("foreign callback owner");Q(head+0x18,status);
            Q(head+0x20,30);excluded("current-epoch queued work");Q(head+0x20,31);excluded("future callback epoch");Q(head+0x20,19);
            Q(head+0x50,Obj(0x20));excluded("retired metadata descriptor");Q(head+0x50,BitConverter.ToUInt64(Read(vector+16,8),0));
            ulong tail=BitConverter.ToUInt64(Read(jobs+8,8),0);D(tail+8,1);excluded("active stock job behind head");D(tail+8,0);
            D(status+LogoRegistryRepair.ConsecutiveAttempts,0);D(status+LogoRegistryRepair.Cooldown,0);
            if(selectedHub)Q(context+0x1D0,selected);
        }
        dispatch();
        for(int i=0;i<200&&registrations==0;i++)dispatch();
        Program.Assert(registrations==162&&U32(status+0x108)==1,"selection menu restores all nine teams and both logo variants without a selected coach");
        int submissions=builds.Count;dispatch();dispatch();
        Program.Assert(registrations==162&&builds.Count==submissions,"populated logos do not rebuild uniforms or repeat registration");
        // A later registry clear must not need another coach event or roster visit.
        entries.Clear();foreach(ulong lib in libraries)D(lib+0x2C,0);dispatch();
        Program.Assert(registrations==324&&U32(status+0x108)==2,"later empty registry repairs without a new coach event");
        dispatch(); // observe populated interval and rearm future repairs
        var original=entries.ToDictionary(e=>e.Key,e=>e.Value);
        foreach(string key in entries.Keys.Where(k=>k.StartsWith("8:")).ToArray())entries.Remove(key);
        D(libraries[8]+0x2C,0);dispatch();
        Program.Assert(registrations==486&&entries.Count==162&&original.All(e=>entries[e.Key]==e.Value),"partial empty library restored while existing image bindings are preserved");
        dispatch();
        // Simulate a native registrar that returns without publishing images.
        // Bounded attempts must not turn every frame into another batch.
        publish=false;entries.Clear();foreach(ulong lib in libraries)D(lib+0x2C,0);
        for(int i=0;i<500;i++)dispatch();
        Program.Assert(registrations==972&&U32(status+LogoRegistryRepair.ConsecutiveAttempts)==3,"unsuccessful logo registration stops after three paced attempts");
        for(int i=0;i<100;i++)dispatch();Program.Assert(registrations==972,"exhausted logo repair does not spin");
        publish=true;Q(status+0x90,BitConverter.ToUInt64(Read(status+0x90,8),0)+1);dispatch();
        Program.Assert(registrations==1134&&entries.Count==162&&builds.Count==submissions,"new epoch rearms logos without another uniform job");
        if(reloaded)Program.Assert(savedQueue.SequenceEqual(Read(jobs,56))&&savedHead.SequenceEqual(Read(head,0x60))&&U32(status+8)==12&&U32(status+0x84)==1,
            "deferred logo repair preserves native queue, old request, unfinished pass and uniform submission count");
    }
    private void Residency()
    {
        LoadPass();loaded=0;Q(manager+0x1D0,records);
        for(int n=0;n<20&&builds.Count==9;n++)dispatch();
        Program.Assert(builds.Count==10&&builds.Last()==slots[0],"week-style record loss restarts without Career notification");
        for(int n=0;n<9;n++){Complete();dispatch();}
        Program.Assert(builds.Count==18&&U32(status+8)==16&&U32(status+0x48)==9,"all teams return after residency invalidation");
    }
    private void Coach()
    {
        LoadPass();dispatch();dispatch();
        Program.Assert(U32(status+0xE0)==1,"coach monitor arms only after completed pass");
        D(selected+0xF78,1807);Q(manager+0x1A0,vector+8);
        for(int n=0;n<6;n++)dispatch();
        Program.Assert(builds.Count==9&&U32(status+4)==0,"coach refresh waits for stock native queue ownership");
        Q(manager+0x1A0,vector);loaded=0;Q(manager+0x1D0,records);
        for(int n=0;n<20&&builds.Count==9;n++)dispatch();
        Program.Assert(builds.Count==10&&builds.Last()==slots[0],"coach refresh resumes after native queue drains");
        for(int n=0;n<9;n++){Complete();dispatch();}
        Program.Assert(builds.Count==18&&U32(status+8)==16&&U32(status+0x48)==9,"coach refresh restores every team, not just selected school");
    }
    private void Recovery()
    {
        enter(new IntPtr((long)service));byte[] old=pending.Dequeue();Q(manager+0x1A0,vector);
        for(int n=0;n<20&&builds.Count==1;n++)dispatch();
        Program.Assert(builds.Count==2&&builds[0]==builds[1]&&U32(status+0xA4)==1,"existing empty-queue cancellation recovery retries exact interrupted slot");
        uint count=U32(status+0x48);Deliver(old);
        Program.Assert(U32(status+4)==1&&U32(status+0x48)==count&&U32(status+0x98)==1,"late canceled callback cannot complete replacement job");
        for(int n=0;n<9;n++){Complete();dispatch();}
        Program.Assert(builds.Count==10&&U32(status+8)==16&&U32(status+0x48)==9,"recovered pass retains all team completions");
    }
    private TeamBuilderReentryRepair Repair()
    {
        var repair=(TeamBuilderReentryRepair)FormatterServices.GetUninitializedObject(typeof(TeamBuilderReentryRepair));
        Set(repair,"_base",image);Set(repair,"_remoteBase",remote);Set(repair,"_status",status);
        Set(repair,"_processHandle",GetCurrentProcess());Set(repair,"_log",new Action<string>(s=>{}));return repair;
    }
    private static void Set(object obj,string field,object value){obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);}
    private static void Invoke(object obj,string method){obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,null);}
    private void Installer()
    {
        var p=GameProfile.Current;var repair=Repair();
        foreach(var h in p.validations.Concat(p.hooks)){ulong at=image+(ulong)p.Rva(h.target);byte[] expected=GameProfile.Hex(h.expected);Commit(at,expected.Length);Write(at,expected);}
        foreach(var bad in p.validations.Concat(p.hooks)) {
            ulong at=image+(ulong)p.Rva(bad.target);byte value=Read(at,1)[0];Marshal.WriteByte(new IntPtr((long)at),(byte)(value^1));
            Program.Reject(()=>Invoke(repair,"InstallNewsPreview"),"private installer signature "+bad.name);
            foreach(var h in p.hooks)if(h!=bad)Program.Assert(Read(image+(ulong)p.Rva(h.target),GameProfile.Hex(h.expected).Length).SequenceEqual(GameProfile.Hex(h.expected)),"no partial hook before all signatures pass");
            Marshal.WriteByte(new IntPtr((long)at),value);
        }
        Invoke(repair,"InstallNewsPreview");
        foreach(var h in p.hooks)Program.Assert(Read(image+(ulong)p.Rva(h.target),2).SequenceEqual(new byte[]{0xFF,0x25}),"news/audio hook installed "+h.name);
        ulong changed=image+(ulong)p.Rva(p.hooks[0].target);Marshal.WriteByte(new IntPtr((long)changed),0x90);
        Invoke(repair,"RestoreNewsPreview");
        Program.Assert(Read(changed,1)[0]==0x90,"cleanup preserves another owner's hook");
        foreach(var h in p.hooks.Skip(1))Program.Assert(Read(image+(ulong)p.Rva(h.target),GameProfile.Hex(h.expected).Length).SequenceEqual(GameProfile.Hex(h.expected)),"owned hook restored "+h.name);
        Invoke(repair,"RestoreNewsPreview");
        Set(repair,"_installed",true);Set(repair,"_careerRelay",remote);Set(repair,"_originalCareer",remote+0x10000);
        Program.Assert(repair.OwnsMainChain(remote,remote+0x10000),"exact own chained hook recognized");
        Program.Assert(!repair.OwnsMainChain(remote+1,remote+0x10000),"foreign owner rejected");
        byte saved=Read(remote,1)[0];Marshal.WriteByte(new IntPtr((long)remote),(byte)(saved^1));
        Program.Assert(!repair.OwnsMainChain(remote,remote+0x10000),"changed own code rejected");Marshal.WriteByte(new IntPtr((long)remote),saved);
        D(status+0x48,9);byte[] before=Read(remote+0x4000,0x7000);var health=repair.CaptureHealth();
        Program.Assert(Convert.ToUInt32(health["teamsCompleted"])==9&&before.SequenceEqual(Read(remote+0x4000,0x7000)),"health snapshot is read-only");
        WatchdogAndCleanup(repair);
    }
    private void WatchdogAndCleanup(TeamBuilderReentryRepair repair)
    {
        var p=GameProfile.Current;var core=typeof(CFB27TeamBuilderBatchQueuePreMcrProbe);
        ulong table=image+(ulong)p.Rva("ServiceVtable"),global=image+(ulong)p.Rva("ServiceGlobal");
        Commit(table,0x1000);Commit(global,8);Q(global,service);
        ulong careerSlot=table+0x4D8,keySlot=table+0x870;
        Q(careerSlot,remote);Q(keySlot,remote+0x18000);
        object patch=Activator.CreateInstance(core.GetNestedType("DurablePairRepairPatch",BindingFlags.NonPublic),true);
        Action<string,long> field=(name,value)=>patch.GetType().GetField(name).SetValue(patch,value);
        field("SingletonGlobalRva",p.Rva("ServiceGlobal"));field("CareerStubAddress",(long)remote+0x10000);
        field("AssetKeyStubAddress",(long)remote+0x18000);field("OriginalCareerTarget",(long)image+p.Rva("CareerHub"));
        field("OriginalAssetKeyTarget",(long)image+p.Rva("AssetKey"));field("StatusAddress",(long)remote+0x16000);
        var method=core.GetMethod("DurablePairWatchdogTick",BindingFlags.Static|BindingFlags.NonPublic);
        Func<string> tick=()=> (string)method.Invoke(null,new object[]{GetCurrentProcess(),(long)image,0x10000000,patch,new Action<string>(s=>{})});
        try {
            CFB27TeamBuilderBatchQueuePreMcrProbe.IsOwnedAssetChain=(handle,owner,main)=>repair.OwnsMainChain(owner,main);
            Program.Assert(tick()=="ARMED"&&BitConverter.ToUInt64(Read(careerSlot,8),0)==remote,"main watchdog accepts exact owned chain without overwrite");
            Q(careerSlot,remote+1);Program.Assert(tick().StartsWith("UNKNOWN_CAREER_TARGET"),"main watchdog rejects foreign chain");
            Q(careerSlot,remote);byte saved=Read(remote,1)[0];Marshal.WriteByte(new IntPtr((long)remote),(byte)(saved^1));
            Program.Assert(tick().StartsWith("UNKNOWN_CAREER_TARGET"),"main watchdog rejects modified chain code");Marshal.WriteByte(new IntPtr((long)remote),saved);
            Set(repair,"_vtable",table);Set(repair,"_careerSlotAddress",careerSlot);Set(repair,"_careerPatched",true);
            repair.SafeUninstall();
            Program.Assert(BitConverter.ToUInt64(Read(careerSlot,8),0)==remote+0x10000,"asset cleanup restores main engine before main cleanup");
            object[] args={GetCurrentProcess(),patch,0};
            bool ok=(bool)core.GetMethod("RestoreDurablePairRepair",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
            Program.Assert(ok&&BitConverter.ToUInt64(Read(careerSlot,8),0)==image+(ulong)p.Rva("CareerHub")&&BitConverter.ToUInt64(Read(keySlot,8),0)==image+(ulong)p.Rva("AssetKey"),"ordered cleanup restores both stock slots");
        } finally { CFB27TeamBuilderBatchQueuePreMcrProbe.IsOwnedAssetChain=null; }
    }
    internal static void CheckAll()
    {
        using(var fixture=new NativeFixture())fixture.Sequence();
        using(var fixture=new NativeFixture())fixture.Admission();
        using(var fixture=new NativeFixture())fixture.EmptyQueue();
        foreach(bool initial in new[]{false,true})
            foreach(string change in new[]{"selected pointer","Career owner","selected slot","converter","vector end","vector begin","cursor alignment"})
                using(var fixture=new NativeFixture())fixture.AdmissionRace(initial,change);
        using(var fixture=new NativeFixture())fixture.Residency();
        using(var fixture=new NativeFixture())fixture.SelectionLogos();
        using(var fixture=new NativeFixture())fixture.SelectionLogos(true);
        using(var fixture=new NativeFixture())fixture.SelectionLogos(true,true);
        using(var fixture=new NativeFixture())fixture.Coach();
        using(var fixture=new NativeFixture())fixture.Recovery();
        using(var fixture=new NativeFixture())fixture.Installer();
    }
    public void Dispose(){foreach(IntPtr p in objects)Marshal.FreeHGlobal(p);VirtualFree(new IntPtr((long)remote),UIntPtr.Zero,0x8000);VirtualFree(new IntPtr((long)image),UIntPtr.Zero,0x8000);GC.KeepAlive(delegates);}
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr VirtualAlloc(IntPtr at,UIntPtr size,uint type,uint protect);
    [DllImport("kernel32.dll")] private static extern bool VirtualFree(IntPtr at,UIntPtr size,uint type);
}
