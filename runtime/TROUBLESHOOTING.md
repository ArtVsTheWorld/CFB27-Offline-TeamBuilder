# Troubleshooting2.1.7

OFFLINE TEAMBUILDER 2.1.7
JDHalfRack / Ace / Timberline Tex

Use downloaded TeamBuilder teams in offline Dynasty, including custom uniforms,
stadiums, logos and stadium sounds. Teams must already be downloaded.

INSTALL

1. Back up your save. Close both the game and MMC Mod Manager.
2. Copy Plugins/OfflineTeamBuilderRuntime.dll into MMC/Plugins, replacing the old
   version. This is the only DLL to install. Move legacy OfflineTeamBuilder.dll,
   OfflineTeamBuilderReentry.dll and DLL backups outside Plugins.
3. Import and enable this ZIP's OfflineTeamBuilder.fbmod. Enable one version.
   Keep required AIO and matching compatibility mods for an AIO dynasty.
4. Restart MMC and launch normally. Keep MMC running. No F9 step is needed.

The fbproject is for developers, not installation. Existing settings and
TeamBuilder caches should be preserved.

USER DATA AND LOGS

Press Win+R and open:
%LOCALAPPDATA%\Ace\OfflineTeamBuilder

The plugin writes settings, runtime.log, status.json, readable status.txt,
environment/game context reports, engine output and its verified native helper
there. Runtime output is not written into MMC/Plugins or MMC/Output.
This supports plugin installations under Program Files or other read-only
folders. MMC itself still needs appropriate access for its own files/builds.

The native helper is embedded in the installation DLL and deployed automatically
under Native/<SHA-256>/OfflineTeamBuilderNative.dll. Its existing verified bytes
and Windows loading path are retained. There is no extra loader to install.

On first use from an MMC location, legacy plugin settings/repair configuration
are copied to user data if absent. Existing user-data settings win; conflicts
and old logs are archived under Migration. Originals are never deleted.
All MMC copies under the same Windows account share the new settings/runtime
folder. The existing default asset store at
%LOCALAPPDATA%\OfflineTeamBuilder\local-assets-v111 is retained without copying
or deleting cached teams. An explicitly configured cache/copy destination must
be outside the MMC installation; relative cache paths resolve under user data.

DIAGNOSING AN ISSUE

Open status.txt first. It identifies the current stage, reason and next step.
Ready means runtime hooks are active; it does not prove textures are rendered.
The report retains the last failure after the game exits. runtime.log includes
session IDs, deployment/load stages, Windows errors and native contract results.
For detailed logs, set verboseLogging to true in the settings file in user data.

Run Collect-Support.ps1 from this ZIP to create a report under the user-data
Support folder. From PowerShell:
powershell -NoProfile -ExecutionPolicy Bypass -File .\Collect-Support.ps1

Share that ZIP with the matchup, exact symptom, custom versus placeholder
uniforms and whether this was a fresh launch or reload. Review personal paths
before sharing. It includes current diagnostics and bounded native-log tails,
not settings, saves, cached assets, binaries or memory dumps. Nothing is uploaded.

KNOWN LIMITATION

Broadcast Universe 0.7-beta1 + AIO 1.4.1 + the affected TeamBuilder dynasty can
still hang during matchup loading/reloading. This was also reproduced on 2.1.5.
That interaction is unresolved; disable Broadcast for a focused baseline test.

This release retains the tester-confirmed Windows export startup fix, native
capacity/registry guards, asset reuse and exact Midgame Save 0.2.2/0.2.4 sharing.
The guards are not a guarantee against every loading failure. Unsupported game
layouts and unknown shared-hook owners remain blocked. No antivirus-detection
claim is made.

Windows x64, MMC and College Football 27's October 30 native layout. Offline
only. Unofficial. Future game updates may require a new native profile.