# Handoff — the two-program patcher on Mac (patcher P fetches patcher G)

**Task:** give Mac players what Windows players got on 2026-10-11: a small program they download ("patcher P") that
fetches the launcher ("patcher G") into their own folder, keeps it up to date, and starts it — so a launcher change
reaches Mac players without them downloading anything again. Action plan `MT-50` (ON-GOING). Runbook section:
"Handing out updates" in `docs/2-systems/networking/starting-the-network.md`.

**Status:** Mac BUILT AND TESTED ON THIS MAC, NOT PUBLISHED. Windows is live since 2026-10-11 13:55 GMT+8 and
unchanged in behaviour. Nobody has play-tested either platform yet, and no Mac P or new Mac G window has been seen
on a screen (the user ruled terminal-only testing: no window may open on their screen).

User decisions (2026-10-11): Mac players get the AUTOMATIC hand-over; a Mac launcher opened directly that is behind
shows a message and does not start the game; testing is terminal-only.

## What's done

- **Mac P** — `launcher-updater-mac/` (new, shares no file with the Windows P so Windows P's fingerprint cannot
  change): `MacFetcher.cs` (the logic), `MacWindow.cs` (AppKit through the Objective-C runtime, one line of text,
  game font + palette), `Program.cs`, `MacApps.cs` (path checks, one-step folder swap with `renamex_np`
  `RENAME_SWAP`, leftover recovery, Info.plist reading, `OldLaunchers` via `LSCopyApplicationURLsForBundleIdentifier`
  — also compiled into G), `LauncherUpdaterMac.csproj` (Native AOT). One universal app (arm64 + x64 joined with
  `lipo`), bundle id `com.unwritten.patcher`, CFBundleVersion = build time. Address: env `PATCH_URL`, then
  `~/Library/Application Support/unwritten/patcher/launcher.txt`, then its own sealed one; written back only after
  an answer. Lock file `patcher/.lock` (FileShare.None = flock, measured). `UNWRITTEN_PATCHER_TEXT=1` prints its
  lines instead of opening a window.
- **G** — `launcher/Patcher.cs` (Mac-only additions; Windows paths unchanged): reads `patcher/launcher.txt` before its
  sealed address on a Mac; lock for its lifetime; opened directly + behind → "A newer launcher is out…"; after the
  game is current, replaces with P (from `game/unwritten-patcher.app/`) the P the player opened when its build number
  is lower, and every OLD launcher the Mac's app index lists (bundle id `com.unwritten.launcher`, no CFBundleVersion,
  not in Trash / AppTranslocation / the unwritten folder); Uninstall removes `patcher/` too; runs `game ?? play ??
  start`; refuses to start anything under `unwritten-patcher.app/`. `Program.cs` reads `--beside-id`,
  `--beside-version`.
- **Publish** — `tools/publish-update.py`: `mac/patcher-arm64.json` / `-x64.json` (file lists + address + previous),
  Mac list hand-over (`start` = `play` = P, `game`, `fetcher`), `mac_launchers()` checks (only when Mac is
  published), Mac zips written only by a publish that includes Mac, `--launcher-only` includes Mac when
  `builds/launcher/mac` exists. **Build** — `tools/build-launcher.sh`: Mac G → `mac-patcher-<chip>/` with a build
  number; Mac P on a Mac only → `builds/launcher/mac/` + `builds/launcher/mac.zip` (ditto, no extras); off a Mac,
  `builds/launcher/mac` is left alone. `tools/platform.sh open-launcher` opens Mac P.
- **Docs** — runbook section rewritten; website Mac steps gained the "Already have the game?" line; learnings page
  `docs/5-ai-learnings/a-started-program-cannot-ask-who-started-it.md`, and the backslash page gained an incident.

### Proved (measured 2026-10-11 on the Mac, against a local copy of the update folder, HOME pointed at throwaway folders)

The real P and the real publish script; a stand-in G compiled from the real `launcher/Patcher.cs` with text output;
old launchers compiled from `git show aaa56b9a:launcher/Patcher.cs` (first) and the pre-change `Patcher.cs` (today's),
each closing the moment it pressed Start:
1. New player: P from the download zip → G installed (signature verifies) → game started.
2. Opened again: nothing downloaded.
3. First launcher's Start → P → G → game; both old test launchers in an indexed folder replaced by P, signatures
   verify, no leftovers. 4. Today's launcher (runs `play`) the same. 5. The replaced app opened: untouched.
6. Lock held → P "The patcher is already open…", G "The launcher is already open." 7. Dead stored address → fell back
   to the sealed one and stored the good one. 8. Damaged launcher download → started the installed G.
9. Newer P published → the player's older P replaced. 10. Uninstall → unwritten folder gone, player's P kept.
11. A list naming `../../../escaped` → refused, nothing written outside. 12. A swap cut short → app restored.
13. The REAL 108 MB G installed by the real P (starting it blocked with `sandbox-exec`) → byte-identical, signature verifies.
P's window drawn offscreen from the shipping code: font, centring, wrapping and palette fill correct.

### Not verified

- The Downloads-folder permission prompt when G replaces an app in `~/Downloads` (expected once; unmeasured).
- App Translocation: P resolving its real location when run from a quarantined download (`SecTranslocate*`, private
  but exported; needs a Finder-opened download, which puts a window on screen).
- Any window on a real screen, on either platform; G started by P coming to the front.
- Building on Windows with the new code (`tools/build-launcher.sh` there), and Windows P staying byte-identical
  (its sources are untouched; only a comment in its project file changed).

## Uncommitted working-tree changes

All of the above is uncommitted on `main` on the Mac. Run `git status` on arrival — do not rely on this paragraph.

## What's left

1. Commit and push (needs the user's go), `git pull` on the Windows PC.
2. Windows PC: `tools/build-launcher.sh` (Windows P + G, Mac G with build numbers).
3. Copy `builds/launcher/mac.zip` from the Mac (built 2026-10-11 15:22 with
   `PATCH_URL=https://unwritten.tailc2913b.ts.net:10000`) to the Windows PC, unzip inside `builds/launcher/`.
   Publish refuses it if Windows' `PATCH_URL` differs — then rebuild on the Mac with that value.
4. Windows PC: `tools/publish-update.py --launcher-only` — writes the Mac lists, the Mac hand-over and the Mac zips,
   and the new Windows G (its Mac-only code changes nothing on Windows).
5. Play-test on a real Mac: a fresh download, and an old launcher's Start.

Not done, offered earlier and unanswered: resuming an interrupted launcher download; choosing another drive on
Windows when the system drive is full.

## Dead ends

- Reading the parent process to find the old launcher: the old launcher closes as soon as it starts P, and even a
  50 KB C program reading its parent on its first line lost that race 8 times out of 8. Replaced by the Mac's app
  index, which listed new test apps within 4 seconds without them being opened.
- Proven on Windows earlier: overwriting a running program fails, renaming it aside works; a command-line delete
  inside the folder it removes cannot remove it; Native AOT needs `vswhere.exe` on the PATH.

## Next single step

Ask the user for the go to commit, then steps 2–4 on the Windows PC.

## How to resume

```text
Read handoffs/mac-patcher-p-and-g.md and continue from "What's left". Windows is live; do not change its behaviour.
```
