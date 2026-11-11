using System;
using System.IO;
using System.Runtime.InteropServices;
using OfflineTeamBuilderRuntime;

internal static class NativeGuardChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint Initialize(IntPtr context);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibraryW(string path);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    internal static void CheckAll()
    {
        string helper=NativeHelperDeploymentChecks.CheckAll();
        NativeAssetGuards.ValidateHelper(helper,NativeAssetGuards.ExpectedHash);
        Program.Checks++;
        byte[] pe=File.ReadAllBytes(helper);
        Program.Assert(NativePe.ExportRva(pe,"Initialize")>0,"native initialization export");
        Program.Reject(()=>NativePe.ExportRva(new byte[256],"Initialize"),"invalid native PE");
        Program.Reject(()=>NativePe.ExportRva(pe,"MissingExport"),"missing native export");
        Program.Reject(()=>NativeAssetGuards.ValidateHelper(helper,new string('0',64)),"mismatched helper package");
        NativeSystemExportsChecks.CheckAll();
        byte[] context=NativeAssetGuards.Context(0x140000000,0x123456789ABC);
        Program.Assert(context.Length==40&&BitConverter.ToUInt32(context,4)==40,"native init context ABI");
        Program.Assert(BitConverter.ToUInt64(context,8)==0x140000000&&BitConverter.ToUInt64(context,32)==0x123456789ABC,"native init pointer widths");

        // Load only in this private test process. DllMain never patches code;
        // Initialize must decline because CollegeFB27.exe is not loaded here.
        IntPtr module=LoadLibraryW(helper),argument=IntPtr.Zero;
        Program.Assert(module!=IntPtr.Zero,"private sidecar load");
        try {
            var initialize=(Initialize)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module,"Initialize"),typeof(Initialize));
            Program.Assert(initialize(IntPtr.Zero)==8,"native rejects null ABI context");
            argument=Marshal.AllocHGlobal(context.Length);Marshal.Copy(context,0,argument,context.Length);
            Program.Assert(initialize(argument)==7,"native declines non-game process without patching");
            Marshal.Copy(argument,context,0,context.Length);
            ulong status=BitConverter.ToUInt64(context,16);
            Program.Assert(status!=0,"native status pointer round trip");
            byte[] bytes=new byte[NativeAssetGuards.StatusSize];Marshal.Copy(new IntPtr(unchecked((long)status)),bytes,0,bytes.Length);
            var health=NativeAssetGuards.ParseStatus(bytes);
            Program.Assert(!(bool)health["ready"]&&(uint)health["installed"]==0,"declined native helper remains uninstalled");
            Buffer.BlockCopy(BitConverter.GetBytes(3u),0,bytes,12,4);
            Buffer.BlockCopy(BitConverter.GetBytes(1u),0,bytes,16,4);
            Buffer.BlockCopy(BitConverter.GetBytes(123456789012L),0,bytes,72,8);
            health=NativeAssetGuards.ParseStatus(bytes);
            Program.Assert((bool)health["ready"]&&(long)health["registryCalls"]==123456789012L,"native readiness and 64-bit telemetry ABI");
            bytes[0]^=1;Program.Reject(()=>NativeAssetGuards.ParseStatus(bytes),"native status wrong magic");
        } finally {
            if(argument!=IntPtr.Zero)Marshal.FreeHGlobal(argument);
            if(module!=IntPtr.Zero)FreeLibrary(module);
        }
    }
}
