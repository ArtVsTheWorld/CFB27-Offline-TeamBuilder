# Build Offline TeamBuilder 2.4.14

Use Windows x64, PowerShell 7, the .NET 4.8 targeting pack and your own MMC
SDK. Run this from the repository's runtime folder or the source ZIP root.

```powershell
.\build.ps1 -MmcRoot 'C:\path\to\MMC' -OutputDirectory 'C:\new\checked-build'
```

The build compiles the detached host and runtime, runs the managed and support
checks, and writes to a fresh output directory. The checked native helper and
artwork codec are under dist/v2.4.14 in the repository. The source ZIP has
them under Native/bin/Release and ArtworkNative/bin. To compile either input
with the x64 MSVC build tools and Windows SDK, use -RebuildNative or
-RebuildArtworkNative.

SharedUi/worker.zip is the exact launcher worker embedded in the DLL. It
includes the editable Python code and its bundled runtime. ArtworkNative
includes the checked templates and the codec source.

The player ZIP ships the two separate fbmods next to its install guide. The
companion starts as an empty seed and the runtime prepares it per MMC pack.
The previous one-fbmod packaging script is for the 2.1.7 distribution.
