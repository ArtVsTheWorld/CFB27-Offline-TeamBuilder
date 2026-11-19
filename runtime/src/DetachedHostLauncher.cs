using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace OfflineTeamBuilderRuntime;
internal static class DetachedHostLauncher
{
    internal const string Resource="OfflineTeamBuilderRuntime.HostPayload.exe",Manifest="OfflineTeamBuilderRuntime.HostManifest.json";
    internal sealed class Record{public string version;public string sha256;}
    internal static byte[] ResourceBytes(string name){using(var s=typeof(DetachedHostLauncher).Assembly.GetManifestResourceStream(name)){if(s==null)throw new IOException("Embedded helper missing: "+name);using(var b=new MemoryStream()){s.CopyTo(b);return b.ToArray();}}}
    internal static string VerifyInstalled(string pluginDirectory=null,UserRuntimeStore store=null)
    {
        var manifest=new JavaScriptSerializer().Deserialize<Record>(Encoding.UTF8.GetString(ResourceBytes(Manifest)));
        byte[] payload=ResourceBytes(Resource);
        if(manifest==null||manifest.version!=PluginIdentity.Version||manifest.sha256==null||manifest.sha256.Length!=64||UserRuntimeStore.Hash(payload)!=manifest.sha256)
            throw new IOException("Embedded managed helper failed version/hash verification.");
        store=store??RuntimePaths.Current;
        string path=store.Resolve("Host/"+manifest.version+"/"+manifest.sha256+"/OfflineTeamBuilder.Host.exe");
        UserRuntimeStore.RejectInstallWrite(path,pluginDirectory??RuntimePaths.PluginDirectory);
        if(!File.Exists(path))UserRuntimeStore.PublishNew(path,payload);
        if(UserRuntimeStore.Hash(File.ReadAllBytes(path))!=manifest.sha256)
            throw new IOException("Managed helper cache failed verification: "+path);
        return path;
    }
    internal static ProcessStartInfo StartInfo(string helper,string arguments)=>new ProcessStartInfo{FileName=helper,Arguments=arguments,WorkingDirectory=Path.GetDirectoryName(helper),UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
    internal static void Launch(Process game,RuntimeDiagnostics log)
    {
        string helper=VerifyInstalled();var a=new HostArguments{GamePid=game.Id,GameStart=game.StartTime.ToUniversalTime().Ticks,PluginPath=RuntimeHostIdentity.PluginPath,ManagerPath=RuntimeHostIdentity.ManagerPath,ManagerPid=RuntimeHostIdentity.ManagerPid};
        using(var child=Process.Start(StartInfo(helper,a.CommandLine())))log.Write("INFO","HOST_LAUNCHED helperPid="+child.Id+" gamePid="+a.GamePid+" version="+PluginIdentity.Version+"; detached helper owns server, watchdogs and runtime; wait for Ready before closing MMC.");
    }
}
