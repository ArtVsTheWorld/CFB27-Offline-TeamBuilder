using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using OfflineTeamBuilder.Repair;
namespace OfflineTeamBuilderRuntime;
internal static class HostProgram
{
    private static int Main(string[] args)
    {
        if((args.Length==2||args.Length==4)&&args[0]=="--prepare-artwork"){
            UserRuntimeStore paths=null;
            try{
                string plugin=Path.GetFullPath(HostArguments.Decode(args[1]));if(Path.GetFileName(plugin)!="OfflineTeamBuilderRuntime.dll")throw new IOException("Invalid compiler plugin identity.");RuntimeHostIdentity.PluginPath=plugin;
                if(args.Length==4&&args[2]!="--private-root")throw new IOException("Unknown artwork compiler option.");
                paths=args.Length==4?new UserRuntimeStore(HostArguments.Decode(args[3])):RuntimePaths.Current;
                paths.Prepare(Path.GetDirectoryName(plugin));
                using(var lease=new Mutex(false,"Local\\Ace.OfflineTeamBuilder.Artwork."+UserRuntimeStore.Hash(Encoding.UTF8.GetBytes(paths.Root.ToUpperInvariant())))){
                    bool held=false;try{
                        try{held=lease.WaitOne(300000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Artwork compiler is already busy.");
                        object report=ArtworkCompiler.Compile(paths);
                        UserRuntimeStore.Atomic(paths.Resolve("ArtworkCache/prepare-result.json"),Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(report)));return 0;
                    }finally{if(held)lease.ReleaseMutex();}
                }
            }catch(Exception ex){if(paths!=null)try{UserRuntimeStore.Atomic(paths.Resolve("ArtworkCache/prepare-result.json"),Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new{ready=false,error=ex.ToString()})));}catch{}return 6;}
        }
        if(args.Length==4&&args[0]=="--probe")return Probe(HostArguments.Decode(args[1]),int.Parse(args[2]),long.Parse(args[3]));
        RuntimeDiagnostics log=null;
        try{
            var a=HostArguments.Parse(args);a.Apply();
            using(var game=Process.GetProcessById(a.GamePid)){
                if(!HostArguments.Matches(game,a.GamePid,a.GameStart,"CollegeFB27"))return 3;
                using(var lease=HostLease.Acquire(a.GamePid,a.GameStart)){
                    if(lease==null)return 0;
                    log=new RuntimeDiagnostics(null);log.Startup(RuntimePaths.Current.Prepare(RuntimePaths.PluginDirectory));
                    RuntimePaths.ReadOptions();
                    if(new[]{"OfflineTeamBuilder.dll","OfflineTeamBuilderReentry.dll"}.AnyFile(RuntimePaths.PluginDirectory))throw new IOException("Duplicate legacy TeamBuilder plugins; no runtime started.");
                    log.Write("INFO","HOST_STARTED hostPid="+Process.GetCurrentProcess().Id+" gamePid="+a.GamePid+"; MMC lifetime is not the session lifetime.");log.Game(game);
                    if(!RuntimeCoordinator.Preflight(game,log))return 4;
                    using(var session=new UnifiedSession(game,log))session.Run();
                    log.Write("INFO","HOST_STOPPED gamePid="+a.GamePid+"; runtime cleanup complete.");return RuntimeCoordinator.Alive(game)?5:0;
                }
            }
        }catch(Exception ex){if(log==null)try{log=new RuntimeDiagnostics(null);}catch{}log?.Failure(0,"CoordinatorFailed",ex,"Check the detached helper startup error. No automatic restart over unknown hooks.");return 2;}
    }
    // Private lifecycle fixture: real loopback service, ordinary target process,
    // no game discovery, native calls or game memory access.
    private static int Probe(string directory,int pid,long ticks)
    {
        using(var target=Process.GetProcessById(pid))using(var lease=HostLease.Acquire(pid,ticks)){
            if(lease==null)return 0;if(!HostArguments.Matches(target,pid,ticks))return 3;
            var options=new RepairOptions{StoreDirectory=Path.Combine(directory,"Store")};
            using(var server=new LocalMcrService(directory,options,0)){
                server.Start();int checks=0;
                while(HostArguments.Matches(target,pid,ticks)){
                    UserRuntimeStore.Atomic(Path.Combine(directory,"probe.json"),Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new{hostPid=Process.GetCurrentProcess().Id,port=server.Port,checks=++checks,healthy=server.IsHealthy})));
                    Thread.Sleep(100);
                }
            }
            File.WriteAllText(Path.Combine(directory,"probe-exited.txt"),"server disposed; target exited");return 0;
        }
    }
}
internal static class HostArrayExtensions{internal static bool AnyFile(this string[] names,string root){foreach(var n in names)if(File.Exists(Path.Combine(root,n)))return true;return false;}}
