# Build 2.1.7

Use Windows x64, PowerShell 7, a .NET SDK supporting net48, .NET Framework 4.8
and your own compatible MMC SDK. Run from runtime/ (or the source ZIP root):

```powershell
.\build.ps1 -MmcRoot 'C:\path\to\MMC' -OutputDirectory 'C:\new\checked-build'
.\tests\SupportChecks.ps1 -Collector .\collect-diagnostics.ps1
```

The build verifies the native input against its manifest, embeds it in the
managed DLL, compiles/runs private tests and stages the player layout. Output
checkpoints are not overwritten. It does not install or start the game. Native
C++ source/checks are included. The repository supplies the verified build input
under dist/v2.1.7/; the source ZIP supplies Native/bin/Release/. To rebuild the
native helper with MSVC x64 Build Tools/Windows SDK, pass -RebuildNative. This
updates the native manifest before compiling C#. Do not swap arbitrary helpers.

To reproduce the player/source packages after building, Python 3 is also needed:

```powershell
.\package-release.ps1 -MmcRoot 'C:\path\to\MMC' -BuildDirectory 'C:\new\checked-build' -WorkingFbmod 'C:\path\to\OfflineTeamBuilder.fbmod' -ExpectedFbmodHash 'BASELINE_SHA256' -CacheIndex 'C:\path\to\cache-index.json' -OutputDirectory 'C:\new\package'
```

The cache metadata needs head and the ordered superbundles array only. A trimmed
copy is included in the source ZIP. The baseline mod remains read-only. Packaging
preserves every game resource/payload while updating display metadata, checks
MMC reader parity, exports/verifies an editable project, checks both ZIPs and
includes a matching source archive. The player ZIP contains only the DLL, fbmod
and installation README; editable project and verification metadata stay outside
it. Support tools remain in the source/repository. Native SDKs and user data are excluded.

Native game/owner/helper fingerprints remain mandatory. Preserve the per-user
storage boundary when adding outputs. Tests use private Temp fixtures, including
an ACL-denied fake installation; never apply those ACLs to a real MMC folder.
The runtime tests inject private user-data storage to avoid reading/writing real
user settings. Keep all live-game tests separate and report them explicitly.