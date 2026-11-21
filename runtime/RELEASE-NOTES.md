# Offline TeamBuilder 2.4.14

## Shared UI works below unrelated mods

- Shared UI now checks whether higher-priority mods replace the same Dynasty hub or new Dynasty setup UI. Gameplay or visual mods that leave those menus alone can sit above it.
- If a higher-priority mod does replace that UI, the runtime clears the old generated companion and lets the game launch with the companion inactive. `SharedUi/launch.log` names the conflicting mods. Move Shared UI above them to apply its changes on the next launch.
- The player ZIP includes both separate `.fbmod` files, updated install instructions, and the illustrated custom artwork PDF.

## Install or update

1. Close the game and MMC and replace `OfflineTeamBuilderRuntime.dll` in MMC's `Plugins` folder. If you're updating from 2.4.13, you can keep its two installed `.fbmod` files.
2. Enable `OfflineTeamBuilder.fbmod`. If you need shared UI compatibility, enable **Offline TeamBuilder Shared UI (Automatic)** above the base TeamBuilder mod and any AIO or Dynasty customizer mod that changes the same menus. Disable the old static TeamBuilder UI patch for that mod.
3. Launch through MMC and keep it open until the runtime reports **Ready**.

The lower-priority path was verified in a live fourth-priority launch. All 52 launcher checks and 6,323 runtime checks passed. The nine standard test packs produced the same companion files as 2.4.13.

[Source comparison with 2.4.13](https://github.com/ArtVsTheWorld/CFB27-Offline-TeamBuilder/compare/v2.4.13...v2.4.14) · [Full install instructions](https://github.com/ArtVsTheWorld/CFB27-Offline-TeamBuilder/blob/v2.4.14/runtime/USER-README.txt) · [Illustrated artwork guide](https://github.com/ArtVsTheWorld/CFB27-Offline-TeamBuilder/blob/v2.4.14/dist/v2.4.14/Offline-TeamBuilder-Custom-Artwork-Guide.pdf)
