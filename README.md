# Offline TeamBuilder for College Football 27

Use downloaded TeamBuilder teams in offline Dynasty, including uniforms,
stadiums, logos and custom stadium sounds, without resetting Dynasty progress.

## Download

[Download version 2.1.5](dist/v2.1.5/OfflineTeamBuilder-2.1.5.zip).
The ZIP includes the plugin, fbmod, editable fbproject and installation guide.
Fixes TeamBuilder assets not loading when you control a built-in school.
Confirmed working in-game on November 2, 2026. Still targets the October 30
game update. Existing 2.1.4 users only need to replace the DLL.

## Install

1. Back up your saves. Close the game and MMC Mod Manager.
2. Copy the DLL from the ZIP's `Plugins` folder into MMC's `Plugins` folder.
   Replace the older version. Move any `OfflineTeamBuilder.dll` and
   `OfflineTeamBuilderReentry.dll` out of that folder.
3. Import and enable `OfflineTeamBuilder.fbmod` in MMC. Disable older copies
   of the Offline TeamBuilder fbmod.
4. Launch through MMC and keep MMC open or minimized. Load your offline Dynasty or create
   one and add your downloaded teams. Allow a moment for the assets to load.

The fbproject is for editing in MMC Editor, not for installation. No Play Now
or practice-mode trick is needed. Offline only.

Why leave MMC open? It acts as a helper that hands the game your TeamBuilder
files when needed and helps keep the plugin working. Closing MMC removes that
support, even if uniforms already loaded still look fine.

## Source and updates

- [Build from source](BUILDING.md)
- [Updating after an EA patch](UPDATING.md)
- [Editable fbmod and fbproject](assets/)
- [Known issues and logs](runtime/TROUBLESHOOTING.md)

Targets the October 30, 2026 game update. Later game updates may require changes.
There is no game EXE hash requirement. The previous game-tested version remains
available under the v2.1.3 tag for older game builds.

If something stops loading, restart the game. For bug reports, include the game
update, plugin version, what happened, and the relevant log message. Logs are in
`MMC/Plugins/OfflineTeamBuilderRuntime`. Remove personal paths before posting.
Do not upload game executables, saves, memory dumps or private configuration.

## Credits

JDHalfRack / Ace / Timberline Tex.

Unofficial mod. Not affiliated with EA. See [credits and notices](CREDITS.md).
