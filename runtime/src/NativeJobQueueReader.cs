using System;
using System.Collections.Generic;
using System.IO;

namespace OfflineTeamBuilderRuntime;

// The validated manager uses a deque of job pointers, not a contiguous vector.
// Iterators contain {current, first, last, mapNode}; each block holds 32 pointers.
// This reader never calls, cancels, consumes or changes native jobs.
internal static class NativeJobQueueReader
{
    internal const int BlockBytes = 0x100;
    internal static byte[] Read(Func<ulong,int,byte[]> read, byte[] manager, int maximum)
    {
        ulong map=Q(manager,0x170), capacity=BitConverter.ToUInt32(manager,0x178);
        ulong begin=Q(manager,0x180), first=Q(manager,0x188), last=Q(manager,0x190), node=Q(manager,0x198);
        ulong end=Q(manager,0x1A0), endFirst=Q(manager,0x1A8), endLast=Q(manager,0x1B0), endNode=Q(manager,0x1B8);
        if(begin==0 && end==0 && first==0 && last==0 && node==0 && endFirst==0 && endLast==0 && endNode==0)
            return new byte[0];
        if(maximum<1 || maximum>128 || map<0x10000 || map%8!=0 || capacity<1 || capacity>4096 ||
            map>0x7FFFFFFFFFFFUL-capacity*8 || node<map || endNode<node ||
            endNode>=map+capacity*8 || (node-map)%8!=0 || (endNode-node)%8!=0)
            throw new InvalidDataException("Native job deque map bounds/alignment mismatch.");
        Iterator(begin,first,last); Iterator(end,endFirst,endLast);
        ulong blocks=(endNode-node)/8+1;
        if(blocks>(ulong)(maximum/32+2)) throw new InvalidDataException("Native job deque block bound exceeded.");
        byte[] pointers=Memory(read,node,checked((int)blocks*8));
        var output=new List<byte>();
        for(int i=0;i<(int)blocks;i++) {
            ulong block=Q(pointers,i*8);
            if(block<0x10000 || block%8!=0 || block>0x7FFFFFFFFFFFUL-BlockBytes)
                throw new InvalidDataException("Native job deque block pointer invalid.");
            if(i==0 && block!=first || i==(int)blocks-1 && block!=endFirst)
                throw new InvalidDataException("Native job deque iterator/map mismatch.");
            ulong from=i==0?begin:block, to=i==(int)blocks-1?end:block+BlockBytes;
            if(to<from || to-from>BlockBytes || (to-from)%8!=0 || output.Count+(long)(to-from)>maximum*8L)
                throw new InvalidDataException("Native job deque job bound exceeded.");
            if(to!=from) output.AddRange(Memory(read,from,checked((int)(to-from))));
        }
        byte[] after=Memory(read,node,pointers.Length);
        for(int i=0;i<pointers.Length;i++) if(after[i]!=pointers[i])
            throw new InvalidDataException("Native job deque map changed during read.");
        return output.ToArray();
    }
    private static void Iterator(ulong cursor,ulong first,ulong last)
    {
        if(first<0x10000 || first%8!=0 || first>0x7FFFFFFFFFFFUL-BlockBytes ||
            last!=first+BlockBytes || cursor<first || cursor>=last || (cursor-first)%8!=0)
            throw new InvalidDataException("Native job deque iterator bounds/alignment mismatch.");
    }
    private static ulong Q(byte[] b,int at)=>BitConverter.ToUInt64(b,at);
    private static byte[] Memory(Func<ulong,int,byte[]> read,ulong at,int size)
    {
        if(at<0x10000 || at>=0x7FFFFFFFFFFFUL || size<=0 || size>0x4000 ||
            (ulong)size>0x7FFFFFFFFFFFUL-at) throw new InvalidDataException("Native job deque read bounds.");
        byte[] data=read(at,size);
        if(data==null || data.Length!=size)throw new IOException("Native job deque memory unavailable.");
        return data;
    }
}
