# Build from source

You need Windows x64, .NET Framework 4.8, .NET SDK 8 or newer, and your own
MMC installation containing `FrostyCore.dll` and `FrostySdk.dll`.

From the repository folder:

```powershell
.\runtime\build.ps1 -MmcRoot 'C:\path\to\MMC'
```

This builds the plugin, runs the local tests, and creates runtime/source ZIPs
in `runtime/artifacts`. It does not install anything or open the game.
Use a new `-OutputDirectory` when rebuilding a release.

The source targets .NET Framework 4.8. Do not copy test-runner dependencies
into MMC's Plugins folder. MMC/Frosty assemblies are not included here.

The `dist` folders preserve versioned packages, not automatic build
outputs. A different compiler or MMC reference version can change a rebuilt
DLL's hash. Test new binaries before replacing a published release.

## Repository checks

```powershell
python scripts/verify_repo.py
```

GitHub runs these same release-integrity checks on pushes and pull requests.
They check the shipped files, project structure, version and credits. They do
not compile the DLL or test the game. Full runtime tests require the MMC
references above; the 2.1.4 suite contains 4,309 local checks.

For complete packaging commands, see [runtime/BUILD.md](runtime/BUILD.md).
