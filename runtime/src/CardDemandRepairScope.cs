using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OfflineTeamBuilderRuntime;

// The native frontend picker releases its previous secondary team. In an
// offline save its own metadata map may be empty, so it cannot restore that
// team on the next visit. Permit the existing missing-only saved-team pass
// once for each newly viewed, absent secondary team. Never cancel native work,
// alter the queue, or turn ordinary residency trimming into a polling reload.
internal static class CardDemandRepairScope
{
    internal const int LastViewedSlot=0x1C8, RepairRequests=0x1CC;
    internal static byte[] Apply(string name,byte[] code,GameProfile profile,ulong[] args)
    {
        if(name!="Dispatcher")return code;
        byte[] marker=GameProfile.Hex("4183bbb4000000010f83");
        int[] sites=Enumerable.Range(0,code.Length-marker.Length+1).Where(n=>marker.Select((v,i)=>code[n+i]==v).All(x=>x)).ToArray();
        if(sites.Length!=1)throw new InvalidDataException("Unknown completed-pass residency budget boundary.");
        int at=sites[0];ulong image=args[1]-(ulong)profile.Rva("DispatcherContinuation");var b=new List<byte>(code);
        var declines=new List<int>();var noView=new List<int>();
        Action<string> emit=s=>b.AddRange(GameProfile.Hex(s));
        Action<ulong> q=v=>b.AddRange(BitConverter.GetBytes(v));
        Action<int,int> patch=(p,target)=>{byte[] d=BitConverter.GetBytes(target-p-4);for(int i=0;i<4;i++)b[p+i]=d[i];};
        Action<string,List<int>> branch=(op,list)=>{emit(op);list.Add(b.Count);b.AddRange(new byte[4]);};
        Action<int> jump=target=>{emit("e9");int p=b.Count;b.AddRange(new byte[4]);patch(p,target);};
        int stub=b.Count;emit("9c505152415041514152"); // flags and volatile registers except R11 (status)
        // Exact stock GetIsFlowEntered contract, read without calling the
        // adapter: enabled && entryDepth>0 && exitDepth<=0. Restrict this
        // bypass to the full player-card flow, never uniform select/game entry.
        emit("48b8");q(image+(ulong)profile.Rva("CardDetailsAdapterGlobal"));emit("488b004885c0");branch("0f84",noView);
        emit("49ba");q(image+(ulong)profile.Rva("CardDetailsAdapterVtable"));emit("4c3910");branch("0f85",declines);
        emit("488b80180400004885c0");branch("0f84",noView);
        emit("49ba");q(image+(ulong)profile.Rva("CardDetailsFlowVtable"));emit("4c3910");branch("0f85",declines);
        emit("80b8b100000000");branch("0f84",noView);
        emit("83b8b400000000");branch("0f8e",noView);
        emit("83b8c400000000");branch("0f8f",noView);
        emit("498b4b104885c9");branch("0f84",declines);
        emit("48b8");q(image+(ulong)profile.Rva("ServiceGlobal"));emit("483908");branch("0f85",declines);
        emit("488b81680400004885c0");branch("0f84",declines);
        emit("49394350");branch("0f85",declines);
        emit("49ba");q(image+(ulong)profile.Rva("CareerManagerVtable"));emit("4c3910");branch("0f85",declines);
        emit("4883b8b001000000");branch("0f85",declines); // match-entry converter must be absent
        emit("488b80e8010000483d00000100");branch("0f82",declines);
        emit("48a907000000");branch("0f85",declines);
        emit("448b4804"); // native picker secondary team slot; no metadata map mutation
        emit("4181f909070000");branch("0f82",noView);emit("4181f96b070000");branch("0f87",noView);
        emit("45398bc8010000");branch("0f84",declines);
        emit("498b4b304885c9");branch("0f84",declines);
        emit("48b8");q(image+(ulong)profile.Rva("Rva_0B38B3B8"));emit("483901");branch("0f85",declines);
        emit("488b8180010000483b81a0010000");branch("0f85",declines); // retain queued native ownership
        emit("4c8b81c80100004c8b91d00100004c89d04c29c0");branch("0f82",declines);
        emit("483d00300000");branch("0f87",declines);
        // A nonempty vector must have a real aligned base. An empty one may
        // have null cursors. The normal dispatcher independently validates it.
        emit("4d39d0");var empty=new List<int>();branch("0f84",empty);
        emit("4981f800000100");branch("0f82",declines);emit("49f7c007000000");branch("0f85",declines);
        int aligned=b.Count;foreach(int p in empty)patch(p,aligned);
        emit("33d241ba7800000049f7f285d2");branch("0f85",declines);
        // R10 was used as the divisor; recover the native vector end.
        emit("4c8b91d001000045898bc8010000");
        int loop=b.Count;emit("4d39d0");var missing=new List<int>();branch("0f83",missing);
        emit("45394858");branch("0f84",declines);emit("4983c078");jump(loop);
        int request=b.Count;foreach(int p in missing)patch(p,request);
        emit("41ff83cc010000"); // private observation counter only
        // The normal detector requires eight consecutive missing observations.
        // This explicit, validated card request supplies that debounce while
        // the ordinary detector still rechecks the missing saved-team record.
        emit("41c783b000000007000000");
        emit("415a415941585a59589d");jump(at+14); // bypass budget once, retain all normal missing-record checks
        int reset=b.Count;foreach(int p in noView)patch(p,reset);emit("41c783c801000000000000");
        int original=b.Count;foreach(int p in declines)patch(p,original);
        emit("415a415941585a59589d");b.AddRange(code.Skip(at).Take(8));jump(at+8);
        b[at]=0xE9;patch(at+1,stub);for(int i=5;i<8;i++)b[at+i]=0x90;
        if(b.Count>=0x2000)throw new InvalidDataException("Viewed-team repair relay overlaps its allocation boundary.");
        return b.ToArray();
    }
}
