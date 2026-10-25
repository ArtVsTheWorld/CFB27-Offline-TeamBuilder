using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Frosty.Core;
using FrostySdk;
using FrostySdk.Interfaces;
using OfflineTeamBuilderReentry;

namespace OfflineTeamBuilderRuntime;

// The ONLY registered MMC action. Engines are compiled into this assembly;
// no embedded legacy DLL, runtime Assembly.Load, or second plugin is required.
public sealed class RuntimeAction : ExecutionAction
{
    private static int started;
    public override Action<ILogger,PluginManagerType,CancellationToken> PreLaunchAction { get { return PreLaunch; } }
    public override Action<ILogger,PluginManagerType,CancellationToken> PostLaunchAction { get { return Start; } }
    private static void PreLaunch(ILogger logger,PluginManagerType type,CancellationToken token)
    {
        // Preserve the existing explicit, optional copy-job configuration. This
        // does not edit original saves; it is not part of the asset reentry path.
        new CFB27TeamBuilderOfflineFrostyRuntime.TeamBuilderOfflineExecutionAction().PreLaunchAction(logger,type,token);
        Start(logger,type,token);
    }
    private static void Start(ILogger logger,PluginManagerType type,CancellationToken token)
    {
        if(!ProfilesLibrary.IsLoaded(ProfileVersion.CollegeFootball27))return;
        if(Interlocked.CompareExchange(ref started,1,0)!=0)return;
        var diagnostics=new RuntimeDiagnostics(logger);
        try {
            var thread=new Thread(()=>Run(diagnostics)) { IsBackground=true,Name="Offline TeamBuilder Runtime coordinator" };
            thread.Start();
        } catch(Exception ex) { Interlocked.Exchange(ref started,0);diagnostics.Write("ERROR","START_FAILED "+ex.Message); }
    }
    internal static string[] FindLegacyNames(IEnumerable<string> assemblyNames)
    {
        return assemblyNames.Where(n=>string.Equals(n,"OfflineTeamBuilder",StringComparison.OrdinalIgnoreCase)||string.Equals(n,"OfflineTeamBuilderReentry",StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
    }
    private static void Run(RuntimeDiagnostics log)
    {
        try {
            string[] duplicates=FindLegacyNames(AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name));
            var files=new[]{"OfflineTeamBuilder.dll","OfflineTeamBuilderReentry.dll"}.Where(n=>File.Exists(Path.Combine(RuntimePaths.PluginDirectory,n))).ToArray();
            if(duplicates.Length!=0||files.Length!=0) {
                string error="DUPLICATE_LEGACY_PLUGIN: remove the separate OfflineTeamBuilder.dll and OfflineTeamBuilderReentry.dll, then restart MMC. No game memory changed.";
                log.Write("ERROR",error);log.Status(0,"BlockedDuplicatePlugins",error);return;
            }
            log.Write("INFO",PluginIdentity.DisplayName+" "+PluginIdentity.Version+" | "+PluginIdentity.Authors+" | normal hub loading | no executable checksum requirement");
            long previousStart=0;int previousPid=0;
            while(true) {
                Process game=FindNewest();
                if(game==null){log.Status(0,"WaitingForGame",null);Thread.Sleep(500);continue;}
                using(game) {
                    long start=game.StartTime.ToUniversalTime().Ticks;
                    if(game.Id==previousPid&&start==previousStart){Thread.Sleep(500);continue;}
                    previousPid=game.Id;previousStart=start;
                    if(!Preflight(game,log)) { while(Alive(game))Thread.Sleep(1000);continue; }
                    using(var session=new UnifiedSession(game,log))session.Run();
                }
            }
        } catch(Exception ex) { log.Write("ERROR","COORDINATOR_FAILED "+ex);log.Status(0,"CoordinatorFailed",ex.Message); }
    }
    private static Process FindNewest()
    {
        Process selected=null;DateTime latest=DateTime.MinValue;
        foreach(Process p in Process.GetProcessesByName("CollegeFB27")) {
            try {
                if(!p.HasExited&&p.StartTime>latest){selected?.Dispose();selected=p;latest=p.StartTime;}
                else p.Dispose();
            } catch { p.Dispose(); }
        }
        return selected;
    }
    internal static bool Alive(Process game) { try { return !game.HasExited; } catch { return false; } }
    private static bool Preflight(Process game,RuntimeDiagnostics log)
    {
        var timer=Stopwatch.StartNew();string previous=null;
        while(Alive(game)&&timer.Elapsed.TotalSeconds<90) {
            try {
                game.Refresh();var module=game.MainModule;
                using(var reader=new NativeObserver(game.Id,(ulong)module.BaseAddress.ToInt64(),module.ModuleMemorySize)) {
                    List<string> errors=GameProfile.Current.CheckNative(module.ModuleMemorySize,(ulong)module.BaseAddress.ToInt64(),reader.ReadRva);
                    if(errors.Count==0){log.Write("INFO","COMPATIBILITY_PASS "+GameProfile.Current.id+"; all required native signatures and lifecycle slots verified (no executable checksum).");return true;}
                    string reason=string.Join(" | ",errors);
                    if(reason!=previous){log.Write("WARNING","COMPATIBILITY_WAIT "+reason);previous=reason;}
                    log.Status(game.Id,"WaitingForNativeCompatibility",errors);
                }
            } catch(Exception ex) { if(previous!=ex.Message){previous=ex.Message;log.Write("WARNING","COMPATIBILITY_WAIT "+ex.Message);} }
            Thread.Sleep(500);
        }
        if(!Alive(game)) { log.Status(game.Id,"GameExitedDuringPreflight","Startup process exited before validation completed; waiting for a new game process.");return false; }
        log.Write("ERROR","UNSUPPORTED_NATIVE_LAYOUT: no hooks installed. Check status.json; a new native profile may be needed.");
        log.Status(game.Id,"BlockedUnsupportedLayout",previous);return false;
    }
}

internal sealed class UnifiedSession : IDisposable
{
    private readonly Process game;
    private readonly RuntimeDiagnostics log;
    private readonly object gate=new object();
    private TeamBuilderReentryRepair assets;
    private Thread importThread;
    private bool closing;
    private int exitCode;
    private int assetAttempts;
    private bool failed;
    internal UnifiedSession(Process game,RuntimeDiagnostics log) { this.game=game;this.log=log; }
    internal void Run()
    {
        CFB27TeamBuilderBatchQueuePreMcrProbe.ExternalLog=log.Engine;
        CFB27TeamBuilderBatchQueuePreMcrProbe.BootstrapLog=log.Engine;
        CFB27TeamBuilderBatchQueuePreMcrProbe.BeforeRestore=BeforeImportCleanup;
        CFB27TeamBuilderBatchQueuePreMcrProbe.IsOwnedAssetChain=(handle,owner,main)=> {
            lock(gate)return assets!=null&&assets.OwnsMainChain(owner,main);
        };
        CFB27TeamBuilderBatchQueuePreMcrProbe.LastSessionReachedReady=false;
        string output=Path.Combine(RuntimePaths.Root,"Engine");
        try { Directory.CreateDirectory(output); }
        catch(Exception ex) {
            failed=true;
            log.Write("ERROR","OUTPUT_DIRECTORY_UNAVAILABLE "+output+": "+ex.Message+". Allow write access to the plugin's data folder; no hooks installed.");
            log.Status(game.Id,"BlockedOutputDirectory",ex.Message);return;
        }
        importThread=new Thread(()=> {
            try { exitCode=CFB27TeamBuilderBatchQueuePreMcrProbe.RunEmbeddedSession(game,output); }
            catch(Exception ex){exitCode=99;log.Write("ERROR","IMPORT_RUNTIME_FAILED "+ex);BeforeImportCleanup();}
        }) {IsBackground=true,Name="Offline TeamBuilder Runtime import service"};
        importThread.Start();
        log.Status(game.Id,"ArmingImportRuntime",null);
        while(RuntimeAction.Alive(game)&&importThread.IsAlive) {
            lock(gate) {
                if(closing)break;
                if(CFB27TeamBuilderBatchQueuePreMcrProbe.LastSessionReachedReady) {
                    if(assets==null)assets=new TeamBuilderReentryRepair(game,log.Engine);
                    if(!assets.IsInstalled) {
                        if(assets.TryInstall())log.Write("INFO","RUNTIME_READY import + multi-team assets + news previews + stadium audio");
                        else {
                            log.Status(game.Id,"WaitingForAssetRuntime","Import runtime ready; waiting for validated asset hooks. Details in runtime.log.");
                            if(++assetAttempts>=30) {
                                failed=true;log.Write("ERROR","ASSET_INSTALL_FAILED after 30 attempts; restoring owned runtime hooks.");
                                log.Status(game.Id,"BlockedAssetRuntime","See runtime.log for the failing signature or access check.");break;
                            }
                        }
                    }
                    if(assets.IsInstalled)log.Status(game.Id,"Ready",assets.CaptureHealth());
                }
            }
            Thread.Sleep(1000);
        }
        if(!failed)log.Status(game.Id,RuntimeAction.Alive(game)?"RuntimeStopped":"GameExited",new { importExitCode=exitCode });
    }
    private void BeforeImportCleanup()
    {
        lock(gate) { closing=true;assets?.Dispose();assets=null; }
    }
    public void Dispose()
    {
        BeforeImportCleanup();CFB27TeamBuilderBatchQueuePreMcrProbe.RequestStop();
        if(importThread!=null&&importThread.IsAlive&&Thread.CurrentThread!=importThread) {
            if(!importThread.Join(5000)) {
                log.Write("WARNING","Waiting for prior import runtime to finish cleanup before another game session can start.");
                while(importThread.IsAlive)importThread.Join(500);
            }
        }
        if(importThread==null||!importThread.IsAlive) {
            CFB27TeamBuilderBatchQueuePreMcrProbe.BeforeRestore=null;
            CFB27TeamBuilderBatchQueuePreMcrProbe.IsOwnedAssetChain=null;
            CFB27TeamBuilderBatchQueuePreMcrProbe.ExternalLog=null;
            CFB27TeamBuilderBatchQueuePreMcrProbe.BootstrapLog=null;
        }
    }
}
