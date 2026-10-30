# Maintaining native compatibility

The embedded `Profiles/cfb27-2026-10-30.json` is the native layout boundary.
It groups RVAs, expected entry bytes, lifecycle slots, transport contracts and
relocatable relay templates. `GameProfile.cs` validates it before the engines run.
It is not an editable end-user override or an EXE-hash whitelist.

## Updating for a title update

1. Save a working release and its source. Collect `status.json` and `runtime.log`.
   Determine the failed signature/vtable role rather than assuming every RVA moved.
2. Inspect the relevant native routines in the new executable. Confirm semantics,
   ABI, structure fields and request lifetimes, not just a similar-looking prologue.
   Absolute offsets cannot prove a future layout is safe.
3. Update named symbols/signatures in the profile. If instructions, structure
   layouts or relay control flow changed, update the templates and corresponding
   tests as well. Do not blindly apply one global RVA delta.
4. Preserve ASLR-relative relocations, native callback ownership, epoch rejection,
   original calls/returns, sequential queue behavior, news readiness and audio
   database guards. Do not replace this with a full importer/converter call.
5. Run the complete standalone suite. Review golden differences individually.
   Never regenerate golden references merely to silence a failing assertion.
   Add tests for each intentionally changed native contract and failure path.
6. Build a new version, then do a small in-game smoke test on disposable save
   copies: reload, two-TeamBuilder news card, week advance, coach switch, away
   stadium and custom sound. Local mocks cannot establish game-side correctness.

There is deliberately no exact game-image-size gate or EXE checksum gate.
Minimum/maximum image bounds and concrete native contracts remain mandatory.
Unsupported contracts stop installation with a diagnostic; do not add a forced
compatibility bypass.

The October 19 profile remains as an immutable test baseline. The golden tests
reverse only named address relocations before comparing historical hashes;
instruction bytes and relocation contracts must still match. Native fixtures
use named symbols so the updated addresses are exercised in private memory.

## One assembly, two responsibilities

The import engine arms first; the asset engine chains only after it reports ready.
The import watchdog recognizes the companion chain only when the owner address,
original target and complete emitted relay match this session. It does not accept
arbitrary foreign hooks. Cleanup removes asset hooks before restoring import hooks.
A prior session must finish cleanup before a new game session begins.

The profile contains native relay byte templates because much of the working
behavior runs in the game's update/dispatcher thread, not in managed C#. The
profile is therefore executable code, not just configuration. Review changes with
the same care as C# source. For deeper relay disassembly, retain development
tools outside the distributable; ordinary build/test requires none of them.

## Versioning

Use semantic versioning for the plugin, separate from the profile ID/game date:
major for packaging/configuration breaks, minor for features or intentional
behavior changes, patch for compatible fixes. A new native layout should have a
descriptive profile ID and an explicit changelog entry. Unsupported game versions
must be reported as such, not inferred from a broad game version string.
