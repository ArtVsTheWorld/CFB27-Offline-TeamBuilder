using System;
using System.IO;
using System.Linq;

namespace OfflineTeamBuilderRuntime;

// A selected school identifies the coach/context. It is NOT necessarily the
// TeamBuilder school whose assets we are restoring. Only broaden that identity
// check; descriptor slots, jobs and news requests retain their 1801..1899 guards.
internal static class CoachContextScope
{
    internal static byte[] Apply(string name, byte[] code, GameProfile profile)
    {
        if (name != "Career" && name != "Dispatcher") return code;
        byte[] result = (byte[])code.Clone();
        // Exact frozen instruction pair: movzx edx,word [rax+F78]; cmp edx,1801.
        // Both Career sites and both dispatcher coach-monitor sites must agree.
        // Leave the following unsigned branches and upper bound (1899) intact.
        byte[] marker = GameProfile.Hex("0fb790780f000081fa09070000");
        int[] sites = Enumerable.Range(0, Math.Max(0, code.Length - marker.Length + 1))
            .Where(n => marker.Select((v, i) => code[n+i] == v).All(v => v)).ToArray();
        if (sites.Length != 2) throw new InvalidDataException("Unknown selected-school scope: " + name);
        foreach (int at in sites) {
            if (at + 31 > code.Length || code[at+13] != 0x0F || code[at+14] != 0x82 ||
                !code.Skip(at+19).Take(8).SequenceEqual(GameProfile.Hex("81fa6b0700000f87")))
                throw new InvalidDataException("Unknown selected-school range branches: " + name);
            // The emitted range is only an identity sanity bound. It never
            // supplies a built-in ID to the native TeamBuilder package builder.
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, result, at + 9, 4);
        }
        return result;
    }
}
