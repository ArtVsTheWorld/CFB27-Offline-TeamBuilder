using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OfflineTeamBuilderRuntime
{
    // Keep PowerShell grandchildren inside the same lifetime as the worker.
    // Closing or terminating this unnamed job cannot affect the game or MMC.
    internal sealed class ChildJob : IDisposable
    {
        private IntPtr handle;
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
        {
            public long ProcessTime,JobTime;public uint Flags;
            public UIntPtr MinimumWorkingSet,MaximumWorkingSet;public uint ActiveProcessLimit;
            public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters
        {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
        {
            public BasicLimits Basic;public IoCounters Io;
            public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;
        }
        [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr CreateJobObject(IntPtr security,string name);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetInformationJobObject(IntPtr job,int info,ref ExtendedLimits limits,uint size);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool TerminateJobObject(IntPtr job,uint exitCode);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        public ChildJob()
        {
            handle=CreateJobObject(IntPtr.Zero,null);if(handle==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            var limits=new ExtendedLimits { Basic=new BasicLimits { Flags=0x2000 } };
            if(!SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf(typeof(ExtendedLimits))))
            {int error=Marshal.GetLastWin32Error();Dispose();throw new Win32Exception(error);}
        }
        public void Attach(Process process)
        {
            if(!AssignProcessToJobObject(handle,process.Handle))
            {int error=Marshal.GetLastWin32Error();try{process.Kill();}catch{}throw new Win32Exception(error);}
        }
        public void Stop(){if(handle!=IntPtr.Zero)TerminateJobObject(handle,1);}
        public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
    }
}

