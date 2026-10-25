using System;
using System.Runtime.InteropServices;

namespace OfflineTeamBuilderRuntime;

// Diagnostic/preflight reader only. The original engines retain their own
// checked write handles and ownership-aware rollback implementation.
internal sealed class NativeObserver : IDisposable
{
    private IntPtr handle;
    private readonly ulong image;
    private readonly long size;
    internal NativeObserver(int pid,ulong image,long size)
    {
        this.image=image;this.size=size;handle=OpenProcess(0x410,false,pid);
        if(handle==IntPtr.Zero)throw new InvalidOperationException("READ_ACCESS_DENIED Win32="+Marshal.GetLastWin32Error());
    }
    internal byte[] ReadRva(long rva,int length)
    {
        if(rva<0||length<=0||length>0x100000||rva>size-length)return null;
        var data=new byte[length];UIntPtr got;
        if(!ReadProcessMemory(handle,new IntPtr(unchecked((long)image+rva)),data,new UIntPtr((uint)length),out got)||got.ToUInt64()!=(ulong)length)return null;
        return data;
    }
    public void Dispose() { if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;} }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ReadProcessMemory(IntPtr process,IntPtr address,[Out]byte[] data,UIntPtr length,out UIntPtr got);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
