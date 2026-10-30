# 2.1.4 — October 30 compatibility update, confirmed working in-game

- Add the October 30 profile with separately mapped native addresses, full
  signature checks, matching lifecycle slots and MCR-backed transport targets.
- Keep the relay instruction templates, job admission, preview gate, logo
  recovery, audio handoff and Dynasty-data protections unchanged.
- Rebase the fbmod game revision and three collector GUIDs only after comparing
  all three vanilla APT data/constant streams and checking superbundle names.
  Preserve the entire manifest and all authored payloads byte for byte.
- Preserve the historical golden hashes. Reverse only reviewed address
  relocations for those comparisons; use named current-profile symbols in the
  executable private-memory fixtures.
- Pass 4,309 runtime checks, 21 packaging checks and 40 live read-only checks,
  including both capacity wrappers and the capacity accessor. The user confirmed
  version 2.1.4 works in-game on October 30, 2026.

# 2.1.3 — user-verified reload-logo fix

- User confirmed the reload-logo candidate works. The release retains the exact
  tested DLL, working fbmod payloads and shared author credit. Includes a matching
  v18 editable fbproject and simplified installation README; no additional native
  loading changes were made during packaging.

- Add logo-only registration for the captured deferred-pass reload state without
  relaxing uniform-job admission. Current Career/context/metadata ownership and
  valid cached DDS images remain mandatory.
- Restrict the queue exception to a pending package read owned by this plugin
  from an older epoch, with a descriptor in the current metadata vector and
  only unstarted jobs behind it. Normal completed-pass queue exclusion remains.
- Preserve native queue/job bytes, loading-pass flags, callback ownership and
  uniform submission counts. No cancellation, forced completion or importer call.
- Add private regressions for selector and selected-school hub contexts, foreign
  or current/future-epoch requests, active tail jobs, changed contexts/mappings,
  metadata retirement, retry bounds, existing-image preservation and recursion.
- Add logoDeferredPassAttempts to diagnostics; it reports guarded attempts, not
  rendered logos or repaired native I/O. Uniform/news/audio code is unchanged.
- 2.1.2 was reported to help first load but fail on reload. The user confirmed
  only logos were visibly affected; incomplete runtime records do not prove a
  visible uniform/stadium failure. The user subsequently confirmed 2.1.3 works.

# 2.1.2 — logo-recovery candidate, partially game-verified; reload failure reported

- Recover empty native logo libraries after a completed asset pass without
  requiring a new coach event or a selected-school object. The team-selector
  capture had nine empty libraries despite valid cached DDS images for all teams.
- Retain current-Career/service/manager ownership, converter/import/map exclusion,
  native queue exclusion and existing typed metadata/DDS/native registrar guards.
- Reuse the frozen dual-image logo validator and nonclearing registration helper;
  no full logo-store/clear call, importer, uniform job or UI native call is added.
- Observe later library clears, fill both image variants/all nine styles and
  preserve already-registered entries. Limit unsuccessful attempts to three,
  spaced by dispatcher ticks, rearming on a new epoch or restored interval.
- Add logo attempt/batch/result counters. Existing readiness fields continue to
  mark texture/consumer readiness Unverified; registration is not rendered UI.
- Preserve 2.1.1 admission checks, main engine, news gating and custom audio.
- This does not yet fix a widget that cached a placeholder, partially missing
  keys in entirely nonempty libraries, or a screen before any successful asset
  pass. Return-to-hub clear/repopulate and image rebind remain separate trace leads.

# 2.1.1 — transition-admission candidate, not yet game-verified

- Recheck Career ownership/vtable, selected-school pointer/slot and absence of
  an active converter at the final admission boundary of each loading relay.
- Recheck the metadata vector's live end, bounded/aligned extent and cursor
  before consuming another slot. A failed check leaves work deferred.
- Reproduce the old submission gap with a private-process mutation injected
  after the relay's earlier checks. Exercise initial and deferred admission,
  preserving the next team and resuming when valid context returns.
- Keep the frozen main/news/audio code, importer suppression, callbacks and
  native queue ownership unchanged. No forced retirement or native cancellation.
- 2.1.0 passed the user's controlled nine-team reload/coach/week session and one
  custom-stadium match. The capture showed repair overlapping match-prep asset
  unloading; it did not reproduce or establish the cause of the old stuck read.
- This narrows a pre-submission race, not all match-preparation repair. Existing
  in-flight reads still belong to the game; a true hub/match scope gate and a
  safe native cancellation contract remain unresolved.

# 2.1.0 — reliability candidate

- Defer the initial asset job and subsequent jobs while the shared native queue
  is busy, including when an old plugin job survives pass/epoch invalidation.
- Defer subsequent asset work during native import/saved-map processing.
- Resume on the native dispatcher without a second CareerHub notification,
  without skipping the first team, and without recursive callback submission.
- Leave native job ownership, payload destruction, original calls/returns and
  sequential callback handling intact. No forced cancellation or success flags.
- Read-only, bounded per-team runtime-record and queued-job observations, with
  epoch/context consistency checks and suspected queue-stall diagnostics.
- Explicitly distinguish runtime records from unverified stadium, texture, logo
  and playback readiness; this is not yet a match-entry streaming gate.
- Fix ordinary `teamFailures=0` status lines being mislabeled as warnings.
- The fbmod, broad-import suppression, main engine, news capture/readiness code
  and audio restoration code are unchanged. No early-start or EXE hash gate.
- Stable packaged releases and the exact installed DLL are preserved separately.

# 2.0.2

- New diagnostics and session files go under `Plugins\OfflineTeamBuilderRuntime`
  beside the installed DLL; no new AppData log folder or fallback is used.
- Support exports and automatic installer backups default to the same data folder.
  Backups use `.backup` filenames so MMC cannot load them as extra plugins.
- Preserve existing asset cache and historical logs without moving/deleting them.
- User release bundles one DLL, the working fbmod with updated display metadata,
  and a plain-text installation guide. Both components use the same author trio.
- Native loading, news, audio, compatibility profile and fbmod payloads unchanged.

# 2.0.1

- Fix managed startup overflow: compact profile transport signatures now use the
  same byte parser as native profile validation. 2.0.0 passed live compatibility
  checks but crashed before installing its transport hooks.
- Add regression coverage that reproduces the exact startup exception in 2.0.0,
  constructs all three real transport hook definitions, and validates their bytes,
  RVAs, patch lengths and kinds.
- Validate transport profile entries during profile loading, before startup.
- Import fatal exceptions now reach the central runtime log even with verbose
  logging off. Detailed import sessions now live under the documented Engine path.
- Do not misreport an exited temporary startup process as an unsupported layout.
- No native relay, game signature, fbmod, metadata or asset-loading changes.

# 2.0.0

Authors: JDHalfRack / Ace / Timberline Tex

- Replaces the legacy pair with one `OfflineTeamBuilderRuntime.dll` and one MMC
  execution action, while retaining the working v24 asset/news/audio native code.
- Explicit import-before-assets startup and reverse cleanup; exact owned-chain
  validation prevents the import watchdog mistaking its own asset hook for a
  foreign hook.
- Full standalone C# build, central embedded native profile, semantic versioning,
  frozen native byte-parity vectors and a private-process regression runner.
- Native signature/vtable compatibility checks before hook installation, with
  actionable failure reports. No game EXE hash or exact image-size requirement.
- Rotating central logs, change-only status snapshots and read-only health counters.
- Single-DLL installer with verified backup/rollback and whitelisted support ZIP.
- Does not change the fbmod, introduce early loading, rewrite Dynasty metadata,
  or claim fixes for remaining minor menu refresh/header issues.

Validation: 243 standalone checks, 17 installer/support packaging checks, plus
13 development regressions (multi-team
coach/queue/logo behavior, native audio guards and relay ABI/disassembly). Native
relay generation is byte-exact with v24 at multiple relocation bases. A first
in-game smoke test of the unified DLL remains necessary.
