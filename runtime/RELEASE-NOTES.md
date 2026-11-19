# Offline TeamBuilder 2.4.13

## What's new since 2.1.7

- Updated for the **November 19, 2026 College Football 27 title update**.
- **Add custom artwork per TeamBuilder school.** Drop PNGs into a folder named after your school to set its Dynasty hub background, full player-card background, or headshot jersey. The headshot jersey and menu background also work in the in-game depth chart. Pregame team select supports separate left and right artwork. Each image is optional. Multiple schools can use their own artwork in the same save, including schools renamed after import.
- **Automatic shared UI compatibility.** The included companion uses your enabled MMC mods at launch. It keeps the winning Dynasty menus and applies TeamBuilder's hub logo refresh and offline school picker changes. The main mod and companion are separate files, and the launcher is built into the runtime DLL.
- **More reliable team loading.** The runtime catches missed startup work and can request a missing team's uniforms when you open a full player card. The experimental Dynasty loading-screen gate remains disabled.
- **Midgame Save compatibility.** Added support for the newer CareerHub handoff while retaining support for the verified 0.2.2 and 0.2.4 DLLs.
- **MMC can close after TeamBuilder is Ready.** A separate helper now keeps the local asset server and recovery running with the game. Leave MMC open during launch and setup.

## Install

1. Close the game and MMC. Copy `Plugins/OfflineTeamBuilderRuntime.dll` from the player ZIP into MMC's `Plugins` folder. Move any old TeamBuilder DLLs and `TeamBuilderSharedUi.Launch.dll` out of that folder.
2. Import and enable `OfflineTeamBuilder.fbmod`. Disable older TeamBuilder and separate Folder Artwork mods.
3. Import `TeamBuilderSharedUi.fbmod`. If you use AIO Dynasty or a Dynasty customizer, enable **Offline TeamBuilder Shared UI (Automatic)** at highest priority. Disable the old static TeamBuilder UI patch for the same mod and keep the original AIO/customizer mod enabled. The companion is optional if your pack needs no shared UI compatibility.
4. Restart MMC, launch through it and keep it open until the TeamBuilder runtime reports **Ready**.

For custom artwork, see `CUSTOM-ARTWORK.txt` in the ZIP. Put your PNGs under `%LOCALAPPDATA%\Ace\OfflineTeamBuilder\Artwork\<school name>`, then restart the game and MMC.

## Compatibility checked

AIO Dynasty 1.5.0 and the combined Dynasty Customizer 1.1.1 worked in game with this release candidate. Standalone ZEO, Conference Customizer and Playoff Customizer passed offline MMC checks. One AIO launch exited during startup. The retry reached Ready and showed the AIO menus, TeamBuilder logo and offline Dynasty picker. Two customized TeamBuilder teams facing each other with pregame artwork remain unverified in game.

[Source comparison from 2.1.7 to 2.4.13](https://github.com/ArtVsTheWorld/CFB27-Offline-TeamBuilder/compare/v2.1.7...v2.4.13) · [Full install guide](https://github.com/ArtVsTheWorld/CFB27-Offline-TeamBuilder/blob/v2.4.13/runtime/USER-README.txt) · [Custom artwork guide](https://github.com/ArtVsTheWorld/CFB27-Offline-TeamBuilder/blob/v2.4.13/runtime/CUSTOM-ARTWORK.txt)
