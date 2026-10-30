# Changelog

## 2.1.4 — October 30 compatibility update

- Map the native hooks, globals, vtables and transport hooks to the new game.
- Rebase the fbmod revision and its three collector links. The existing menu
  scripts and all embedded payloads are unchanged.
- Preserve the uniform, preview, logo, audio and Dynasty-data safeguards.
- Keep the historical relay hashes and make native tests use profile symbols.
- Pass 4,309 local checks, 21 packaging checks and 40 read-only checks against
  the running game. Full game loading and playback verification is pending.

## 2.1.3

- Restore cached TeamBuilder logos after reloading a Dynasty, including the
  team-selection menu.
- Keep the working multi-team uniforms, news previews and custom stadium audio.
- Preserve Dynasty progress instead of reimporting original team metadata.
- Include the tested DLL, matching fbmod, editable project and simple README.

The repository upload does not change the released DLL or fbmod payloads.
Earlier technical notes are in [runtime/CHANGELOG.md](runtime/CHANGELOG.md).
