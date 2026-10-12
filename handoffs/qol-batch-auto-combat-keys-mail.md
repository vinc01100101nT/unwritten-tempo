# Handoff — QoL batch: auto-combat (built, not compiled), key remapping, mail

**Task:** the user's QoL to-do list from the 2026-10-11 play-test session. Built 2026-10-11/12: buyback, combo +
EXP/zeny per hour + Combat Log tab, item rarity, Keeper (save point / storage / teleport) + Butterfly/Fly Wing, plus
fixes from five red-team passes, and now auto-combat. Left: **remap every key**, **mail**, then one play-test.

**Status:** IN PROGRESS. Auto-combat was written on 2026-10-12 in a CLOUD session with **no Unity** — the game C# has
never been compiled (only syntax-checked with a C# parser). The character server WAS built (`dotnet build`, clean) and
its auto-combat door was run end to end against a throwaway postgres (all checks passed). Nothing in this batch has
been PLAYED. **Don't trust prior "it works" claims** for anything not seen in Play.

## What's done

- **Buyback, kill combo/rates/Combat Log, rarity, Keeper + wings, see-through blue shape, floating words on top** —
  as described in `.claude/memory/playtest-qol-batch-2026-10-11.md`, `docs/2-systems/keeper-and-wings.md`, and the
  commit "handoff from auto combat builder".
- **Auto-combat** — design and code map in `docs/2-systems/auto-combat.md`. Files: `AutoCombatSettings.cs` (settings +
  `AutoCombatMath`), `NetPlayer.AutoCombat.cs` (server on/off, settings command, row save, auto-potion),
  `AutoCombatClient.cs`, `Player3DController.AutoCombat.cs` (hunt modes, auto-skills), `AutoCombatWindow.cs`; edits in
  `Character` (AutoCombat, KeepOnlyAutoSkills, UsableSkills), `GameNetwork` (Owns counts auto slots;
  `UseItemOnServer` lifted out of the item-use handler), `MapServerLink` + `PlayerFighter` (row arrives with the
  character), `SaveOutbox` (third document), `NetPlayer` (partial; save waits for three rows; doors refused while on;
  every trip stops it), `Rewards`/`LootDropper` (per-entry auto-loot with drop chances), `SkillRunner.ActivateAt`,
  `IHotbarSkill.cs` (`HotbarReady`), `Portal3D`, `AutoPilotIndicator` (tag AUTO-COMBAT), `SystemWindow`,
  `HotbarPanel`; charserver `CharacterRows.cs` etc. (the earlier sub-agent's half). Build string
  `unwritten-character-v24` on both servers.
- **Removed field:** `Player3DController.roamRange` — its saved value is in `unity/Assets/Scenes/Systems.unity`, so
  that scene reports unsaved changes once; saving it once settles it.

## Uncommitted working-tree changes

None — everything is committed on branch `claude/peaceful-cannon-p3gm6e` (re-measure with `git status`).

## What's left

1. **Compile on Windows:** `tools/check-unity-code.sh`, then read the console. Expect a few errors in the auto-combat
   files — fix them there.
2. **Remap every key** — original list item 20, never answered by the user; decision: inside the existing Settings
   window, every key in `GameKeys` remappable, saved per account/character via the existing settings path. Design it,
   then build.
3. **Mail** — original item 22, never answered; needs a plan (server-saved). Decide: send items + gold to a character
   by name, collected at any Keeper; plan first, red-team it.
4. Not doable without Play: item 3 (snow/rain FPS measurement) — ask the user for Play mode.
5. Then ONE play-test checklist for the whole batch (rules: rebuild/restart table first).

## Manual steps the user must do before playing

1. `cd accounts` then `npm run migrate` (applies 012 and 013).
2. Restart the character server (v24; storage door; auto-combat door).
3. Rebuild the game server build, restart the map server(s) — v24 refuses a v23 character server and back.
4. Rebuild the client. Save `Systems.unity` once when Unity reports it changed (the removed `roamRange`).

## Next single step

`tools/check-unity-code.sh` on the Windows machine.

## How to resume

```text
Read handoffs/qol-batch-auto-combat-keys-mail.md and continue: compile-check the auto-combat build and fix any errors, then key remapping and mail. One agent at a time, or work in the main session.
```
