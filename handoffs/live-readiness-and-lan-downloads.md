# Handoff — live-readiness fixes, and downloading the game over the Wi-Fi

**Task:** build the four live-readiness fixes from [the plan](../docs/1-planning/live-readiness-fixes.md) (item
checks, saves surviving a character server outage, the code half of encryption, continuous backups), and let other
devices on the home Wi-Fi download the game from a page on the Windows PC instead of copying builds by hand.

**Status: IN PROGRESS — built, partly play-tested; one bug open.** Played on 2026-10-02 over the Wi-Fi from a
Mac and from this PC: signing in, entering the world and character creation work on both, and the Mac launcher
downloads and starts the game. Fixed after play-testing and **not yet re-played**: the gold window after `/gold`,
and the weapon in hand after a weapon swap (version `2026.10.02-1221`). **Open: the ink outline and paper look are
missing in the world in built games** (they show in the character-creation preview). The rest of the batch (outage,
crash restart, item checks, backups) has not been played. **Don't trust a prior "it works" for anything not listed
as measured below.**

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
(409). `charserver/run.sh` builds once and restarts the server 5 s after any exit except 0, 2 (port taken) and Ctrl+C;
`deploy/unwritten-charserver.service` for the rented machine. **Measured:** on a scratch database with a spare
server, saves with no number / the live number / an older number / an unknown character returned 200 / 200 / 409 /
404 and the stored level was right.

**Encryption, code half** — `MAP_SERVER_ARRIVALS_ADDRESS` (map server sends it, character server delivers there,
map server listens only there when set); Unity insecure HTTP = development builds only; `build-game.sh --dev`.

**Backups** (built by a helper, spot-checked) — `tools/backup-start-archiving.sh`, `backup-full-copy.sh`,
`backup-rehearse-restore.sh`, four new settings in `settings.env.example`. Rehearsed by the helper on a throwaway
Postgres; **never run against this PC's database** (archiving is still off here).

**Wi-Fi downloads** — the website's Download page (`website/index.html`, with detailed Mac steps), `tools/serve-updates.sh`,
`publish-update.py` writes the website and zips the small Windows and Mac programs into `download/`, and publishes this
computer's platform by default. Mac on the Windows PC: Unity Mac Build Support (Mono) installed, `rcodesign` 0.29.0
in `~/.local/bin` (fingerprint checked), `platform.sh sign-mac-app` uses it off a Mac, `build-game.sh --mac` switches
the editor to the macOS build profile and back. The Mac launcher's `launcher.txt` now lives in
`Contents/Resources` (it made the Mac call the app "damaged" in `Contents/MacOS`); `Patcher.cs` reads it there.

**Found by play-testing, fixed (2026-10-02, after the first commit):**
- **Shaders looked up by name were never in any build** — `InkScreen` (ink), `HandDrawnGrade` (paper/grade) and
  `AtlasRecolor` (character colours, `CharacterDye3D`) were neither in Always Included Shaders nor used by any shipped
  material, in any commit; every built game's `Player.log` said "Shader 'Unwritten/InkScreen' not found". Added all
  three to `GraphicsSettings.asset`; proven present in both new builds. The other 14 `Shader.Find` names were already
  covered. Memory: `shader-find-missing-in-builds`.
- **A server-side gold change never reached the screen** (`GmCommands.CmdGold` sent nothing). General fix:
  `PlayerFighter` subscribes the character's `OnGoldChanged` to `NetPlayer.PushBagToOwner`, which now only marks the
  bag owed; `SendOwedBag` sends it once per frame from `Update`, so the 14 existing explicit pushes and the new one
  coalesce.
- **Gear sent by the server was written in silently** (`Character.RestoreWornFrom` → `RestoreEquipment` fired no
  event), so `WeaponSocket`, the stat sheet and the bars never redrew. It now raises `OnStatsChanged` and
  `OnHealthChanged`, as `SetEquipped` does. No health clamp on the player's machine (that would be client maths).
- **Mac:** the game needs macOS's Local Network permission (it was on); every new signed version adds another
  "unwritten" line to that list because a certificate-free signature is identified by its fingerprint — harmless, no
  supported way to remove them; a real fix is an Apple developer account. The Mac game is now built through the
  macOS build profile, so it is Intel + Apple chip.
- **`tools/build-and-publish.sh`** — Windows game, Mac game, publish, in one command (`--dev`, `--server`). Written,
  syntax-checked, **never run end to end**.

**This PC's state:** `settings.env` (never committed) has `MAP_SERVER_ADDRESS=192.168.1.8`,
`PUBLIC_ACCOUNTS_URL`/`PUBLIC_CHAR_SERVER_URL` = `http://192.168.1.8:8080`/`8081`, `PATCH_URL=http://192.168.1.8:8090`.
Builds: `builds/client` (Windows, development), `builds/mac/client` (Mac, development, Intel + Apple chip),
`builds/server`, `builds/launcher/*`, published as version `2026.10.02-1221` in `builds/patch` (all git-ignored).
The website's Download page carries step-by-step Mac instructions (Open Anyway, the `xattr` line).

## Uncommitted working-tree changes

Everything up to the first push is in commit `b6a3ca18` on `main`. **Uncommitted at the end of the session**
(measured 13:01): `Character.cs`, `NetPlayer.cs`, `PlayerFighter.cs` (gold and gear fixes), `GraphicsSettings.asset`
(three shaders), `tools/build-and-publish.sh` (new), `tools/README.md`, the runbook, the networking notes, `ASSETS.md` (rule 12:
shaders looked up by name), two memory files, the hook tally.
Re-measure with `git status` before trusting this.

## What's left

1. **Re-play gold and weapon swap** on version 1221 (`/gold 1000` moves the bag and status bar; a weapon swap changes
   the weapon in hand).
2. **Ink outline in the world, built games only.** No cause found. Measured: the shader is in the build (the
   preview shows it); depth texture is on in every quality level (all use `UniversalRP.asset`); the ink quad is a
   separate root object on layer TransparentFX that `GameRoot.Quiet` switches off at sign-in and `Wake` / the
   component's own `LateUpdate` switch back on. Not yet known: whether it is missing on Windows too (asked, not
   answered), and what the running game shows. Next: ask that question, then look at a running Windows build.
3. **Play-test the rest of the batch** — outage, crash restart, item checks (see the plan's "expected result").
4. **Backups:** compress archived files, daily partial copies, alert, off-site (waits for hosting).
5. **Hosting / domain:** the front door, certificates, closing plain doors; the main website (it already
   lives in the update folder, so the same web server serves the site and the update files).

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

Ask whether the in-world ink is missing on Windows as well as the Mac; if on Windows, run the Windows build with
permission and read `HandDrawnFX`'s state in the world (instance, quad active, camera).

## How to resume

```text
Read handoffs/live-readiness-and-lan-downloads.md and docs/1-planning/live-readiness-fixes.md, then help me play-test the batch.
```
