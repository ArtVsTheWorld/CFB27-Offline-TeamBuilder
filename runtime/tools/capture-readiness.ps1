#requires -PSEdition Desktop
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RuntimeDll,
    [Parameter(Mandatory=$true)][string]$OutputFile
)
$ErrorActionPreference='Stop'
$dll=(Resolve-Path -LiteralPath $RuntimeDll).Path
$output=[IO.Path]::GetFullPath($OutputFile)
if(Test-Path -LiteralPath $output){throw 'Refusing to overwrite an existing readiness capture.'}
$games=@(Get-Process -Name CollegeFB27 -ErrorAction Stop)
if($games.Count -ne 1){throw 'Expected exactly one running game.'}
$game=$games[0]
try {
    # This helper has a READ/QUERY handle only. It never loads the candidate
    # into MMC or the game, invokes a native game function, suspends a thread,
    # installs a hook, or writes any remote memory.
    Add-Type -ReferencedAssemblies System.Web.Extensions -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
public sealed class TeamReadinessCapture : IDisposable {
    private IntPtr handle;
    private TeamReadinessCapture(int pid) {
        handle=OpenProcess(0x410,false,pid);
        if(handle==IntPtr.Zero)throw new IOException("Read-only process access denied.");
    }
    private byte[] Read(ulong at,int count) {
        if(at<0x10000||at>=0x7FFFFFFFFFFFUL||count<=0||count>0x4000||(ulong)count>0x7FFFFFFFFFFFUL-at)
            throw new IOException("Read bounds.");
        byte[] data=new byte[count];UIntPtr got;
        if(!ReadProcessMemory(handle,new IntPtr(unchecked((long)at)),data,new UIntPtr((uint)count),out got)||got.ToUInt64()!=(ulong)count)
            throw new IOException("Read-only memory unavailable.");
        return data;
    }
    private ulong Q(ulong at) { return BitConverter.ToUInt64(Read(at,8),0); }
    public static string Capture(string dll,int pid,ulong image) {
        var asm=Assembly.LoadFrom(dll);
        var type=asm.GetType("OfflineTeamBuilderRuntime.GameProfile",true);
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        object profile=type.GetProperty("Current",flags).GetValue(null,null);
        Func<string,ulong> rva=name=>Convert.ToUInt64(type.GetMethod("Rva",flags).Invoke(profile,new object[]{name}));
        using(var memory=new TeamReadinessCapture(pid)) {
            ulong service=memory.Q(image+rva("ServiceGlobal"));
            if(memory.Q(service)!=image+rva("ServiceVtable"))throw new IOException("Service contract mismatch.");
            ulong relay=memory.Q(image+rva("ServiceVtable")+0x4D8);
            byte[] prefix=memory.Read(relay,12);
            byte[] expected={0x53,0x55,0x56,0x48,0x83,0xEC,0x60,0x48,0x8B,0xD9,0x48,0xBE};
            for(int n=0;n<expected.Length;n++)if(prefix[n]!=expected[n])throw new IOException("Expected installed asset Career relay was not found. Prefix="+BitConverter.ToString(prefix));
            ulong status=memory.Q(relay+12);
            var reader=asm.GetType("OfflineTeamBuilderRuntime.TeamReadinessReader",true).GetMethod("Capture",flags);
            object snapshot=reader.Invoke(null,new object[]{new Func<ulong,int,byte[]>(memory.Read),image,status,profile});
            return new JavaScriptSerializer().Serialize(new {capturedUtc=DateTime.UtcNow.ToString("O"),pid=pid,readOnly=true,readerVersion=asm.GetName().Version.ToString(),readiness=snapshot});
        }
    }
    public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
    [DllImport("kernel32.dll",SetLastError=true)]private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool ReadProcessMemory(IntPtr process,IntPtr address,[Out]byte[] data,UIntPtr count,out UIntPtr got);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr handle);
}
'@
    $json=[TeamReadinessCapture]::Capture($dll,$game.Id,[uint64]$game.MainModule.BaseAddress.ToInt64())
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
    [IO.File]::WriteAllText($output,$json)
    $result=$json | ConvertFrom-Json
    $result.readiness | Select-Object observation,currentCareerOwned,expectedTeams,runtimeRecordsPresent,nativeQueueCount,olderEpochPluginJobs,reason | Format-List
    Write-Host "Read-only capture: $output"
} finally {$game.Dispose()}
