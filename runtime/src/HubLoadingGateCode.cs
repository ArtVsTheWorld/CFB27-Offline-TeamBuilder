using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OfflineTeamBuilderRuntime;

internal sealed class HubLoadingGateProfile { public NativeCheck[] validations; }

// Private control memory only. Native jobs, Dynasty data and import state are untouched.
internal static class HubLoadingGateCode
{
    internal const uint TimeoutMs=60000;
    // First 64 bytes: enabled, pending; loader, career, epoch, UI thread, started; reason, shows, holds, releases.
    // Extra: incoming, hide calls, stock completions, cancels, last type, reassertions, show epoch, wait ms.
    // 0x68: one Dynasty-entry ticket; 0x70: waits canceled on match-state entry.
    internal static byte[] Hide(ulong state,ulong status,ulong serviceGlobal,ulong serviceVtable,ulong careerVtable,ulong original,ulong tick,ulong matchStatesGlobal)
    {
        var a=new Asm();a.H("9c5041524153");a.MovR11(state);a.H("41ff434441833b01");a.J("0f85","forward");
        a.H("49ba");a.Q(matchStatesGlobal);a.H("49833a00");a.J("0f85","forward");
        a.H("41837b0400");a.J("0f85","context");a.H("41837b6801");a.J("0f85","forward");a.L("context");
        a.H("49ba");a.Q(serviceGlobal);a.H("4d8b124d85d2");a.J("0f84","forward");
        a.H("48b8");a.Q(serviceVtable);a.H("493902");a.J("0f85","forward");
        a.H("4180bab107000000");a.J("0f84","forward");
        a.H("4180bab307000000");a.J("0f85","forward");
        a.H("4183bad807000000");a.J("0f85","forward");
        a.H("498b82680400004885c0");a.J("0f84","forward");
        a.H("49ba");a.Q(careerVtable);a.H("4c3910");a.J("0f85","forward");
        a.H("49ba");a.Q(status);a.H("49394218");a.J("0f85","forward");a.H("49394250");a.J("0f85","forward");
        a.H("41837a080b");a.J("0f84","eligible");a.H("41837a080c");a.J("0f85","forward");
        a.L("eligible");a.H("49394b08");a.J("0f85","arm");a.H("49394310");a.J("0f85","arm");
        a.H("41837b0400");a.J("0f85","held");
        a.L("arm");a.H("41c743680000000041c743400000000049894b0849894310498b82900000004989431865488b04254800000049894320");
        a.H("4883ec28");a.Call(tick);a.H("4883c428");a.MovR11(state);a.H("4989432849c743600000000041c743300000000041ff433841c7430401000000");
        a.L("held");a.H("415b415a589dc3");
        a.L("forward");a.H("41837b0400");a.J("0f84","forward_plain");
        a.H("49ba");a.Q(status);a.H("41837a0810");a.J("0f85","forward_cancel");
        a.H("498b425049394310");a.J("0f85","forward_cancel");
        a.H("498b829000000049394318");a.J("0f85","forward_cancel");
        a.H("41ff433c41ff434841c7433005000000");a.J("e9","forward_plain");
        a.L("forward_cancel");a.H("41ff434c41c7433004000000");
        a.L("forward_plain");a.H("41c7430400000000415b415a589d");a.Tail(original);return a.Finish();
    }
    internal static byte[] Show(ulong state,ulong status,ulong serviceGlobal,ulong original)
    {
        var a=new Asm();a.H("9c5041524153");a.MovR11(state);a.H("41ff43344189535041c7434000000000");
        a.H("41837b0400");a.J("0f84","track");
        // A new stock Show owns the view. Never turn a match-entry request
        // into another Dynasty wait, even if it uses the same view type.
        a.L("cancel");a.H("41c743040000000041c743300300000041ff434c");
        a.L("track");a.H("83fa01");a.J("0f85","done");
        // Watch this view's entry. If Hide precedes asset startup on a reload,
        // the first safe UI callback reasserts the view once work actually starts.
        a.H("41833b01");a.J("0f85","done");a.H("41837b6801");a.J("0f85","done");a.H("41c743400100000049ba");a.Q(status);a.H("498b829000000049894358");
        a.L("done");a.H("415b415a589d");a.Tail(original);return a.Finish();
    }
    internal static byte[] Return(ulong state,ulong status,ulong serviceGlobal,ulong loaderGlobal,ulong serviceVtable,ulong careerVtable,ulong loaderVtable,ulong originalShow,ulong originalHide,ulong tick,ulong undefinedGlobal,ulong matchStatesGlobal)
    {
        var a=new Asm();a.H("9c41534152");a.MovR11(state);
        // One ticket per observed inactive-career boundary, consumed on first
        // hold. A non-null match-state owner always wins, without calling UI.
        a.H("49ba");a.Q(matchStatesGlobal);a.H("49833a00");a.J("0f85","match_entry");
        a.H("41833b01");a.J("0f85","scope_done");
        a.H("48b8");a.Q(serviceGlobal);a.H("488b004885c0");a.J("0f84","scope_done");
        a.H("49ba");a.Q(serviceVtable);a.H("4c3910");a.J("0f85","scope_done");
        a.H("80b8b107000000");a.J("0f85","scope_done");
        a.H("4883b86804000000");a.J("0f85","scope_done");
        a.H("41c7436801000000");a.J("e9","scope_done");
        a.L("match_entry");a.H("41c743680000000041c743400000000041837b0400");a.J("0f84","scope_done");
        a.H("41c743040000000041c743300600000041ff434c41ff4370");
        a.L("scope_done");a.H("415a41837b0400");a.J("0f85","slow");
        a.H("41833b01");a.J("0f85","finish");a.H("41837b4001");a.J("0f85","finish");a.L("slow");
        // Original helper changes only RAX. Preserve every other volatile register,
        // XMM0..5 and flags even when replaying the deferred hide on its UI thread.
        a.H("51524150415141524881ec80000000");
        for(int i=0;i<6;i++)a.H("f30f7f"+(0x44+i*8).ToString("x2")+"24"+(0x20+i*16).ToString("x2"));
        a.H("41837b0400");a.J("0f84","try_arm");
        a.H("65488b04254800000049394320");a.J("0f85","restore");
        a.Call(tick);a.MovR11(state);a.H("492b4328483d");a.D(TimeoutMs);a.J("0f83","timeout");
        a.H("41837b0402");a.J("0f85","restore");a.H("41c7433001000000");a.J("e9","release");
        a.L("timeout");a.H("41c7433002000000");
        a.L("release");a.H("4989436041c7430400000000");
        a.H("49ba");a.Q(status);a.H("498b829000000049394318");a.J("0f85","cancel");
        a.H("49ba");a.Q(loaderGlobal);a.H("498b4b0849390a");a.J("0f85","cancel");
        a.H("49ba");a.Q(serviceGlobal);a.H("4d8b124d85d2");a.J("0f84","cancel");
        a.H("498b826804000049394310");a.J("0f85","cancel");
        a.H("4180bab107000000");a.J("0f84","cancel");
        a.H("41ff433c");a.Call(originalHide);a.J("e9","restore");
        a.L("cancel");a.H("41c743300400000041ff434c");a.J("e9","restore");
        a.L("try_arm");a.H("41837b6801");a.J("0f85","restore");
        a.H("49ba");a.Q(serviceGlobal);a.H("4d8b124d85d2");a.J("0f84","restore");
        a.H("48b8");a.Q(serviceVtable);a.H("493902");a.J("0f85","restore");
        a.H("4180bab107000000");a.J("0f84","restore");
        a.H("4180bab307000000");a.J("0f85","restore");
        a.H("4183bad807000000");a.J("0f85","restore");
        a.H("498b82680400004885c0");a.J("0f84","restore");
        a.H("49ba");a.Q(careerVtable);a.H("4c3910");a.J("0f85","restore");
        a.H("49ba");a.Q(status);
        // Both ownership fields must match the incoming career.
        a.H("49394218");a.J("0f85","restore");a.H("49394250");a.J("0f85","restore");
        a.H("41837a080b");a.J("0f84","arm");a.H("41837a080c");a.J("0f84","arm");
        a.H("41837a0810");a.J("0f85","restore");
        a.H("498b829000000049394358");a.J("0f84","restore");a.H("41c7434000000000");a.J("e9","restore");
        a.L("arm");a.H("41c743680000000049894310498b82900000004989431849ba");a.Q(loaderGlobal);a.H("498b0a4885c9");a.J("0f84","restore");
        a.H("48b8");a.Q(loaderVtable);a.H("483901");a.J("0f85","restore");
        a.H("49894b0865488b04254800000049894320");a.Call(tick);a.MovR11(state);
        a.H("4989432849c743600000000041c743300000000041c743400000000041ff433841ff435441c7430401000000498b4b08ba01000000");
        a.Call(originalShow);
        a.L("restore");
        for(int i=0;i<6;i++)a.H("f30f6f"+(0x44+i*8).ToString("x2")+"24"+(0x20+i*16).ToString("x2"));
        a.H("4881c480000000415a415941585a59");
        a.L("finish");a.H("415b9d48b8");a.Q(undefinedGlobal);a.H("488b00c3");return a.Finish();
    }
    // Native AS argument marker: mov [rsp+20h],r9; ret. Keep its caller-home
    // write and preserve RAX as well as all registers/flags retained by Tick.
    internal static byte[] Callback(ulong tick)
    {
        var a=new Asm();a.H("4c894c24209c504883ec28");a.Call(tick);
        a.H("4883c428589dc3");return a.Finish();
    }
    // The entry patch must not clobber an incoming argument register.
    internal static byte[] Entry(ulong target)=>GameProfile.Hex("ff2500000000").Concat(BitConverter.GetBytes(target)).Concat(new byte[]{0x90,0x90}).ToArray();
    private sealed class Asm
    {
        private readonly List<byte> b=new List<byte>();private readonly Dictionary<string,int> labels=new Dictionary<string,int>();
        private readonly List<Tuple<int,string>> refs=new List<Tuple<int,string>>();
        internal void H(string h){b.AddRange(GameProfile.Hex(h));} internal void Q(ulong q){b.AddRange(BitConverter.GetBytes(q));} internal void D(uint n){b.AddRange(BitConverter.GetBytes(n));}
        internal void MovR11(ulong p){H("49bb");Q(p);} internal void Call(ulong p){H("48b8");Q(p);H("ffd0");}
        internal void Tail(ulong p){H("ff2500000000");Q(p);} internal void L(string s){labels.Add(s,b.Count);}
        internal void J(string opcode,string target){H(opcode);refs.Add(Tuple.Create(b.Count,target));D(0);}
        internal byte[] Finish(){foreach(var r in refs){byte[] v=BitConverter.GetBytes(labels[r.Item2]-r.Item1-4);for(int i=0;i<4;i++)b[r.Item1+i]=v[i];}if(b.Count>0x800)throw new InvalidDataException("Hub gate thunk exceeded its private region.");return b.ToArray();}
    }
}

internal sealed class HubLoadingGatePolicy
{
    private ulong epoch;private long stableSince=-1;
    internal bool Observe(TeamReadinessSnapshot s,uint stage,uint pending,uint deferred,uint failures,ulong requestedEpoch,long now)
    {
        bool ready=s!=null&&s.observation=="StableSample"&&s.currentCareerOwned&&s.careerActive&&!s.stockImportActive&&!s.savedMapBusy&&
            s.expectedTeams>0&&s.runtimeRecordsPresent==s.expectedTeams&&s.teams.Count==s.expectedTeams&&s.teams.All(t=>t.runtimeRecord=="Present")&&
            s.nativeQueueCount==0&&s.jobs.Count==0&&s.olderEpochPluginJobs==0&&s.epoch==requestedEpoch&&stage==16&&pending==0&&deferred==0&&failures==0;
        if(!ready){stableSince=-1;return false;}
        if(stableSince<0||epoch!=s.epoch||now<stableSince){stableSince=now;epoch=s.epoch;return false;}
        return now-stableSince>=150;
    }
    internal void Reset(){stableSince=-1;}
}
