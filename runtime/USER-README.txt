Offline TeamBuilder 2.4.13
JDHalfRack / Ace / Timberline Tex

For College Football 27's November 19, 2026 title update. Offline Dynasty
only. Download your TeamBuilder schools before using them in a Dynasty.

Install or update

1. Close the game and MMC. Back up your Dynasty save.
2. Copy Plugins\OfflineTeamBuilderRuntime.dll from this ZIP into MMC's
   Plugins folder. Replace the previous version. Move old split TeamBuilder
   DLLs (OfflineTeamBuilder.dll and OfflineTeamBuilderReentry.dll), the old
   TeamBuilderSharedUi.Launch.dll and any DLL backups out of Plugins.
3. Import and enable the included OfflineTeamBuilder.fbmod. Disable older
   TeamBuilder and separate Folder Artwork (Auto) fbmods. Keep the filename
   OfflineTeamBuilder.fbmod so the runtime can find it.
4. Import the included TeamBuilderSharedUi.fbmod. If you use AIO Dynasty or a
   Dynasty customizer, enable its entry, Offline TeamBuilder Shared UI
   (Automatic), at highest priority. Disable your old static TeamBuilder UI
   patch for that same mod. Keep AIO or the customizer itself enabled.
   The companion is optional if your pack needs no shared UI compatibility.
5. Restart MMC and launch the game through it. Keep MMC open until the
   TeamBuilder runtime reports Ready. You can leave it minimized. Once Ready,
   a separate helper serves the team assets while the game is running.

Custom images

See CUSTOM-ARTWORK.txt. Use one folder per school. Your images and settings
carry over when you replace the runtime and import the two fbmods.

Troubleshooting

Press Win+R, paste %LOCALAPPDATA%\Ace\OfflineTeamBuilder and press Enter.
Check status.txt and runtime.log for startup, SharedUi\launch.log for the
companion, or ArtworkCache\prepare-result.json for custom images.
If a launch closes during startup, restart MMC and retry once. Keep the logs
if it happens again.
