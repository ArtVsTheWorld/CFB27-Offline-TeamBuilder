using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OfflineTeamBuilderRuntime;

// Admission, NOT cancellation. The native queue retains ownership of every
// submitted job, including jobs whose callback epoch we have retired. Never
// append replacement work behind one of those jobs or a stock import/update.
// Detours only replace known, relocation-free instruction boundaries in our
// own emitted relays. The profile and all game/news/audio entry hooks stay put.
internal static class NativeWorkAdmission
{
    internal const int CareerDeferrals = 0x180;
    internal const int DispatcherDeferrals = 0x184;

    internal static byte[] Apply(string name, byte[] code, GameProfile profile, ulong[] args)
    {
        if (name != "Career" && name != "Dispatcher") return code;
        bool career = name == "Career";
        ulong image = career ? args[5] - (ulong)profile.Rva("Enter")
                             : args[1] - (ulong)profile.Rva("DispatcherContinuation");
        // Career: after bounded vector setup, BEFORE consuming its first slot.
        // Dispatcher: before consuming deferred-ready or setting pending=1.
        int at = Unique(code, GameProfile.Hex(career ? "488b46384c8b46404c39c0" :
            "41c783840000000000000041c7430401000000"));
        int count = career ? 8 : 11;
        int resume = at + count;
        int exit = Unique(code, GameProfile.Hex(career ? "8bc54883c4605e5d5bc3" :
            "f30f6f842480000000f30f6f8c2490000000"));
        var b = new Stub(code);
        // Volatile registers and flags at the intercepted boundary must survive
        // a successful admission. No native calls, allocator or queue mutation.
        b.Hex("9c5051524150415141524153");
        b.Hex("48b9"); b.Q(image + (ulong)profile.Rva("ServiceGlobal"));
        b.Hex("488b094885c9"); b.Fail("0f84");
        if (career) b.Hex("4839d9");       // global service == RBX
        else b.Hex("49394b10");          // global service == status->service
        b.Fail("0f85");
        b.Hex("80b9b107000000"); b.Fail("0f84");
        b.Hex("80b9b307000000"); b.Fail("0f85");
        b.Hex("83b9d807000000"); b.Fail("0f85");
        // Re-read ownership at admission, not merely at the dispatcher's
        // earlier lifecycle check. Context may disappear during match prep.
        b.Hex("488b81680400004885c0"); b.Fail("0f84");
        b.Hex(career ? "48394650" : "49394350"); b.Fail("0f85");
        b.Hex(career ? "48394618" : "49394318"); b.Fail("0f85");
        b.Hex("48ba"); b.Q(image + (ulong)profile.Rva("CareerManagerVtable"));
        b.Hex("483910"); b.Fail("0f85");
        b.Hex("4883b8b001000000"); b.Fail("0f85"); // converter must be absent
        b.Hex("488b81700400004885c0"); b.Fail("0f84");
        b.Hex("488b80d00100004885c0"); b.Fail("0f84");
        b.Hex(career ? "48394658" : "49394358"); b.Fail("0f85");
        b.Hex("0fb790780f0000");
        b.Hex(career ? "3b96d8000000" : "413b93d8000000"); b.Fail("0f85");
        b.Hex("48b9"); b.Q(image + (ulong)profile.Rva("Rva_0E350900"));
        b.Hex("488b094885c9"); b.Fail("0f84");
        b.Hex(career ? "48394e30" : "49394b30"); b.Fail("0f85");
        b.Hex("48ba"); b.Q(image + (ulong)profile.Rva("Rva_0B38B3B8"));
        b.Hex("483911"); b.Fail("0f85");
        b.Hex("48ba"); b.Q(image + (ulong)profile.Rva("Rva_0317B260"));
        b.Hex("488b0148395020"); b.Fail("0f85");
        // Reject a retired/rebounded metadata vector before consuming its
        // cursor. Bounds do not prove allocation identity if addresses are
        // reused; retain the normal typed-node and callback-epoch checks too.
        b.Hex("4c8b81300100004d85c0"); b.Fail("0f84");
        b.Hex("49f7c007000000"); b.Fail("0f85");
        b.Hex("4c8b8938010000");
        b.Hex(career ? "4c3b4e40" : "4d3b4b40"); b.Fail("0f85");
        b.Hex("4d89ca4d29c2"); b.Fail("0f82");
        b.Hex("4981fa00010000"); b.Fail("0f87");
        b.Hex("49f7c207000000"); b.Fail("0f85");
        b.Hex(career ? "488b4638" : "498b4338");
        b.Hex("4c39c0"); b.Fail("0f82");
        b.Hex("4c39c8"); b.Fail("0f87");
        b.Hex("4c29c048a907000000"); b.Fail("0f85");
        // An unallocated empty queue (both cursors zero) is also empty. The
        // builder owns its allocation; we never dereference either cursor.
        b.Hex("488b8180010000");
        b.Hex("483b81a0010000"); b.Fail("0f85");
        b.Hex("415b415a415941585a59589d");
        b.Bytes(code.Skip(at).Take(count).ToArray());
        b.Branch("e9", resume);
        int declined = b.Position;
        // Only private diagnostic counters change on a declined submission.
        b.Hex(career ? "ff8680010000" : "41ff8384010000");
        if (career) {
            // CareerHub may notify only once. Publish the same native deferred
            // request that a normal callback publishes, WITHOUT consuming a
            // slot, constructing a job, or re-calling the Career chain. The
            // existing dispatcher will start slot zero after the queue drains.
            var callback = profile.templates["Career"].relocations.Single(r => r.arg == 0 && r.Add(profile) != 0);
            b.Hex("48b8"); b.Q(unchecked(args[0] + (ulong)callback.Add(profile)));
            b.Hex("4889466048b8"); b.Q(image + (ulong)profile.Rva("Rva_02AEF4A0"));
            b.Hex("48894670c746080c000000c7868400000001000000");
        }
        b.Hex("415b415a415941585a59589d");
        b.Branch("e9", exit);
        b.ResolveFailures(declined);
        b.Detour(at, count, code.Length);
        byte[] result = b.ToArray();
        if (result.Length >= 0x2000) throw new InvalidDataException("Admission relay overlaps the next native allocation region.");
        return result;
    }

    private static int Unique(byte[] code, byte[] marker)
    {
        var matches = Enumerable.Range(0, Math.Max(0, code.Length - marker.Length + 1))
            .Where(n => marker.Select((v, i) => code[n + i] == v).All(x => x)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Native work-admission boundary is not unique; refusing to emit relay.");
        return matches[0];
    }

    private sealed class Stub
    {
        private readonly List<byte> bytes;
        private readonly List<int> failures = new List<int>();
        internal Stub(byte[] original) { bytes = new List<byte>(original); }
        internal int Position => bytes.Count;
        internal void Hex(string value) { Bytes(GameProfile.Hex(value)); }
        internal void Bytes(byte[] value) { bytes.AddRange(value); }
        internal void Q(ulong value) { Bytes(BitConverter.GetBytes(value)); }
        internal void Branch(string opcode, int target) {
            Hex(opcode); int displacement = Position; Bytes(new byte[4]); Patch(displacement, target);
        }
        internal void Fail(string opcode) { Hex(opcode); failures.Add(Position); Bytes(new byte[4]); }
        internal void ResolveFailures(int target) { foreach (int at in failures) Patch(at, target); }
        private void Patch(int at, int target) {
            byte[] value = BitConverter.GetBytes(checked(target - at - 4));
            for (int i = 0; i < 4; i++) bytes[at + i] = value[i];
        }
        internal void Detour(int at, int length, int target) {
            if (length < 5) throw new InvalidDataException("Short admission detour.");
            bytes[at] = 0xE9; Patch(at + 1, target);
            for (int i = 5; i < length; i++) bytes[at + i] = 0x90;
        }
        internal byte[] ToArray() => bytes.ToArray();
    }
}
