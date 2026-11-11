using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OfflineTeamBuilderRuntime;

// Initial hub loading is unchanged. Permit one automatic stabilization pass
// after that load; native residency trimming must not repeatedly restart it.
// During stabilization, submit only absent records. Existing records remain
// owned by the game; their presence is not a claim about GPU/world readiness.
// Real hub/coach initialization already resets the inherited recovery budget.
internal static class ResidencyRepairScope
{
    internal const int ResidentSkips=0x1C0;
    internal static byte[] Apply(string name,byte[] code,GameProfile profile)
    {
        if(name!="Dispatcher")return code;
        int budget=Unique(code,GameProfile.Hex("4183bbb4000000030f83"));
        byte[] original=GameProfile.Hex("418953204c895c2420");
        int at=Unique(code,original);
        int next=Unique(code,GameProfile.Hex("498b4338498b4b404839c8"));
        var b=new List<byte>(code);b[budget+7]=1;
        var allow=new List<int>();
        Action<string> emit=s=>b.AddRange(GameProfile.Hex(s));
        Action<int,int> patch=(where,target)=>{
            byte[] displacement=BitConverter.GetBytes(target-where-4);
            for(int i=0;i<4;i++)b[where+i]=displacement[i];
        };
        Action<string,List<int>> branch=(op,list)=>{emit(op);list.Add(b.Count);b.AddRange(new byte[4]);};
        Action<int> jump=target=>{emit("e9");int p=b.Count;b.AddRange(new byte[4]);patch(p,target);};
        int stub=b.Count;
        emit("9c505152415041514152"); // flags, RAX/RCX/RDX/R8/R9/R10
        emit("4183bbb400000000");branch("0f84",allow); // initial pass
        emit("498b4b304885c9");branch("0f84",allow); // manager
        emit("4c8b81c80100004c8b91d0010000");          // record begin/end
        emit("4d85c0");branch("0f84",allow);
        emit("4c89d04c29c0");branch("0f82",allow);   // bounded vector
        emit("483d00300000");branch("0f87",allow);
        emit("89d133d241b97800000049f7f185d2");branch("0f85",allow); // stride
        int loop=b.Count;
        emit("4d39d0");branch("0f83",allow);
        emit("41394858");var found=new List<int>();branch("0f84",found);
        emit("4983c078");jump(loop);
        int skip=b.Count;foreach(int p in found)patch(p,skip);
        emit("41ff434841ff83c0010000"); // logical completed slot + observation
        emit("415a415941585a59589d");jump(next);
        int submit=b.Count;foreach(int p in allow)patch(p,submit);
        emit("415a415941585a59589d");b.AddRange(original);jump(at+original.Length);
        b[at]=0xE9;patch(at+1,stub);for(int i=5;i<original.Length;i++)b[at+i]=0x90;
        return b.ToArray();
    }
    private static int Unique(byte[] code,byte[] marker)
    {
        var matches=Enumerable.Range(0,Math.Max(0,code.Length-marker.Length+1))
            .Where(n=>marker.Select((v,i)=>code[n+i]==v).All(x=>x)).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("Unknown residency repair boundary.");
        return matches[0];
    }
}
