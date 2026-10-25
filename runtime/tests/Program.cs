using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using OfflineTeamBuilderRuntime;
using OfflineTeamBuilder.Repair;

internal static class Program
{
    internal static int Checks;
    internal static void Assert(bool ok,string name) { if(!ok)throw new Exception("FAILED: "+name);Checks++; }
    internal static void Reject(Action action,string name)
    { try { action(); } catch { Checks++;return; } throw new Exception("FAILED to reject: "+name); }
    private static int Main()
    {
        try {
            Metadata();Profile();TransportStartup();Golden();AdmissionContracts();Store();Diagnostics();ReadinessChecks.CheckAll();NativeFixture.CheckAll();
            Console.WriteLine("PASS "+Checks+" checks. Private allocations/temporary files only; no game process opened.");return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
    private static void Metadata()
    {
        var assembly=typeof(RuntimeAction).Assembly;
        Assert(assembly.GetName().Name=="OfflineTeamBuilderRuntime","single assembly identity");
        Assert(assembly.GetName().Version.ToString()==PluginIdentity.AssemblyVersion,"assembly version");
        var attrs=assembly.GetCustomAttributesData();
        Assert(attrs.Count(a=>a.AttributeType.Name=="RegisterExecutionAction")==1,"one registered MMC action");
        Assert((string)attrs.Single(a=>a.AttributeType.Name=="PluginAuthorAttribute").ConstructorArguments[0].Value==PluginIdentity.Authors,"author credit");
        Assert((string)attrs.Single(a=>a.AttributeType.Name=="PluginVersionAttribute").ConstructorArguments[0].Value==PluginIdentity.Version,"MMC version");
        Assert(!assembly.GetReferencedAssemblies().Any(a=>a.Name=="OfflineTeamBuilder"||a.Name=="OfflineTeamBuilderReentry"),"no old DLL reference");
        Assert(assembly.GetManifestResourceNames().All(n=>!n.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)),"no hidden legacy DLL payload");
        Assert(RuntimeAction.FindLegacyNames(new[]{"OfflineTeamBuilder","offlineteambuilderreentry","Unrelated"}).Length==2,"duplicate legacy detection");
        Assert(RuntimeAction.FindLegacyNames(new[]{"OfflineTeamBuilderRuntime","Unrelated"}).Length==0,"unrelated plugins allowed");
    }
    private static Dictionary<long,byte> Image(GameProfile profile,ulong image)
    {
        var bytes=new Dictionary<long,byte>();
        Action<long,byte[]> put=(at,data)=> { for(int i=0;i<data.Length;i++)bytes[at+i]=data[i]; };
        foreach(var h in profile.hooks.Concat(profile.validations).Concat(profile.mainPrefixes))put(profile.Rva(h.target),GameProfile.Hex(h.expected));
        put(profile.Rva("Dispatcher"),GameProfile.Hex("48895c2408488974241048897c241855"));
        foreach(var slot in profile.lifecycle)put(profile.Rva("ServiceVtable")+slot.slot,BitConverter.GetBytes(image+(ulong)profile.Rva(slot.target)));
        return bytes;
    }
    private static void Profile()
    {
        var p=GameProfile.Current;const ulong image=0x140000000;
        var bytes=Image(p,image);
        Func<long,int,byte[]> read=(at,n)=>Enumerable.Range(0,n).Select(i=>bytes.ContainsKey(at+i)?bytes[at+i]:(byte)0).ToArray();
        foreach(long size in new[]{0x11FFE000L,0x11FFF000L,0x12000000L})Assert(p.CheckNative(size,image,read).Count==0,"signature-compatible image size "+size);
        bytes[0x2000]=0xCC;Assert(p.CheckNative(0x11FFE000,image,read).Count==0,"unrelated executable bytes do not gate compatibility");
        Assert(p.CheckNative(0x1000,image,read).Count!=0,"bounds safety");
        foreach(var check in p.hooks.Concat(p.validations).Concat(p.mainPrefixes)) {
            long at=p.Rva(check.target);byte saved=bytes[at];bytes[at]^=1;
            Assert(p.CheckNative(0x11FFE000,image,read).Count!=0,"reject native signature "+(check.name??check.target));bytes[at]=saved;
        }
        foreach(var slot in p.lifecycle) {
            long at=p.Rva("ServiceVtable")+slot.slot;byte saved=bytes[at];bytes[at]^=1;
            Assert(p.CheckNative(0x11FFE000,image,read).Count!=0,"reject vtable role "+slot.target);bytes[at]=saved;
        }
        Assert(p.CheckNative(0x11FFE000,image,(at,n)=>null).Count>0,"unreadable native memory");
        var json=new JavaScriptSerializer {MaxJsonLength=1024*1024};
        var bad=json.Deserialize<GameProfile>(json.Serialize(p));bad.templates["News"].relocations[0].offset=int.MaxValue;
        Reject(()=>bad.Validate(),"relocation bounds");
        bad=json.Deserialize<GameProfile>(json.Serialize(p));bad.symbols["ServiceGlobal"]="0x70000000";
        Reject(()=>bad.Validate(),"symbol bounds");
        bad=json.Deserialize<GameProfile>(json.Serialize(p));bad.transport[0].expected="not-hex";
        Reject(()=>bad.Validate(),"invalid transport hex rejected before startup");
        bad=json.Deserialize<GameProfile>(json.Serialize(p));bad.transport[0].rva="0x70000000";
        Reject(()=>bad.Validate(),"transport RVA bounds");
        bad=json.Deserialize<GameProfile>(json.Serialize(p));bad.transport[0].kind="unsupported";
        Reject(()=>bad.Validate(),"transport kind validation");
    }
    private sealed class GoldenCase { public string type=null; public string method=null; public string[] arguments=null; public string sha256=null; public int length=0; }
    private static void TransportStartup()
    {
        // Exercise the actual managed startup constructor, not just its native
        // emitter: 2.0.0 overflowed here when the compact profile reached Hex().
        var type=typeof(CFB27TeamBuilderBatchQueuePreMcrProbe).GetNestedType("HookSpec",BindingFlags.NonPublic);
        foreach(var spec in GameProfile.Current.transport) {
            object hook=Activator.CreateInstance(type,new object[]{spec.name,0L,"90","fixture"});
            Assert(((byte[])type.GetField("Expected").GetValue(hook)).SequenceEqual(GameProfile.Hex(spec.expected)),"startup compact transport signature "+spec.name);
            Assert((long)type.GetField("Rva").GetValue(hook)==GameProfile.Parse(spec.rva),"startup transport RVA "+spec.name);
            Assert((string)type.GetField("Kind").GetValue(hook)==spec.kind,"startup transport kind "+spec.name);
            Assert((int)type.GetField("PatchLen").GetValue(hook)==GameProfile.Hex(spec.expected).Length,"startup transport patch size "+spec.name);
        }
        object fallback=Activator.CreateInstance(type,new object[]{"private-fixture",42L,"48 85 F6","fixture"});
        Assert(((byte[])type.GetField("Expected").GetValue(fallback)).SequenceEqual(new byte[]{0x48,0x85,0xF6}),"spaced transport signature still supported");
    }
    private static void Golden()
    {
        var rows=new JavaScriptSerializer().Deserialize<GoldenCase[]>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"golden-relays.json")));
        foreach(var row in rows) {
            var method=typeof(RuntimeAction).Assembly.GetType(row.type).GetMethod(row.method,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static);
            var ps=method.GetParameters();object[] args=new object[ps.Length];
            for(int n=0;n<args.Length;n++) args[n]=row.arguments[n].StartsWith("bytes:")?(object)GameProfile.Hex(row.arguments[n].Substring(6)):Convert.ChangeType(GameProfile.Parse(row.arguments[n]),ps[n].ParameterType);
            byte[] code=(byte[])method.Invoke(null,args);
            if(row.method=="BuildCareerRelay"||row.method=="BuildDispatcherRelay") {
                // The historical v24 template is still immutable. Admission
                // adds a bounded stub, tested by executing the real emitted
                // code below; it does not rewrite news/audio or the importer.
                string template=row.method=="BuildCareerRelay"?"Career":"Dispatcher";
                Assert(code.Length>row.length&&code.Length<0x2000,"bounded admission stub "+row.method);
                code=GameProfile.Current.templates[template].Emit(GameProfile.Current,args.Cast<ulong>().ToArray());
            }
            using(var sha=SHA256.Create())Assert(code.Length==row.length&&BitConverter.ToString(sha.ComputeHash(code)).Replace("-","").ToLowerInvariant()==row.sha256,"v24 byte-exact native generator "+row.method);
        }
    }
    private static void Store()
    {
        string root=Path.Combine(Path.GetTempPath(),"tbr-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            var options=new RepairOptions {StoreDirectory=root};options.Validate();
            using(var store=new LocalAssetStore(options)) {
                var metadata=new MetadataRecord {Id="123-TeamBuilderLogo1Data-456",Kind="Logo1Data",Group="123-456"};
                string staged=store.NewStagingFile();File.WriteAllBytes(staged,Encoding.ASCII.GetBytes("private mock asset data"));
                bool replay,revised;store.Commit(metadata,staged,"/private-fixture","fixture",out replay,out revised);
                Assert(!replay&&!revised,"store first revision");
                var record=store.Get(metadata.Id);using(var stream=store.OpenVerified(record))Assert(stream.Length==23,"verified asset readback");
                staged=store.NewStagingFile();File.WriteAllBytes(staged,Encoding.ASCII.GetBytes("private mock asset data"));
                store.Commit(metadata,staged,"/private-fixture","fixture",out replay,out revised);Assert(replay&&!revised,"idempotent replay");
                File.WriteAllBytes(Path.Combine(root,"objects",record.Sha256+".blob"),new byte[23]);
                Reject(()=>store.OpenVerified(record),"corrupt cache content is not served");
            }
            string safe=ReloadDiagnostics.SafeUrl("http://127.0.0.1/api/test?access_token=secret-value");
            Assert(!safe.Contains("secret-value"),"sensitive URL redaction");
        } finally { Directory.Delete(root,true); }
    }
    private static void AdmissionContracts()
    {
        var p=GameProfile.Current;const ulong image=0x140000000,remote=0x200000000,status=remote+0x4000;
        foreach(string name in new[]{"Career","Dispatcher"}) {
            ulong[] args=name=="Career"?new[]{status,remote+0x10000,image+(ulong)p.Rva("CareerManagerVtable"),image+(ulong)p.Rva("Add"),image+(ulong)p.Rva("Needs"),image+(ulong)p.Rva("Enter")}:
                new[]{status,image+(ulong)p.Rva("DispatcherContinuation")};
            byte[] raw=p.templates[name].Emit(p,args),emitted=p.Emit(name,args);
            byte[] marker=GameProfile.Hex(name=="Career"?"488b46384c8b46404c39c0":"41c783840000000000000041c7430401000000");
            int at=Enumerable.Range(0,raw.Length-marker.Length+1).Single(n=>marker.Select((b,i)=>raw[n+i]==b).All(v=>v));
            int count=name=="Career"?8:11;
            int logoAt=name=="Dispatcher"?Enumerable.Range(0,raw.Length-9).Single(n=>GameProfile.Hex("f30f6f842480000000").Select((b,i)=>raw[n+i]==b).All(v=>v)):-1;
            Assert(Enumerable.Range(0,raw.Length).Where(n=>(n<at||n>=at+count)&&(logoAt<0||n<logoAt||n>=logoAt+9)).All(n=>raw[n]==emitted[n]),"original template unchanged outside admission and logo boundaries: "+name);
            Assert(emitted[at]==0xE9&&at+5+BitConverter.ToInt32(emitted,at+1)==raw.Length,"admission detour targets appended stub: "+name);
            Assert(Enumerable.Range(at+5,count-5).All(n=>emitted[n]==0x90),"whole overwritten instructions NOP padded: "+name);
            Assert(p.templates[name].relocations.All(r=>r.offset+8<=at||r.offset>=at+count),"no native relocation overwritten: "+name);
            raw[at]^=1;
            Reject(()=>NativeWorkAdmission.Apply(name,raw,p,args),"unknown admission boundary rejected before installation: "+name);
        }
    }
    private static void Diagnostics()
    {
        Assert(RuntimePaths.Root==Path.Combine(RuntimePaths.PluginDirectory,"OfflineTeamBuilderRuntime"),"logs live beside the installed plugin");
        Assert(RuntimePaths.DataDirectory(@"D:\portable MMC\Plugins")==@"D:\portable MMC\Plugins\OfflineTeamBuilderRuntime","portable plugin data path, including spaces");
        string root=Path.Combine(Path.GetTempPath(),"tbr-logs-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            var log=new RuntimeDiagnostics(null,root);log.Write("INFO","private fixture");log.Status(0,"Ready",new { teams=9 });
            log.Engine("IMPORT_RUNTIME_FATAL private fixture exception");
            Assert(File.ReadAllText(Path.Combine(root,"runtime.log")).Contains("[ERROR] IMPORT_RUNTIME_FATAL"),"import fatal reaches central diagnostics without verbose mode");
            log.Engine("ASSET_STATUS teamFailures=0 stage=16");
            log.Engine("ASSET_STATUS teamFailures=2 stage=16");
            log.Engine("ASSET_READINESS_WARNING olderEpochJobs=1");
            string lines=File.ReadAllText(Path.Combine(root,"runtime.log"));
            Assert(lines.Contains("[INFO] ASSET_STATUS teamFailures=0"),"zero failure counter is informational");
            Assert(lines.Contains("[WARNING] ASSET_STATUS teamFailures=2"),"real asset failures remain warnings");
            Assert(lines.Contains("[WARNING] ASSET_READINESS_WARNING"),"readiness warning reaches central diagnostics");
            string path=Path.Combine(root,"status.json"),before=File.ReadAllText(path);
            Assert(before.Contains(PluginIdentity.Version)&&before.Contains("Ready")&&before.Contains("teams"),"readable support status");
            log.Status(0,"Ready",new {teams=9});Assert(File.ReadAllText(path)==before,"unchanged status is not rewritten");
            File.WriteAllText(Path.Combine(root,"runtime.log"),new string('x',2*1024*1024+1));log.Write("INFO","rotation fixture");
            Assert(File.Exists(Path.Combine(root,"runtime.log.1")),"bounded diagnostic rotation");
        } finally { Directory.Delete(root,true); }
    }
}
