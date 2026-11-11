using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using FrostySdk.Interfaces;

namespace OfflineTeamBuilderRuntime;

internal sealed class RuntimeDiagnostics
{
    private readonly object sync=new object();
    private readonly ILogger logger;
    private readonly string root,session,mutexName;
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private string lastStatus,lastState,lastEngineIssue;
    private long lastWrite=-6000;
    private object lastFailure;
    private bool ioReported;
    internal readonly bool Verbose;
    internal RuntimeDiagnostics(ILogger logger,string directory=null) {
        this.logger=logger;root=directory??RuntimePaths.Root;
        session=DateTime.UtcNow.ToString("yyyyMMddTHHmmss",CultureInfo.InvariantCulture)+"-"+Process.GetCurrentProcess().Id+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
        mutexName="Local\\Ace.OfflineTeamBuilder.Diagnostics."+UserRuntimeStore.Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant()));
        try {
            Directory.CreateDirectory(root);
            string settings=Path.Combine(root,"OfflineTeamBuilderRuntime.settings.json");
            if(File.Exists(settings)) {
                var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(settings));object value;
                Verbose=data.TryGetValue("verboseLogging",out value)&&value is bool&&(bool)value;
            }
        }catch(Exception ex){Write("WARNING","SETTINGS_READ_FAILED "+ex.GetType().Name+": "+ex.Message);}
    }
    internal static string Safe(string text) {
        string value=text??"";
        value=Regex.Replace(value,@"(?i)(authorization\s*[:=]\s*)(bearer|basic)\s+[^\s]+","$1[redacted]");
        value=Regex.Replace(value,@"(?i)([?&](?:access_token|token|password|secret|api_key|authorization)=)[^&\s]+","$1[redacted]");
        return value.Length>12288?value.Substring(0,12288)+" [truncated]":value;
    }
    private void FileGate(Action action) {
        using(var mutex=new Mutex(false,mutexName)) {
            bool owned=false;
            try {try{owned=mutex.WaitOne(1500);}catch(AbandonedMutexException){owned=true;}
                if(!owned)throw new IOException("Diagnostics storage is busy in another MMC instance.");action();
            }finally{if(owned)mutex.ReleaseMutex();}
        }
    }
    private void ReportIo(Exception ex) {
        if(ioReported)return;ioReported=true;
        string message="[Offline TeamBuilder Runtime] DIAGNOSTIC_WRITE_FAILED "+root+": "+ex.GetType().Name+": "+ex.Message;
        try{logger?.Log(message);}catch{}Debug.WriteLine(message);
    }
    internal void Write(string level,string message) {
        message=Safe(message);string line=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture)+" ["+level+"] "+message+" [session="+session+"]";
        lock(sync) {
            try {FileGate(()=>{
                Directory.CreateDirectory(root);string path=Path.Combine(root,"runtime.log");
                if(File.Exists(path)&&new FileInfo(path).Length>2*1024*1024) {
                    string second=path+".2",first=path+".1";
                    if(File.Exists(second))File.Delete(second);if(File.Exists(first))File.Move(first,second);File.Move(path,first);
                }
                File.AppendAllText(path,line+Environment.NewLine,new UTF8Encoding(false));
            });}catch(Exception ex){ReportIo(ex);}
        }
        try{logger?.Log("[Offline TeamBuilder Runtime] "+level+" "+message);}catch{}
    }
    internal void Startup(object migration) {
        var data=new Dictionary<string,object>{{"schemaVersion",2},{"sessionId",session},{"plugin",PluginIdentity.DisplayName},{"version",PluginIdentity.Version},
            {"profile",GameProfile.Current.id},{"dataRoot",root},{"assetStore",RuntimePaths.DefaultAssetStore},{"settings",RuntimePaths.SettingsPath},
            {"repairConfiguration",RuntimePaths.OptionsPath},{"pluginPath",typeof(RuntimeAction).Assembly.Location},{"managerPid",Process.GetCurrentProcess().Id},
            {"os",Environment.OSVersion.VersionString},{"clr",Environment.Version.ToString()},{"process64Bit",Environment.Is64BitProcess},
            {"os64Bit",Environment.Is64BitOperatingSystem},{"createdUtc",DateTime.UtcNow.ToString("O")},{"migration",migration}};
        try {
            data["pluginSha256"]=UserRuntimeStore.Hash(File.ReadAllBytes(typeof(RuntimeAction).Assembly.Location));
            data["nativeHelperSha256"]=NativeAssetGuards.ExpectedHash;
            using(var host=Process.GetCurrentProcess()){data["managerPath"]=host.MainModule.FileName;data["managerVersion"]=host.MainModule.FileVersionInfo.FileVersion;}
            data["pluginInventory"]=Directory.GetFiles(RuntimePaths.PluginDirectory,"*.dll",SearchOption.TopDirectoryOnly).Take(128).Select(path=>new {
                name=Path.GetFileName(path),version=FileVersionInfo.GetVersionInfo(path).ProductVersion,bytes=new FileInfo(path).Length}).ToArray();
        }catch(Exception ex){data["inventoryError"]=Safe(ex.GetType().Name+": "+ex.Message);}
        try{FileGate(()=>UserRuntimeStore.Atomic(Path.Combine(root,"environment.json"),Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(data))));}
        catch(Exception ex){ReportIo(ex);}
        Write("INFO","STARTUP version="+PluginIdentity.Version+" session="+session+" data="+root+" plugin="+typeof(RuntimeAction).Assembly.Location);
        Write("INFO","USER_DATA_READY settings="+RuntimePaths.SettingsPath+" assetStore="+RuntimePaths.DefaultAssetStore+"; legacy originals retained");
        var report=migration as IDictionary<string,object>;object errors;
        if(report!=null&&report.TryGetValue("errors",out errors)&&errors is System.Collections.IEnumerable)
            foreach(object error in (System.Collections.IEnumerable)errors)Write("WARNING","MIGRATION_WARNING "+error);
    }
    internal void Game(Process game) {
        try {var module=game.MainModule;
            Write("INFO","GAME_SELECTED pid="+game.Id+" startUtc="+game.StartTime.ToUniversalTime().ToString("O")+" path="+module.FileName+
                " version="+module.FileVersionInfo.FileVersion+" base=0x"+module.BaseAddress.ToInt64().ToString("X")+" imageBytes="+module.ModuleMemorySize);
            object context=ModContext(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MMC","manager_config.json"),Path.GetDirectoryName(module.FileName));
            var snapshot=new {sessionId=session,pid=game.Id,capturedUtc=DateTime.UtcNow.ToString("O"),selectionAtGameDetection=context};
            FileGate(()=>UserRuntimeStore.Atomic(Path.Combine(root,"game-context.json"),Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(snapshot))));
        }catch(Exception ex){Write("WARNING","GAME_IDENTITY_UNAVAILABLE pid="+game.Id+" "+ex.Message);}
    }
    internal static Dictionary<string,object> ModContext(string configPath,string gameDirectory) {
        var result=new Dictionary<string,object>{{"scope","MMC selection and built mod manifest captured at game detection; not a live content-loading guarantee"}};
        try {
            if(!File.Exists(configPath)){result["unavailable"]="MMC configuration not found";return result;}
            if(new FileInfo(configPath).Length>4*1024*1024)throw new IOException("MMC configuration exceeds diagnostic size limit.");
            var json=new JavaScriptSerializer {MaxJsonLength=4*1024*1024};
            var document=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(configPath));
            var games=(Dictionary<string,object>)document["Games"];var game=(Dictionary<string,object>)games["CollegeFB27"];
            var options=(Dictionary<string,object>)game["Options"];string pack=Convert.ToString(options["SelectedPack"]);
            if(string.IsNullOrWhiteSpace(pack)||pack.IndexOfAny(Path.GetInvalidFileNameChars())>=0||pack=="."||pack==".."||pack.EndsWith(".")||pack.EndsWith(" "))throw new IOException("MMC selected pack name is not a single folder name.");
            result["selectedPack"]=pack;string manifest=Path.Combine(gameDirectory,"ModData",pack,"Data","mods.json");
            if(!File.Exists(manifest)){result["unavailable"]="Built mod manifest not found";return result;}
            if(new FileInfo(manifest).Length>4*1024*1024)throw new IOException("Built mod manifest exceeds diagnostic size limit.");
            var rows=(object[])json.DeserializeObject(File.ReadAllText(manifest));var mods=new List<object>();
            foreach(var entry in rows.Take(512)) {
                var row=entry as Dictionary<string,object>;if(row==null)continue;object value;
                string name=row.TryGetValue("file_name",out value)?Path.GetFileName(Convert.ToString(value)):"<unknown>";
                string version=row.TryGetValue("version",out value)?Convert.ToString(value):null;
                mods.Add(new {file=Safe(name),version=Safe(version)});
            }
            result["builtMods"]=mods;result["manifestRows"]=rows.Length;
        }catch(Exception ex){result["unavailable"]=Safe(ex.GetType().Name+": "+ex.Message);}
        return result;
    }
    internal void CompatibilityWait(string reason) {
        string[] parts=(reason??"").Split(new[]{" | "},StringSplitOptions.None);
        Write("WARNING","COMPATIBILITY_WAIT checks="+parts.Length+" "+string.Join(" | ",parts.Take(3))+
            (parts.Length>3?" | additional checks in status.json":""));
        if(Verbose&&parts.Length>3)Write("INFO","COMPATIBILITY_WAIT_DETAIL "+reason);
    }
    internal void Engine(string text) {
        if(text==null)return;
        bool fatal=text.IndexOf("FATAL",StringComparison.OrdinalIgnoreCase)>=0;
        bool error=fatal||text.IndexOf("FAIL",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("ERROR",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("ABORT",StringComparison.OrdinalIgnoreCase)>=0;
        if(text.StartsWith("ASSET_STATUS "))error=Regex.IsMatch(text,@"\bteamFailures=[1-9][0-9]*\b");
        if(text.StartsWith("ASSET_READINESS_WARNING "))error=true;
        if(error)lastEngineIssue=Safe(text);
        if(error||Verbose||text.StartsWith("ASSET_STATUS ")||text.StartsWith("ASSET_RUNTIME_READY")||text.StartsWith("NEWS_AUDIO_READY")||text.Contains("STATE READY"))
            Write(fatal?"ERROR":error?"WARNING":"INFO",text.Replace("PLAYNOW_ASSET_PROBE","ASSET_RUNTIME"));
    }
    internal void Failure(int pid,string state,Exception exception,string nextStep=null) {
        var error=new Dictionary<string,object>{{"type",exception.GetType().FullName},{"message",Safe(exception.Message)},
            {"hresult","0x"+exception.HResult.ToString("X8")},{"stack",Safe(exception.StackTrace)}, {"nextStep",nextStep}};
        var win32=exception as Win32Exception;if(win32!=null)error["win32Error"]=win32.NativeErrorCode;
        if(exception.InnerException!=null)error["inner"]=Safe(exception.InnerException.GetType().Name+": "+exception.InnerException.Message);
        Write("ERROR",state+" "+exception);Status(pid,state,error);
    }
    internal static Dictionary<string,object> Diagnosis(string state,object details) {
        string code=state,summary="See runtime.log for the current runtime stage.",next="Collect the support ZIP and include the matchup, mod versions and fresh-launch/reload result.";
        switch(state) {
            case "WaitingForGame":summary="Waiting for College Football 27.";next="Launch through MMC and keep MMC running.";break;
            case "WaitingForNativeCompatibility":summary="Waiting for game code or a shared hook to become available.";next="Stay at the main menu while startup checks complete. This is not yet a terminal failure.";break;
            case "Ready":code="RUNTIME_ACTIVE";summary="Runtime hooks are active. This does not by itself verify rendered uniforms or stadium assets.";next="If assets are missing, include this status and the runtime log in a support report.";break;
            case "BlockedNativeGuards":summary="Native guard initialization failed; TeamBuilder import and asset services did not start.";break;
            case "BlockedConfiguration":summary="Configuration is invalid; no game hooks were installed.";next="Check the repair configuration in this user-data folder. Runtime storage must be outside the MMC installation.";break;
            case "BlockedDuplicatePlugins":summary="Legacy duplicate TeamBuilder DLLs were found.";next="Move OfflineTeamBuilder.dll and OfflineTeamBuilderReentry.dll outside Plugins and restart MMC.";break;
            case "BlockedSharedCareerHub":summary="The shared CareerHub owner was not ready or is not a supported binary.";next="Include plugin versions and the shared-hook error. Do not replace hooks while the game is running.";break;
            case "BlockedUnsupportedLayout":summary="The game's native contracts did not match within the startup window.";next="Report the game update and failed native checks; do not bypass signature validation.";break;
            case "BlockedAssetRuntime":summary="Import started, but validated asset hooks could not be installed.";break;
            case "CoordinatorFailed":summary="The runtime coordinator stopped after an exception.";break;
        }
        var health=details as IDictionary<string,object>;object readiness;
        if(health!=null&&state.StartsWith("Blocked")) {
            object value;
            if(health.TryGetValue("nextStep",out value)&&value is string&&!string.IsNullOrWhiteSpace((string)value))next=(string)value;
            if(health.TryGetValue("type",out value)&&Convert.ToString(value).EndsWith("UnauthorizedAccessException")) {
                code="USER_DATA_ACCESS_DENIED";summary="Windows denied file or folder access. See the exact user-data path in the error.";
                next="Check Modify permission for the user-data folder. Moving MMC or changing permissions on Plugins is not required for normal runtime output.";
            }else if(health.TryGetValue("win32Error",out value)&&Convert.ToInt32(value)==5) {
                code="NATIVE_PROCESS_ACCESS_DENIED";summary="Windows denied a native process operation; see the operation and Win32 error in the details.";
            }else if(health.TryGetValue("message",out value)&&Convert.ToString(value).IndexOf("system export",StringComparison.OrdinalIgnoreCase)>=0) {
                code="WINDOWS_EXPORT_REJECTED";summary="Windows API resolution could not validate the target entry. TeamBuilder services did not start.";
            }
        }
        if(state=="Ready"&&health!=null&&health.TryGetValue("readiness",out readiness)) {
            var snapshot=readiness as TeamReadinessSnapshot;
            if(snapshot!=null&&snapshot.queueStallSuspected){code="ACTIVE_PACKAGE_QUEUE_STALLED";summary="Runtime is active, but a native package job has not progressed in the sampled interval.";next="Include the matchup, custom versus placeholder uniform state and support ZIP. Do not force-complete or remove native jobs.";}
        }
        return new Dictionary<string,object>{{"code",code},{"summary",summary},{"nextStep",next}};
    }
    internal void Status(int pid,string state,object details) {
        try {
            var diagnosis=Diagnosis(state,details);
            var json=new JavaScriptSerializer {MaxJsonLength=1024*1024};
            lock(sync) {
                string transition=pid+":"+state;
                if(lastState!=transition) {
                    lastState=transition;
                    if(state.StartsWith("Blocked")||state.EndsWith("Failed"))lastFailure=new {utc=DateTime.UtcNow.ToString("O"),pid,state,details};
                    Write(state.StartsWith("Blocked")||state.EndsWith("Failed")?"ERROR":"INFO","STATE "+state+" pid="+pid+" "+diagnosis["summary"]);
                }
                var values=new Dictionary<string,object>{{"schemaVersion",2},{"plugin",PluginIdentity.DisplayName},{"version",PluginIdentity.Version},
                    {"authors",PluginIdentity.Authors},{"sessionId",session},{"managerPid",Process.GetCurrentProcess().Id},{"pid",pid},{"state",state},
                    {"profile",GameProfile.Current.id},{"compatibility","Native signatures/vtables; no executable checksum or exact-size allow-list"},
                    {"dataRoot",root},{"diagnosis",diagnosis},{"lastFailure",lastFailure},{"lastEngineIssue",lastEngineIssue},{"details",details}};
                string fingerprint=json.Serialize(values);
                if(lastStatus==fingerprint&&clock.ElapsedMilliseconds-lastWrite<5000)return;
                string now=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture);values["updatedUtc"]=now;
                var text=new StringBuilder().AppendLine("Offline TeamBuilder "+PluginIdentity.Version).AppendLine("State: "+state).AppendLine("Code: "+diagnosis["code"])
                    .AppendLine("Game PID: "+pid+" | MMC PID: "+Process.GetCurrentProcess().Id).AppendLine("Updated UTC: "+now).AppendLine("Session: "+session)
                    .AppendLine().AppendLine((string)diagnosis["summary"]).AppendLine("Next: "+diagnosis["nextStep"])
                    .AppendLine().AppendLine("Details: "+Safe(json.Serialize(details))).AppendLine("Most recent failure: "+Safe(json.Serialize(lastFailure)))
                    .AppendLine("Last engine warning: "+lastEngineIssue).AppendLine().AppendLine("Logs and support files: "+root);
                FileGate(()=>{Directory.CreateDirectory(root);UserRuntimeStore.Atomic(Path.Combine(root,"status.json"),Encoding.UTF8.GetBytes(json.Serialize(values)));
                    UserRuntimeStore.Atomic(Path.Combine(root,"status.txt"),Encoding.UTF8.GetBytes(text.ToString()));});
                lastStatus=fingerprint;lastWrite=clock.ElapsedMilliseconds;
            }
        }catch(Exception ex){ReportIo(ex);}
    }
}
