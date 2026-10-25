using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OfflineTeamBuilderRuntime;

namespace OfflineTeamBuilderReentry;

internal sealed partial class TeamBuilderReentryRepair
{
    private List<OwnedHook> profileHooks;
    private NativeQueueProgress readinessProgress;
    private string lastReadinessWarning;
    private static readonly System.Diagnostics.Stopwatch readinessClock=System.Diagnostics.Stopwatch.StartNew();
    private sealed class OwnedHook { internal NativeCheck Spec; internal byte[] Patch; internal bool Owned; }

    private void InstallProfileHooks()
    {
        GameProfile profile=GameProfile.Current;
        // Validate every prerequisite BEFORE installing ANY news/audio hook.
        foreach(var h in profile.validations.Concat(profile.hooks))
            if(!BytesEqual(Read(_base+(ulong)profile.Rva(h.target),GameProfile.Hex(h.expected).Length),GameProfile.Hex(h.expected)))
                throw new InvalidOperationException("NATIVE_CONTRACT_MISMATCH "+h.name+" at "+profile.symbols[h.target]+". No news/audio hooks installed.");
        Write(_remoteBase+0x6000,BuildNewsPreviewRelay(_base,_status,_remoteBase+0x6000));
        profileHooks=new List<OwnedHook>();
        foreach(var h in profile.hooks)
        {
            byte[] patch=BuildNewsPreviewEntry(_remoteBase+0x6000+(ulong)h.offset,GameProfile.Hex(h.expected).Length);
            var owned=new OwnedHook { Spec=h,Patch=patch };
            profileHooks.Add(owned);
            // Claim restoration responsibility BEFORE an OS write which could
            // partially succeed. Cleanup still requires an exact patch match.
            owned.Owned=true;
            SetCodePatch(_base+(ulong)profile.Rva(h.target),patch);
            // Retained for the existing private installer-contract harness.
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            GetType().GetField("_news_"+h.name+"_patch",flags).SetValue(this,patch);
            GetType().GetField("_news_"+h.name+"_owned",flags).SetValue(this,true);
        }
        Log("NEWS_AUDIO_READY profile="+profile.id+"; v24 native contracts; no broad reimport or early-start path.");
    }
    private void RestoreProfileHooks()
    {
        if(profileHooks==null) return;
        foreach(var owned in profileHooks.AsEnumerable().Reverse())
        {
            if(!owned.Owned) continue;
            owned.Owned=false;
            try {
                ulong at=_base+(ulong)GameProfile.Current.Rva(owned.Spec.target);
                if(BytesEqual(Read(at,owned.Patch.Length),owned.Patch)) SetCodePatch(at,GameProfile.Hex(owned.Spec.expected));
                else Log("CLEANUP_SKIP changed hook owner: "+owned.Spec.name);
            } catch(Exception ex) { Log("CLEANUP_ERROR "+owned.Spec.name+": "+ex.Message); }
        }
    }
    internal Dictionary<string,object> CaptureHealth()
    {
        lock(this) return CaptureHealthCore();
    }
    private Dictionary<string,object> CaptureHealthCore()
    {
        // This observer never writes private control flags or native game data.
        ulong news=_remoteBase+0x8000, audio=_remoteBase+0xA000;
        var health=new Dictionary<string,object> {
            {"stage",ReadU32(_status+8)}, {"pendingTeamJob",ReadU32(_status+4)},
            {"teamsCompleted",ReadU32(_status+0x48)}, {"teamFailures",ReadU32(_status+0x4C)},
            {"currentSlot",ReadU32(_status+0x20)}, {"epoch",ReadU64(_status+0x90)},
            {"submissions",ReadU32(_status+0x68)}, {"callbacks",ReadU32(_status+0x80)},
            {"staleCallbacks",ReadU32(_status+0x98)}, {"recoveryRetries",ReadU32(_status+0xA4)},
            {"newsPending",ReadU32(news+8)}, {"newsReleased",ReadU32(news+0x34)},
            {"newsTimeouts",ReadU32(news+0x40)}, {"newsOverflow",ReadU32(news+0x3C)},
            {"audioHandoffs",ReadU32(audio+8)}, {"audioWriterInvocations",ReadU32(audio+0xC)},
            {"audioInvalidMaps",ReadU32(audio+0x10)}, {"audioEmptyMaps",ReadU32(audio+0x14)},
            {"audioDatabaseUnavailable",ReadU32(audio+0x1C)},
            {"careerAdmissionDeferrals",ReadU32(_status+NativeWorkAdmission.CareerDeferrals)},
            {"dispatcherAdmissionDeferrals",ReadU32(_status+NativeWorkAdmission.DispatcherDeferrals)},
            {"logoRepairAttempts",ReadU32(_status+LogoRegistryRepair.Attempts)},
            {"logoDeferredPassAttempts",ReadU32(_status+LogoRegistryRepair.DeferredAttempts)},
            {"logoRepairConsecutiveAttempts",ReadU32(_status+LogoRegistryRepair.ConsecutiveAttempts)},
            {"logoRegistrationBatches",ReadU32(_status+0x108)},
            {"logoRegistrationResult",ReadU32(_status+0x110)},
            {"note","Counters are non-atomic observations, not proof of visible uniforms, sound playback, or SQL success."}
        };
        var snapshot=TeamReadinessReader.Capture(Read,_base,_status,GameProfile.Current);
        if(readinessProgress==null)readinessProgress=new NativeQueueProgress();
        readinessProgress.Observe(snapshot,readinessClock.ElapsedMilliseconds);
        health["readiness"]=snapshot;
        string warning=null;
        if(snapshot.observation=="StableSample"&&(snapshot.olderEpochPluginJobs!=0||snapshot.queueStallSuspected)) {
            var head=snapshot.jobs.FirstOrDefault();
            warning="epoch="+snapshot.epoch+" nativeQueue="+snapshot.nativeQueueCount+
                " olderEpochJobs="+snapshot.olderEpochPluginJobs+" suspectedStall="+snapshot.queueStallSuspected+
                " headKind="+head?.kind+" headSlot="+head?.slot+
                "; native ownership retained; no forced completion/cancellation.";
        }
        if(warning!=lastReadinessWarning) {
            if(warning!=null)Log("ASSET_READINESS_WARNING "+warning);
            lastReadinessWarning=warning;
        }
        return health;
    }
    private string CaptureSummary()
    {
        var h=CaptureHealth();
        var readiness=(TeamReadinessSnapshot)h["readiness"];
        return string.Join(" ",h.Where(x=>x.Key!="note"&&x.Key!="readiness"&&
            x.Key!="careerAdmissionDeferrals"&&x.Key!="dispatcherAdmissionDeferrals")
            .Select(x=>x.Key+"="+Convert.ToString(x.Value,CultureInfo.InvariantCulture)))+
            " readinessObservation="+readiness.observation+" expectedTeams="+readiness.expectedTeams+
            " runtimeRecordsPresent="+readiness.runtimeRecordsPresent+" nativeQueue="+readiness.nativeQueueCount+
            " olderEpochJobs="+readiness.olderEpochPluginJobs+" queueStallSuspected="+readiness.queueStallSuspected;
    }
    internal bool OwnsMainChain(ulong currentOwner,ulong expectedMain)
    {
        if(!_installed||currentOwner!=_careerRelay||_originalCareer!=expectedMain)return false;
        byte[] expected=BuildCareerRelay(_status,_originalCareer,_base+(ulong)GameProfile.Current.Rva("CareerManagerVtable"),
            _base+(ulong)GameProfile.Current.Rva("Add"),_base+(ulong)GameProfile.Current.Rva("Needs"),_base+(ulong)GameProfile.Current.Rva("Enter"));
        return BytesEqual(Read(_careerRelay,expected.Length),expected);
    }
}
