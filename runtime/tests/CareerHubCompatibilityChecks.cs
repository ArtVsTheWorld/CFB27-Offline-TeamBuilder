using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using OfflineTeamBuilderRuntime;

internal static class CareerHubCompatibilityChecks
{
    private static void Check(bool ok,string name)=>Program.Assert(ok,"shared hub: "+name);
    private sealed class Fixture
    {
        internal readonly GameProfile Profile=GameProfile.Current;
        internal const ulong Image=0x140000000, Partner=0x70000000;
        internal readonly Dictionary<ulong,byte> Bytes=new Dictionary<ulong,byte>();
        internal readonly CareerHubCompatibility.Module Module;
        internal readonly CareerHubCompatibility.Layout Layout;
        internal ulong Slot=>Image+(ulong)Profile.Rva("ServiceVtable")+0x4D8;
        internal ulong Stock=>Image+(ulong)Profile.Rva("CareerHub");
        internal Fixture(CareerHubCompatibility.Layout layout=null) {
            Layout=layout??CareerHubCompatibility.Legacy;Module=new CareerHubCompatibility.Module {Base=Partner,Hash=Layout.Hash};
            foreach(var h in Profile.hooks.Concat(Profile.validations).Concat(Profile.mainPrefixes))Put(Image+(ulong)Profile.Rva(h.target),GameProfile.Hex(h.expected));
            Put(Image+(ulong)Profile.Rva("Dispatcher"),GameProfile.Hex("48895c2408488974241048897c241855"));
            foreach(var s in Profile.lifecycle)Q(Image+(ulong)Profile.Rva("ServiceVtable")+(ulong)s.slot,Image+(ulong)Profile.Rva(s.target));
            Put(Partner+Layout.CallbackRva,GameProfile.Hex(Layout.CallbackHex));
            Put(Partner+Layout.InstalledRva,BitConverter.GetBytes(1));
            Q(Partner+Layout.SlotCellRva,Slot);Q(Partner+Layout.NextCellRva,Stock);
        }
        internal void Put(ulong at,byte[] b){for(int n=0;n<b.Length;n++)Bytes[at+(ulong)n]=b[n];}
        internal void Q(ulong at,ulong value)=>Put(at,BitConverter.GetBytes(value));
        internal byte[] Read(ulong at,int size) {
            byte[] b=new byte[size];for(int n=0;n<size;n++)if(!Bytes.TryGetValue(at+(ulong)n,out b[n]))return null;return b;
        }
        internal CareerHubCompatibility.Contract Capture(bool requested=true)=>CareerHubCompatibility.Inspect(Read,Image,Profile,Module,requested);
        internal void InstallPartner()=>Q(Slot,Partner+Layout.CallbackRva);
    }
    internal static void CheckAll()
    {
        var f=new Fixture();
        var alone=CareerHubCompatibility.Inspect(f.Read,Fixture.Image,f.Profile,null,false);
        Check(alone.Ready&&alone.StorageSlot==f.Slot&&alone.Target==f.Stock,"ordinary installation keeps stock slot");
        var waiting=CareerHubCompatibility.Inspect(f.Read,Fixture.Image,f.Profile,null,true);
        Check(!waiting.Ready&&waiting.Waiting,"TeamBuilder-first startup waits for requested native partner");
        Check(!f.Capture().Ready&&f.Capture().Waiting,"loaded partner must finish hook installation first");
        f.InstallPartner();
        var before=f.Bytes.ToArray();var c=f.Capture();
        Check(c.Ready&&c.StorageSlot==Fixture.Partner+CareerHubCompatibility.NextCellRva&&c.Target==f.Stock,"Midgame-first routes TeamBuilder behind outer observer");
        Func<long,int,byte[]> canonical=(r,n)=>r==f.Profile.Rva("ServiceVtable")+0x4D8&&n==8?BitConverter.GetBytes(c.Target):f.Read(Fixture.Image+(ulong)r,n);
        Check(f.Profile.CheckNative(0x1200A000,Fixture.Image,canonical).Count==0,"known shared hook passes original stock validation through logical cell");
        ulong unrelated=Fixture.Image+(ulong)f.Profile.Rva("ServiceVtable")+0x870;ulong saved=BitConverter.ToUInt64(f.Read(unrelated,8),0);
        f.Q(unrelated,0x60000000);Check(f.Profile.CheckNative(0x1200A000,Fixture.Image,canonical).Count!=0,"shared hub does not relax another native vtable role");f.Q(unrelated,saved);
        Check(before.All(p=>f.Bytes[p.Key]==p.Value),"inspection is read only");
        f.Put(Fixture.Partner+CareerHubCompatibility.InstalledRva,BitConverter.GetBytes(0));
        Check(!f.Capture().Ready&&f.Capture().Waiting,"partially published native hook is not used");
        f.Put(Fixture.Partner+CareerHubCompatibility.InstalledRva,BitConverter.GetBytes(1));
        byte[] code=GameProfile.Hex(CareerHubCompatibility.CallbackHex);
        for(int n=0;n<code.Length;n++) {
            ulong at=Fixture.Partner+CareerHubCompatibility.CallbackRva+(ulong)n;f.Bytes[at]^=1;
            Check(!f.Capture().Ready,"changed callback byte "+n+" rejected");f.Bytes[at]^=1;
        }
        f.Module.Hash="unknown";Check(!f.Capture().Ready,"unsupported partner identity rejected");f.Module.Hash=CareerHubCompatibility.SupportedHash;
        f.Q(Fixture.Partner+CareerHubCompatibility.SlotCellRva,f.Slot+8);Check(!f.Capture().Ready,"wrong native vtable role rejected");
        f.Q(Fixture.Partner+CareerHubCompatibility.SlotCellRva,f.Slot);
        foreach(ulong next in new[]{0UL,1UL,ulong.MaxValue,Fixture.Partner+CareerHubCompatibility.CallbackRva,Fixture.Partner+CareerHubCompatibility.NextCellRva}) {
            f.Q(Fixture.Partner+CareerHubCompatibility.NextCellRva,next);Check(!f.Capture().Ready,"invalid or cyclic next target "+next);
        }
        f.Q(Fixture.Partner+CareerHubCompatibility.NextCellRva,0x60000000);
        c=f.Capture();Check(c.Ready,"resolver reports pointer without claiming it is a stock target");
        Check(f.Profile.CheckNative(0x1200A000,Fixture.Image,(r,n)=>r==f.Profile.Rva("ServiceVtable")+0x4D8&&n==8?BitConverter.GetBytes(c.Target):f.Read(Fixture.Image+(ulong)r,n)).Count!=0,
            "normal profile validation still rejects an unknown chained target");
        f.Q(Fixture.Partner+CareerHubCompatibility.NextCellRva,f.Stock);
        int seen=0;
        var changed=CareerHubCompatibility.Inspect((at,n)=> {
            byte[] result=f.Read(at,n);
            if(at==Fixture.Partner+CareerHubCompatibility.NextCellRva&&++seen==1)f.Q(at,0x60000000);
            return result;
        },Fixture.Image,f.Profile,f.Module,true);
        Check(!changed.Ready&&changed.Waiting,"chain mutation during validation rejected");
        string temp=Path.Combine(Path.GetTempPath(),"tb-shared-hub-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        Check(Path.GetFullPath(temp).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(temp).StartsWith("tb-shared-hub-",StringComparison.Ordinal),"private cleanup target remains inside temp root");
        try {
            string cfg=Path.Combine(temp,"plugin.cfg");
            Check(CareerHubCompatibility.ReadFlag(cfg,"plugin_enabled",true),"missing config uses enabled launcher default");
            File.WriteAllText(cfg,"; plugin_enabled=1\nplugin_enabled=0\nplugin_enabled = 1 ; last value wins\n");
            Check(CareerHubCompatibility.ReadFlag(cfg,"plugin_enabled",false),"launcher comments and last value preserved");
            File.WriteAllText(cfg,"plugin_enabled=0\n");Check(!CareerHubCompatibility.ReadFlag(cfg,"plugin_enabled",true),"disabled launcher does not block TeamBuilder");
            string peer=Path.Combine(temp,"MidgameSave");Directory.CreateDirectory(peer);
            string status=Path.Combine(peer,"midgamesave-status.json");
            DateTime started=new DateTime(2026,11,10,20,0,0,DateTimeKind.Utc);
            Func<string,string> json=state=>"{\"source\":\"plugin\",\"pid\":42,\"state\":\""+state+"\",\"updated\":\"2026-11-10T20:00:01Z\"}";
            foreach(string terminal in new[]{"standing_aside","disabled"}) {
                File.WriteAllText(status,json(terminal));Check(CareerHubCompatibility.LauncherDeclined(temp,42,started)==terminal,"intentional launcher no-load "+terminal);
            }
            foreach(string nonterminal in new[]{"failed","error","injected","loading","waiting"}) {
                File.WriteAllText(status,json(nonterminal));Check(CareerHubCompatibility.LauncherDeclined(temp,42,started)==null,"no early fallback on possible late injection "+nonterminal);
            }
            File.WriteAllText(status,json("standing_aside"));
            Check(CareerHubCompatibility.LauncherDeclined(temp,43,started)==null,"old process's launcher status ignored");
            Check(CareerHubCompatibility.LauncherDeclined(temp,42,started.AddMinutes(1))==null,"PID reuse cannot borrow old no-load status");
            File.WriteAllText(status,json("standing_aside").Replace("\"plugin\"","\"dll\""));
            Check(CareerHubCompatibility.LauncherDeclined(temp,42,started)==null,"native failure does not masquerade as intentional launcher skip");
        }finally{Directory.Delete(temp,true);}
        LayoutChecks(CareerHubCompatibility.Legacy);LayoutChecks(CareerHubCompatibility.Version024);
        NativeChain(false,CareerHubCompatibility.Legacy);NativeChain(true,CareerHubCompatibility.Legacy);
        NativeChain(false,CareerHubCompatibility.Version024);NativeChain(true,CareerHubCompatibility.Version024);
    }

    private static void LayoutChecks(CareerHubCompatibility.Layout layout)
    {
        var f=new Fixture(layout);f.InstallPartner();
        var c=f.Capture();
        Check(c.Ready&&c.StorageSlot==Fixture.Partner+layout.NextCellRva&&c.Reason.Contains(layout.Version),"versioned next-call contract "+layout.Version);
        Check(CareerHubCompatibility.LayoutFor(layout.Hash.ToUpperInvariant())==layout,"case-insensitive supported package identity");
        foreach(int n in Enumerable.Range(0,GameProfile.Hex(layout.CallbackHex).Length)) {
            ulong at=Fixture.Partner+layout.CallbackRva+(ulong)n;byte saved=f.Bytes[at];f.Bytes[at]^=1;
            Check(!f.Capture().Ready,"reject callback mutation "+layout.Version+" byte "+n);f.Bytes[at]=saved;
        }
        f.Put(Fixture.Partner+layout.InstalledRva,BitConverter.GetBytes(0));
        Check(!f.Capture().Ready&&f.Capture().Waiting,"wait for complete versioned publication "+layout.Version);
        f.Put(Fixture.Partner+layout.InstalledRva,BitConverter.GetBytes(1));
        f.Q(Fixture.Partner+layout.SlotCellRva,f.Slot+8);Check(!f.Capture().Ready,"reject wrong physical slot metadata "+layout.Version);
        f.Q(Fixture.Partner+layout.SlotCellRva,f.Slot);
        foreach(ulong invalid in new[]{0UL,Fixture.Partner+layout.CallbackRva,Fixture.Partner+layout.NextCellRva,0x7FFFFFFFFFFFUL}) {
            f.Q(Fixture.Partner+layout.NextCellRva,invalid);Check(!f.Capture().Ready,"reject cyclic or invalid next pointer "+layout.Version);
        }
        f.Q(Fixture.Partner+layout.NextCellRva,f.Stock);
        f.Module.Hash=new string('0',64);Check(!f.Capture().Ready,"reject unknown package identity");
        f.Module.Hash=layout.Hash;
        int reads=0;
        c=CareerHubCompatibility.Inspect((at,n)=> {
            byte[] result=f.Read(at,n);
            if(at==Fixture.Partner+layout.SlotCellRva&&++reads==1)f.Q(at,f.Slot+8);
            return result;
        },Fixture.Image,f.Profile,f.Module,true);
        Check(!c.Ready&&c.Waiting,"reject changing slot metadata "+layout.Version);
    }

    // Execute the EXACT verified MGS hub callback in private allocations. Its
    // event handle stays zero, so no import or external code is called. Tiny
    // TeamBuilder stand-ins deliberately call stock twice, verifying MGS is
    // notified once per real outer entry and is retained through both cleanups.
    private static void NativeChain(bool teamBuilderStartsFirst,CareerHubCompatibility.Layout layout)
    {
        var allocations=new List<IntPtr>();
        Func<ulong,uint,ulong> allocate=(size,protection)=> {
            IntPtr p=VirtualAlloc(IntPtr.Zero,new UIntPtr(size),0x3000,protection);
            if(p==IntPtr.Zero)throw new Exception("Private native allocation failed");allocations.Add(p);return unchecked((ulong)p.ToInt64());
        };
        try {
            ulong module=allocate(0x100000,0x04),image=allocate(0x1000,0x04);
            // Use a copy of the profile with just the vtable location relocated
            // into our small private image; production resource stays immutable.
            GameProfile profile=GameProfile.Current;
            long realVtable=profile.Rva("ServiceVtable");
            ulong pretendImage=unchecked(image-(ulong)realVtable),slot=image+0x4D8;
            ulong stock=allocate(0x1000,0x04),main=allocate(0x1000,0x04),assets=allocate(0x1000,0x04),counts=allocate(0x1000,0x04);
            Action<ulong,byte[]> put=(at,b)=>Marshal.Copy(b,0,new IntPtr(unchecked((long)at)),b.Length);
            Action<ulong,ulong> q=(at,value)=>put(at,BitConverter.GetBytes(value));
            Func<ulong,ulong> get=at=>unchecked((ulong)Marshal.ReadInt64(new IntPtr(unchecked((long)at))));
            Func<ulong,int,byte[]> read=(at,n)=>{byte[] b=new byte[n];Marshal.Copy(new IntPtr(unchecked((long)at)),b,0,n);return b;};
            Func<ulong,int,uint> count=(at,off)=>unchecked((uint)Marshal.ReadInt32(new IntPtr(unchecked((long)at)),off));
            Func<ulong,byte[]> inc=at=>GameProfile.Hex("48b8").Concat(BitConverter.GetBytes(at)).Concat(GameProfile.Hex("f0ff00")).ToArray();
            put(stock,GameProfile.Hex("48b8").Concat(BitConverter.GetBytes(counts+0x20)).Concat(GameProfile.Hex("488908")).Concat(inc(counts)).Concat(new byte[]{0xc3}).ToArray());
            Func<ulong,ulong,int,byte[]> caller=(counter,target,times)=> {
                var b=new List<byte>(GameProfile.Hex("4883ec28"));b.AddRange(inc(counter));
                for(int i=0;i<times;i++){b.AddRange(GameProfile.Hex("48b8"));b.AddRange(BitConverter.GetBytes(target));b.AddRange(GameProfile.Hex("ffd0"));}
                b.AddRange(GameProfile.Hex("4883c428c3"));return b.ToArray();
            };
            put(main,caller(counts+4,stock,2));put(assets,caller(counts+8,main,1));
            put(module+layout.CallbackRva,GameProfile.Hex(layout.CallbackHex));
            foreach(ulong executable in new[]{stock,main,assets,module+layout.CallbackRva}) {
                uint old;ulong page=executable&~0xFFFUL;
                Check(VirtualProtect(new IntPtr(unchecked((long)page)),new UIntPtr(executable==module+layout.CallbackRva?0x2000U:0x1000U),0x20,out old),"private callback page executable");
            }
            q(slot,pretendImage+(ulong)profile.Rva("CareerHub"));
            var partner=new CareerHubCompatibility.Module {Base=module,Hash=layout.Hash};
            if(teamBuilderStartsFirst) {
                var pending=CareerHubCompatibility.Inspect(read,pretendImage,profile,null,true);
                Check(!pending.Ready&&pending.Waiting,"native TeamBuilder-first path does not replace physical slot early");
            }
            q(module+layout.NextCellRva,stock);q(module+layout.SlotCellRva,slot);
            put(module+layout.InstalledRva,BitConverter.GetBytes(1));q(slot,module+layout.CallbackRva);
            var contract=CareerHubCompatibility.Inspect(read,pretendImage,profile,partner,true);
            Check(contract.Ready&&contract.StorageSlot==module+layout.NextCellRva,"native shared storage resolved");
            CareerHubCompatibility.BeginSession(System.Diagnostics.Process.GetCurrentProcess().Id,partner);
            Check(CareerHubCompatibility.Storage(GetCurrentProcess(),pretendImage,slot)==contract.StorageSlot,"actual checked process reader selects partner cell");
            q(contract.StorageSlot,main);q(contract.StorageSlot,assets);
            Check(CareerHubCompatibility.Storage(GetCurrentProcess(),pretendImage,slot)==contract.StorageSlot,"running watchdog resolves same logical asset slot");
            var invoke=(Hub)Marshal.GetDelegateForFunctionPointer(new IntPtr(unchecked((long)get(slot))),typeof(Hub));
            const ulong service=0x1122334455667788;invoke(service);
            Check(count(module,checked((int)layout.CounterRva))==1&&count(counts,0)==2&&count(counts,4)==1&&count(counts,8)==1,"one MGS notification covers asset/main/stock double call");
            Check(get(counts+0x20)==service,"native service argument survives every layer");
            q(contract.StorageSlot,main);invoke(service);
            Check(count(module,checked((int)layout.CounterRva))==2&&count(counts,0)==4&&count(counts,4)==2&&count(counts,8)==1,"asset cleanup keeps MGS and main lifecycle");
            q(contract.StorageSlot,stock);invoke(service);
            Check(count(module,checked((int)layout.CounterRva))==3&&count(counts,0)==5&&count(counts,4)==2,"main cleanup keeps Midgame Save and stock");
            Check(get(slot)==module+layout.CallbackRva,"neither layer takes away Midgame Save physical ownership");
        }finally{CareerHubCompatibility.BeginSession(0,null);foreach(IntPtr p in allocations)VirtualFree(p,UIntPtr.Zero,0x8000);}
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate ulong Hub(ulong service);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern IntPtr VirtualAlloc(IntPtr address,UIntPtr size,uint allocation,uint protection);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool VirtualFree(IntPtr address,UIntPtr size,uint type);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool VirtualProtect(IntPtr address,UIntPtr size,uint protection,out uint previous);
    [DllImport("kernel32.dll")]private static extern IntPtr GetCurrentProcess();
}
