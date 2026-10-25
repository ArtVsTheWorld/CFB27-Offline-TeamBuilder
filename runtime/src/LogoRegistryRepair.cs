using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace OfflineTeamBuilderRuntime;

// Logo-only registration on the existing native dispatcher. The team selector
// legitimately has no selected-school object. Never relax the uniform loader's
// ownership rules to accommodate that screen. No importer/UI refresh call here.
internal static class LogoRegistryRepair
{
    internal const int Attempts=0x188, Cooldown=0x18C, ConsecutiveAttempts=0x190, SeenEpoch=0x1A0;
    internal const int DeferredPass=0x1A8, DeferredAttempts=0x1B0;
    private const int BodyStart=0x432, BodyEnd=0x73A;
    // rel32 operand offsets in the frozen, already-tested v15 logo validator.
    // All internal jumps are rebased; the sole call still reaches the original
    // dual-variant helper. No runtime x64 instruction guessing is performed.
    private static readonly int[] Branches={18,37,53,66,89,106,125,148,157,175,183,213,222,235,248,267,286,305,314,337,350,360,370,400,419,433,444,453,466,478,488,510,523,552,572,601,621,641,676,731,761};

    internal static byte[] Apply(string name,byte[] code,GameProfile profile,ulong[] args)
    {
        if(name!="Dispatcher")return code;
        byte[] raw=GameProfile.Hex(profile.templates[name].code);
        using(var sha=SHA256.Create()) {
            string digest=BitConverter.ToString(sha.ComputeHash(raw.Skip(BodyStart).Take(BodyEnd-BodyStart).ToArray())).Replace("-","").ToLowerInvariant();
            if(digest!="91901ecfe8d63d705d6f67c861ce0a3177a5901098173ff0a5194b66ba5e48f7")
                throw new InvalidDataException("Unknown saved-logo validator; refusing logo repair relay.");
        }
        ulong image=args[1]-(ulong)profile.Rva("DispatcherContinuation");
        byte[] prefix=GameProfile.Hex("f30f6f842480000000f30f6f8c2490000000");
        int at=Enumerable.Range(0,code.Length-prefix.Length+1).Single(n=>prefix.Select((v,i)=>code[n+i]==v).All(x=>x));
        var b=new Buffer(code);
        // Dispatcher has saved flags, all volatile GPRs/XMMs and shadow space.
        // Reacquire R11: earlier exits need not leave it pointing at status.
        b.Hex("49bb");b.Q(args[0]);
        b.Hex("41c783a801000000000000");
        foreach(int field in new[]{4,0x4C,0xC8,0x104}) {
            b.Hex("4183bb");b.D(field);b.Hex("00");b.J("0f85","finish");
        }
        b.Hex("41837b0810");b.J("0f85","deferred_pass");
        b.Hex("4183bb8400000000");b.J("0f85","finish");
        b.Hex("41837b4800");b.J("0f84","finish");
        b.J("e9","ownership");
        b.Label("deferred_pass");
        b.Hex("41837b080c");b.J("0f85","finish");
        b.Hex("4183bb8400000001");b.J("0f85","finish");
        b.Hex("41c783a801000001000000");
        b.Label("ownership");
        b.Hex("48b9");b.Q(image+(ulong)profile.Rva("ServiceGlobal"));
        b.Hex("488b094885c9");b.J("0f84","finish");
        b.Hex("49394b10");b.J("0f85","finish");
        b.Hex("48b8");b.Q(image+(ulong)profile.Rva("ServiceVtable"));
        b.Hex("483901");b.J("0f85","finish");
        b.Hex("80b9b107000000");b.J("0f84","finish");
        b.Hex("80b9b307000000");b.J("0f85","finish");
        b.Hex("83b9d807000000");b.J("0f85","finish");
        b.Hex("488b81680400004885c0");b.J("0f84","finish");
        b.Hex("49394350");b.J("0f85","finish");
        b.Hex("49394318");b.J("0f85","finish");
        b.Hex("48ba");b.Q(image+(ulong)profile.Rva("CareerManagerVtable"));
        b.Hex("483910");b.J("0f85","finish");
        b.Hex("4883b8b001000000");b.J("0f85","finish");
        b.Hex("4183bba801000000");b.J("0f84","scope_checked");
        b.Hex("488b81700400004885c0");b.J("0f84","finish");
        b.Hex("48ba");b.Q(image+(ulong)profile.Rva("CareerContextVtable"));
        b.Hex("483910");b.J("0f85","finish");
        b.Hex("488b80d00100004885c0");b.J("0f84","scope_checked");
        b.Hex("49394358");b.J("0f85","finish");
        b.Hex("0fb790780f0000413b93d8000000");b.J("0f85","finish");
        b.Label("scope_checked");
        b.Hex("48b9");b.Q(image+(ulong)profile.Rva("Rva_0E350900"));
        b.Hex("488b094885c9");b.J("0f84","finish");
        b.Hex("49394b30");b.J("0f85","finish");
        b.Hex("48b8");b.Q(image+(ulong)profile.Rva("Rva_0B38B3B8"));
        b.Hex("483901");b.J("0f85","finish");
        b.Hex("488b813801000049394340");b.J("0f85","finish");
        b.Hex("48b8");b.Q(image+(ulong)profile.Rva("Rva_0B36D3D0"));
        b.Hex("48394120");b.J("0f85","finish");
        // Read only bounded library headers here. The copied validator below
        // independently verifies metadata, both DDS images and native targets.
        b.Hex("488d51384c8d42484531d249b9");b.Q(image+(ulong)profile.Rva("Rva_0B36D3E0"));
        b.Label("libraries");b.Hex("488b024885c0");b.J("0f84","finish");
        b.Hex("4c3908");b.J("0f85","finish");
        b.Hex("81782c00100000");b.J("0f87","finish");
        b.Hex("83782c00");b.J("0f85","next");b.Hex("41ba01000000");
        b.Label("next");b.Hex("4883c2084c39c2");b.J("0f82","libraries");
        b.Hex("4585d2");b.J("0f84","present");
        // Three bounded attempts per epoch or successfully restored interval.
        // Cooldown is dispatcher ticks, NOT a seconds-based completion timeout.
        b.Hex("498b8390000000493b83a0010000");b.J("0f84","same_epoch");
        b.Hex("498983a001000041c783900100000000000041c7838c01000000000000");
        b.Label("same_epoch");b.Hex("4183bb9001000003");b.J("0f83","finish");
        b.Hex("4183bb8c01000000");b.J("0f84","repair");
        b.Hex("41ff8b8c010000");b.J("e9","finish");
        b.Label("repair");b.Hex("41ff838801000041ff839001000041c7838c0100003c000000");
        b.Hex("4183bba801000001");b.J("0f84","deferred_queue");
        int copied=b.Position;
        b.Bytes(code.Skip(BodyStart).Take(BodyEnd-BodyStart).ToArray());
        int finished=b.Position;
        foreach(int displacement in Branches) {
            int original=BodyStart+displacement;
            int target=original+4+BitConverter.ToInt32(code,original);
            int relocated=target==BodyEnd?finished:
                target>=BodyStart&&target<BodyEnd?copied+target-BodyStart:target;
            if(target!=BodyEnd&&!(target>=BodyStart&&target<BodyEnd)&&target!=2982)
                throw new InvalidDataException("Unknown saved-logo control-flow target.");
            b.Patch(copied+displacement,relocated);
        }
        b.J("e9","finish");
        // This is NOT general permission to register during native work. The
        // observed reload has a pending read owned by this plugin from an old
        // epoch, and only unstarted jobs behind it. No queue/job fields change.
        b.Label("deferred_queue");
        b.Hex("4c8b81800100004c8b89a00100004d85c0");b.J("0f84","finish");
        b.Hex("49f7c007000000");b.J("0f85","finish");
        b.Hex("4c89c84c29c0");b.J("0f86","finish");
        b.Hex("483d00040000");b.J("0f87","finish");
        b.Hex("a807");b.J("0f85","finish");
        b.Hex("498b104885d2");b.J("0f84","finish");
        b.Hex("48b8");b.Q(image+(ulong)profile.Rva("Rva_0B38CA08"));
        b.Hex("483902");b.J("0f85","finish");
        b.Hex("837a0801");b.J("0f85","finish");
        b.Hex("4c395a18");b.J("0f85","finish");
        b.Hex("488b4220493b8390000000");b.J("0f83","finish");
        // Require the old read's descriptor to still belong to this live
        // metadata vector. The normal copied validator checks every descriptor
        // and both supplied DDS blobs before invoking the nonclearing registrar.
        b.Hex("4c8b91300100004d85d2");b.J("0f84","finish");
        b.Hex("49f7c207000000");b.J("0f85","finish");
        b.Hex("488b81380100004c29d0");b.J("0f86","finish");
        b.Hex("483d00010000");b.J("0f87","finish");
        b.Hex("a807");b.J("0f85","finish");
        b.Hex("488b5250");
        b.Label("head_metadata");b.Hex("493912");b.J("0f84","head_found");
        b.Hex("4983c2084c3b9138010000");b.J("0f82","head_metadata");b.J("e9","finish");
        b.Label("head_found");b.Hex("498d5008");
        b.Label("tail_jobs");b.Hex("4c39ca");b.J("0f83","queue_checked");
        b.Hex("488b024885c0");b.J("0f84","finish");
        b.Hex("83780800");b.J("0f85","finish");
        b.Hex("4883c208");b.J("e9","tail_jobs");
        b.Label("queue_checked");
        b.Hex("4c3b8180010000");b.J("0f85","finish");
        b.Hex("4c3b89a0010000");b.J("0f85","finish");
        b.Hex("41ff83b0010000");
        // The common guard already checked manager identity/vtable. Resume
        // immediately AFTER the frozen validator's ordinary empty-queue check;
        // all remaining registry/vector/blob/signature/recursion checks stay.
        b.Branch("e9",copied+0x46);
        b.Label("present");b.Hex("41c783900100000000000041c7838c01000000000000");
        b.Label("finish");b.Bytes(code.Skip(at).Take(9).ToArray());b.Branch("e9",at+9);
        b.Resolve();b.Detour(at,9,code.Length);
        if(b.Position>=0x2000)throw new InvalidDataException("Logo repair overlaps next native allocation region.");
        return b.ToArray();
    }

    private sealed class Buffer
    {
        private readonly List<byte> bytes;
        private readonly Dictionary<string,int> labels=new Dictionary<string,int>();
        private readonly List<Tuple<int,string>> jumps=new List<Tuple<int,string>>();
        internal Buffer(byte[] original){bytes=new List<byte>(original);}
        internal int Position=>bytes.Count;
        internal void Bytes(byte[] v){bytes.AddRange(v);}
        internal void Hex(string v){Bytes(GameProfile.Hex(v));}
        internal void Q(ulong v){Bytes(BitConverter.GetBytes(v));}
        internal void D(int v){Bytes(BitConverter.GetBytes(v));}
        internal void Label(string label){labels.Add(label,Position);}
        internal void J(string op,string label){Hex(op);jumps.Add(Tuple.Create(Position,label));D(0);}
        internal void Branch(string op,int target){Hex(op);int at=Position;D(0);Patch(at,target);}
        internal void Patch(int at,int target){byte[] value=BitConverter.GetBytes(checked(target-at-4));for(int i=0;i<4;i++)bytes[at+i]=value[i];}
        internal void Resolve(){foreach(var j in jumps)Patch(j.Item1,labels[j.Item2]);}
        internal void Detour(int at,int size,int target){bytes[at]=0xE9;Patch(at+1,target);for(int i=5;i<size;i++)bytes[at+i]=0x90;}
        internal byte[] ToArray()=>bytes.ToArray();
    }
}
