using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using OfflineTeamBuilderRuntime;

internal static class NativeBuildModeChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint Produce(IntPtr frame, ulong ignored, ulong mode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void Configure(IntPtr job, IntPtr config);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint Select(IntPtr job);
    private static ulong Address(IntPtr p)=>unchecked((ulong)p.ToInt64());
    private static void Write(IntPtr p,byte[] b)=>Marshal.Copy(b,0,p,b.Length);
    private static byte[] Read(IntPtr p,int n){var b=new byte[n];Marshal.Copy(p,b,0,n);return b;}
    private static void Jump(byte[] code,int at,int target) {
        code[at]=0xE9;Buffer.BlockCopy(BitConverter.GetBytes(target-at-5),0,code,at+1,4);
    }
    internal static void CheckAll()
    {
        var p=GameProfile.Current;const ulong image=0x140000000,remote=0x200000000,status=remote+0x4000;
        foreach(string name in new[]{"Career","Dispatcher"}) {
            ulong[] args=name=="Career"?new[]{status,remote+0x10000,image+(ulong)p.Rva("CareerManagerVtable"),image+(ulong)p.Rva("Add"),image+(ulong)p.Rva("Needs"),image+(ulong)p.Rva("Enter")}:
                new[]{status,image+(ulong)p.Rva("DispatcherContinuation")};
            byte[] code=p.Emit(name,args);
            byte[] marker=GameProfile.Hex(name=="Career"?"4533c0488b01c7460401000000c746080b000000":"4533c041c743080b00000041ff4368");
            int sites=Enumerable.Range(0,code.Length-marker.Length+1).Count(n=>marker.Select((b,i)=>code[n+i]==b).All(x=>x));
            Program.Assert(sites==1,"production relay retains original zero/full-build argument: "+name);
        }
        foreach(string name in new[]{"MainAssetKey","MainCareer","News"}) {
            ulong[] args={image,status,remote,image,image,image};
            Program.Assert(p.Emit(name,args).SequenceEqual(p.templates[name].Emit(p,args)),"full-build control leaves original "+name+" unchanged");
        }
        // Captured native code demonstrates why presence alone is insufficient:
        // even a record containing only a slot number takes the reuse branch.
        NativeBranches();
    }
    private static void NativeBranches()
    {
        var fixture=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"resident-reuse-native.json")));
        IntPtr native=VirtualAlloc(IntPtr.Zero,(UIntPtr)0x1000,0x3000,0x40),data=Marshal.AllocHGlobal(0x600);
        if(native==IntPtr.Zero)throw new Exception("Private native fixture allocation failed");
        try {
            Write(data,new byte[0x600]);
            // Exact captured producer arithmetic; unrelated RIP-relative address
            // setup is omitted. RBP is redirected to private fixture storage.
            byte[] producer=GameProfile.Hex("55488be944884577").Concat(GameProfile.Hex(fixture["producer"]))
                .Concat(GameProfile.Hex("0fb645d45dc3")).ToArray();
            Write(native,producer);
            IntPtr configureAt=IntPtr.Add(native,0x80),selectAt=IntPtr.Add(native,0x100);
            Write(configureAt,GameProfile.Hex(fixture["configure"]));
            // Execute the captured search and resident/rebuild decision with
            // their original offsets. Only the two downstream game paths are
            // replaced by return markers; no allocator or game API is called.
            byte[] consumer=Enumerable.Repeat((byte)0xCC,0x210).ToArray();
            consumer[0]=0x53;consumer[1]=0x57;consumer[2]=0xEB;consumer[3]=0x0C;
            var search=GameProfile.Hex(fixture["residentSearch"]);Buffer.BlockCopy(search,0,consumer,0x10,search.Length);
            var branch=GameProfile.Hex(fixture["residentBranch"]);Buffer.BlockCopy(branch,0,consumer,0x16C,branch.Length);
            Buffer.BlockCopy(GameProfile.Hex("b802000000"),0,consumer,0x38,5);Jump(consumer,0x3D,0x200);
            Buffer.BlockCopy(GameProfile.Hex("b801000000"),0,consumer,0x182,5);Jump(consumer,0x187,0x200);
            Buffer.BlockCopy(GameProfile.Hex("5f5bc3"),0,consumer,0x200,3);Write(selectAt,consumer);
            var produce=(Produce)Marshal.GetDelegateForFunctionPointer(native,typeof(Produce));
            var configure=(Configure)Marshal.GetDelegateForFunctionPointer(configureAt,typeof(Configure));
            var select=(Select)Marshal.GetDelegateForFunctionPointer(selectAt,typeof(Select));
            IntPtr frame=IntPtr.Add(data,0x40),config=IntPtr.Add(frame,-0x39),job=IntPtr.Add(data,0x100),vector=IntPtr.Add(data,0x400),records=IntPtr.Add(data,0x440);
            Marshal.WriteInt64(job,0x68,(long)Address(vector));
            foreach(ulong mode in new ulong[]{0,1,0xABCDEF0100000001}) {
                uint force=produce(frame,0,mode);
                Program.Assert(force==(mode==0?1U:0U),"captured producer maps byte argument to inverse force flag");
                Marshal.WriteInt32(config,8,1809);configure(job,config);
                Program.Assert(Marshal.ReadByte(job,0x45)==force,"captured job configurator preserves rebuild decision");
                foreach(bool present in new[]{false,true}) {
                    Marshal.WriteInt64(vector,0,(long)Address(records));
                    Marshal.WriteInt64(vector,8,(long)Address(records)+(present?0x78:0));
                    Marshal.WriteInt32(records,0x58,1809);
                    byte[] untouched=Read(records,0x78);
                    Program.Assert(select(job)==(present&&mode!=0?1U:2U),"captured native lookup reuses resident only with new argument; missing record still loads");
                    Program.Assert(untouched.SequenceEqual(Read(records,0x78)),"resident record remains untouched by native decision fixture");
                }
                // A record for another team is not permission to skip loading.
                Marshal.WriteInt32(records,0x58,1811);
                Program.Assert(select(job)==2,"different team's resident record still requires metadata/loading");
            }
        } finally { Marshal.FreeHGlobal(data);if(native!=IntPtr.Zero)VirtualFree(native,UIntPtr.Zero,0x8000); }
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr VirtualAlloc(IntPtr at,UIntPtr size,uint kind,uint protection);
    [DllImport("kernel32.dll")] private static extern bool VirtualFree(IntPtr at,UIntPtr size,uint kind);
}
