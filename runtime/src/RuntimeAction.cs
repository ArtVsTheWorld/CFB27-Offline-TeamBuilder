using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Frosty.Core;
using FrostySdk;
using FrostySdk.Interfaces;
namespace OfflineTeamBuilderRuntime;
public sealed class RuntimeAction : ExecutionAction
{
    private static int started;
    public override Action<ILogger,PluginManagerType,CancellationToken> PreLaunchAction=>PreLaunch;
    public override Action<ILogger,PluginManagerType,CancellationToken> PostLaunchAction=>Start;
    private static void PreLaunch(ILogger logger,PluginManagerType type,CancellationToken token)
    {
        if(!PrepareStorage(logger))return;
        ArtworkPreparation.Prepare(logger,token);
        SharedUiPreparation.Prepare(logger,type,token);
        new CFB27TeamBuilderOfflineFrostyRuntime.TeamBuilderOfflineExecutionAction().PreLaunchAction(logger,type,token);
        Start(logger,type,token);
    }
    private static bool PrepareStorage(ILogger logger)
    {
        if(!ProfilesLibrary.IsLoaded(ProfileVersion.CollegeFootball27))return false;
        try{RuntimePaths.Current.Prepare(RuntimePaths.PluginDirectory);return true;}
        catch(Exception ex){logger?.Log("[Offline TeamBuilder] USER_DATA_UNAVAILABLE "+ex.Message);return false;}
    }
    private static void Start(ILogger logger,PluginManagerType type,CancellationToken token)
    {
        if(!PrepareStorage(logger)||Interlocked.CompareExchange(ref started,1,0)!=0)return;
        var log=new RuntimeDiagnostics(logger==null?(Action<string>)null:(text=>logger.Log(text)));
        var duplicates=FindLegacyNames(AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name));
        if(duplicates.Length!=0||new[]{"OfflineTeamBuilder.dll","OfflineTeamBuilderReentry.dll"}.Any(n=>File.Exists(Path.Combine(RuntimePaths.PluginDirectory,n)))) {
            log.Write("ERROR","DUPLICATE_LEGACY_PLUGIN: remove legacy copies before launching. No helper started.");return;
        }
        new Thread(()=>Watch(log)){IsBackground=true,Name="Offline TeamBuilder helper launcher"}.Start();
    }
    internal static string[] FindLegacyNames(System.Collections.Generic.IEnumerable<string> names)=>names.Where(n=>string.Equals(n,"OfflineTeamBuilder",StringComparison.OrdinalIgnoreCase)||string.Equals(n,"OfflineTeamBuilderReentry",StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
    private static void Watch(RuntimeDiagnostics log)
    {
        int previous=0;long ticks=0;
        while(true){
            try{
                Process selected=null;DateTime latest=DateTime.MinValue;
                foreach(var p in Process.GetProcessesByName("CollegeFB27"))try{if(!p.HasExited&&p.StartTime>latest){selected?.Dispose();selected=p;latest=p.StartTime;}else p.Dispose();}catch{p.Dispose();}
                if(selected!=null)using(selected){long start=selected.StartTime.ToUniversalTime().Ticks;if(selected.Id!=previous||start!=ticks){previous=selected.Id;ticks=start;DetachedHostLauncher.Launch(selected,log);}}
            }catch(Exception ex){log.Write("ERROR","HOST_LAUNCH_FAILED "+ex);}
            Thread.Sleep(300);
        }
    }
}
