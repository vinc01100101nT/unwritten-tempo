# Handoff — live-readiness fixes, and downloading the game over the Wi-Fi

**Task:** build the four live-readiness fixes from [the plan](../docs/1-planning/live-readiness-fixes.md) (item
checks, saves surviving a character server outage, the code half of encryption, continuous backups), and let other
devices on the home Wi-Fi download the game from a page on the Windows PC instead of copying builds by hand.

**Status: IN PROGRESS — everything is built and committed; almost none of it has been PLAY-TESTED.** Compile checks,
server checks and one scripted test of the hand-over check passed; nobody has played any of it. A Mac has opened the
signed launcher only as far as macOS's "could not verify … free of malware" prompt (the expected one); nobody has yet
got past it, downloaded the game, or played on a Mac. **Don't trust a prior "it works" for anything not listed as
measured below.**

## What's done

**Item checks** — `BagStack.cs` (new: one shared "still the stack they meant" check, item name now required),
`NetPlayer.StillTheSameStack` and `GameNetwork.OnItemUsedRequest` call it; `Character.MayUse` / `MayBind` refuse any
skill with an "item used" trigger (all 19 item skills, measured); `SkillRunner.FireAsPassive` returns whether it ran,
so a potion whose effect did not run stays in the bag; "Insert card" removed from `ItemActionMenu`.

**Saves survive an outage** — `SaveOutbox.cs` (new: one outbox per map server, newest save per character, retried
every 15 s, kept on disk under `persistentDataPath/unsent-saves-<port>/` only after a failed send and at shutdown,
refused-for-good saves kept in `refused/`); `NetPlayer.SaveNow` submits to it; `MapServerLink` loads it on start,
ticks it, holds registration while a save has failed; `GameNetwork.SaveAndDrop` flushes a waiting save for a player
no longer connected; `ServerHttp.Reply` now carries the status number. Character server: migration
`007_character_handover.sql` (APPLIED to this PC's database), `Handover.cs` (numbering + `StartupGrace` 40 s entry
refusal), `Characters.EnterAsync` / move route bump the number and send it, the progress route refuses an older one
(409). `charserver/run.sh` builds once and restarts the server 5 s after any exit except 0 and 2 (port taken);
`deploy/unwritten-charserver.service` for the rented machine. **Measured:** on a scratch database with a spare
server, saves with no number / the live number / an older number / an unknown character returned 200 / 200 / 409 /
404 and the stored level was right.

**Encryption, code half** — `MAP_SERVER_ARRIVALS_ADDRESS` (map server sends it, character server delivers there,
map server listens only there when set); Unity insecure HTTP = development builds only; `build-game.sh --dev`;
`ServerAddress.TypedAccountsUrl` lets a phone type a whole `https://` address.

**Backups** (built by a helper, spot-checked) — `tools/backup-start-archiving.sh`, `backup-full-copy.sh`,
`backup-rehearse-restore.sh`, four new settings in `settings.env.example`. Rehearsed by the helper on a throwaway
Postgres; **never run against this PC's database** (archiving is still off here).

**Wi-Fi downloads** — `launcher/download.html` (the page, with detailed Mac steps), `tools/serve-updates.sh`,
`publish-update.py` writes the page and zips the Windows and two Mac launchers into `download/`, and publishes this
computer's platform by default. Mac on the Windows PC: Unity Mac Build Support (Mono) installed, `rcodesign` 0.29.0
in `~/.local/bin` (fingerprint checked), `platform.sh sign-mac-app` uses it off a Mac, `build-game.sh --mac` switches
the editor to the macOS build profile and back. The Mac launcher's `launcher.txt` now lives in
`Contents/Resources` (it made the Mac call the app "damaged" in `Contents/MacOS`); `Patcher.cs` reads it there.

**This PC's state:** `settings.env` (never committed) has `MAP_SERVER_ADDRESS=192.168.1.8`,
`PUBLIC_ACCOUNTS_URL`/`PUBLIC_CHAR_SERVER_URL` = `http://192.168.1.8:8080`/`8081`, `PATCH_URL=http://192.168.1.8:8090`.
Builds: `builds/client` (Windows, development), `builds/mac/client` (Mac, development, **Intel-only** — built with no
build profile active), `builds/server`, `builds/launcher/*`, published as version `2026.10.02-1049` in
`builds/patch` (all git-ignored).

## Uncommitted working-tree changes

None after this session's final commit: everything listed under "What's done", the docs (plan, runbook, notes,
roadmap, action plan MT-35/MT-36, two new learnings pages), the rule-file changes and the memories went into one
commit pushed to `main` at the end of the session. Re-measure with `git status` before trusting this line.

## What's left

1. **Play-test the whole batch** — the checklist is in the session's final report and in the plan's "expected
   result" sections: start the character server with `charserver/run.sh` (new code), the map server, sign in within
   40 s (refused, then let in), potions/equip/drop/reorder, outage test (Ctrl+C the character server, loot, log out,
   restart → loot kept), crash test (Task Manager ▸ End task CharServer.exe → restarts in 5 s), a Wi-Fi device.
2. **Mac:** get past the "could not verify" prompt (Open Anyway, or the `xattr` line on the page), confirm the
   launcher downloads the game and the game runs. Then rebuild the Mac game through the macOS profile so it is
   Intel + Apple chip.
3. **Backups:** compress archived change-log files before going live (16 MB each even when nearly empty — about
   23 GB on a busy day); daily partial copies; failure alert; off-site bucket (waits for hosting).
4. **Hosting** (waits on buying a domain): the encrypting front door, certificates, closing the plain doors.

## Dead ends (all resolved — kept so they are not repeated)

**Proven:** switching platform and building in one editor request leaves the editor where it was ("Unable to build
with the current configuration"); a switch request sent while the editor compiles is dropped; with a build profile
active, `SwitchActiveBuildTarget` is undone after the reload; a newly installed Unity module is not loaded until the
editor restarts (Build Profiles says "No macOS module loaded"); a non-program file in a Mac app's `Contents/MacOS`
is never sealed (codesign: "a sealed resource is missing or invalid"). Pages: `docs/5-ai-learnings/
build-for-another-platform-in-a-running-editor.md`, `mac-app-damaged-unsealed-file.md`.

**Hypothesis, unchecked:** the Mac launcher program has 5,305 bytes after its signature that the signature does not
cover; the Mac did not complain about it, so it was left alone.

## Next single step

Run the play-test checklist on this PC (step 1 above); if the outage test loses loot, read `logs/server.log` for
"could not write character … down yet" and the character server's window for "refused a save … from hand-over".

## How to resume

```text
Read handoffs/live-readiness-and-lan-downloads.md and docs/1-planning/live-readiness-fixes.md, then help me play-test the batch.
```
