# Handoff — Windows PC setup and one set of scripts for both computers

**Task:** make the project (built on the Mac for two months) work on the Windows PC, through one OS detector
(`tools/platform.sh`) that every script, `.mcp.json` and the docs ask, instead of Mac-only paths and commands.

**Status: IN PROGRESS — the Windows half is done and proven; the Mac half has never been run.** Everything under
"What's done" was measured on the Windows PC on 2026-10-01. The Mac paths were kept and read, not executed —
don't trust any "works on both" claim until the Mac steps below have run.

## What's done (measured on Windows, 2026-10-01)

- **Installed:** .NET 10 SDK, PostgreSQL 17 (Windows service `postgresql-x64-17`, superuser `postgres` /
  `postgres`, starts with Windows), a real `python3` (copy of Python 3.12 at `~/.local/bin`, first on the user
  PATH), `accounts/` node packages, Unity's Windows Dedicated Server build module.
- **Fresh database `unwritten`**, all 6 migrations, `settings.env` with new secrets
- **`tools/platform.sh`** — os, python, unity-data, unity-cli, browser, native-path, build-target, game-program,
  open-game, port-info, stop-port, lan-address, editor-log, player-log, open-launcher, db-start, db-stop,
  sign-mac-app — wired into the hooks starter, `tools/start-mcp.sh`, both code checkers, `build-game.sh`,
  `build-launcher.sh`, `run-map-server.sh`, `publish-update.py`, `check-ui-prototypes.py`, `ui-geometry.py`, the
  charserver test scripts and the character server's port hint.
- **The whole chain runs on Windows:** `tools/build-game.sh server` (402 MB) and `client` (743 MB, dials
  localhost), `tools/build-launcher.sh` (Windows + both Mac launchers), `cd accounts && npm start`,
  `charserver/run.sh`, `tools/run-map-server.sh` (the starting town loads; "the character server knows this map
  server holds 1 map(s)"), and the GM account signed in, created a character and entered: the map server logged
  `[map] expecting GmProbe — their character arrived ahead of them.`
- **The content-build failure** on Windows was the add-ons package's report window — see memory
  `addressables-report-window-fails-the-build`; the per-machine preference is now off on this PC.
- **Unity's crash at 21:04:24** was Windows Update installing the NVIDIA driver 32.0.16.1088 mid-session
  (System log 21:03:03–21:04:07); Unity's crash report stops in `CheckDeviceStatus` / `D3D12SwapChain::Present`
  on the old driver 32.0.15.9636. Not caused by the project.
- **Network guide:** Windows forms of the two GM database lines added BESIDE the Mac ones (the Windows database
  needs the password; both forms measured working); notes that `rsync` must run from the Mac; build/publish steps
  say which copy each computer builds. No Mac command was removed.
- **Launcher** uses the game's interface font (DejaVu Sans Condensed); builds clean.

## Uncommitted working-tree changes

Re-measure with `git status`. At the time of writing: the network guide, the build tool's size line, a new memory
and its index line, this handoff, the new `unity/Assets/AddressableAssetsData/Windows.meta` (Windows twin of the
committed `OSX.meta`), and three Unity settings files whose only change is line endings.

## What's left

1. **On the Mac:** `tools/check-unity-code.sh`, `tools/check-game-math.sh`, `tools/build-game.sh client`, and
   `sh tools/start-mcp.sh unity-editor` (both Claude connections). If the Mac's content build fails in 0 s,
   see the memory above.
2. **Release launchers come from the Mac** — the Mac ones built on Windows are unsigned.
3. **User decision:** stop Windows Update installing graphics drivers by itself (that is what crashed Unity).
4. **Launcher font not yet seen on screen.**

## Dead ends

- Bare `unity status` / `unity cmd …` on Windows find no editor; `--project-path <repo>/unity` works.
- Python heredocs through the Bash tool turn `\033` / `\n` into raw bytes — see
  `docs/5-ai-learnings/backslashes-sent-through-the-terminal-arrive-changed.md`.

## Next single step

On the Mac: pull, then run the four commands in "What's left" step 1.

## How to resume

```text
Read handoffs/windows-setup-and-cross-os.md and do "What's left" step 1 on the Mac.
```
