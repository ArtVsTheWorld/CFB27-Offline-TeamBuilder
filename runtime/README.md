# Offline TeamBuilder Runtime 2.1.4

Authors: **JDHalfRack / Ace / Timberline Tex**

2.1.4 updates the native profile and fbmod compatibility for the October 30 game
update. Native contracts and local tests pass, and the user confirmed it works
in-game on October 30, 2026. The working
relay instruction templates and fbmod payloads are preserved. Shared-native-queue
safeguards and per-team observations remain narrower
than full streaming readiness; this does not claim to repair all stalled reads.

2.1.1 rechecks the current Career, selected school/slot, converter and metadata
vector bounds at the last admission boundary, before consuming another team
slot. A change there defers the submission rather than starting work for stale
context. This narrows a submission race; it does not cancel native requests or
establish a complete hub-versus-match loading gate.

2.1.2 adds a logo-only recovery check on the existing native dispatcher. After a
successful, quiescent asset pass, an empty logo library can be repopulated from
the current Career's cached DDS images even when the team selector has no selected
school and no new coach event. It fills missing entries without clearing existing
images. Failed attempts are paced and limited to three per epoch/restored interval.
It does not rebuild UI widgets or guarantee their cached placeholders refresh.

2.1.3 adds a separate, restricted deferred-pass case. In the captured reload,
the cached images and Career/metadata ownership were valid, but an older native
read prevented the loading pass from finishing. Logo registration no longer
depends on that pass finishing in this specific case: the native head must be
this plugin's old-epoch read, its descriptor must remain in the live metadata
vector, and all queued tail jobs must still be unstarted. Active converters,
import/map work and changed ownership still exclude registration. No request or
queue is canceled, advanced, retried or modified by this logo path.

New team-loading work waits until the shared native queue is empty and the stock importer/map
processor is inactive. An old callback epoch does not mean the native job has
gone away. No native job is forcibly canceled, destroyed, or declared complete.
If an earlier package read never returns, the plugin reports that evidence but
does not pretend this safeguard has repaired the underlying read.

One MMC plugin for offline TeamBuilder import and Dynasty asset restoration.
`OfflineTeamBuilderRuntime.dll` replaces both `OfflineTeamBuilder.dll` and
`OfflineTeamBuilderReentry.dll`. It is one compiled assembly, not a DLL wrapper
that extracts the old pair.

## Install

The user release includes the matching `OfflineTeamBuilder.fbmod`, a Plugins
folder with the single DLL, the matching editable `OfflineTeamBuilder.fbproject`,
and a plain-text installation guide. The project is for MMC Editor 1.1.0.6 and
is not required for playing. It is reconstructed from the active working mod's
three legacy movies and all collector links, not copied from the old project.
The fbmod's
game payloads are unchanged from the installed working v16 file; only its display
details and filename are modernized. Follow `README.txt` in that ZIP.

The developer/runtime-only package also supports the following DLL installer.
Close **both the game and MMC** first, then run:

```powershell
.\install.ps1 -MmcRoot 'C:\path\to\MMC'
```

The script verifies this plugin package, preserves your installed DLL set under
`Plugins\OfflineTeamBuilderRuntime\Backups` using non-loadable `.backup` files,
installs the single new DLL, and removes the two legacy DLLs from Plugins.
It prints the backup folder and exact rollback command. These DLL removals are
recoverable from that backup. No saves, fbmods, asset cache, configuration files,
or unrelated plugins are changed. Do not keep backup DLLs inside Plugins.

For manual installation, move both legacy DLLs out of Plugins and copy only
`OfflineTeamBuilderRuntime.dll` into Plugins. Restart MMC so the old assemblies
are no longer loaded. Leaving either legacy plugin installed blocks the unified
runtime to avoid double hooking.

Preview the install without changing anything:

```powershell
.\install.ps1 -MmcRoot 'C:\path\to\MMC' -ValidateOnly
```

## What is preserved

- Multi-team uniforms, stadiums, logos and repeated Dynasty loading/week advances.
- News-preview uniform readiness gating for both teams.
- The working v24 custom stadium audio handoff.
- No broad TeamBuilder reimport during Dynasty reentry: Dynasty-owned records,
  rosters, progression, recruiting, NIL and team metadata remain outside this path.
- Normal hub loading. The experimental v25 early-start path is not included.
- Existing asset cache and optional `OfflineTeamBuilder.repair.json` remain usable.

This consolidation does not claim to fix the remaining occasional tile-switch
refresh or active-header-logo issues. It also does not eliminate native asset
streaming delays.

## Readiness scope

`status.json` now includes `details.readiness`: a distinct list of team slots,
runtime-record presence, queued work, current-career ownership, and queued jobs
from older callback epochs. Uniform textures, stadium world/field dependencies,
logo textures and audio playback remain explicitly `Unverified`. `matchEntryReady`
is null until native consumer readiness is actually established. Do not use
record presence, callback totals, or the coordinator's `Ready` state as proof
that a custom stadium is fully streamed or safe to enter.

An unchanged queue head observed continuously for 30 seconds produces a
suspected-stall warning, not an automatic retry, queue reset or completion.
Changed/unreadable snapshots discard partial per-team results. See
`TROUBLESHOOTING.md` for interpreting these observations.

## Compatibility and diagnostics

There is **no game EXE checksum requirement**, and no exact image-size allow-list.
Compatibility is established using the specific native instruction signatures and
vtable roles the plugin needs, plus memory bounds checks. Different EXE hashes
are allowed when those contracts still match. A title update that changes a
required contract is blocked with a readable error rather than guessed offsets.
This does not make the plugin automatically compatible with all future updates.

The installer hashes only the supplied plugin DLL for package/backup integrity;
it never hashes the game executable.

Human-readable log and health snapshot:

```text
MMC folder\Plugins\OfflineTeamBuilderRuntime\runtime.log
MMC folder\Plugins\OfflineTeamBuilderRuntime\status.json
```

See `TROUBLESHOOTING.md`. The source package includes `BUILD.md`, `UPDATING.md`,
the native compatibility profile and a self-contained private-process test suite.

## Release validation

Local automated tests check metadata/registration, native byte parity with v24,
multi-team lifecycle and stale callbacks, fail-closed installation, hook ownership
and cleanup, diagnostics, and asset-cache integrity. They do not open the game.
The user confirmed 2.0.1 works in-game. Version 2.0.2 changed output paths and
packaging only. In a controlled 2.1.0 session, initial nine-team loading, reloads,
coach switches, week advances and one custom-stadium match completed. An exit
after switching coaches happened after that refresh finished, so this did not
reproduce an in-flight exit or the earlier stalled package read. Match preparation
did expose repair submissions overlapping native context teardown.

2.1.1 has private-process regression coverage for context changes at admission.
The 2.1.2 team-selector capture found nine empty native logo libraries with valid
cached DDS images and no selected school. A private-process regression reproduces
that gap, checks both variants/all teams, later library clears, preservation of
existing entries, recursion exclusion, native busy/import guards and bounded
retries. The user confirmed 2.1.2 first-load logo improvement but reported reload
failure. The captured reload had empty logo libraries and an unfinished pass;
uniforms and stadiums still appeared functional according to the user. Runtime
record counts alone cannot contradict that visual result. 2.1.3 reproduces this
deferred case in private tests; the user installed it and confirmed it works.
Local checks also preserve queue/job state and the working uniform/news/audio
code. Native read cancellation and complete stadium streaming readiness remain
unresolved. No live install is performed while packaging. Close game and MMC
before replacing the DLL.
