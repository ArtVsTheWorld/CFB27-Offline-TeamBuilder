Offline TeamBuilder 2.4.14
JDHalfRack / Ace / Timberline Tex

For College Football 27's November 19, 2026 title update. Offline Dynasty
only. Download your TeamBuilder schools before using them in a Dynasty.

Install or update

1. Close the game and MMC. Back up your Dynasty save.
2. Copy Plugins\OfflineTeamBuilderRuntime.dll from this ZIP into MMC's
   Plugins folder. Replace the previous version. Move old split TeamBuilder
   DLLs (OfflineTeamBuilder.dll and OfflineTeamBuilderReentry.dll), the old
   TeamBuilderSharedUi.Launch.dll and any DLL backups out of Plugins.
3. Import and enable OfflineTeamBuilder.fbmod. Disable older TeamBuilder
   and separate Folder Artwork (Auto) mods. Keep the included filename.
4. Import TeamBuilderSharedUi.fbmod. Enable Offline TeamBuilder Shared UI
   (Automatic) if you use AIO Dynasty or another Dynasty UI mod that needs
   TeamBuilder compatibility. Keep that mod enabled and disable its old
   static TeamBuilder UI compatibility patch. See priority below.
5. Restart MMC and launch the game through it. Keep MMC open until the
   TeamBuilder runtime reports Ready. You can leave it minimized. Once Ready,
   a separate helper serves the team assets while the game is running.

Shared UI priority

Put Shared UI at higher priority than OfflineTeamBuilder.fbmod and any mod
that changes the same Dynasty hub or new Dynasty setup menus. It can sit
below unrelated gameplay, equipment, and visual mods.

If a higher-priority mod edits the same UI, the game can still launch with
the companion inactive. To restore its changes, open SharedUi\launch.log
and move Shared UI above any mod listed under PRIORITY CONFLICT.

Shared UI is optional for packs that only use the base TeamBuilder UI.

Custom images

Open Offline-TeamBuilder-Custom-Artwork-Guide.pdf for the illustrated
tutorial. CUSTOM-ARTWORK.txt is a quick reference for image names and sizes.
Your existing artwork folders and settings carry over when you update.

Troubleshooting

Press Win+R, paste %LOCALAPPDATA%\Ace\OfflineTeamBuilder and press Enter.
Check status.txt and runtime.log for startup, SharedUi\launch.log for the
companion, or ArtworkCache\prepare-result.json for custom images.
