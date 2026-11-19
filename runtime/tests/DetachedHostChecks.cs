using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using OfflineTeamBuilderRuntime;
internal static class DetachedHostChecks
{
    private static void Check(bool ok,string name)=>Program.Assert(ok,"detached host: "+name);
    private static void Wait(Func<bool> condition,string message){var t=Stopwatch.StartNew();while(t.ElapsedMilliseconds<10000){if(condition())return;Thread.Sleep(30);}throw new Exception("Timeout: "+message);}
    private sealed class Probe{public int hostPid{get;set;}public int port{get;set;}public int checks{get;set;}public bool healthy{get;set;}}
    private static Probe Read(string file){try{return new JavaScriptSerializer().Deserialize<Probe>(File.ReadAllText(file));}catch(IOException){return null;}}
    internal static void CheckAll()
    {
        var a=new HostArguments{GamePid=123,GameStart=456,ManagerPid=789,PluginPath=@"C:\MMC space ü\Plugins\OfflineTeamBuilderRuntime.dll",ManagerPath=@"C:\MMC space ü\MMCModManager.exe"};
        var b=HostArguments.Parse(a.CommandLine().Split(' '));Check(b.PluginPath==a.PluginPath&&b.ManagerPath==a.ManagerPath&&b.GamePid==123&&b.GameStart==456,"base64 argument roundtrip with spaces/non-ASCII paths");
        Program.Reject(()=>HostArguments.Parse(new[]{"--game","0"}),"short helper command rejected");
        Program.Reject(()=>HostArguments.Parse(a.CommandLine().Replace("--game 123","--game 0").Split(' ')),"invalid target PID rejected");
        Program.Reject(()=>HostArguments.Parse(a.CommandLine().Replace(HostArguments.Encode(a.PluginPath),HostArguments.Encode("relative.dll")).Split(' ')),"relative plugin path rejected");
        var pi=DetachedHostLauncher.StartInfo(@"C:\test\helper.exe","--probe");Check(!pi.UseShellExecute&&pi.CreateNoWindow&&pi.WindowStyle==ProcessWindowStyle.Hidden,"hidden independent helper; no shell or redirected MMC I/O");
        var helper=Environment.GetEnvironmentVariable("OFFLINETB_CHECK_HOST");if(string.IsNullOrEmpty(helper))throw new Exception("OFFLINETB_CHECK_HOST must point to checked helper");
        var assembly=Assembly.ReflectionOnlyLoad(File.ReadAllBytes(helper));Check(!assembly.GetReferencedAssemblies().Any(r=>r.Name.StartsWith("Frosty")||r.Name=="OfflineTeamBuilderRuntime"),"self-contained managed host has no MMC/plugin assembly dependency");
        Check(assembly.GetName().Version.ToString()==PluginIdentity.AssemblyVersion,"helper version matches plugin");
        string root=Path.Combine(Path.GetTempPath(),"otb-detached-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string plugins=Path.Combine(root,"MMC","Plugins");Directory.CreateDirectory(plugins);
        var store=new UserRuntimeStore(Path.Combine(root,"LocalAppData"));
        string installed=DetachedHostLauncher.VerifyInstalled(plugins,store);
        Check(UserRuntimeStore.Inside(installed,store.Root)&&!UserRuntimeStore.Inside(installed,plugins),"embedded helper automatically deployed outside installation");
        Check(File.ReadAllBytes(installed).SequenceEqual(File.ReadAllBytes(helper)),"extracted helper matches build input exactly");
        Check(DetachedHostLauncher.VerifyInstalled(plugins,store)==installed,"verified version/hash cache is reused");
        var bytes=File.ReadAllBytes(installed);File.AppendAllText(installed,"bad");Program.Reject(()=>DetachedHostLauncher.VerifyInstalled(plugins,store),"mismatched helper fails closed");File.WriteAllBytes(installed,bytes);
        string probe=Path.Combine(root,"probe"),stop=Path.Combine(root,"target.stop");Directory.CreateDirectory(probe);
        string checks=typeof(Program).Assembly.Location;Process target=null,parent=null,host=null;
        try{
            target=Process.Start(DetachedHostLauncher.StartInfo(checks,"--wait-target "+HostArguments.Encode(stop)));
            long ticks=target.StartTime.ToUniversalTime().Ticks;Check(HostArguments.Matches(target,target.Id,ticks)&&!HostArguments.Matches(target,target.Id,ticks+1),"PID start time prevents reuse");
            string args="--launch-host-probe "+HostArguments.Encode(installed)+" "+HostArguments.Encode(probe)+" "+target.Id+" "+ticks;
            parent=Process.Start(DetachedHostLauncher.StartInfo(checks,args));Check(parent.WaitForExit(10000)&&parent.ExitCode==0,"launcher process exits independently");
            string state=Path.Combine(probe,"probe.json");Wait(()=>File.Exists(state)&&Read(state)?.healthy==true,"child loopback startup after parent exit");var first=Read(state);host=Process.GetProcessById(first.hostPid);
            Check(!host.HasExited&&!target.HasExited&&parent.HasExited,"host and target survive launcher exit");
            var request=WebRequest.CreateHttp("http://127.0.0.1:"+first.port+"/health");request.Timeout=3000;request.Proxy=null;
            using(var response=(HttpWebResponse)request.GetResponse())Check(response.StatusCode==HttpStatusCode.OK,"real asset-service health endpoint survives launcher exit");
            Wait(()=>Read(state)?.checks>first.checks,"independent watchdog ticks");
            using(var duplicate=Process.Start(DetachedHostLauncher.StartInfo(installed,"--probe "+HostArguments.Encode(probe)+" "+target.Id+" "+ticks)))Check(duplicate.WaitForExit(5000)&&duplicate.ExitCode==0,"duplicate target helper stands aside");
            Check(Read(state).hostPid==first.hostPid,"duplicate leaves owning helper intact");
            File.WriteAllText(stop,"stop private fixture");Check(target.WaitForExit(5000),"private target exits");Check(host.WaitForExit(10000),"host exits when its target exits");
            Check(File.Exists(Path.Combine(probe,"probe-exited.txt")),"server cleanup completed");
            bool refused=false;try{var q=WebRequest.CreateHttp("http://127.0.0.1:"+first.port+"/health");q.Timeout=1000;q.Proxy=null;using(var response=q.GetResponse()){} }catch(WebException){refused=true;}Check(refused,"server port closed at target exit");
            Check(Directory.GetFiles(plugins,"*",SearchOption.AllDirectories).Length==0,"installation tree contains binary only; generated files stayed in private data folder");
        }finally{
            File.WriteAllText(stop,"stop private fixture");foreach(var p in new[]{target,parent,host})if(p!=null){try{if(!p.HasExited&&!p.WaitForExit(3000))p.Kill();}catch{}p.Dispose();}
            Directory.Delete(root,true);
        }
    }
}
