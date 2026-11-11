using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using OfflineTeamBuilder.Repair;

namespace OfflineTeamBuilderRuntime;

internal static class RuntimePaths
{
    private static UserRuntimeStore current;
    internal static UserRuntimeStore Current {
        get {
            if(current==null) {
                string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                Interlocked.CompareExchange(ref current,new UserRuntimeStore(DataDirectory(local),Path.Combine(local,"OfflineTeamBuilder","local-assets-v111")),null);
            }
            return current;
        }
    }
    internal static string PluginDirectory {get{return Path.GetDirectoryName(typeof(RuntimePaths).Assembly.Location);}}
    internal static string Root {get{return Current.Root;}}
    internal static string DataDirectory(string localAppData) {
        if(string.IsNullOrWhiteSpace(localAppData)||!Path.IsPathRooted(localAppData))throw new IOException("Windows did not provide an absolute Local AppData folder.");
        return Path.GetFullPath(Path.Combine(localAppData,"Ace","OfflineTeamBuilder"));
    }
    internal static string SettingsPath {get{return Current.Resolve("OfflineTeamBuilderRuntime.settings.json");}}
    internal static string OptionsPath {get{return Current.Resolve("OfflineTeamBuilderRuntime.repair.json");}}
    internal static string OptionsFileName {get{return "OfflineTeamBuilderRuntime.repair.json";}}
    internal static string DefaultAssetStore {get{return Current.AssetStore;}}
    internal static string EngineDirectory {get{return Current.Resolve("Engine");}}
    internal static string NativeDirectory {get{return Current.Resolve("Native");}}
    internal static string NativeLogsDirectory {get{return Current.Resolve("NativeLogs");}}
    internal static RepairOptions ReadOptions() {
        var options=File.Exists(OptionsPath)?RepairJson.Decode<RepairOptions>(File.ReadAllBytes(OptionsPath)):new RepairOptions();
        return Current.Configure(options,PluginDirectory);
    }
}

internal sealed class UserRuntimeStore
{
    internal readonly string Root,AssetStore;
    private readonly object gate=new object();
    private readonly Dictionary<string,object> prepared=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
    internal UserRuntimeStore(string root,string assetStore=null) {
        if(string.IsNullOrWhiteSpace(root)||!Path.IsPathRooted(root))throw new IOException("A full user-data path is required.");
        Root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        AssetStore=Path.GetFullPath(assetStore??Path.Combine(Root,"AssetStore"));
    }
    internal static bool Inside(string path,string parent) {
        string full=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),prefix=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        return full.Equals(prefix,StringComparison.OrdinalIgnoreCase)||full.StartsWith(prefix+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }
    internal static string InstallDirectory(string plugins) {
        string full=Path.GetFullPath(plugins).TrimEnd(Path.DirectorySeparatorChar);
        return Path.GetFileName(full).Equals("Plugins",StringComparison.OrdinalIgnoreCase)?Path.GetDirectoryName(full):full;
    }
    internal static void RejectInstallWrite(string destination,string plugins) {
        if(Inside(destination,InstallDirectory(plugins)))throw new IOException("Runtime output must be outside the MMC installation. Choose a writable user-data location: "+destination);
    }
    internal string Resolve(string relative) {
        if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.IndexOfAny(new[]{':','\0'})>=0||
            relative.Replace('\\','/').Split('/').Any(p=>p.Length==0||p=="."||p==".."||p.EndsWith(".")||p.EndsWith(" ")))
            throw new IOException("Invalid relative user-data path.");
        string result=Path.GetFullPath(Path.Combine(Root,relative));
        if(!Inside(result,Root))throw new IOException("User-data path escaped its root.");
        CheckLinks(result);return result;
    }
    private void CheckLinks(string path) {
        for(string part=path;part!=null&&Inside(part,Root);part=Path.GetDirectoryName(part)) {
            if((File.Exists(part)||Directory.Exists(part))&&(File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Linked runtime data paths are not supported: "+part);
            if(part.Equals(Root,StringComparison.OrdinalIgnoreCase))break;
        }
    }
    internal static string Hash(byte[] bytes) {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    internal static void Atomic(string path,byte[] bytes) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes,0,bytes.Length);output.Flush(true);}
            if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal static bool PublishNew(string path,byte[] bytes) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes,0,bytes.Length);output.Flush(true);}
            try{File.Move(temporary,path);return true;}catch(IOException){if(!File.Exists(path))throw;return false;}
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal object Prepare(string plugins) {
        string origin=Path.GetFullPath(plugins);RejectInstallWrite(Root,plugins);CheckLinks(Root);
        lock(gate) {
            object previous;if(prepared.TryGetValue(origin,out previous))return previous;
            Directory.CreateDirectory(Root);
            using(new FileStream(Resolve("write-check-"+Guid.NewGuid().ToString("N")+".tmp"),FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)){}
            string id=Hash(Encoding.UTF8.GetBytes(origin.ToUpperInvariant())).Substring(0,16);
            using(var mutex=new Mutex(false,"Local\\Ace.OfflineTeamBuilder.Storage."+Hash(Encoding.UTF8.GetBytes(Root.ToUpperInvariant())))) {
                bool owned=false;
                try {
                    try{owned=mutex.WaitOne(15000);}catch(AbandonedMutexException){owned=true;}
                    if(!owned)throw new IOException("Timed out waiting for TeamBuilder user-data migration.");
                    string marker=Resolve("Migration/"+id+"/migration.json");
                    if(File.Exists(marker)) {var done=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(marker));prepared[origin]=done;return done;}
                    var copied=new List<string>();var errors=new List<string>();
                    foreach(string name in new[]{"OfflineTeamBuilderRuntime.settings.json","OfflineTeamBuilderRuntime.repair.json","OfflineTeamBuilder.repair.json"}) {
                        string destination=name=="OfflineTeamBuilder.repair.json"?"OfflineTeamBuilderRuntime.repair.json":name;
                        CopyLegacy(Path.Combine(origin,name),Resolve(destination),id,name,copied,errors);
                    }
                    foreach(string name in new[]{"runtime.log","runtime.log.1","runtime.log.2","status.json"})
                        CopyLegacy(Path.Combine(origin,"OfflineTeamBuilderRuntime",name),Resolve("Migration/"+id+"/Logs/"+name),id,name,copied,errors);
                    string settings=Resolve("OfflineTeamBuilderRuntime.settings.json");
                    if(!File.Exists(settings))PublishNew(settings,Encoding.UTF8.GetBytes("{\"verboseLogging\":false}"));
                    var report=new Dictionary<string,object>{{"origin",origin},{"createdUtc",DateTime.UtcNow.ToString("O")},{"copied",copied},{"errors",errors},{"originalFilesRetained",true}};
                    if(errors.Count==0)Atomic(marker,Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(report)));
                    prepared[origin]=report;return report;
                }finally{if(owned)mutex.ReleaseMutex();}
            }
        }
    }
    private void CopyLegacy(string source,string destination,string id,string name,List<string> copied,List<string> errors) {
        if(!File.Exists(source))return;
        try {
            if(new FileInfo(source).Length>8*1024*1024)throw new IOException("Legacy file exceeds migration size limit.");
            byte[] bytes=File.ReadAllBytes(source);
            if(PublishNew(destination,bytes)){copied.Add(source+" -> "+destination);return;}
            if(Hash(File.ReadAllBytes(destination))==Hash(bytes))return;
            destination=Resolve("Migration/"+id+"/Conflicts/"+name+"-"+Hash(bytes).Substring(0,16)+".bak");
            if(!PublishNew(destination,bytes)&&Hash(File.ReadAllBytes(destination))!=Hash(bytes))throw new IOException("Migration conflict archive differs; originals retained.");
            copied.Add(source+" -> "+destination);
        }catch(Exception ex){errors.Add(name+": "+ex.GetType().Name+": "+ex.Message);}
    }
    internal RepairOptions Configure(RepairOptions options,string plugins) {
        if(options==null)throw new InvalidDataException("Repair configuration is empty.");
        if(string.IsNullOrWhiteSpace(options.StoreDirectory))options.StoreDirectory=AssetStore;
        else if(!Path.IsPathRooted(options.StoreDirectory))options.StoreDirectory=Path.GetFullPath(Path.Combine(Root,options.StoreDirectory));
        RejectInstallWrite(options.StoreDirectory,plugins);options.Validate();return options;
    }
}
