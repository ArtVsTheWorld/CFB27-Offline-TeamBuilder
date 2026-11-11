using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OfflineTeamBuilderRuntime;

internal sealed class SystemExportModule
{
    internal string Name,Path;
    internal ulong Base;
    internal int Size;
}

internal sealed class SystemExportAddress
{
    internal SystemExportModule Module;
    internal uint Rva;
    internal ulong Address;
    internal string Chain;
}

// Read the target's own published export entries. Do not compare function
// bodies across processes, follow detour instructions, or modify system DLLs.
// PE export/forwarder layout: Microsoft PE Format, Export Address Table.
internal static class NativeSystemExports
{
    private static string ModuleName(string name) {
        if(string.IsNullOrEmpty(name)||name.IndexOfAny(new[]{'\\','/',':'})>=0)
            throw new InvalidDataException("Invalid system export module name.");
        string result=name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)?name.ToLowerInvariant():name.ToLowerInvariant()+".dll";
        if(result!="kernel32.dll"&&result!="kernelbase.dll"&&result!="ntdll.dll")
            throw new InvalidDataException("Unsupported system export forwarder host: "+result);
        return result;
    }

    internal static bool ExecutableImage(ulong allocationBase,ulong expectedBase,uint state,uint protect,uint type) {
        uint access=protect&0xff;
        return allocationBase==expectedBase&&state==0x1000&&type==0x1000000&&(protect&0x100)==0&&
            (access==0x10||access==0x20||access==0x40||access==0x80);
    }

    internal static SystemExportAddress Resolve(Func<ulong,int,byte[]> read,IEnumerable<SystemExportModule> modules,string systemDirectory,string module,string symbol) {
        var loaded=modules.ToArray();var seen=new HashSet<string>(StringComparer.Ordinal);var chain=new List<string>();
        string systemRoot=System.IO.Path.GetFullPath(systemDirectory);
        for(int depth=0;depth<12;depth++) {
            string name=ModuleName(module),key=name+"!"+symbol;
            if(!seen.Add(key))throw new InvalidDataException("Cyclic system export forwarder: "+key);
            chain.Add(key);
            var matches=loaded.Where(m=>string.Equals(m.Name,name,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length!=1)throw new InvalidDataException("System export owner is missing or ambiguous: "+name);
            var owner=matches[0];
            if(!string.Equals(System.IO.Path.GetFullPath(owner.Path),System.IO.Path.Combine(systemRoot,name),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("System export owner is outside the Windows system directory: "+name);
            var image=new Image(read,owner);
            uint entrySlot;uint rva=image.Lookup(symbol,out entrySlot);
            if(rva==0)throw new InvalidDataException("System export has no implementation: "+key);
            if(rva>=image.Export&&rva-image.Export<image.ExportSize) {
                string forwarder=image.String(rva,Math.Min(512,(int)(image.ExportSize-(rva-image.Export))));
                int dot=forwarder.LastIndexOf('.');
                if(dot<=0||dot==forwarder.Length-1)throw new InvalidDataException("Invalid system export forwarder: "+key);
                image.Confirm(entrySlot,rva);
                module=forwarder.Substring(0,dot);symbol=forwarder.Substring(dot+1);continue;
            }
            if(!image.Executable(rva))throw new InvalidDataException("System export is not in executable image code: "+key);
            image.Read(rva,1); // Require the entry to be readable; no opcode equality or detour following.
            image.Confirm(entrySlot,rva);
            return new SystemExportAddress {Module=owner,Rva=rva,Address=checked(owner.Base+rva),Chain=string.Join(" -> ",chain)};
        }
        throw new InvalidDataException("System export forwarder depth exceeded.");
    }

    private sealed class Image
    {
        private readonly Func<ulong,int,byte[]> reader;
        private readonly SystemExportModule module;
        private readonly byte[] sections;
        private readonly byte[] exportHeader;
        private readonly uint directoryLocation;
        private readonly uint functionCount,nameCount,ordinalBase,functionTable,nameTable,ordinalTable;
        internal readonly uint Export,ExportSize;
        internal Image(Func<ulong,int,byte[]> read,SystemExportModule owner) {
            reader=read;module=owner;
            if(owner.Base<0x10000||owner.Size<0x1000||owner.Size>0x20000000||owner.Base>ulong.MaxValue-(ulong)owner.Size)
                throw new InvalidDataException("Invalid system module image bounds.");
            byte[] dos=Read(0,0x40);
            if(BitConverter.ToUInt16(dos,0)!=0x5a4d)throw new InvalidDataException("System module DOS header mismatch.");
            uint pe=BitConverter.ToUInt32(dos,0x3c);byte[] header=Read(pe,24);
            int count=BitConverter.ToUInt16(header,6),optionalSize=BitConverter.ToUInt16(header,20);
            if(BitConverter.ToUInt32(header,0)!=0x4550||BitConverter.ToUInt16(header,4)!=0x8664||count<1||count>96||optionalSize<240||optionalSize>4096)
                throw new InvalidDataException("System module AMD64 PE contract mismatch.");
            byte[] optional=Read(Add(pe,24),optionalSize);
            if(BitConverter.ToUInt16(optional,0)!=0x20b||BitConverter.ToUInt32(optional,56)!=(uint)owner.Size||BitConverter.ToUInt32(optional,108)<1)
                throw new InvalidDataException("System module image-size or export-directory contract mismatch.");
            Export=BitConverter.ToUInt32(optional,112);ExportSize=BitConverter.ToUInt32(optional,116);
            directoryLocation=Add(Add(pe,24),112);
            if(Export==0||ExportSize<40)throw new InvalidDataException("System module export directory missing.");
            Bounds(Export,ExportSize);
            sections=Read(Add(Add(pe,24),(uint)optionalSize),count*40);
            for(int i=0;i<sections.Length;i+=40) {
                uint size=Math.Max(BitConverter.ToUInt32(sections,i+8),BitConverter.ToUInt32(sections,i+16));
                if(size!=0)Bounds(BitConverter.ToUInt32(sections,i+12),size);
            }
            byte[] exports=Read(Export,40);
            exportHeader=exports;
            ordinalBase=BitConverter.ToUInt32(exports,16);functionCount=BitConverter.ToUInt32(exports,20);nameCount=BitConverter.ToUInt32(exports,24);
            functionTable=BitConverter.ToUInt32(exports,28);nameTable=BitConverter.ToUInt32(exports,32);ordinalTable=BitConverter.ToUInt32(exports,36);
            if(functionCount==0||functionCount>65536||nameCount>65536)throw new InvalidDataException("System export count exceeds bounds.");
            Bounds(functionTable,checked(functionCount*4));
            if(nameCount!=0){Bounds(nameTable,checked(nameCount*4));Bounds(ordinalTable,checked(nameCount*2));}
        }
        private static uint Add(uint a,uint b) {return checked(a+b);}
        private void Bounds(uint rva,uint size) {
            if(rva>=(uint)module.Size||size>(uint)module.Size-rva)throw new InvalidDataException("System export read outside owning image.");
        }
        internal byte[] Read(uint rva,int size) {
            if(size<=0||size>0x40000)throw new InvalidDataException("Invalid system export read size.");
            Bounds(rva,(uint)size);byte[] bytes=reader(checked(module.Base+rva),size);
            if(bytes==null||bytes.Length!=size)throw new InvalidDataException("Incomplete system export read.");return bytes;
        }
        internal uint Dword(uint rva) {return BitConverter.ToUInt32(Read(rva,4),0);}
        internal void Confirm(uint slot,uint entry) {
            byte[] directory=Read(directoryLocation,8);
            if(BitConverter.ToUInt32(directory,0)!=Export||BitConverter.ToUInt32(directory,4)!=ExportSize||
                !Read(Export,40).SequenceEqual(exportHeader)||Dword(slot)!=entry)
                throw new InvalidDataException("System export metadata changed during resolution.");
        }
        internal string String(uint rva,int limit=512) {
            if(limit<1)throw new InvalidDataException("Invalid system export string extent.");
            var bytes=new List<byte>();
            while(bytes.Count<limit) {
                uint at=Add(rva,(uint)bytes.Count);int size=Math.Min(32,Math.Min(limit-bytes.Count,module.Size-checked((int)at)));
                if(size<=0)throw new InvalidDataException("Unterminated system export string.");
                foreach(byte value in Read(at,size)) {
                    if(value==0)return Encoding.ASCII.GetString(bytes.ToArray());
                    if(value<0x21||value>0x7e)throw new InvalidDataException("Invalid system export string.");
                    bytes.Add(value);
                }
            }
            throw new InvalidDataException("System export string exceeds bounds.");
        }
        internal uint Lookup(string name,out uint slot) {
            uint index;
            if(!string.IsNullOrEmpty(name)&&name[0]=='#') {
                uint ordinal;
                if(!uint.TryParse(name.Substring(1),System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out ordinal)||ordinal<ordinalBase||ordinal-ordinalBase>=functionCount)
                    throw new InvalidDataException("System export ordinal outside table.");
                index=ordinal-ordinalBase;
            } else {
                if(string.IsNullOrEmpty(name)||name.Length>256)throw new InvalidDataException("Invalid system export name.");
                int lo=0,hi=checked((int)nameCount)-1,found=-1;
                while(lo<=hi) {
                    int mid=lo+(hi-lo)/2;string current=String(Dword(Add(nameTable,checked((uint)mid*4))));
                    int order=string.CompareOrdinal(current,name);
                    if(order==0){found=mid;break;}
                    if(order<0)lo=mid+1;else hi=mid-1;
                }
                if(found<0)throw new InvalidDataException("System export not found: "+name);
                index=BitConverter.ToUInt16(Read(Add(ordinalTable,checked((uint)found*2)),2),0);
                if(index>=functionCount)throw new InvalidDataException("System export name ordinal outside table.");
            }
            slot=Add(functionTable,checked(index*4));return Dword(slot);
        }
        internal bool Executable(uint rva) {
            for(int i=0;i<sections.Length;i+=40) {
                uint start=BitConverter.ToUInt32(sections,i+12),size=Math.Max(BitConverter.ToUInt32(sections,i+8),BitConverter.ToUInt32(sections,i+16));
                if(rva>=start&&rva-start<size&&(BitConverter.ToUInt32(sections,i+36)&0x20000000)!=0)return true;
            }
            return false;
        }
    }
}
