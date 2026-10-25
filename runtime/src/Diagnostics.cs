using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using FrostySdk.Interfaces;

namespace OfflineTeamBuilderRuntime;

internal static class RuntimePaths
{
    internal static string PluginDirectory { get { return Path.GetDirectoryName(typeof(RuntimePaths).Assembly.Location); } }
    internal static string Root { get { return DataDirectory(PluginDirectory); } }
    internal static string DataDirectory(string pluginDirectory) { return Path.Combine(pluginDirectory,"OfflineTeamBuilderRuntime"); }
    internal static string OptionsFileName { get { return File.Exists(Path.Combine(PluginDirectory,"OfflineTeamBuilderRuntime.repair.json"))?"OfflineTeamBuilderRuntime.repair.json":"OfflineTeamBuilder.repair.json"; } }
}

internal sealed class RuntimeDiagnostics
{
    private readonly object sync=new object();
    private readonly ILogger logger;
    private readonly string root;
    private string lastStatus;
    internal readonly bool Verbose;
    internal RuntimeDiagnostics(ILogger logger,string directory=null)
    {
        this.logger=logger;root=directory??RuntimePaths.Root;
        try {
            Directory.CreateDirectory(root);
            string settings=Path.Combine(RuntimePaths.PluginDirectory,"OfflineTeamBuilderRuntime.settings.json");
            if(File.Exists(settings)) {
                var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(settings));
                object value;Verbose=data.TryGetValue("verboseLogging",out value)&&value is bool&&(bool)value;
            }
        } catch(Exception ex) { Write("WARNING","Diagnostics settings/default folder: "+ex.Message); }
    }
    internal void Write(string level,string message)
    {
        // Never pass HTTP request bodies or authorization headers here.
        string line=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture)+" ["+level+"] "+message;
        lock(sync) {
            try {
                Directory.CreateDirectory(root);
                string path=Path.Combine(root,"runtime.log");
                if(File.Exists(path)&&new FileInfo(path).Length>2*1024*1024) {
                    string second=path+".2",first=path+".1";
                    if(File.Exists(second))File.Delete(second);
                    if(File.Exists(first))File.Move(first,second);
                    File.Move(path,first);
                }
                File.AppendAllText(path,line+Environment.NewLine);
            } catch { /* A read-only install/log folder must not crash MMC. */ }
        }
        try { logger?.Log("[Offline TeamBuilder Runtime] "+message); } catch { }
    }
    internal void Engine(string text)
    {
        bool fatal=text.IndexOf("FATAL",StringComparison.OrdinalIgnoreCase)>=0;
        bool error=fatal||text.IndexOf("FAIL",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("ERROR",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("ABORT",StringComparison.OrdinalIgnoreCase)>=0;
        // "teamFailures=0" is a counter, not an error. Preserve real native
        // failures while keeping ordinary readiness snapshots informational.
        if(text.StartsWith("ASSET_STATUS "))error=System.Text.RegularExpressions.Regex.IsMatch(text,@"\bteamFailures=[1-9][0-9]*\b");
        if(text.StartsWith("ASSET_READINESS_WARNING "))error=true;
        if(error||Verbose||text.StartsWith("ASSET_STATUS ")||text.StartsWith("ASSET_RUNTIME_READY")||text.StartsWith("NEWS_AUDIO_READY")||text.Contains("STATE READY"))
            Write(fatal?"ERROR":error?"WARNING":"INFO",text.Replace("PLAYNOW_ASSET_PROBE","ASSET_RUNTIME"));
    }
    internal void Status(int pid,string state,object details)
    {
        try {
            var values=new Dictionary<string,object> {
                {"plugin",PluginIdentity.DisplayName},{"version",PluginIdentity.Version},
                {"authors",PluginIdentity.Authors}, {"pid",pid},{"state",state},
                {"profile",GameProfile.Current.id},{"compatibility","Native signatures/vtables; no executable checksum or exact-size allow-list"},
                {"details",details}
            };
            var json=new JavaScriptSerializer { MaxJsonLength=1024*1024 };
            string fingerprint=json.Serialize(values);
            lock(sync) {
                if(lastStatus==fingerprint)return;
                values["updatedUtc"]=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture);
                Directory.CreateDirectory(root);
                string path=Path.Combine(root,"status.json"),temporary=path+".tmp";
                File.WriteAllText(temporary,json.Serialize(values));
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
                lastStatus=fingerprint;
            }
        } catch(Exception ex) { if(Verbose)Write("WARNING","Status snapshot unavailable: "+ex.Message); }
    }
}
