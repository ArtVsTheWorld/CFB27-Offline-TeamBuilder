# Troubleshooting

Check `MMC folder\Plugins\OfflineTeamBuilderRuntime\status.json` first, then
`runtime.log`. The central log rotates at about 2 MiB with two retained copies.
Status is refreshed only when its data changes; an old timestamp with unchanged
counters is not automatically a stalled plugin. Counters are non-atomic snapshots,
not proof that a visible uniform rendered or an audio clip played.

| State/message | Meaning/action |
| --- | --- |
| `WaitingForGame` | MMC has armed the coordinator; waiting for CollegeFB27. |
| `BlockedDuplicatePlugins` | Remove both old plugin DLLs from Plugins and restart MMC. Backups must be outside Plugins. |
| `WaitingForNativeCompatibility` | Read-only preflight is waiting for initialized native contracts, for up to 90 seconds. |
| `BlockedUnsupportedLayout` / `SIGNATURE_MISMATCH` / `VTABLE_MISMATCH` | Required native layout changed or another hook owns that target. No unified runtime hooks were installed during this preflight. Send the diagnostics to a maintainer. |
| `ArmingImportRuntime` | Import service is installing its validated hooks. |
| `WaitingForAssetRuntime` | Main engine is ready but the asset engine is not yet installed. See the log for the signature/access failure. |
| `Ready` | Both engines installed; health counters describe ongoing asset/news/audio work. It is not a guarantee of every UI image or sound. |
| `BlockedAssetRuntime` | Asset installation failed repeatedly; the coordinator restores its owned hooks. |
| `BlockedOutputDirectory` | MMC cannot write to `Plugins\OfflineTeamBuilderRuntime`. Allow write access to this folder. There is no silent AppData fallback. |
| `RuntimeStopped` / `CoordinatorFailed` | Inspect the log and restart MMC/game after addressing the failure. |
| `GameExited` | Game process ended; session cleanup is complete/in progress. |

## Make a support ZIP

```powershell
.\collect-diagnostics.ps1 -MmcRoot 'C:\path\to\MMC'
```

This copies only `runtime.log`, its two rotations, `status.json`, and a small
plugin-version report. No saves, asset cache, raw memory, game EXE, configuration
secrets or detailed service logs are included. Nothing is uploaded. Review before
sharing: errors can still include Windows usernames, local paths and addresses.
Missing files are allowed, including before the first launch.
The ZIP and review copies are saved under `Plugins\OfflineTeamBuilderRuntime\Support`
unless an explicit output directory is supplied. Old AppData diagnostics are not
moved or deleted. The existing asset cache remains at its previous path.

For verbose central logging, copy `OfflineTeamBuilderRuntime.settings.example.json`
to `Plugins\OfflineTeamBuilderRuntime.settings.json`, set `verboseLogging` to true,
then restart MMC. Turn it off after diagnosis. Never share your repair config,
service request bodies or authorization data. Existing detailed import-session
logs under the central folder's `Engine` subdirectory are deliberately not bundled.

## Roll back

Close the game and MMC. Use the backup path printed by the installer:

```powershell
.\install.ps1 -MmcRoot 'C:\path\to\MMC' -RestoreFrom 'C:\path\to\backup'
```

Rollback preserves the current DLL set in another backup before restoring the
selected set. It verifies plugin file integrity only, never the game executable.
New automatic backups live under `Plugins\OfflineTeamBuilderRuntime\Backups`
with `.backup` extensions, so MMC cannot load them as duplicate plugins. The
installer can also restore the older backup format from outside Plugins.
For manual installs, move the unified DLL out of Plugins and restore your old
pair. Never run old and unified plugins together.

Keep the existing compatible fbmod. A plugin compatibility pass does not validate
fbmod compatibility. Known sporadic header-logo/tile-refresh quirks are unchanged.

## 2.1.x candidates: readiness and custom-stadium failures

2.1.2 adds `logoRepairAttempts`, `logoRepairConsecutiveAttempts`,
`logoRegistrationBatches` and `logoRegistrationResult`. These describe attempted
registration, not proof of visible images. Result 1 means the helper returned;
3 means its native/blob guard declined. Three unsuccessful paced attempts stop
until a new epoch or an observed populated interval rearms recovery. No timeout
declares a logo ready. If registration succeeds but the menu still shows EA,
reopen that menu once to distinguish a stale widget from an empty registry.
No selected school is required for the completed-pass logo-only path, but a
current owned Career, successful pass and quiescent native queue still are.

2.1.3 has a separate deferred-pass exception for a proven owned old-epoch package
read with current metadata and unstarted tail jobs. `logoDeferredPassAttempts`
counts those guarded attempts. It does not mean the pending read was repaired,
retired or canceled. A current-epoch/foreign read or an active tail job still
blocks this exception. Do not interpret incomplete runtime-record counts as
proof that visible uniforms or stadiums stopped working; check the actual result.

`Ready` means the runtime hooks are installed; it is not a match-readiness signal.
In `status.json`, `details.readiness` explains the narrower observed milestones:

- `StableSample`: relevant context/vector boundaries matched before and after
  a bounded read. This is not an atomic engine snapshot or a streaming guarantee.
- `ChangedDuringRead`/`Unavailable`: partial team results are discarded, not
  carried forward from another save as if they were current.
- `currentCareerOwned`: plugin identity matches the live Career/selected team.
  False means do not treat the manager's record list as current-save readiness.
- `runtimeRecord: Present`: a slot has a native runtime record. Stadium world,
  field textures, uniform materials, UI logos and actual playback are separate,
  unverified consumers. A custom stadium can still fail after this milestone.
- `olderEpochPluginJobs`: jobs remain in the native queue with this plugin's
  capture but an older pass epoch. Retiring a pass did not retire these jobs.
- `queueStallSuspected`: the same native head identity/state was observed for
  at least 30 seconds with no long monitoring gap. This is diagnostic evidence,
  not proof of deadlock. No timeout deletes a job or fakes completion.
- `careerAdmissionDeferrals` and `dispatcherAdmissionDeferrals` are native tick
  counts, not errors or seconds. They can increase quickly during normal loading.

The admission safeguard prevents new plugin work being appended behind an
occupied native queue. It does not yet cancel an abandoned package read, replace
a broken stadium, or prove that earlier community crashes are fixed.

2.1.1 also declines work if Career/school ownership, converter state or metadata
vector bounds changed at admission. The same deferral counters cover these
checks; they do not identify which condition failed. A native request submitted
before a transition still has its normal native lifetime. Match prep can clear
non-match records while Career remains active, so automatic repair can still
begin before the selected-school context disappears. A complete hub/match scope
gate has not been established. Do not treat this patch as native cancellation or
an already-stalled read repair.

For a stadium hang/crash, keep the runtime support ZIP, exact venue/team and
enabled mod list. A privately shared copy of the affected TeamBuilder package
and pre-match save allows local reproduction; logs alone cannot establish the
stadium's full dependency graph. Never post saves/dumps publicly without consent.
