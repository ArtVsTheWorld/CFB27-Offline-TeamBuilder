using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OfflineTeamBuilderRuntime;

internal static class NativeHelperDeploymentChecks
{
    internal static string CheckAll() {
        string root=Path.Combine(Path.GetTempPath(),"otb-native-resource-"+Guid.NewGuid().ToString("N"));
        byte[] payload=NativeHelperDeployment.Payload();string expected=NativeAssetGuards.ExpectedHash;
        string valid=Path.Combine(root,"valid");
        Program.Assert(payload.Length>0,"native payload is embedded");
        Program.Assert(NativeHelperDeployment.PathFor(valid,expected.ToUpperInvariant())==NativeHelperDeployment.PathFor(valid,expected),"native cache identity is case normalized");
        Program.Reject(()=>NativeHelperDeployment.PathFor(valid,"../escape"),"invalid helper identity cannot select a path");
        Program.Reject(()=>NativeHelperDeployment.Ensure(valid,null,expected),"missing native resource rejected");
        Program.Reject(()=>NativeHelperDeployment.Ensure(valid,payload,new string('0',64)),"resource digest mismatch rejected before filesystem writes");
        byte[] corrupt=(byte[])payload.Clone();corrupt[0]^=1;
        Program.Reject(()=>NativeHelperDeployment.Ensure(valid,corrupt,expected),"modified native resource rejected");
        Program.Assert(!Directory.Exists(root),"invalid resource attempts create no deployment files");
        string helper=NativeHelperDeployment.Ensure(valid,payload,expected);
        Program.Assert(File.ReadAllBytes(helper).SequenceEqual(payload),"published native helper exactly matches embedded bytes");
        Program.Assert(Path.GetDirectoryName(helper)==Path.Combine(Path.GetFullPath(valid),expected.ToLowerInvariant()),"helper stays in hash-specific plugin data directory");
        DateTime marker=new DateTime(2001,1,1,0,0,0,DateTimeKind.Utc);File.SetLastWriteTimeUtc(helper,marker);
        Program.Assert(NativeHelperDeployment.Ensure(valid,payload,expected)==helper&&File.GetLastWriteTimeUtc(helper)==marker,"valid cached helper reused without rewriting");
        Program.Assert(Directory.GetFiles(Path.GetDirectoryName(helper),"*.tmp").Length==0,"successful deployment leaves no staging file");
        string invalidRoot=Path.Combine(root,"invalid-cache"),invalidTarget=NativeHelperDeployment.PathFor(invalidRoot,expected);
        Directory.CreateDirectory(Path.GetDirectoryName(invalidTarget));File.WriteAllBytes(invalidTarget,new byte[]{1,2,3});
        Program.Reject(()=>NativeHelperDeployment.Ensure(invalidRoot,payload,expected),"corrupt cached helper rejected without replacement");
        Program.Assert(File.ReadAllBytes(invalidTarget).SequenceEqual(new byte[]{1,2,3}),"failed validation preserves existing cached file");
        string concurrent=Path.Combine(root,"concurrent");var start=new ManualResetEventSlim(false);
        var first=Task.Run(()=>{start.Wait();return NativeHelperDeployment.Ensure(concurrent,payload,expected);});
        var second=Task.Run(()=>{start.Wait();return NativeHelperDeployment.Ensure(concurrent,payload,expected);});
        start.Set();Task.WaitAll(first,second);start.Dispose();
        Program.Assert(first.Result==second.Result,"concurrent deployment resolves to one verified helper");
        Program.Assert(File.ReadAllBytes(first.Result).SequenceEqual(payload),"concurrent publication preserves native bytes");
        Program.Assert(Directory.GetFiles(Path.GetDirectoryName(first.Result),"*.tmp").Length==0,"concurrent deployment leaves no staging files");
        Program.Assert(Directory.GetFiles(concurrent,"*.dll",SearchOption.AllDirectories).Length==1,"concurrent deployment publishes a single DLL");
        return helper;
    }
}
