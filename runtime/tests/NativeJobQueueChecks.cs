using System;
using System.Collections.Generic;
using System.Linq;
using OfflineTeamBuilderRuntime;

internal static class NativeJobQueueChecks
{
    internal static void CheckAll()
    {
        byte[] header=new byte[0x1D8];var memory=new Dictionary<ulong,byte>();
        Action<ulong,byte[]> put=(p,b)=>{for(int i=0;i<b.Length;i++)memory[p+(ulong)i]=b[i];};
        Action<int,ulong> q=(p,v)=>Buffer.BlockCopy(BitConverter.GetBytes(v),0,header,p,8);
        Func<ulong,int,byte[]> read=(p,n)=>Enumerable.Range(0,n).Select(i=>memory[p+(ulong)i]).ToArray();
        Func<byte[]> capture=()=>NativeJobQueueReader.Read(read,header,128);
        Program.Assert(capture().Length==0,"deque: unallocated empty queue");
        const ulong map=0x100000, first=0x300000, second=0x200000;
        q(0x170,map);q(0x178,8);put(map,BitConverter.GetBytes(first));put(map+8,BitConverter.GetBytes(second));
        q(0x180,first+0xF0);q(0x188,first);q(0x190,first+0x100);q(0x198,map);
        q(0x1A0,first+0xF0);q(0x1A8,first);q(0x1B0,first+0x100);q(0x1B8,map);
        Program.Assert(capture().Length==0,"deque: allocated empty queue");
        put(first+0xF0,BitConverter.GetBytes(0x500000UL));put(first+0xF8,BitConverter.GetBytes(0x600000UL));
        q(0x1A0,first+0xF8);
        Program.Assert(BitConverter.ToUInt64(capture(),0)==0x500000,"deque: one block preserves FIFO");
        put(second,BitConverter.GetBytes(0x700000UL));q(0x1A0,second+8);q(0x1A8,second);q(0x1B0,second+0x100);q(0x1B8,map+8);
        var result=capture();
        Program.Assert(result.Length==24&&Enumerable.Range(0,3).Select(i=>BitConverter.ToUInt64(result,i*8)).SequenceEqual(new[]{0x500000UL,0x600000UL,0x700000UL}),"deque: cross-block FIFO with descending addresses");
        var before=memory.ToArray();capture();Program.Assert(before.SequenceEqual(memory.ToArray()),"deque: observations never write native memory");
        q(0x198,map+1);Program.Reject(()=>capture(),"deque: misaligned map node");q(0x198,map);
        q(0x190,first+0x200);Program.Reject(()=>capture(),"deque: wrong block size");q(0x190,first+0x100);
        q(0x1B8,map+64);Program.Reject(()=>capture(),"deque: end node outside map");q(0x1B8,map+8);
        q(0x1A0,second+1);Program.Reject(()=>capture(),"deque: misaligned end cursor");q(0x1A0,second+8);
        put(map+8,BitConverter.GetBytes(first));Program.Reject(()=>capture(),"deque: block map and iterator disagree");put(map+8,BitConverter.GetBytes(second));
        Program.Reject(()=>NativeJobQueueReader.Read(read,header,1),"deque: maximum jobs enforced across blocks");
        int mapReads=0;
        Func<ulong,int,byte[]> moving=(p,n)=>{byte[] b=read(p,n);if(p==map&&++mapReads==2)b[0]^=8;return b;};
        Program.Reject(()=>NativeJobQueueReader.Read(moving,header,128),"deque: concurrent map change rejected");
    }
}
