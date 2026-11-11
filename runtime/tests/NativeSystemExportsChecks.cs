using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using OfflineTeamBuilderRuntime;

internal static class NativeSystemExportsChecks
{
    private sealed class Fixture
    {
        internal byte[] Bytes=new byte[0x5000];
        internal SystemExportModule Module;
        internal Fixture(string name="kernel32.dll",ulong address=0x70000000) {
            Module=new SystemExportModule {Name=name,Path=Path.Combine(Environment.SystemDirectory,name),Base=address,Size=Bytes.Length};
            W(0,0x5a4d);D(0x3c,0x80);D(0x80,0x4550);W(0x84,0x8664);W(0x86,2);W(0x94,240);
            W(0x98,0x20b);D(0x98+56,(uint)Bytes.Length);D(0x98+108,16);D(0x98+112,0x1000);D(0x98+116,0x400);
            D(0x188+8,0x1000);D(0x188+12,0x2000);D(0x188+16,0x1000);D(0x188+36,0x60000020);
            D(0x1b0+8,0x1000);D(0x1b0+12,0x1000);D(0x1b0+16,0x1000);D(0x1b0+36,0x40000040);
            D(0x1010,7);D(0x1014,1);D(0x1018,1);D(0x101c,0x1100);D(0x1020,0x1120);D(0x1024,0x1140);
            D(0x1100,0x2000);D(0x1120,0x1200);W(0x1140,0);S(0x1200,"LoadLibraryW");Bytes[0x2000]=0xc3;
        }
        internal void D(int offset,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,Bytes,offset,4);}
        internal void W(int offset,ushort value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,Bytes,offset,2);}
        internal void S(int offset,string text){byte[] value=Encoding.ASCII.GetBytes(text+"\0");Buffer.BlockCopy(value,0,Bytes,offset,value.Length);}
        internal void Forward(string target){D(0x1100,0x1280);S(0x1280,target);}
    }
    private static byte[] Read(Fixture[] fixtures,ulong address,int count) {
        foreach(var fixture in fixtures)if(address>=fixture.Module.Base&&address-fixture.Module.Base<(ulong)fixture.Bytes.Length) {
            ulong offset=address-fixture.Module.Base;
            if(count<0||(ulong)count>(ulong)fixture.Bytes.Length-offset)throw new IOException("Private fixture read outside bounds.");
            var result=new byte[count];Buffer.BlockCopy(fixture.Bytes,(int)offset,result,0,count);return result;
        }
        throw new IOException("Unknown private fixture address.");
    }
    private static SystemExportAddress Resolve(Fixture[] fixtures,string name="LoadLibraryW",Func<ulong,int,byte[]> read=null) {
        return NativeSystemExports.Resolve(read??((at,n)=>Read(fixtures,at,n)),fixtures.Select(f=>f.Module),Environment.SystemDirectory,"kernel32.dll",name);
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();

    internal static void CheckAll() {
        var direct=new Fixture();var a=new[]{direct};
        Program.Assert(Resolve(a).Address==direct.Module.Base+0x2000,"target PE export resolves by name");
        Program.Assert(Resolve(a,"#7").Address==direct.Module.Base+0x2000,"target PE export resolves an ordinal");
        Program.Reject(()=>Resolve(a,"#6"),"ordinal below export base rejected");
        Program.Reject(()=>Resolve(a,"#8"),"ordinal beyond export table rejected");
        Program.Reject(()=>Resolve(a,"Missing"),"missing system export rejected");
        var relocated=new Fixture(address:0x90000000);
        Program.Assert(Resolve(new[]{relocated}).Address==0x90002000,"target ASLR base used independently");
        direct.Bytes[0x2000]=0xe9;direct.D(0x2001,0x12345678);
        byte[] before=(byte[])direct.Bytes.Clone();
        Program.Assert(Resolve(a).Address==direct.Module.Base+0x2000,"published entry retained despite per-process detour bytes");
        Program.Assert(before.SequenceEqual(direct.Bytes),"resolver never rewrites or follows entry-point detours");
        var first=new Fixture();var second=new Fixture("kernelbase.dll",0x71000000);first.Forward("KERNELBASE.LoadLibraryW");
        Program.Assert(Resolve(new[]{first,second}).Address==second.Module.Base+0x2000,"named forwarded system export resolved");
        first.Forward("kernelbase.dll.#7");
        Program.Assert(Resolve(new[]{first,second}).Address==second.Module.Base+0x2000,"ordinal forwarded system export resolved");
        second.Forward("kernel32.LoadLibraryW");first.Forward("kernelbase.LoadLibraryW");
        Program.Reject(()=>Resolve(new[]{first,second}),"cyclic system forwarder rejected");
        first.Forward("unknown.LoadLibraryW");Program.Reject(()=>Resolve(new[]{first}),"unknown forwarder host rejected");
        first.Forward("KERNELBASE.");Program.Reject(()=>Resolve(new[]{first,second}),"malformed forwarder rejected");
        var invalid=new Fixture();invalid.Module.Path=Path.Combine(Path.GetTempPath(),"kernel32.dll");
        Program.Reject(()=>Resolve(new[]{invalid}),"non-system-directory owner rejected");
        Program.Reject(()=>Resolve(new[]{direct,direct}),"duplicate module owner rejected");
        Program.Reject(()=>Resolve(new Fixture[0]),"missing module owner rejected");
        RejectMutation(f=>f.W(0,0),"bad DOS header rejected");
        RejectMutation(f=>f.D(0x80,0),"bad PE signature rejected");
        RejectMutation(f=>f.W(0x84,0x14c),"non-AMD64 system image rejected");
        RejectMutation(f=>f.D(0x98+56,0x6000),"wrong image size rejected");
        RejectMutation(f=>f.D(0x98+112,0x4ff0),"out-of-image export directory rejected");
        RejectMutation(f=>f.D(0x98+116,39),"truncated export directory rejected");
        RejectMutation(f=>f.D(0x1014,65537),"unbounded function count rejected");
        RejectMutation(f=>f.D(0x1018,65537),"unbounded export name count rejected");
        RejectMutation(f=>f.D(0x101c,0x4fff),"out-of-image export address table rejected");
        RejectMutation(f=>f.W(0x1140,1),"invalid named export ordinal rejected");
        RejectMutation(f=>f.D(0x1100,0),"empty export entry rejected");
        RejectMutation(f=>f.D(0x1100,0x5001),"out-of-image function entry rejected");
        RejectMutation(f=>f.D(0x1100,0x1500),"non-executable data export rejected");
        RejectMutation(f=>{f.D(0x1100,0x1280);for(int i=0x1280;i<0x1400;i++)f.Bytes[i]=(byte)'A';},"unterminated forwarder stays within directory bounds");
        Program.Reject(()=>Resolve(a,read:(at,n)=>Read(a,at,n).Take(n-1).ToArray()),"incomplete memory reads rejected");
        int reads=0;var changing=new[]{new Fixture()};
        Program.Reject(()=>Resolve(changing,read:(at,n)=>{
            if(at==changing[0].Module.Base+0x1100&&n==4&&++reads==2)return BitConverter.GetBytes(0x2100u);
            return Read(changing,at,n);
        }),"export changing during resolution rejected");
        var changedHeader=new[]{new Fixture()};int headerReads=0;
        Program.Reject(()=>Resolve(changedHeader,read:(at,n)=>{
            if(at==changedHeader[0].Module.Base+0x1000&&n==40&&++headerReads==2)changedHeader[0].D(0x101c,0x1110);
            return Read(changedHeader,at,n);
        }),"export directory changing during resolution rejected");
        Program.Assert(NativeSystemExports.ExecutableImage(0x70000000,0x70000000,0x1000,0x20,0x1000000),"committed executable owning image accepted");
        Program.Assert(!NativeSystemExports.ExecutableImage(0x70000000,0x70000000,0x1000,4,0x1000000),"non-executable page rejected");
        Program.Assert(!NativeSystemExports.ExecutableImage(0x70000000,0x70000000,0x1000,0x120,0x1000000),"guard page rejected");
        Program.Assert(!NativeSystemExports.ExecutableImage(0x70000000,0x70000000,0x1000,0x20,0x20000),"private page posing as image rejected");
        Program.Assert(!NativeSystemExports.ExecutableImage(0x70000000,0x71000000,0x1000,0x20,0x1000000),"foreign allocation owner rejected");
        var region=typeof(NativeAssetGuards).GetNestedType("MemoryRegion",System.Reflection.BindingFlags.NonPublic);
        Program.Assert(Marshal.SizeOf(region)==48,"x64 MEMORY_BASIC_INFORMATION layout");

        // Read this test process only. Never open, patch or create a game process.
        using(var self=Process.GetCurrentProcess()) {
            var modules=self.Modules.Cast<ProcessModule>().Where(m=>new[]{"kernel32.dll","kernelbase.dll","ntdll.dll"}.Contains(m.ModuleName.ToLowerInvariant()))
                .Select(m=>new SystemExportModule {Name=m.ModuleName,Path=m.FileName,Base=(ulong)m.BaseAddress.ToInt64(),Size=m.ModuleMemorySize}).ToArray();
            Func<ulong,int,byte[]> read=(at,n)=>{byte[] bytes=new byte[n];Marshal.Copy(new IntPtr(unchecked((long)at)),bytes,0,n);return bytes;};
            var actual=NativeSystemExports.Resolve(read,modules,Environment.SystemDirectory,"kernel32.dll","LoadLibraryW");
            ulong expected=(ulong)GetProcAddress(GetModuleHandleW("kernel32.dll"),"LoadLibraryW").ToInt64();
            Program.Assert(expected!=0&&actual.Address==expected,"real Windows export matches GetProcAddress in this private process");
            NativeAssetGuards.ValidateSystemExportPage(GetCurrentProcess(),actual);Program.Checks++;
            var data=new SystemExportAddress {Module=actual.Module,Address=actual.Module.Base,Chain="private header-page rejection"};
            Program.Reject(()=>NativeAssetGuards.ValidateSystemExportPage(GetCurrentProcess(),data),"real non-executable header page rejected");
        }
    }
    private static void RejectMutation(Action<Fixture> change,string description) {
        var fixture=new Fixture();change(fixture);Program.Reject(()=>Resolve(new[]{fixture}),description);
    }
}
