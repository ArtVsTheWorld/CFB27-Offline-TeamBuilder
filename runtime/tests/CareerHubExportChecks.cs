using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OfflineTeamBuilderRuntime;

internal static class CareerHubExportChecks
{
    private static void Check(bool value,string name)=>Program.Assert(value,"exported shared hub: "+name);
    private sealed class Fixture
    {
        internal const ulong Image=0x140000000,Base=0x71000000,Record=Base+0x3000,Callback=Base+0x5000,Cell=Base+0x7000;
        internal readonly GameProfile Profile=GameProfile.Current;
        internal readonly CareerHubCompatibility.Module Module=new CareerHubCompatibility.Module {Base=Base,Hash="future-build-not-in-tables",ChainRva=0x3000};
        internal readonly Dictionary<ulong,byte> Bytes=new Dictionary<ulong,byte>();
        internal ulong Slot=>Image+(ulong)Profile.Rva("ServiceVtable")+0x4D8;
        internal ulong Stock=>Image+(ulong)Profile.Rva("CareerHub");
        internal Fixture() {
            Put(Record,new byte[56]);Put(Record,Encoding.ASCII.GetBytes("MGSHUB01"));D(Record+8,1);D(Record+12,56);D(Record+16,1);
            Q(Record+24,Slot);Q(Record+32,Callback);Q(Record+40,Cell);Q(Slot,Callback);Q(Cell,Stock);
        }
        internal void Put(ulong at,byte[] data){for(int i=0;i<data.Length;i++)Bytes[at+(ulong)i]=data[i];}
        internal void Q(ulong at,ulong value)=>Put(at,BitConverter.GetBytes(value));
        internal void D(ulong at,uint value)=>Put(at,BitConverter.GetBytes(value));
        internal byte[] Read(ulong at,int length){var data=new byte[length];for(int i=0;i<length;i++)if(!Bytes.TryGetValue(at+(ulong)i,out data[i]))return null;return data;}
        internal CareerHubCompatibility.Contract Capture()=>CareerHubCompatibility.Inspect(Read,Image,Profile,Module,true);
    }
    internal static void CheckAll()
    {
        var f=new Fixture();var before=f.Bytes.ToArray();var c=f.Capture();
        Check(c.Ready&&c.StorageSlot==Fixture.Cell&&c.PhysicalSlot==f.Slot&&c.Target==f.Stock,"unknown binary hash accepted through published descriptor");
        Check(before.All(p=>f.Bytes[p.Key]==p.Value)&&before.Length==f.Bytes.Count,"export inspection never writes partner memory");
        f.Q(f.Slot,f.Stock);c=f.Capture();Check(!c.Ready&&c.Waiting,"TeamBuilder-first startup waits for exported callback installation");
        f.Q(f.Slot,Fixture.Callback);f.D(Fixture.Record+16,0);c=f.Capture();Check(!c.Ready&&c.Waiting,"unpublished record waits");
        f.D(Fixture.Record+16,1);f.D(Fixture.Record+8,0);Check(!f.Capture().Ready,"unsupported zero descriptor version rejected");
        f.D(Fixture.Record+8,1);f.D(Fixture.Record+12,55);Check(!f.Capture().Ready,"short descriptor rejected");
        f.D(Fixture.Record+12,56);f.Bytes[Fixture.Record]^=1;Check(!f.Capture().Ready,"wrong descriptor magic rejected");f.Bytes[Fixture.Record]^=1;
        f.Q(Fixture.Record+24,f.Slot+8);Check(!f.Capture().Ready,"another lifecycle slot cannot be adopted");f.Q(Fixture.Record+24,f.Slot);
        f.Q(f.Slot,Fixture.Base+0x6000);c=f.Capture();Check(!c.Ready&&!c.Waiting,"foreign physical owner refused");f.Q(f.Slot,Fixture.Callback);
        foreach(ulong pointer in new[]{0UL,1UL,ulong.MaxValue,0x7FFFFFFFFFFFUL}) {
            f.Q(Fixture.Record+40,pointer);Check(!f.Capture().Ready,"invalid next-cell pointer "+pointer);
        }
        f.Q(Fixture.Record+40,Fixture.Cell);
        foreach(ulong cell in new[]{Fixture.Cell+1,Fixture.Callback,Fixture.Record+24,f.Slot}) {
            f.Q(Fixture.Record+40,cell);
            if(cell==Fixture.Record+24)f.Q(cell,f.Slot);
            Check(!f.Capture().Ready,"code, descriptor, physical-slot or misaligned next cell rejected "+cell);
        }
        f.Q(Fixture.Record+40,Fixture.Cell);
        f.Q(Fixture.Record+32,0);f.Q(f.Slot,0);Check(!f.Capture().Ready,"null callback is not a published owner");f.Q(Fixture.Record+32,Fixture.Callback);f.Q(f.Slot,Fixture.Callback);
        foreach(ulong pointer in new[]{0UL,1UL,ulong.MaxValue,0x7FFFFFFFFFFFUL,Fixture.Callback,Fixture.Cell}) {
            f.Q(Fixture.Cell,pointer);Check(!f.Capture().Ready,"invalid/cyclic next target "+pointer);
        }
        f.Q(Fixture.Cell,f.Stock);
        foreach(int field in new[]{8,12,16,24,32,40}) {
            f=new Fixture();int reads=0;
            c=CareerHubCompatibility.Inspect((at,n)=>{
                byte[] data=f.Read(at,n);
                if(at==Fixture.Record&&++reads==1) {
                    if(field==8)f.D(Fixture.Record+8,2);
                    else if(field==12)f.D(Fixture.Record+12,64);
                    else if(field==16)f.D(Fixture.Record+16,0);
                    else f.Q(Fixture.Record+(ulong)field,field==40?Fixture.Cell+8:0UL);
                }
                return data;
            },Fixture.Image,f.Profile,f.Module,true);
            Check(!c.Ready&&c.Waiting,"record field changed during observation "+field);
        }
        foreach(bool owner in new[]{false,true}) {
            f=new Fixture();int reads=0;ulong at=owner?f.Slot:Fixture.Cell;
            c=CareerHubCompatibility.Inspect((address,n)=>{byte[] data=f.Read(address,n);if(address==at&&++reads==1)f.Q(address,0);return data;},Fixture.Image,f.Profile,f.Module,true);
            Check(!c.Ready&&c.Waiting,"chain pointer changed during observation "+owner);
        }
        f=new Fixture();int recordReads=0;
        c=CareerHubCompatibility.Inspect((at,n)=>{byte[] data=f.Read(at,n);if(at==Fixture.Record&&++recordReads==1)f.D(Fixture.Record+20,1);return data;},Fixture.Image,f.Profile,f.Module,true);
        Check(c.Ready,"normal notification counter changes do not block shared hook adoption");
        f=new Fixture();c=CareerHubCompatibility.Inspect((at,n)=>at==Fixture.Record?null:f.Read(at,n),Fixture.Image,f.Profile,f.Module,true);
        Check(!c.Ready&&c.Waiting,"missing remote record waits without installing anything");
        f=new Fixture();c=CareerHubCompatibility.Inspect((at,n)=>at==Fixture.Cell?null:f.Read(at,n),Fixture.Image,f.Profile,f.Module,true);
        Check(!c.Ready&&c.Waiting,"missing next-call storage waits");
        ParserChecks();
    }
    private sealed class Pe
    {
        internal readonly byte[] Bytes=new byte[0x1400];
        internal Pe() {
            W(0,0x5a4d);D(0x3c,0x80);D(0x80,0x4550);W(0x84,0x8664);W(0x86,1);W(0x94,0xf0);
            W(0x98,0x20b);D(0x98+56,0x4000);D(0x98+60,0x400);D(0x98+108,1);D(0x98+112,0x1000);D(0x98+116,0x200);
            D(0x188+8,0x1000);D(0x188+12,0x1000);D(0x188+16,0x1000);D(0x188+20,0x400);D(0x188+36,0xc0000040);
            D(0x400+20,1);D(0x400+24,1);D(0x400+28,0x1100);D(0x400+32,0x1120);D(0x400+36,0x1140);
            D(0x500,0x1800);D(0x520,0x1160);W(0x540,0);
            Encoding.ASCII.GetBytes("mgs_hub_chain\0").CopyTo(Bytes,0x560);
        }
        internal void W(int at,ushort value)=>BitConverter.GetBytes(value).CopyTo(Bytes,at);
        internal void D(int at,uint value)=>BitConverter.GetBytes(value).CopyTo(Bytes,at);
    }
    private static void ParserChecks()
    {
        string dir=Path.Combine(Path.GetTempPath(),"tb-export-pe-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string file=Path.Combine(dir,"midgamesave.dll");
        try {
            var p=new Pe();File.WriteAllBytes(file,p.Bytes);Check(CareerHubCompatibility.ChainExportRva(file)==0x1800,"named PE32+ data export resolved");
            Check(CareerHubCompatibility.ChainExportRva(Path.Combine(dir,"missing.dll"))==0,"missing file has no contract");
            foreach(int cut in new[]{0,0x40,0x90,0x100,0x400,0x528}) {
                File.WriteAllBytes(file,p.Bytes.Take(cut).ToArray());Check(CareerHubCompatibility.ChainExportRva(file)==0,"truncated PE rejected "+cut);
            }
            Action<Action<Pe>,string> reject=(mutate,name)=>{var image=new Pe();mutate(image);File.WriteAllBytes(file,image.Bytes);Check(CareerHubCompatibility.ChainExportRva(file)==0,name);};
            reject(x=>x.D(0x98+112,0),"no export retains legacy discovery path");
            reject(x=>x.Bytes[0x560]=(byte)'x',"different export name not treated as MGS interface");
            reject(x=>x.W(0x98,0x10b),"PE32 export not accepted for x64 contract");
            reject(x=>x.D(0x520,uint.MaxValue),"invalid name RVA rejected");
            reject(x=>x.W(0,0),"bad DOS header rejected");
            reject(x=>x.W(0x84,0x14c),"non-AMD64 image rejected");
            reject(x=>x.D(0x98+108,0),"missing export data directory rejected");
            reject(x=>x.W(0x94,100),"short optional header rejected");
            reject(x=>x.D(0x400+20,65537),"function table count bounded");
            reject(x=>x.D(0x400+24,65537),"name table count bounded");
            reject(x=>{x.W(0x540,2);x.D(0x508,0x1800);},"ordinal outside function table rejected");
            reject(x=>x.D(0x500,0x1180),"forwarded export is not accepted as writable contract");
            reject(x=>x.D(0x500,0x3ff0),"descriptor outside image rejected");
            reject(x=>x.D(0x500,0x1ff0),"descriptor straddling section end rejected");
            reject(x=>x.D(0x400+28,0x1fff),"function table straddling raw section end rejected");
            reject(x=>x.D(0x400+32,0x1fff),"name table straddling raw section end rejected");
            reject(x=>x.D(0x400+36,0x1fff),"ordinal table straddling raw section end rejected");
            // A raw-file overlay must not masquerade as bytes beyond a section.
            reject(x=>{x.D(0x188+16,0x120);x.D(0x400+32,0x1180);x.D(0x580,0x1160);},"virtual padding cannot supply file-backed export tables");
        }finally{Check(Path.GetFullPath(dir).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase),"PE fixtures stay in private temp root");Directory.Delete(dir,true);}
    }
}
