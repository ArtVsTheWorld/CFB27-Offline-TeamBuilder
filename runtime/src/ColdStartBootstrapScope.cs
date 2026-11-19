using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OfflineTeamBuilderRuntime;

// Adopt an already-active frontend when its initial CareerHub notification
// preceded installation. Runs inside the existing AS/native dispatcher after
// register preservation. No hub callback, importer, remote-thread call, native
// queue write or game/save edit. Publish the same private deferred request as
// Career admission; normal admission and callbacks still own all submissions.
internal static class ColdStartBootstrapScope
{
    internal const int Starts=0x1D0, Active=0x1D4, CareerDepth=0x1D8;
    internal static byte[] Apply(string name,byte[] code,GameProfile p,ulong[] args)
    {
        if(name=="Career")return GuardCareer(code);
        if(name!="Dispatcher")return code;
        byte[] marker=GameProfile.Hex("49837b50000f84");
        int[] hits=Enumerable.Range(0,code.Length-marker.Length+1).Where(n=>marker.Select((v,i)=>code[n+i]==v).All(v=>v)).ToArray();
        if(hits.Length!=1)throw new InvalidDataException("Unknown uninitialized dispatcher boundary.");
        int at=hits[0];ulong image=args[1]-(ulong)p.Rva("DispatcherContinuation");
        var a=new Asm(code);int stub=a.Position;
        // Clear the missing-only flag after its pass. A real initialized owner
        // always uses the pre-existing lifecycle/recovery implementation.
        a.H("41837b0810");a.J("0f85","cold");a.Store(Active,0);
        a.L("cold");a.H("49837b5000");a.J("0f85","original");
        foreach(int field in new[]{4,8,0x84,0xC8,CareerDepth}) { a.H("4183bb");a.D(field);a.H("00");a.J("0f85","original"); }
        a.Mov("48b8",image+(ulong)p.Rva("GateMatchStatesGlobal"));a.H("48833800");a.J("0f85","original");
        a.Mov("48b8",image+(ulong)p.Rva("ServiceGlobal"));a.H("488b08");a.Pointer("rcx");
        a.Mov("48b8",image+(ulong)p.Rva("ServiceVtable"));a.H("483901");a.J("0f85","original");
        a.H("80b9b107000000");a.J("0f84","original");
        a.H("80b9b307000000");a.J("0f85","original");
        a.H("83b9d807000000");a.J("0f85","original");
        a.H("48894c2420488b9168040000");a.Pointer("rdx");
        a.Mov("48b8",image+(ulong)p.Rva("CareerManagerVtable"));a.H("483902");a.J("0f85","original");
        a.H("4883bab001000000");a.J("0f85","original");a.H("4889542428");
        a.H("488b8170040000");a.Pointer("rax");
        a.Mov("48ba",image+(ulong)p.Rva("CareerContextVtable"));a.H("483910");a.J("0f85","original");
        a.H("48894424304c8b80d0010000");a.Pointer("r8");
        a.H("410fb780780f000083f801");a.J("0f82","original");a.H("3d6b070000");a.J("0f87","original");
        a.Mov("48b8",image+(ulong)p.Rva("Rva_0E350900"));a.H("4c8b08");a.Pointer("r9");
        a.Mov("48b8",image+(ulong)p.Rva("Rva_0B38B3B8"));a.H("493901");a.J("0f85","original");
        a.Mov("48ba",image+(ulong)p.Rva("Rva_0317B260"));a.H("48395020");a.J("0f85","original");
        // Queue equality admits an unallocated empty queue, as normal admission
        // does. Nothing in the queue or a native descriptor is changed.
        a.H("498b8180010000493b81a0010000");a.J("0f85","original");
        a.H("498b8130010000");a.Pointer("rax");a.H("4889442410");
        a.H("498b913801000048895424184829c2");a.J("0f86","original");
        a.H("4881fa00010000");a.J("0f87","original");a.H("f6c207");a.J("0f85","original");
        a.H("498b81c8010000498b89d00100004889042448894c24084889ca4829c2");a.J("0f82","original");
        a.H("4881fa00300000");a.J("0f87","original");
        a.H("4885c0");a.J("0f84","empty_records");a.Pointer("rax");a.J("e9","records_valid");
        a.L("empty_records");a.H("4885c9");a.J("0f85","original");
        a.L("records_valid");a.H("4889d031d2b97800000048f7f185d2");a.J("0f85","original");
        // Find a typed TeamBuilder descriptor absent from the current runtime
        // records. This avoids restarting work just to populate our status.
        a.H("488b5424104c8b542418");a.L("node");a.H("4c39d2");a.J("0f83","original");
        a.H("488b024885c0");a.J("0f84","next");a.Pointer("rax");
        a.Mov("48b9",image+(ulong)p.Rva("Rva_0B494B90"));a.H("483908");a.J("0f85","next");
        a.H("0fb7481081f909070000");a.J("0f82","next");a.H("81f96b070000");a.J("0f87","next");
        a.H("488b0424");a.L("record");a.H("483b442408");a.J("0f83","missing");
        a.H("394858");a.J("0f84","next");a.H("4883c078");a.J("e9","record");
        a.L("next");a.H("4883c208");a.J("e9","node");
        a.L("missing");
        // Revalidate snapshots before publishing ownership. A changing save,
        // metadata vector or record vector leaves private ownership null.
        a.Mov("48b8",image+(ulong)p.Rva("GateMatchStatesGlobal"));a.H("48833800");a.J("0f85","original");
        a.Mov("48b8",image+(ulong)p.Rva("ServiceGlobal"));a.H("488b08483b4c2420");a.J("0f85","original");
        a.Mov("48b8",image+(ulong)p.Rva("ServiceVtable"));a.H("483901");a.J("0f85","original");
        a.H("488b9168040000483b542428");a.J("0f85","original");
        a.Mov("48b8",image+(ulong)p.Rva("CareerManagerVtable"));a.H("483902");a.J("0f85","original");
        a.H("4883bab001000000");a.J("0f85","original");
        a.H("488b8170040000483b442430");a.J("0f85","original");
        a.Mov("49ba",image+(ulong)p.Rva("CareerContextVtable"));a.H("4c3910");a.J("0f85","original");
        a.H("4c3980d0010000");a.J("0f85","original");
        a.Mov("48b8",image+(ulong)p.Rva("Rva_0E350900"));a.H("4c3908");a.J("0f85","original");
        a.Mov("48b8",image+(ulong)p.Rva("Rva_0B38B3B8"));a.H("493901");a.J("0f85","original");
        a.Mov("49ba",image+(ulong)p.Rva("Rva_0317B260"));a.H("4c395020");a.J("0f85","original");
        foreach(var field in new[]{new[]{0x130,0x10},new[]{0x138,0x18},new[]{0x1C8,0},new[]{0x1D0,8}}) {
            a.H("498b81");a.D(field[0]);a.H("483b4424");a.Byte(field[1]);a.J("0f85","original");
        }
        a.H("498b8180010000493b81a0010000");a.J("0f85","original");
        a.H("80b9b107000000");a.J("0f84","original");a.H("80b9b307000000");a.J("0f85","original");
        a.H("83b9d807000000");a.J("0f85","original");
        a.H("49837b5000");a.J("0f85","original");
        foreach(int field in new[]{4,8,0x84,0xC8,CareerDepth}) { a.H("4183bb");a.D(field);a.H("00");a.J("0f85","original"); }
        // Same private fields and callback as the initial Career pass, but no
        // call through CareerHub/MGS/importer and no fabricated completion.
        a.H("49894b1049895318498953504d8943584d894b30410fb780780f000041894320418983d8000000");
        a.H("488b44241049894338488b44241849894340");
        foreach(int field in new[]{0x48,0x4C,0xA0,0xA4,0xB0,0xB4,0xC8,0xE0,0xE4,0xF4})a.Store(field,0);
        a.H("49ff8390000000");
        var callback=p.templates["Career"].relocations.Single(r=>r.arg==0&&r.Add(p)!=0);
        a.Mov("48b8",unchecked(args[0]+(ulong)callback.Add(p)));a.H("49894360");
        a.Mov("48b8",image+(ulong)p.Rva("Rva_02AEF4A0"));a.H("49894370");
        a.Store(Active,1);a.H("41ff83");a.D(Starts);a.Store(8,12);a.Store(0x84,1);
        a.L("original");a.H("49837b5000");a.Jump(at+5);
        byte[] result=a.Finish();result[at]=0xE9;
        Buffer.BlockCopy(BitConverter.GetBytes(stub-at-5),0,result,at+1,4);
        if(result.Length>=0x2000)throw new InvalidDataException("Cold-start relay overlaps its allocation boundary.");
        return result;
    }
    private static byte[] GuardCareer(byte[] code)
    {
        Func<byte[],int> unique=marker=>{
            int[] hits=Enumerable.Range(0,code.Length-marker.Length+1).Where(n=>marker.Select((v,i)=>code[n+i]==v).All(v=>v)).ToArray();
            if(hits.Length!=1)throw new InvalidDataException("Unknown Career callback depth boundary.");return hits[0];
        };
        int enter=unique(GameProfile.Hex("ff0648b8")),exit=unique(GameProfile.Hex("8bc54883c4605e5d5bc3"));
        var b=new List<byte>(code);
        Action<int,int> patch=(at,to)=>{byte[] d=BitConverter.GetBytes(to-at-4);for(int i=0;i<4;i++)b[at+i]=d[i];};
        Action<int> jump=to=>{b.Add(0xE9);int operand=b.Count;b.AddRange(new byte[4]);patch(operand,to);};
        int enterStub=b.Count;b.AddRange(GameProfile.Hex("f0ff86"));b.AddRange(BitConverter.GetBytes(CareerDepth));
        b.AddRange(code.Skip(enter).Take(12));jump(enter+12);
        int exitStub=b.Count;b.AddRange(GameProfile.Hex("f0ff8e"));b.AddRange(BitConverter.GetBytes(CareerDepth));
        b.AddRange(code.Skip(exit).Take(6));jump(exit+6);
        b[enter]=0xE9;patch(enter+1,enterStub);for(int i=5;i<12;i++)b[enter+i]=0x90;
        b[exit]=0xE9;patch(exit+1,exitStub);b[exit+5]=0x90;
        if(b.Count>=0x2000)throw new InvalidDataException("Career depth relay overlaps its allocation boundary.");
        return b.ToArray();
    }
    private sealed class Asm
    {
        private readonly List<byte>b;private readonly Dictionary<string,int>labels=new Dictionary<string,int>();
        private readonly List<Tuple<int,string>>fixups=new List<Tuple<int,string>>();
        internal Asm(byte[] code){b=new List<byte>(code);}internal int Position=>b.Count;
        internal void H(string s)=>b.AddRange(GameProfile.Hex(s));internal void D(int n)=>b.AddRange(BitConverter.GetBytes(n));
        internal void Byte(int n)=>b.Add((byte)n);internal void Mov(string op,ulong v){H(op);b.AddRange(BitConverter.GetBytes(v));}
        internal void J(string op,string name){H(op);fixups.Add(Tuple.Create(b.Count,name));D(0);}internal void L(string s)=>labels.Add(s,b.Count);
        internal void Jump(int to){H("e9");D(to-b.Count-4);}internal void Store(int field,int v){H("41c783");D(field);D(v);}
        internal void Pointer(string reg) {
            string low=reg=="rax"?"483d00000100":reg=="rcx"?"4881f900000100":reg=="rdx"?"4881fa00000100":reg=="r8"?"4981f800000100":"4981f900000100";
            H(low);J("0f82","original");
            H(reg=="rax"?"48a907000000":reg=="rcx"?"48f7c107000000":reg=="rdx"?"48f7c207000000":reg=="r8"?"49f7c007000000":"49f7c107000000");J("0f85","original");
            H("4152");Mov("49ba",0x7FFFFFFFFFF7UL);
            H(reg=="rax"?"4c39d0":reg=="rcx"?"4c39d1":reg=="rdx"?"4c39d2":reg=="r8"?"4d39d0":"4d39d1");
            H("415a");J("0f87","original");
        }
        internal byte[] Finish(){foreach(var f in fixups){byte[] d=BitConverter.GetBytes(labels[f.Item2]-f.Item1-4);for(int i=0;i<4;i++)b[f.Item1+i]=d[i];}return b.ToArray();}
    }
}
