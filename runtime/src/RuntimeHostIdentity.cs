using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
namespace OfflineTeamBuilderRuntime;
internal static class RuntimeHostIdentity
{
    internal static string PluginPath=typeof(RuntimeHostIdentity).Assembly.Location;
    internal static int ManagerPid=Process.GetCurrentProcess().Id;
    internal static string ManagerPath=Process.GetCurrentProcess().MainModule.FileName;
    internal static bool Detached;
}
internal sealed class HostArguments
{
    internal int GamePid,ManagerPid;internal long GameStart;internal string PluginPath,ManagerPath;
    internal static string Encode(string value)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    internal static string Decode(string value)=>new UTF8Encoding(false,true).GetString(Convert.FromBase64String(value));
    internal string CommandLine()=>"--game "+GamePid+" --start "+GameStart+" --plugin "+Encode(PluginPath)+" --manager "+Encode(ManagerPath)+" --manager-pid "+ManagerPid;
    internal static HostArguments Parse(string[] a)
    {
        if(a.Length!=10||a[0]!="--game"||a[2]!="--start"||a[4]!="--plugin"||a[6]!="--manager"||a[8]!="--manager-pid")throw new ArgumentException("Unsupported host arguments.");
        var result=new HostArguments{GamePid=int.Parse(a[1],CultureInfo.InvariantCulture),GameStart=long.Parse(a[3],CultureInfo.InvariantCulture),PluginPath=Decode(a[5]),ManagerPath=Decode(a[7]),ManagerPid=int.Parse(a[9],CultureInfo.InvariantCulture)};
        if(result.GamePid<=0||result.ManagerPid<=0||result.GameStart<=0||result.GameStart>DateTime.MaxValue.Ticks||!Path.IsPathRooted(result.PluginPath)||!Path.IsPathRooted(result.ManagerPath)||result.PluginPath.IndexOf('\0')>=0||result.ManagerPath.IndexOf('\0')>=0||!Path.GetFileName(result.PluginPath).Equals("OfflineTeamBuilderRuntime.dll",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Invalid host identity.");
        return result;
    }
    internal void Apply(){RuntimeHostIdentity.PluginPath=Path.GetFullPath(PluginPath);RuntimeHostIdentity.ManagerPath=Path.GetFullPath(ManagerPath);RuntimeHostIdentity.ManagerPid=ManagerPid;RuntimeHostIdentity.Detached=true;}
    internal static bool Matches(Process target,int pid,long ticks,string name=null)
    {try{return target.Id==pid&&!target.HasExited&&target.StartTime.ToUniversalTime().Ticks==ticks&&(name==null||target.ProcessName.Equals(name,StringComparison.OrdinalIgnoreCase));}catch{return false;}}
}
internal sealed class HostLease : IDisposable
{
    private readonly Mutex mutex;
    private HostLease(Mutex value){mutex=value;}
    internal static HostLease Acquire(int pid,long ticks)
    {
        var m=new Mutex(false,"Local\\Ace.OfflineTeamBuilder.Host."+pid+"."+ticks);bool owned;
        try{owned=m.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
        if(!owned){m.Dispose();return null;}return new HostLease(m);
    }
    public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}
}
