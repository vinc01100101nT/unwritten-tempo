# Handoff — Windows PC setup and one set of scripts for both computers

**Task:** make the project (built on the Mac for two months) work on the Windows PC, through one OS detector
(`tools/platform.sh`) that every script, `.mcp.json` and the docs ask, instead of Mac-only paths and commands.

**Status: IN PROGRESS.** Everything below "What's done" was measured on the Windows PC on 2026-10-01. **Nothing
has been run on the Mac** — the Mac paths were kept and read, not executed. The full game build has NEVER run on
Windows. Don't trust prior "it works" claims beyond the exact measurements listed.

## What's done (measured on Windows, 2026-10-01)

- **Installed:** .NET 10 SDK, PostgreSQL 17 (Windows service `postgresql-x64-17`, superuser `postgres` /
  `postgres`, starts with Windows), a real `python3` (copy of Python 3.12 at `~/.local/bin`, first on the user
  PATH), `accounts/` node packages, Unity's Windows Dedicated Server build module.
- **Fresh database `unwritten`**, all 6 migrations applied, `settings.env` created with new secrets
  (`DATABASE_URL=postgres://postgres:postgres@localhost:5432/unwritten`). One GM account:
  `gm@unwritten.local` / `password`, `is_gm = true`, no characters.
- **`tools/platform.sh`** (sourced or run as `sh tools/platform.sh <question>`; Python asks via
  `tools/platform_ask.py`): os, python, unity-data, unity-cli, browser, native-path, build-target, game-program,
  open-game, port-info, stop-port, lan-address, editor-log, player-log, open-launcher, db-start, db-stop,
  sign-mac-app.
- **Wired through it:** `tools/hooks/run-hook.sh`, `tools/start-mcp.sh` (both `.mcp.json` entries),
  `check-unity-code.sh`, `check-game-math.sh` (Windows Unity has no `netcorerun`; runs the checks with Unity's
  own `dotnet` + a generated runtimeconfig), `build-game.sh` (builds for the host OS; every editor request names
  `--project-path`, because on Windows `unity status` lists NO editors while the editor answers by project),
  `build-launcher.sh`, `run-map-server.sh`, `publish-update.py`, `check-ui-prototypes.py`, `ui-geometry.py`
  (reply parsed as JSON — the CLI beta.11 reply ends the object after `result`), charserver test scripts,
  the character server's port hint.
- **Removed** the editor's Windows-only build menu (`Editor/BuildGame.cs`); `build-game.sh` is the one way to
  build, and it took over the "Systems scene first" safeguard. Confirmed gone from the compiled editor code.
- **Fixed:** write-scope guard allows the system temp folder on Windows; `prove_mirror.py` falls back to
  junctions/copies where symlinks are refused; the launcher reads the game's current font
  (DejaVuSansCondensed — Cinzel/Inter were deleted in 81e46277); `check-asset-wiring.py` reads `artFolder:`
  (the old `sheet:` field vanished in the roster rename, so the creature-art check had been checking nothing);
  its prover finds its test creature instead of naming `Bear`; `__pycache__/` is git-ignored.
- **Proven working on Windows:** `check-unity-code.sh` (3 passes clean), `check-game-math.sh`, provers for
  game-math / map-ids / biome-table / asset-wiring / write-scope / server-rules, `check-ui-prototypes.py` renders
  in Chrome, `ui-geometry.py` measured Inventory live, `tools/build-launcher.sh` (all three launchers),
  `charserver` build + `charserver/run.sh` (/health ok, /stats ok), `cd accounts && npm start` (/health ok),
  `tools/backup-database.sh`, the wiki `python3 -m http.server`, `pg_isready`, `port-info` / `stop-port`.
  The VS Code terminal on this PC defaults to Git Bash, so the docs' commands run as written there.

## Uncommitted working-tree changes

None intended — everything (including Unity's own re-saves of ground-tile settings, `data/skills.*`, the
generated wiki pages and the hook logs) was committed and pushed together with this file. Re-measure with
`git status` before relying on that.

## What's left

1. **Build the game on Windows**: `tools/build-game.sh both` with Unity open and idle. Never run end to end.
   Then `tools/run-map-server.sh` and `tools/platform.sh open-game` (both untested — no build existed).
2. **Unity crashed** at about 21:01–21:03 (monitor went black ~5 s, then Unity closed with its bug reporter)
   while `tools/prove-asset-wiring-check.py` ran. NOT investigated — the user was asked to confirm the bug
   description first. Nothing in that prover talks to Unity; cause unknown. Read Windows' System event log
   around that time (display driver reset, event 4101) and Unity's crash folder / `tools/platform.sh editor-log`.
3. **Doc fixes proposed, NOT applied** (the user asked whether the network guide still works on Windows — a
   question, so nothing was edited): the GM lines `psql -d unwritten -c ...` FAIL on Windows with
   `fe_sendauth: no password supplied` (Postgres here needs the password; the Mac trusts the OS user). The form
   `psql "$(grep '^DATABASE_URL=' settings.env | cut -d= -f2-)" -c ...` works on both — measured. `rsync`
   (copying an update to the rented server) does not exist in Git Bash on this PC. `tools/platform.sh db-start`
   on Windows opens an administrator prompt — untested.
4. **Run on the Mac:** `tools/check-unity-code.sh`, `tools/check-game-math.sh`, `tools/build-game.sh client`,
   and confirm both Claude connections start (`sh tools/start-mcp.sh unity-editor`).
5. **Mac launchers built on Windows are unsigned** (signing is Mac-only) — build release launchers on the Mac.
6. **Decisions for the user:** the retired `com.coplaydev.unity-mcp` package is still in the manifest and
   rewrote one Claude client config at editor start ("[StartupConfigRewrite] refreshed 1 client config(s)");
   the machine-level rule's clock command (`TZ=Asia/Manila date`) prints UTC on this PC; the launcher's new font
   has not been seen on screen.

## Dead ends

- `unity status` / bare `unity cmd …` on Windows: "No Unity Editor instances found" while the editor is open
  and the MCP tools work. Proven fix: pass `--project-path <repo>/unity` (done in build-game.sh, ui-geometry.py).
- Python heredocs through the Bash tool turned `\033` / `\n` into raw bytes — see
  `docs/5-ai-learnings/backslashes-sent-through-the-terminal-arrive-changed.md`. Use Edit/Write for backslashes.

## Next single step

With Unity open and idle on this PC: `tools/build-game.sh both`, then start the four services in the network
guide's order and sign in as `gm@unwritten.local` / `password`.

## How to resume

```text
Read handoffs/windows-setup-and-cross-os.md and continue from "Next single step". Unity must be open first.
```
