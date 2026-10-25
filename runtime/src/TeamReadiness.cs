using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OfflineTeamBuilderRuntime;

// Read-only evidence, never a new gate for the working news/audio paths.
// Runtime record presence is the first readiness milestone, NOT proof of GPU
// textures, world/field streaming, UI registration or audible stadium sounds.
internal sealed class TeamReadinessSnapshot
{
    public string observation = "Unavailable";
    public string scope = "RuntimeRecordsAndNativeQueueOnly";
    public string reason;
    public bool careerActive;
    public bool stockImportActive;
    public bool savedMapBusy;
    public bool currentCareerOwned;
    public ulong epoch;
    public int expectedTeams;
    public int runtimeRecordsPresent;
    public int nativeQueueCount;
    public int olderEpochPluginJobs;
    public bool queueStallSuspected;
    public long unchangedHeadMilliseconds;
    public bool? matchEntryReady = null;
    public List<TeamReadiness> teams = new List<TeamReadiness>();
    public List<NativeJobReadiness> jobs = new List<NativeJobReadiness>();
    public string limitation = "Read-only, non-atomic observation. A native runtime record does not verify uniform textures, logos, stadium world/field dependencies or audio playback. No match-entry-ready signal is claimed.";
    internal string HeadIdentity;
}

internal sealed class TeamReadiness
{
    public uint slot;
    public string runtimeRecord;
    public int queuedJobs;
    public bool activeTeam;
    public string uniformTextures = "Unverified";
    public string stadiumWorldAndField = "Unverified";
    public string logoTextures = "Unverified";
    public string audioPlayback = "Unverified";
}

internal sealed class NativeJobReadiness
{
    public int index;
    public string kind;
    public uint state;
    public uint? slot;
    public bool pluginOwned;
    public ulong? callbackEpoch;
    public bool olderEpoch;
    internal ulong Address;
    internal ulong Vtable;
}

internal static class TeamReadinessReader
{
    // These are the bounded October-layout vectors already used by the native
    // relays. Validate their owning object and builder before traversing them.
    private const int ServiceSize = 0x800, ManagerSize = 0x1D8;
    private const int StatusSize = 0x188, RecordSize = 0x78;
    private const int MaxTeams = 32, MaxRecords = 102, MaxJobs = 128;

    internal static TeamReadinessSnapshot Capture(Func<ulong,int,byte[]> read, ulong image, ulong status, GameProfile p)
    {
        var result = new TeamReadinessSnapshot();
        try {
            ulong service = Q(Read(read, image + (ulong)p.Rva("ServiceGlobal"), 8), 0);
            ulong manager = Q(Read(read, image + (ulong)p.Rva("Rva_0E350900"), 8), 0);
            byte[] svc = Read(read, service, ServiceSize), mgr = Read(read, manager, ManagerSize), state = Read(read, status, StatusSize);
            if (Q(svc, 0) != image + (ulong)p.Rva("ServiceVtable") ||
                Q(mgr, 0) != image + (ulong)p.Rva("Rva_0B38B3B8") ||
                Q(Read(read, Q(mgr, 0) + 0x20, 8), 0) != image + (ulong)p.Rva("Rva_0317B260")) {
                result.observation = "NativeContractMismatch";
                result.reason = "Readiness object owner/builder did not match the embedded native profile.";
                return result;
            }
            result.epoch = Q(state, 0x90);
            result.careerActive = svc[0x7B1] != 0;
            result.stockImportActive = svc[0x7B3] != 0;
            result.savedMapBusy = D(svc, 0x7D8) != 0;
            ulong context = Q(svc, 0x470), selected = 0;
            uint activeSlot = 0;
            if (result.careerActive && context != 0) {
                selected = Q(Read(read, context + 0x1D0, 8), 0);
                if (selected != 0) activeSlot = BitConverter.ToUInt16(Read(read, selected + 0xF78, 2), 0);
            }
            result.currentCareerOwned = result.careerActive && Q(svc, 0x468) != 0 &&
                Q(state, 0x10) == service && Q(state, 0x18) == Q(svc, 0x468) &&
                Q(state, 0x50) == Q(svc, 0x468) && Q(state, 0x58) == selected &&
                D(state, 0xD8) == activeSlot && Q(state, 0x30) == manager;

            byte[] metadata = Vector(read, Q(mgr, 0x130), Q(mgr, 0x138), 8, MaxTeams);
            byte[] records = Vector(read, Q(mgr, 0x1C8), Q(mgr, 0x1D0), RecordSize, MaxRecords);
            byte[] queue = Vector(read, Q(mgr, 0x180), Q(mgr, 0x1A0), 8, MaxJobs);
            var expected = new HashSet<uint>();
            var nodes = new Dictionary<ulong,byte[]>();
            for (int n = 0; n < metadata.Length; n += 8) {
                ulong address=Q(metadata,n);
                byte[] node = Read(read, address, 0x18);
                nodes[address]=node;
                if (Q(node, 0) != image + (ulong)p.Rva("Rva_0B494B90")) throw new InvalidDataException("Team metadata owner mismatch.");
                uint slot = BitConverter.ToUInt16(node, 0x10);
                if (!TeamSlot(slot)) throw new InvalidDataException("Team metadata slot outside native replacement range.");
                expected.Add(slot);
            }
            var resident = new HashSet<uint>();
            for (int n = 0; n < records.Length; n += RecordSize) {
                uint slot = D(records, n + 0x58);
                if (TeamSlot(slot)) resident.Add(slot);
            }
            for (int n = 0; n < queue.Length; n += 8) {
                ulong address = Q(queue, n);
                byte[] head = Read(read, address, 0x10);
                var job = new NativeJobReadiness { index = n / 8, state = D(head, 8), Address = address, Vtable = Q(head, 0), kind = "OtherNativeJob" };
                // +0x40 is a team slot only for this validated job class. Do
                // not interpret unrelated queue objects as TeamBuilder teams.
                if (job.Vtable == image + (ulong)p.Rva("Rva_0B38CA08")) {
                    byte[] body = Read(read, address, 0x48);
                    if (Q(body, 0) != job.Vtable) throw new InvalidDataException("Native job changed during read.");
                    job.kind = "TeamPackageLoad";
                    uint slot = D(body, 0x40);
                    if (TeamSlot(slot)) job.slot = slot;
                    job.pluginOwned = Q(body, 0x18) == status;
                    if (job.pluginOwned) {
                        job.callbackEpoch = Q(body, 0x20);
                        job.olderEpoch = job.callbackEpoch < result.epoch;
                    }
                }
                result.jobs.Add(job);
            }
            foreach (uint slot in expected.OrderBy(s => s)) {
                result.teams.Add(new TeamReadiness {
                    slot = slot, activeTeam = slot == activeSlot,
                    runtimeRecord = resident.Contains(slot) ? "Present" : "Missing",
                    queuedJobs = result.jobs.Count(j => j.slot == slot)
                });
            }
            result.expectedTeams = expected.Count;
            result.runtimeRecordsPresent = expected.Count(resident.Contains);
            result.nativeQueueCount = result.jobs.Count;
            result.olderEpochPluginJobs = result.jobs.Count(j => j.olderEpoch);

            // Never publish mixed-save results when a header, epoch, mapping,
            // queue or resident-vector boundary changes during observation.
            byte[] afterSvc = Read(read, service, ServiceSize), afterMgr = Read(read, manager, ManagerSize), afterState=Read(read,status,StatusSize);
            bool stable = Q(Read(read, image + (ulong)p.Rva("ServiceGlobal"), 8), 0) == service &&
                Q(Read(read, image + (ulong)p.Rva("Rva_0E350900"), 8), 0) == manager &&
                Same(state, afterState, new[]{0x10,0x18,0x30,0x50,0x58,0x90}, 8) &&
                Same(state, afterState, new[]{4,8,0xD8}, 4) &&
                Same(svc, afterSvc, new[]{0,0x468,0x470}, 8) &&
                Same(svc, afterSvc, new[]{0x7B1,0x7B3}, 1) && Same(svc, afterSvc, new[]{0x7D8}, 4) &&
                Same(mgr, afterMgr, new[]{0,0x130,0x138,0x180,0x1A0,0x1C8,0x1D0}, 8) &&
                Vector(read, Q(mgr, 0x180), Q(mgr, 0x1A0), 8, MaxJobs).SequenceEqual(queue) &&
                Vector(read, Q(mgr, 0x130), Q(mgr, 0x138), 8, MaxTeams).SequenceEqual(metadata) &&
                Vector(read, Q(mgr, 0x1C8), Q(mgr, 0x1D0), RecordSize, MaxRecords).SequenceEqual(records) &&
                nodes.All(n => Read(read,n.Key,n.Value.Length).SequenceEqual(n.Value));
            if (result.careerActive && context != 0) stable = stable && Q(Read(read, context + 0x1D0, 8), 0) == selected;
            if (selected != 0) stable = stable && BitConverter.ToUInt16(Read(read, selected + 0xF78, 2), 0) == activeSlot;
            if (!stable) return Changed();
            result.observation = "StableSample";
            // A moving native head must reset the diagnostic stall timer even
            // when its address survives. This timer NEVER cancels a job.
            if (result.jobs.Count != 0) {
                NativeJobReadiness head = result.jobs[0];
                result.HeadIdentity = head.Address + ":" + head.Vtable + ":" + head.state + ":" + head.callbackEpoch;
            }
            return result;
        } catch (Exception ex) {
            // No partial team list can be mistaken for a validated snapshot.
            return new TeamReadinessSnapshot { reason = ex.GetType().Name + ": " + ex.Message };
        }
    }

    private static TeamReadinessSnapshot Changed() => new TeamReadinessSnapshot {
        observation = "ChangedDuringRead", reason = "Native context changed; partial team/queue observations discarded."
    };
    private static bool TeamSlot(uint slot) => slot >= 1801 && slot <= 1899;
    private static ulong Q(byte[] data, int at) => BitConverter.ToUInt64(data, at);
    private static uint D(byte[] data, int at) => BitConverter.ToUInt32(data, at);
    private static bool Same(byte[] a, byte[] b, int[] offsets, int size) =>
        offsets.All(at => Enumerable.Range(at, size).All(n => a[n] == b[n]));
    private static byte[] Vector(Func<ulong,int,byte[]> read, ulong begin, ulong end, int stride, int max) {
        if (begin == end) return new byte[0];
        if (end < begin || end - begin > (ulong)(max * stride) || (end - begin) % (ulong)stride != 0)
            throw new InvalidDataException("Readiness vector bounds/alignment mismatch.");
        return Read(read, begin, (int)(end - begin));
    }
    private static byte[] Read(Func<ulong,int,byte[]> read, ulong at, int size) {
        if (at < 0x10000 || at >= 0x7FFFFFFFFFFFUL || size <= 0 || size > 0x4000 || (ulong)size > 0x7FFFFFFFFFFFUL - at)
            throw new InvalidDataException("Readiness read outside bounded user memory.");
        byte[] bytes = read(at, size);
        if (bytes == null || bytes.Length != size) throw new IOException("Readiness memory unavailable.");
        return bytes;
    }
}

internal sealed class NativeQueueProgress
{
    private string head;
    private long since;
    private long lastSample;
    internal const long SuspectedStallMilliseconds = 30000;
    internal void Observe(TeamReadinessSnapshot snapshot, long monotonicMilliseconds) {
        snapshot.queueStallSuspected=false;
        snapshot.unchangedHeadMilliseconds=0;
        // Large observation gaps are not evidence of continuous queue stalling.
        bool continuous = lastSample <= monotonicMilliseconds && monotonicMilliseconds - lastSample <= 5000;
        if (snapshot.observation != "StableSample" || snapshot.HeadIdentity == null) {
            head = null; since = monotonicMilliseconds;
        } else {
            if (!continuous || head != snapshot.HeadIdentity) { head = snapshot.HeadIdentity; since = monotonicMilliseconds; }
            snapshot.unchangedHeadMilliseconds = Math.Max(0, monotonicMilliseconds - since);
            snapshot.queueStallSuspected = snapshot.unchangedHeadMilliseconds >= SuspectedStallMilliseconds;
        }
        lastSample = monotonicMilliseconds;
    }
}
