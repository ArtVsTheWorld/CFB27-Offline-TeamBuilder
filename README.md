# Offline TeamBuilder for College Football 27

Use downloaded TeamBuilder schools in offline Dynasty with their uniforms,
stadiums, logos and stadium sounds. Add your own menu, player-card, headshot and
pregame artwork per school.

## Download

[Offline TeamBuilder 2.4.14](https://github.com/ArtVsTheWorld/CFB27-Offline-TeamBuilder/releases/tag/v2.4.14)
targets the November 19, 2026 game update. The player ZIP includes one runtime
DLL and two separate MMC mods.

1. Close the game and MMC, then replace the runtime DLL in MMC's Plugins
   folder. Move older TeamBuilder DLLs and the standalone Shared UI launcher
   out of Plugins.
2. Import and enable the included `OfflineTeamBuilder.fbmod`.
3. Import `TeamBuilderSharedUi.fbmod` if you use AIO Dynasty or another Dynasty
   mod that needs Shared UI compatibility. Put **Offline TeamBuilder Shared UI
   (Automatic)** above the base TeamBuilder mod and any mod that edits the same
   Dynasty hub or new Dynasty menus. Unrelated mods can sit above it. Disable
   the old static TeamBuilder UI patch for the same mod.
4. Launch through MMC and keep it open until the runtime reports **Ready**.

[Full install instructions](runtime/USER-README.txt) ·
[Illustrated artwork guide](dist/v2.4.14/Offline-TeamBuilder-Custom-Artwork-Guide.pdf) ·
[2.4.14 changes](CHANGELOG.md) ·
[2.1.7 and its historical compatibility patches](https://github.com/ArtVsTheWorld/CFB27-Offline-TeamBuilder/releases/tag/v2.1.7)

## Support and development

Open `%LOCALAPPDATA%\Ace\OfflineTeamBuilder` for `status.txt` and
`runtime.log`. `SharedUi\launch.log` records the companion result, and
`ArtworkCache\prepare-result.json` lists the artwork that was prepared.

[Build from source](BUILDING.md) ·
[Collect local support logs](runtime/collect-diagnostics.ps1) ·
[Credits and notices](CREDITS.md)

JDHalfRack / Ace / Timberline Tex. Unofficial project.
