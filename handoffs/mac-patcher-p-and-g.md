# Handoff — the two-program patcher on Mac (patcher P fetches patcher G)

**Task:** give Mac players what Windows players got on 2026-10-11: a small program they download ("patcher P") that
fetches the launcher ("patcher G") into their own folder, keeps it up to date, and starts it — so a launcher change
reaches Mac players without them downloading anything again. No Tempo issue/doc id. Runbook section: "Handing out
updates" in `docs/2-systems/networking/starting-the-network.md`.

**Status:** NOT STARTED on Mac. Windows is BUILT AND LIVE since 2026-10-11 13:55 GMT+8.
- Windows was proved with the real built programs on a hidden desktop (a new download installing all 512 game files;
  the real first launcher handed over through its Start button), but **the user has not play-tested it yet**, and
  nobody has seen the new window on a real screen: its see-through shape and dragging are unconfirmed. Don't trust
  prior "it works" claims beyond what is listed under "Proved".
- The new launcher window (`launcher/`) is shared code, so a Mac launcher built today already has the new look —
  **never run on a Mac**. A borderless see-through window (`SystemDecorations="None"`,
  `TransparencyLevelHint="Transparent"`) is unverified there.

## What's done (Windows, and the shared launcher)

- **Patcher P** — `launcher-updater/` (new): `Fetcher.cs` (all the logic, no window), `Window.cs` (raw Win32 window,
  one line of text, the game's font and palette carried inside), `Program.cs`, `LauncherUpdater.csproj` (Native AOT,
  win-x64, 4.7 MB). Reads `{PATCH_URL}/windows/patcher.json`, downloads `windows/files/<sha256>` into
  `%LOCALAPPDATA%\unwritten\patcher\unwritten-launcher.exe`, writes `launcher.txt` beside it (the address from
  `patcher.json` wins), starts it with `--beside <P's folder>`. Never writes into its own folder.
- **Patcher G** — `launcher/`:
  - `Patcher.cs`: Windows game folder is `%LOCALAPPDATA%\unwritten\game`; `Patcher(string? beside)`;
    `HandedToFetcher` (G opened directly hands back to the copy of P among the game files);
    `KeepPlayersProgramCurrentAsync` (replaces the program in the player's folder with the published P by
    rename-aside, rewrites the address beside it); `MoveFormerCopy` (moves a first-launcher game beside P into the
    player's folder — only with our `installed.json` record there, never while the game runs, free-space check
    across drives, only empty folders removed); `Uninstall` + `RemoveSelfOnceClosed`; `Manifest.play`.
  - `Program.cs` (`--beside`), `MainWindow.axaml(.cs)` (borderless shaped window, artwork background, Uninstall with
    a Yes/Cancel question, – and × round buttons, drag by the artwork, failure text no longer overwritten by a queued
    progress line), `LetteringShape.cs` (the rim round the lettering, worked out with SkiaSharp at start-up),
    `App.axaml.cs` (palette read from `prototypes/ui-tokens.json`), `UnwrittenLauncher.csproj` (links the palette and
    the two pictures in `wiki/assets/`).
- **Publish** — `tools/publish-update.py`: `windows/patcher.json` + G's blob; P listed among the Windows game files
  as `start` with the game as `play` (this is what hands the FIRST launcher over); `--launcher-only` (no build read,
  no version written); `--without-hand-over` (withdraws P from first launchers); `windows_launchers()` proves P and G
  are the right two files before anything is written. `tools/build-launcher.sh`: G → `builds/launcher/windows-patcher/`,
  P → `builds/launcher/windows/` (Windows only; removed elsewhere so a stale one is never published).
- **Website / news** — `website/index.html` (homepage banner from `data-src`, Download page wording),
  `launcher/news.json` (four new items), `wiki/assets/last-line-frostcap-slime-siege.jpg`,
  `wiki/assets/unwritten-lettering.png`.
- **Docs** — the runbook's launcher section rewritten; `docs/5-ai-learnings/run-a-windowed-program-on-a-hidden-desktop.md`.

### Proved (measured 2026-10-11, on the Windows PC)

- The zip from the public address → P → G → all 512 game files installed (749 MB) in 19 s.
- The real first launcher (kept at `builds/first-launcher-2026-10-04/` **on the Windows PC only — `builds/` is not in
  git**) against the live server: updated, Start pressed by a posted click, closed, P ran, G opened, the game moved
  without a download, the old program's file replaced by P, its `launcher.txt` rewritten to the current address.
- Both addresses serve the new files: `http://100.113.81.97:8090` (in first launchers' `launcher.txt`) and
  `PATCH_URL` from `settings.env`.
- Live folder: version unchanged at `2026.10.11-0238`; Windows list 511 → 512 files (only P added); Mac list untouched.

## Uncommitted working-tree changes

None from this work: everything above went into the commit that added this file, pushed to `main`. That commit also
carries two files that were already modified before this work began and are not part of it:
`unity/Assets/3_Data/Resources/Fonts/DejaVuSansCondensed SDF.asset` and `tools/hook-analytics/hook-analytics.jsonl`.
Run `git status` on arrival — do not rely on this paragraph.

## What's left (the Mac work)

Facts to build on (read from the code, not assumed):
- A Mac player's game is ALREADY in `~/Library/Application Support/unwritten/game` (`Patcher.InstallRoot`), so there
  is nothing to move and no `--beside` on Mac; `playersFolder` stays null off Windows.
- The Mac launcher is a `.app` FOLDER (`tools/build-launcher.sh` builds `builds/launcher/mac-arm64|x64/unwritten.app`
  and signs it with no certificate), so "the launcher" on the update server must be a LIST of files with their
  run-permission flag, the same shape as the game's `manifest.json` — not one blob like Windows' `patcher.json`.
  The game `.app` already reaches Mac players exactly that way and runs, so byte-identical files plus restored run
  permission keep the signature valid.
- Today the Mac launcher reads its address from `launcher.txt` in `Contents/Resources`, INSIDE the signed app
  (`Patcher.Setting`). P must not rewrite a file inside the bundle — that breaks the signature. G needs to read
  `~/Library/Application Support/unwritten/patcher/launcher.txt` first on Mac.
- A downloaded Mac app may be run by macOS from a read-only temporary location, so Mac P, like Windows P, must never
  write beside itself — and G cannot replace the player's copy of P there. Keep Mac P as plain as possible.

Steps:
1. **Mac P**: a small program for osx-arm64 and osx-x64, built ON THE MAC (Native AOT does not build across operating
   systems — from .NET's documentation, not measured here). Reuse `launcher-updater/Fetcher.cs`'s flow; the Windows-only
   parts are `Window.cs` and the single-file download. It needs its own one-line window (AppKit), the game's font
   and palette, the same three lines of text ("Checking for patcher update", "Downloading the patcher NN%",
   "Updating the patcher NN%"), and must be wrapped and signed as a `.app` like the launcher is.
2. **Publish**: a per-chip file list for the Mac launcher (for example `mac/patcher-arm64.json`, `mac/patcher-x64.json`,
   contents in `mac/files/`), kept by the blob clean-up; `--launcher-only` extended to Mac; the Mac download zips
   switched to Mac P. Check everything at the top of `main()` as `windows_launchers()` does.
3. **Mac G**: read the address from the folder beside the installed app first; Uninstall removes the installed
   launcher too.
4. **Getting Mac P to the update folder**: the live folder is `builds/patch/` on the Windows PC and `builds/` is not
   in git, so the two Mac P apps must be copied from the Mac into the Windows PC's `builds/launcher/` before
   publishing there. The Mac launcher (G) itself can keep being built on the Windows PC.

Open decisions for the user (ask ONCE, batched, before building):
- **Existing Mac players**: (recommended) no hand-over — they download Mac P once from the website; their game is
  already in the right folder, so nothing downloads twice. Or build the same `start`/`play` hand-over as Windows.
- **A Mac launcher opened directly** (a Dock icon) cannot hand back to P the way Windows does unless a copy of P is
  kept somewhere it can find. Decide whether G should instead say "open the launcher you downloaded" when it is behind.
- **How to test on the Mac**: the hidden-desktop method is Windows-only. Opening windows on the user's screen is
  banned unless they ask for it in that conversation.

Not done on Windows, offered and unanswered: resuming an interrupted 100 MB launcher download (the update server,
`tools/serve-folder.py`, answers a range request with the whole file); letting a player choose another drive when the
system drive has no room (today G stops with a message and moves nothing).

## Dead ends

Proven facts:
- Overwriting a running program's file fails on Windows; renaming it aside works (red-team measurement, then the real
  hand-over run).
- A command-line delete started with its working folder inside the folder it removes cannot remove that folder.
- `dotnet publish` with Native AOT needs `vswhere.exe` on the PATH; Visual Studio installs it in
  `C:\Program Files (x86)\Microsoft Visual Studio\Installer`, which is not on it.
- A backslash inside a shell heredoc that is itself inside a Python string arrives changed — every multi-line edit
  that contained one was redone with the editor tools.

Hypotheses, not verified:
- That an arm64 Mac can build the osx-x64 Mac P as well as its own.
- That a posted mouse click reaches an Avalonia window on a Mac the way it did on Windows.

## Next single step

On the Mac: `git pull`, then ask the user the three open decisions above in one message, then build the smallest
Mac P that prints its three lines to a terminal and fetches a file list — before writing any window code.

## How to resume

```text
Read handoffs/mac-patcher-p-and-g.md and continue the Mac patcher work from "Next single step". Windows is live; do not change its behaviour.
```
