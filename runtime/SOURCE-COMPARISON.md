# 2.4.13 source comparison

The baseline is the public `v2.1.7` source tag. The release DLL is the
tested 2.4.13 candidate preserved in `dist/v2.4.13`. The source rebuild
passes the managed suite. The detached host build can embed path-dependent
debug metadata, so a rebuild in a different directory can have a different
binary checksum.

| Change | Source compared against 2.1.7 |
| --- | --- |
| November 19 title update | `runtime/Profiles/cfb27-2026-11-19.json`, `GameProfile.cs`, native helper manifest and guards |
| Multi-school artwork and name aliases | `ArtworkPreparation.cs`, `ArtworkCompiler.cs`, `ArtworkLibraryWriter.cs`, `Store/TeamArtworkCatalog.cs`, `UnifiedModWriter.cs` |
| Full player-card background, in-game depth chart, and pregame sides | `ArtworkNative/Templates/manifest.json` and the player-card, match and team-select scripts packed in `templates.zip` |
| Automatic shared UI companion | `RuntimeAction.cs`, `SharedUi/SharedUiPreparation.cs`, the builder source in `runtime/SharedUi/worker.zip` |
| Startup and viewed-team recovery | `Engine/AssetRuntime.cs`, `ColdStartBootstrapScope.cs`, `CardDemandRepairScope.cs`, `NativeJobQueueReader.cs` |
| Midgame Save export and legacy fallback | `CareerHubCompatibility.cs`, `tests/CareerHubExportChecks.cs` |
| Detached runtime and disabled loading gate | `DetachedHostLauncher.cs`, `Host/Program.cs`, `RuntimeCoordinator.cs`, `HubLoadingGateRuntime.cs` |

Unless otherwise indicated, source filenames above are under `runtime/src`.
