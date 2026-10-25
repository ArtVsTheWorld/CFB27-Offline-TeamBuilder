# Updating after an EA patch

1. Keep the previous release and its tag. Work on a new branch.
2. Check `runtime.log` and `status.json`. Identify the failed native signature,
   vtable role or fbmod loading step before changing addresses.
3. Update the matching symbols, signatures and relay code in
   `runtime/Profiles/cfb27-2026-10-19.json` and the relevant source files.
   Confirm the function's arguments and object layout, not just its address.
   Do not apply one offset change to every function.
4. Build with your local MMC references and run the tests. Keep the golden
   relay checks; do not regenerate them just to hide a failure.
5. Test a copied save: initial load, reload, week advance, coach switch, logos,
   a two-TeamBuilder news card, a custom stadium and custom audio.
6. Update the plugin version and changelog. Export the fbmod and fbproject if
   their game compatibility or UI assets changed. Update `release-files.json`
   only with files selected for that release, then run the repository checks.
7. Commit the source, tag the version, and publish the tested ZIP. Keep older
   versions available for rollback.

## Where to look

- `runtime/src/GameProfile.cs` and `runtime/Profiles`: native compatibility.
- `runtime/src/Engine`: import service and team-asset loading.
- `runtime/src/NativeWorkAdmission.cs`: native job admission.
- `runtime/src/LogoRegistryRepair.cs`: logo restoration.
- `runtime/src/AssetRuntimeSupport.cs`: news/audio hooks and diagnostics.
- `runtime/tests`: regression fixtures and reference relay hashes.
- `assets`: working fbmod and editable MMC project.

Keep Dynasty-owned records, rosters, progression, recruiting and NIL outside
the asset-restoration path. Do not restore the broad TeamBuilder importer as
a shortcut. Keep signature and ownership checks; there is no EXE hash gate.

The profile contains executable relay templates, not just offsets. New game
layouts can require emitter and test changes as well as a profile update.
See [the native update notes](runtime/UPDATING.md) for more detail.

## Remaining limits

A native package read can outlive a menu/save transition. The plugin does not
forcibly cancel it or declare it complete. Runtime-record presence does not
prove that a stadium or texture is fully streamed. A news tile may occasionally
need to be selected again. Avoid publishing a fix based on local mocks alone.
