# Offline TeamBuilder 2.1.7

JDHalfRack / Ace / Timberline Tex

- Move logs, status, verified helper extraction, engine output and plugin settings
  to `%LOCALAPPDATA%\Ace\OfflineTeamBuilder`. No normal plugin-generated writes
  target the MMC installation, including a Program Files/readonly Plugins folder.
- Copy legacy settings/configuration without deleting originals or replacing
  current user settings. Archive conflicts and old logs separately from current
  status. Preserve the existing per-user asset store and original saves.
- Add readable `status.txt`, actionable diagnostic codes, exception/Win32 details,
  session IDs, startup environment and installed-DLL inventory, selected-pack/built
  mod context, a five-second status heartbeat and last-failure retention.
- Add a support collector with an explicit file allow-list and bounded native-log
  tails. Configuration, migration copies, saves, caches, DLLs and memory dumps are
  excluded. Diagnostics may contain personal paths; review before sharing.
- Retain single-DLL installation and the tester-confirmed target-process Windows
  export resolver. Existing target entry points/instrumentation are honored.
- Include the native capacity/registry guards, resident reuse, limited recovery
  and verified Midgame Save 0.2.2/0.2.4 hook sharing developed after public 2.1.5.
  Native helper bytes, game signatures and hook ownership checks are unchanged.

## Validation and limits

Private runtime checks include real deny-write ACLs on a temporary MMC fixture,
readonly-install migration/deployment, conflict preservation, failure reporting,
resource integrity and Windows export lookup. Source-ZIP reproducibility,
metadata-only fbmod preservation, editable-project payloads and archive hashes
are checked separately. No default test starts or patches the live game.

The external tester confirmed the preceding 2.1.6 startup fix works. The 2.1.7
path/log changes have private filesystem tests, not a new live-game certification.
Earlier tests of the underlying core passed loads/reloads with Broadcast off,
and a prior Midgame Save 0.2.2 combined run passed resume/stat carry, a completed
match and week advancement. Full 0.2.4 resume and Expanded Practice gameplay
remain separate acceptance items.

Broadcast Universe 0.7-beta1 with AIO 1.4.1 and the affected dynasty still caused
loading hangs on public 2.1.5 and the newer core without the other runtime plugins.
This release does not claim to fix that interaction. A runtime record or Ready
state does not establish GPU texture/world readiness. Capacity waits remain
bounded and forward normally on timeout.

See README.txt, BUILD.md and TESTING.txt. The user package includes one plugin DLL;
the source includes the checked native build input and C++ source. SDK binaries,
user data and diagnostic captures are not distributed.