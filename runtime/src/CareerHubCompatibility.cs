using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace OfflineTeamBuilderRuntime;

// A verified Midgame Save build remains the physical CareerHub owner. Its next-call
// cell is the logical hook slot for our two layers: MGS -> assets -> saved-map
// lifecycle -> stock. No MGS code patch, extra hub notification, or job cancel.
internal static class CareerHubCompatibility
{
    internal const string SupportedHash="cddcbe5e48918168bf474e69b1e903a66786830c793a21200d8e4f103b668338";
    internal const ulong CallbackRva=0xEFD0, NextCellRva=0x84A00, SlotCellRva=0x849F8, InstalledRva=0x849E8;
    internal const string CallbackHex="4883ec28488b05255a07004885c07402ffd0f083051a59070001b801000000870593a30300488b0d34d409004885c9740f4883c42848ff25d4d529000f1f40004883c428c3";
    internal sealed class Layout
    {
        internal readonly string Hash,Version,CallbackHex;
        internal readonly ulong CallbackRva,NextCellRva,SlotCellRva,InstalledRva,CounterRva;
        internal Layout(string hash,string version,ulong callback,ulong next,ulong slot,ulong installed,ulong counter,string code) {
            Hash=hash;Version=version;CallbackRva=callback;NextCellRva=next;SlotCellRva=slot;InstalledRva=installed;CounterRva=counter;CallbackHex=code;
        }
    }
    internal static readonly Layout Legacy=new Layout(SupportedHash,"0.2.2",CallbackRva,NextCellRva,SlotCellRva,InstalledRva,0x84904,CallbackHex);
    internal static readonly Layout Version024=new Layout(
        "017b2b426815b986a44a8265d6b9d2ce8ebaf2d0b2ef74486116bc48c671922a","0.2.4",
        0x10270,0x89A00,0x899F8,0x899E8,0x89904,
        "4883ec28488b05859707004885c07402ffd0f083057a96070001b8010000008705f3c00300488b0d74160a004885c9740f4883c42848ff255c232a000f1f40004883c428c3");
    internal static Layout LayoutFor(string hash) {
        if(string.Equals(hash,Legacy.Hash,StringComparison.OrdinalIgnoreCase))return Legacy;
        if(string.Equals(hash,Version024.Hash,StringComparison.OrdinalIgnoreCase))return Version024;
        return null;
    }
    internal sealed class Module
    {
        internal ulong Base;
        internal string Hash;
    }
    internal sealed class Contract
    {
        internal bool Ready, Waiting;
        internal ulong PhysicalSlot, StorageSlot, Target;
        internal string Reason;
    }
    private static readonly object gate=new object();
    private static readonly Dictionary<string,Module> verified=new Dictionary<string,Module>();
    private static int sessionPid;
    private static Module sessionModule;

    internal static bool LoaderRequested(string plugins)
    {
        if(!File.Exists(Path.Combine(plugins,"MidgameSavePlugin.dll")))return false;
        string config=Path.Combine(plugins,"MidgameSave","plugin.cfg");
        return ReadFlag(config,"plugin_enabled",true);
    }
    internal static bool ReadFlag(string path,string name,bool fallback)
    {
        if(!File.Exists(path))return fallback;
        foreach(string raw in File.ReadAllLines(path)) {
            string line=raw.Split(';')[0].Trim();int at=line.IndexOf('=');
            if(at<0||!string.Equals(line.Substring(0,at).Trim(),name,StringComparison.OrdinalIgnoreCase))continue;
            int value;if(!int.TryParse(line.Substring(at+1).Trim(),out value))throw new InvalidDataException("Invalid Midgame Save setting: "+name);
            fallback=value!=0;
        }
        return fallback;
    }
    internal static string LauncherDeclined(string plugins,int pid,DateTime startUtc)
    {
        string path=Path.Combine(plugins,"MidgameSave","midgamesave-status.json");
        try {
            if(!File.Exists(path)||new FileInfo(path).Length>131072)return null;
            var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
            object source,process,state,updated;
            if(data==null||!data.TryGetValue("source",out source)||Convert.ToString(source)!="plugin"||
                !data.TryGetValue("pid",out process)||Convert.ToInt32(process)!=pid||
                !data.TryGetValue("state",out state)||!data.TryGetValue("updated",out updated))return null;
            DateTime timestamp;
            if(!DateTime.TryParse(Convert.ToString(updated),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out timestamp)||timestamp<startUtc.ToUniversalTime())return null;
            // Failed/timed-out injection is NOT definitive: its LoadLibrary
            // thread could still complete later. Only deliberate no-load states.
            string value=Convert.ToString(state);
            return value=="standing_aside"||value=="disabled"?value:null;
        }catch(IOException){return null;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}catch(FormatException){return null;}
    }
    internal static Module FindModule(Process game)
    {
        var matches=game.Modules.Cast<ProcessModule>().Where(m=>string.Equals(m.ModuleName,"midgamesave.dll",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length==0)return null;
        if(matches.Length!=1)throw new InvalidDataException("Multiple Midgame Save modules; refusing shared hook ownership.");
        ProcessModule module=matches[0];ulong address=(ulong)module.BaseAddress.ToInt64();
        string key=game.Id+":"+game.StartTime.ToUniversalTime().Ticks+":"+address;
        lock(gate) {
            Module cached;if(verified.TryGetValue(key,out cached))return cached;
            string hash;
            using(var sha=SHA256.Create())using(var file=File.OpenRead(module.FileName))
                hash=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();
            if(LayoutFor(hash)==null)throw new InvalidDataException("Unsupported Midgame Save binary for shared CareerHub: "+hash+". Expected a verified 0.2.2 or 0.2.4 native DLL.");
            cached=new Module {Base=address,Hash=hash};verified.Add(key,cached);return cached;
        }
    }
    private static ulong Q(Func<ulong,int,byte[]> read,ulong at)
    {
        byte[] bytes=read(at,8);if(bytes==null||bytes.Length!=8)throw new IOException("Shared CareerHub pointer unavailable.");
        return BitConverter.ToUInt64(bytes,0);
    }
    internal static Contract Inspect(Func<ulong,int,byte[]> read,ulong image,GameProfile profile,Module module,bool requested)
    {
        ulong physical=image+(ulong)profile.Rva("ServiceVtable")+0x4D8;
        var result=new Contract {PhysicalSlot=physical,StorageSlot=physical};
        try {
            ulong current=Q(read,physical);result.Target=current;
            if(module==null) {
                if(requested){result.Waiting=true;result.Reason="Waiting for Midgame Save's native module and hub signal.";return result;}
                result.Ready=true;result.Reason="Exclusive stock/TeamBuilder CareerHub slot.";return result;
            }
            Layout layout=LayoutFor(module.Hash);
            if(layout==null){result.Reason="Unverified Midgame Save module identity.";return result;}
            if(current!=module.Base+layout.CallbackRva) {
                result.Waiting=current==image+(ulong)profile.Rva("CareerHub");
                result.Reason=result.Waiting?"Waiting for Midgame Save's completed hub hook.":"Unexpected CareerHub owner while Midgame Save is active.";
                return result;
            }
            byte[] code=read(current,GameProfile.Hex(layout.CallbackHex).Length);
            if(code==null||!code.SequenceEqual(GameProfile.Hex(layout.CallbackHex))){result.Reason="Midgame Save hub callback bytes changed.";return result;}
            byte[] installed=read(module.Base+layout.InstalledRva,4);
            if(installed==null||installed.Length!=4||BitConverter.ToUInt32(installed,0)!=1) {
                result.Waiting=true;result.Reason="Midgame Save hub publication is not complete.";return result;
            }
            if(Q(read,module.Base+layout.SlotCellRva)!=physical){result.Reason="Midgame Save hub slot metadata does not match this game's CareerHub.";return result;}
            ulong next=Q(read,module.Base+layout.NextCellRva);
            if(next<0x10000||next>=0x7FFFFFFFFFFFUL||next==current||next==module.Base+layout.NextCellRva) {
                result.Reason="Invalid or cyclic Midgame Save hub chain.";return result;
            }
            // Read again around the non-atomic observation; never reuse a stale
            // outer slot/cell after Midgame Save begins cleanup or changes owner.
            if(Q(read,physical)!=current||Q(read,module.Base+layout.NextCellRva)!=next||Q(read,module.Base+layout.SlotCellRva)!=physical) {
                result.Waiting=true;result.Reason="CareerHub chain changed during validation.";return result;
            }
            result.StorageSlot=module.Base+layout.NextCellRva;result.Target=next;result.Ready=true;
            result.Reason="Verified Midgame Save "+layout.Version+" outer hub signal; TeamBuilder uses its next-call cell.";
        }catch(IOException ex){result.Waiting=true;result.Reason=ex.Message;}
        return result;
    }
    internal static void BeginSession(int pid,Module module)
    {
        lock(gate){sessionPid=pid;sessionModule=module;}
    }
    internal static ulong Storage(IntPtr handle,ulong image,ulong physical)
    {
        Module module;int pid=unchecked((int)GetProcessId(handle));
        lock(gate){module=pid==sessionPid?sessionModule:null;}
        if(module==null)return physical;
        Func<ulong,int,byte[]> read=(at,n)=>Read(handle,at,n);
        var contract=Inspect(read,image,GameProfile.Current,module,true);
        if(!contract.Ready||contract.PhysicalSlot!=physical)throw new InvalidOperationException("SHARED_CAREER_HOOK_CHANGED: "+contract.Reason);
        return contract.StorageSlot;
    }
    internal static byte[] Read(IntPtr handle,ulong at,int length)
    {
        if(at<0x10000||at>=0x7FFFFFFFFFFFUL||length<=0||length>0x10000)return null;
        byte[] bytes=new byte[length];UIntPtr got;
        return ReadProcessMemory(handle,new IntPtr(unchecked((long)at)),bytes,new UIntPtr((uint)length),out got)&&got.ToUInt64()==(ulong)length?bytes:null;
    }
    [DllImport("kernel32.dll")]private static extern uint GetProcessId(IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool ReadProcessMemory(IntPtr process,IntPtr at,[Out]byte[] data,UIntPtr count,out UIntPtr got);
}
