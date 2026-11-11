using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace OfflineTeamBuilderRuntime;

// One installation DLL; its verified native resource is published before use.
// No crash collector, debugger, remote executable buffer or extra MMC action.
internal sealed class NativeAssetGuards : IDisposable
{
    internal const uint StatusMagic=0x4F544247;
    internal const int StatusSize=128;
    private readonly Process game;
    private readonly RuntimeDiagnostics log;
    private IntPtr process,allocation;
    private bool inFlight;
    private ulong statusAddress,helperBase;
    private int helperSize;
    internal string NativeLogPath { get; private set; }
    internal NativeAssetGuards(Process game,RuntimeDiagnostics log) { this.game=game;this.log=log; }

    internal static string HelperPath { get { return RuntimePaths.Current.Resolve("Native/"+ExpectedHash+"/OfflineTeamBuilderNative.dll"); } }
    internal static string ExpectedHash {
        get {
            using(var stream=typeof(RuntimeAction).Assembly.GetManifestResourceStream("OfflineTeamBuilderRuntime.NativeManifest.json"))
            using(var reader=new StreamReader(stream??throw new InvalidDataException("Native helper manifest is missing."))) {
                var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
                if(Convert.ToInt32(data["abi"])!=1||Convert.ToInt32(data["statusSize"])!=StatusSize||
                    Convert.ToInt32(data["registryEntries"])!=14||Convert.ToString(data["file"])!="OfflineTeamBuilderNative.dll")
                    throw new InvalidDataException("Native helper ABI manifest mismatch.");
                return Convert.ToString(data["sha256"]);
            }
        }
    }
    internal static void ValidateHelper(string path,string expected) {
        if(!File.Exists(path))throw new FileNotFoundException("Verified native helper file is missing.",path);
        using(var file=File.OpenRead(path))using(var sha=SHA256.Create()) {
            string actual=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");
            if(!string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Native helper differs from this plugin build. Restart both game and MMC; review the specific cached file before retrying.");
        }
    }
    private ulong RemoteSystemExport(string name) {
        game.Refresh();
        var modules=game.Modules.Cast<ProcessModule>().Where(m=>string.Equals(m.ModuleName,"kernel32.dll",StringComparison.OrdinalIgnoreCase)||
            string.Equals(m.ModuleName,"kernelbase.dll",StringComparison.OrdinalIgnoreCase)||string.Equals(m.ModuleName,"ntdll.dll",StringComparison.OrdinalIgnoreCase))
            .Select(m=>new SystemExportModule {Name=m.ModuleName,Path=m.FileName,Base=unchecked((ulong)m.BaseAddress.ToInt64()),Size=m.ModuleMemorySize}).ToArray();
        var resolved=NativeSystemExports.Resolve(Read,modules,Environment.SystemDirectory,"kernel32.dll",name);
        ValidateSystemExportPage(process,resolved);
        log.Write("INFO","WINDOWS_EXPORT_RESOLVED "+resolved.Chain+" rva=0x"+resolved.Rva.ToString("X"));
        return resolved.Address;
    }
    internal static void ValidateSystemExportPage(IntPtr target,SystemExportAddress resolved) {
        MemoryRegion region;
        if(VirtualQueryEx(target,new IntPtr(unchecked((long)resolved.Address)),out region,(UIntPtr)Marshal.SizeOf(typeof(MemoryRegion)))==UIntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"System export memory query");
        ulong start=unchecked((ulong)region.BaseAddress.ToInt64());
        if(resolved.Address<start||resolved.Address-start>=region.RegionSize.ToUInt64()||
            !NativeSystemExports.ExecutableImage(unchecked((ulong)region.AllocationBase.ToInt64()),resolved.Module.Base,region.State,region.Protect,region.Type))
            throw new InvalidDataException("System export entry is not executable memory in its owning image: "+resolved.Chain);
    }
    private uint Invoke(ulong entry,ulong parameter) {
        IntPtr thread=CreateRemoteThread(process,IntPtr.Zero,UIntPtr.Zero,new IntPtr(unchecked((long)entry)),new IntPtr(unchecked((long)parameter)),0,out _);
        if(thread==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Native helper thread");
        inFlight=true;
        try {
            if(WaitForSingleObject(thread,15000)!=0)throw new TimeoutException("Native helper did not finish initialization. Restart the game before retrying; its argument buffer is retained until exit.");
            inFlight=false;
            if(!GetExitCodeThread(thread,out uint code))throw new Win32Exception(Marshal.GetLastWin32Error());
            return code;
        } finally { CloseHandle(thread); }
    }
    private void Write(ulong at,byte[] bytes) {
        if(!WriteProcessMemory(process,new IntPtr(unchecked((long)at)),bytes,(UIntPtr)bytes.Length,out UIntPtr written)||written.ToUInt64()!=(ulong)bytes.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Native helper argument write");
    }
    private byte[] Read(ulong at,int size) {
        byte[] bytes=new byte[size];
        if(!ReadProcessMemory(process,new IntPtr(unchecked((long)at)),bytes,(UIntPtr)size,out UIntPtr read)||read.ToUInt64()!=(ulong)size)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Native helper status read");
        return bytes;
    }
    internal static byte[] Context(ulong image,ulong logPath) {
        byte[] bytes=new byte[40];
        Buffer.BlockCopy(BitConverter.GetBytes(1u),0,bytes,0,4);Buffer.BlockCopy(BitConverter.GetBytes(40u),0,bytes,4,4);
        Buffer.BlockCopy(BitConverter.GetBytes(image),0,bytes,8,8);Buffer.BlockCopy(BitConverter.GetBytes(uint.MaxValue),0,bytes,24,4);
        Buffer.BlockCopy(BitConverter.GetBytes(logPath),0,bytes,32,8);return bytes;
    }
    internal void Install() {
        string helper=HelperPath;
        log.Write("INFO","NATIVE_PAYLOAD_CHECK path="+helper+" expectedSha256="+ExpectedHash+" cached="+File.Exists(helper));
        helper=NativeHelperDeployment.Ensure(RuntimePaths.NativeDirectory,NativeHelperDeployment.Payload(),ExpectedHash);
        log.Write("INFO","NATIVE_PAYLOAD_READY path="+helper);
        uint initializeRva=NativePe.ExportRva(File.ReadAllBytes(helper),"Initialize");
        process=OpenProcess(0x43A,false,(uint)game.Id);
        if(process==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Native guard process access");
        allocation=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)0x4000,0x3000,4);
        if(allocation==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Native guard arguments");
        ulong memory=unchecked((ulong)allocation.ToInt64());
        string directory=RuntimePaths.NativeLogsDirectory;Directory.CreateDirectory(directory);
        NativeLogPath=Path.Combine(directory,game.Id+"-"+DateTime.UtcNow.Ticks+".log");
        byte[] libraryPath=Encoding.Unicode.GetBytes(helper+"\0"),logPath=Encoding.Unicode.GetBytes(NativeLogPath+"\0");
        if(libraryPath.Length>0x1000||logPath.Length>0x1000)throw new PathTooLongException("Native helper path exceeds its argument region.");
        Write(memory,libraryPath);Write(memory+0x2000,logPath);
        log.Write("INFO","NATIVE_LOAD_REQUEST pid="+game.Id+" path="+helper);
        uint loaderResult=Invoke(RemoteSystemExport("LoadLibraryW"),memory);
        log.Write("INFO","NATIVE_LOAD_RETURN low32=0x"+loaderResult.ToString("X8"));
        ProcessModule loaded=null;
        for(int attempt=0;attempt<20&&loaded==null;attempt++) {
            game.Refresh();loaded=game.Modules.Cast<ProcessModule>().FirstOrDefault(m=>string.Equals(Path.GetFullPath(m.FileName),helper,StringComparison.OrdinalIgnoreCase));
            if(loaded==null)Thread.Sleep(50);
        }
        if(loaded==null)throw new InvalidOperationException("Native helper did not load from its expected user-data path: "+helper+". Loader return low32=0x"+loaderResult.ToString("X8")+"; filePresent="+File.Exists(helper));
        helperBase=unchecked((ulong)loaded.BaseAddress.ToInt64());helperSize=loaded.ModuleMemorySize;
        ulong image=unchecked((ulong)game.MainModule.BaseAddress.ToInt64());
        var timer=Stopwatch.StartNew();uint lastCode=uint.MaxValue;
        do {
            Write(memory+0x1000,Context(image,memory+0x2000));
            uint code=Invoke(helperBase+initializeRva,memory+0x1000);
            if(code!=lastCode){log.Write("INFO","NATIVE_INIT_RESULT code="+code+" pid="+game.Id);lastCode=code;}
            byte[] context=Read(memory+0x1000,40);statusAddress=BitConverter.ToUInt64(context,16);
            if(code==0) {
                if(helperSize<StatusSize||statusAddress<helperBase||statusAddress-helperBase>(ulong)(helperSize-StatusSize))throw new InvalidDataException("Native status pointer is outside its pinned module.");
                var health=ParseStatus(Read(statusAddress,StatusSize));
                if(!(bool)health["ready"])throw new InvalidDataException("Native guards did not confirm both installations.");
                log.Write("INFO","NATIVE_GUARDS_READY pressure admission + instance-registry synchronization; native log="+NativeLogPath);return;
            }
            if(code!=1)throw new InvalidOperationException("Native guard initialization failed (code "+code+"). Restart the game before retrying; see "+NativeLogPath);
            Thread.Sleep(500);
        } while(RuntimeAction.Alive(game)&&timer.Elapsed.TotalSeconds<90);
        throw new InvalidOperationException("Native guard code contracts did not become available; no import runtime started.");
    }
    internal static Dictionary<string,object> ParseStatus(byte[] data) {
        if(data==null||data.Length!=StatusSize||BitConverter.ToUInt32(data,0)!=StatusMagic||BitConverter.ToUInt32(data,4)!=1||BitConverter.ToUInt32(data,8)!=StatusSize)
            throw new InvalidDataException("Native guard status ABI mismatch.");
        return new Dictionary<string,object> {
            {"ready",BitConverter.ToUInt32(data,12)==3&&BitConverter.ToUInt32(data,16)==1&&BitConverter.ToUInt32(data,20)==0},
            {"installed",BitConverter.ToUInt32(data,12)}, {"failure",BitConverter.ToUInt32(data,20)},
            {"headroom",BitConverter.ToUInt32(data,24)}, {"deadlineMs",BitConverter.ToUInt32(data,28)},
            {"capacityCalls",BitConverter.ToInt64(data,32)}, {"capacityPressure",BitConverter.ToInt64(data,40)},
            {"capacityWaits",BitConverter.ToInt64(data,48)}, {"capacityTimeouts",BitConverter.ToInt64(data,56)}, {"capacityWaitMs",BitConverter.ToInt64(data,64)},
            {"registryCalls",BitConverter.ToInt64(data,72)}, {"registryContentions",BitConverter.ToInt64(data,80)}, {"registryWaitMs",BitConverter.ToInt64(data,88)},
            {"activeReads",BitConverter.ToInt64(data,96)}, {"activeWrites",BitConverter.ToInt64(data,104)}, {"writeDuringRead",BitConverter.ToInt64(data,112)},
            {"observation","Non-atomic native counters; not a guarantee of rendered assets or gameplay readiness."}
        };
    }
    internal Dictionary<string,object> CaptureHealth() {
        var health=ParseStatus(Read(statusAddress,StatusSize));health["statusAddress"]="0x"+statusAddress.ToString("X");health["nativeLog"]=NativeLogPath;return health;
    }
    public void Dispose() {
        // Never free arguments still used by a timed-out native thread. Native
        // code/locks remain pinned until game exit, including if MMC closes.
        if(process!=IntPtr.Zero) {
            if(allocation!=IntPtr.Zero&&!inFlight)VirtualFreeEx(process,allocation,UIntPtr.Zero,0x8000);
            allocation=IntPtr.Zero;CloseHandle(process);process=IntPtr.Zero;
        }
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,UIntPtr size,uint type,uint protect);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool VirtualFreeEx(IntPtr process,IntPtr address,UIntPtr size,uint type);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] data,UIntPtr size,out UIntPtr written);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ReadProcessMemory(IntPtr process,IntPtr address,[Out] byte[] data,UIntPtr size,out UIntPtr read);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr CreateRemoteThread(IntPtr process,IntPtr attributes,UIntPtr stackSize,IntPtr start,IntPtr parameter,uint flags,out uint tid);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetExitCodeThread(IntPtr thread,out uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryRegion {
        internal IntPtr BaseAddress,AllocationBase;
        internal uint AllocationProtect;
        internal UIntPtr RegionSize;
        internal uint State,Protect,Type;
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern UIntPtr VirtualQueryEx(IntPtr process,IntPtr address,out MemoryRegion info,UIntPtr size);
}

internal static class NativePe
{
    internal static uint ExportRva(byte[] bytes,string name) {
        if(bytes==null||bytes.Length<0x100||BitConverter.ToUInt16(bytes,0)!=0x5A4D)throw new InvalidDataException("Native helper PE header");
        int pe=BitConverter.ToInt32(bytes,0x3C);
        if(pe<0||pe>bytes.Length-0x100||BitConverter.ToUInt32(bytes,pe)!=0x4550||BitConverter.ToUInt16(bytes,pe+4)!=0x8664||BitConverter.ToUInt16(bytes,pe+24)!=0x20B)
            throw new InvalidDataException("Native helper must be AMD64 PE32+.");
        int count=BitConverter.ToUInt16(bytes,pe+6),optional=BitConverter.ToUInt16(bytes,pe+20),table=pe+24+optional;
        Func<uint,int,int> offset=(rva,size)=> {
            for(int i=0;i<count;i++) {
                int at=checked(table+i*40);if(at>bytes.Length-40)throw new InvalidDataException("Native PE sections");
                uint start=BitConverter.ToUInt32(bytes,at+12),raw=BitConverter.ToUInt32(bytes,at+20),length=BitConverter.ToUInt32(bytes,at+16);
                if(rva>=start&&(ulong)rva-start+(ulong)size<=length) {
                    ulong found=(ulong)raw+rva-start;
                    if(found+(ulong)size>(ulong)bytes.Length)throw new InvalidDataException("Native PE data bounds");return checked((int)found);
                }
            }
            throw new InvalidDataException("Native export RVA outside file sections.");
        };
        uint export=BitConverter.ToUInt32(bytes,pe+24+112),exportSize=BitConverter.ToUInt32(bytes,pe+24+116);
        int directory=offset(export,40);uint names=BitConverter.ToUInt32(bytes,directory+24);
        if(names>4096)throw new InvalidDataException("Native export count");
        uint functions=BitConverter.ToUInt32(bytes,directory+28),nameTable=BitConverter.ToUInt32(bytes,directory+32),ordinals=BitConverter.ToUInt32(bytes,directory+36);
        for(uint i=0;i<names;i++) {
            uint text=BitConverter.ToUInt32(bytes,offset(checked(nameTable+i*4),4));var chars=new List<byte>();
            for(uint n=0;n<256;n++){byte c=bytes[offset(checked(text+n),1)];if(c==0)break;chars.Add(c);}
            if(Encoding.ASCII.GetString(chars.ToArray())!=name)continue;
            uint ordinal=BitConverter.ToUInt16(bytes,offset(checked(ordinals+i*2),2));
            if(ordinal>=BitConverter.ToUInt32(bytes,directory+20))throw new InvalidDataException("Native export ordinal");
            uint result=BitConverter.ToUInt32(bytes,offset(checked(functions+ordinal*4),4));
            if(result>=export&&(ulong)result-export<exportSize)throw new InvalidDataException("Forwarded native initializer is not supported.");
            offset(result,1);return result;
        }
        throw new InvalidDataException("Native initializer export missing: "+name);
    }
}
