# 2.4.14 source comparison

Compared directly with public tag `v2.4.13`. The shipped DLL matches
the runtime verified in the fourth-priority launch.

- `src/SharedUi/SharedUiPreparation.cs` removes the blanket first-priority
  requirement. A reported overlap clears the generated companion under the
  existing pack lock and lets MMC proceed.
- `SharedUi/worker.zip` contains the updated `Builder/Tools/resolve-pack.ps1`
  and `Builder/Tools/auto_launch.py`. The resolver identifies higher-priority
  writes to the same UI rows, target collectors, or generated chunks. The
  worker passes that result back to the launcher.
- `src/PluginIdentity.cs` and `Host/host-manifest.json` identify and validate
  the new embedded build. The native profile and native helper are unchanged.
- Both shipped fbmods match the 2.4.13 release byte for byte. The companion
  seed is empty until the launcher generates it for the enabled pack.
