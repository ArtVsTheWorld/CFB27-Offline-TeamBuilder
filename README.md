# Offline TeamBuilder for College Football 27

Use downloaded TeamBuilder teams in offline Dynasty with custom uniforms,
stadiums, logos and stadium sounds, without resetting Dynasty progress.

## Download

[Download 2.1.7](dist/v2.1.7/OfflineTeamBuilder-2.1.7.zip) · [Matching source](dist/v2.1.7/OfflineTeamBuilder-2.1.7-source.zip)

2.1.7 moves plugin-generated data out of MMC/Plugins, improves startup/failure
reports and retains the confirmed Windows-export startup fix. One DLL to install;
the checked native helper is embedded and deployed automatically to user data.

## Install

1. Back up the save and close the game and MMC.
2. Copy `Plugins/OfflineTeamBuilderRuntime.dll` from the ZIP into MMC/Plugins.
   Replace the old version and move legacy split TeamBuilder DLLs/backups out.
3. Import/enable the included `OfflineTeamBuilder.fbmod`; keep one version.
   Retain required AIO and matching compatibility mods for AIO dynasties.
4. Restart MMC, launch normally and keep MMC running. No F9 step is required.

The editable fbproject is for developers. Teams must already be downloaded.
Targets the October 30, 2026 native layout; future game updates may need a port.
Offline only. Native fingerprints and hook ownership checks remain required.

## Logs and support

Open `%LOCALAPPDATA%\Ace\OfflineTeamBuilder` (paste into Win+R).
Start with `status.txt`, then `runtime.log`. Reports include the blocking stage,
next step, Windows errors, session IDs, environment/plugin inventory and selected
pack/built-mod context. `Ready` means runtime hooks are active, not that textures
are proven rendered. The last failure remains in the current session's status.

Run `Collect-Support.ps1` from the ZIP for a report under that folder's `Support`
directory. Review personal paths before sharing. Settings, saves, caches, DLLs
and memory dumps are excluded; no automatic upload occurs.

Legacy settings are copied without deleting originals; existing user settings
win and conflicts/old logs are archived under `Migration`. All MMC copies for
one account share user data. The old Local AppData asset store is retained.
The plugin does not write runtime files into the installation; MMC itself still
needs normal access for its own files. See [installation/support details](runtime/USER-README.txt).

## Known limitation and validation

Broadcast Universe 0.7-beta1 plus AIO 1.4.1 and the affected dynasty can still hang
during matchup loading/reloading, including on 2.1.5. That interaction is not
claimed fixed; disable Broadcast for a focused baseline.

The previous startup fix was tester-confirmed. 2.1.7 has private native/runtime,
actual write-denied filesystem, migration, diagnostic and package tests; this is
not a new live-game certification. See [validation](runtime/VALIDATION.txt).

## Development

[Build from source](BUILDING.md) · [Native updates](UPDATING.md) · [Changelog](CHANGELOG.md) · [Editable assets](assets/)

JDHalfRack / Ace / Timberline Tex. Unofficial; not affiliated with EA.
See [credits/notices](CREDITS.md). Earlier distributions remain under their tags.