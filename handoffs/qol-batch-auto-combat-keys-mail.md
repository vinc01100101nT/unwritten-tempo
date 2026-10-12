# Handoff — QoL batch: auto-combat window (in progress), key remapping, mail

**Task:** the user's QoL to-do list from the 2026-10-11 play-test session. Built this session (2026-10-11/12):
buyback, combo + EXP/zeny per hour + Combat Log tab, item rarity, Keeper (save point / storage / teleport) +
Butterfly/Fly Wing, plus fixes from five red-team passes. Left: the **auto-combat window** (mid-build when the usage
limit ended the session), **remap every key**, **mail**.

**Status:** IN PROGRESS. Nothing in this batch has been PLAYED — every feature is compiled and checked offline only.
**Don't trust prior "it works" claims** for anything not seen in Play. The auto-combat build was STOPPED mid-way by
the usage limit: its partial edits are in the tree and were NOT reviewed (see below).

## What's done (all uncommitted)

- **Buyback** — `ShopMath.cs`, `BuybackList.cs` (SoldEntry/SoldLine), `NetPlayer` CmdSell(+expectedCopyId)/CmdBuyBack,
  `BagWire.buyback`, `SaveData.buyback` (SoldItem[]), `ShopPanel` "Buy back" rows (minutes rewritten in place,
  `keptRows`/`RetimeKept`). Shared gear conversion `EquipmentInstance.ToSaved/FromSaved` now used by bag save, worn
  save, bag message, buyback, storage. Early-bag parking keeps bar + buyback (`TargetBagChanged`).
- **Kill combo / rates / Combat Log** — `KillCombo*.cs` (steady clock `KillCombo.Now`), `Rewards.PayForKill`,
  `SavedCharacters` rescue pay, `GameNetwork` KillComboShown + `TellOwnerTheirRatesIfMoved` (once a minute from
  `NetPlayer.PushMyHealth`), `ChatController` CombatLog tab (excluded from All; drawn only while open, ≤4×/s),
  `KillComboHud` (left edge).
- **Rarity** — `Rarity.cs`; forge two-step roll (`CraftingMath` shares 50/30/14/5/1, `Trademarks.Roll`);
  `SkillDef.trademarkRarity/Set`; 15 sword skill assets set; materials by kind+step; server sends rarity per copy
  (`EquipmentInstance.rarity/raritySent`, `BagWire.rarities/wornRarities`, `SoldLine.rarity`, storage rows);
  `WornList.Decode` accepts ≥3 fields; floor label readable on hover (`GroundDrop.SetPlateFill`, `UiKit.Readable(c,bg)`).
- **Keeper + wings** — Keeper row in all 16 towns (`WorldTownsfolk.asset`), `KeeperPanel` (on new `UiListView`),
  `KeeperMath`, `StoredItems`, `StorageMoves`, `charserver/Storage.cs` + `Program.cs`, migration
  `accounts/sql/012_account_storage.sql`, `charserver/test-storage.sh` (throwaway DB, passes). Skill effects 25/26,
  Butterfly/Fly Wing items in the Tool Dealer. Server decides the map (`ChangeMapOnServer`, CmdReportMap only
  confirms), server-ordered trips are expected arrivals (`ExpectArrival`), door check in CmdWalkThroughPortal (GMs let
  through), Butterfly Wing refund, save point at a Keeper stores the town (`savePointAtKeeper`).
- **Earlier this session** — creatures in a fight with you show as the blue see-through shape for 10 s
  (`NetMob.TradedABlowWithMe`, `OccludedSilhouette.shader` clips on the sprite); floating words always on top
  (`UiKit.DrawOverTheWorld`, TMP Overlay shader added to Always Included Shaders).
- Shared build string `unwritten-character-v23` on both servers.

## Auto-combat build — PARTIAL, NOT REVIEWED

Stopped by the usage limit before it could report. Its last words: "The character server won't build until its
announce function takes the new rows, so I'm finishing that one change first" — so **the CHARACTER SERVER is likely
mid-change and may not build** (build it first: see charserver/ README). The game C# compiled clean right after the
stop (checked 12:15). Assume NOTHING else about which steps are done. Evidence it started: `accounts/sql/013_character_auto_combat.sql` exists
(untracked). **First move next session: run `tools/check-unity-code.sh`, then `git diff --stat` and read the diff of
the auto-combat files** (likely Player3DController, NetPlayer, GameNetwork, LootDropper, Rewards, SettingsWindow,
DropAmountPrompt, HotbarPanel/HotbarDragBind/HotbarBindings, UiKit, Portal3D, MonsterAI3D, SkillRunner, Character,
SaveOutbox, charserver, a new window script) to see which plan steps landed.

The full plan with every red-team fix folded in (steps A–H: one owner for "on"; portal refusal; hunting modes with
anchor and server-gated Free Roam ask; auto-skills via one lifted readiness test; auto-loot at the fall for a present
killer, decided per won entry; auto-potion on the server ≤1/s per resource, never two MP draughts; settings row via
migration 013 carried like the hotbar row with SaveNow/SaveOutbox waiting for three rows; window "Auto-Combat" from
lifted Settings/drop-prompt/hotbar pieces, tag renamed AUTO-COMBAT) is the brief in this session's transcript —
re-derive from this summary if the transcript is gone. Decisions already taken: wings and Keeper teleport are NOT
refused while on (they switch it off via MoveToMap); Fly Wing keeps it on and moves the Radius anchor; Radius = start
within 10, keep to 14; auto-skills aim at the current target; slots take skills only; defaults all off + Radius;
relog = off.

## Uncommitted working-tree changes (measured 2026-10-12 ~12:20)

Large: ~60 modified scripts under `unity/Assets/Scripts` (many also hold the user's OWN earlier uncommitted work —
never discard), new scripts listed above, two untracked migrations (012, 013), charserver edits, many docs/wiki/
memory edits, `GraphicsSettings.asset`. Re-measure with `git status` before acting.

## What's left

1. Finish auto-combat (from the diff + plan above), checkers, red-team only if the user asks (user is token-budget
   conscious: one agent at a time; prefer doing it in the main session).
2. **Remap every key** — original list item 20, never answered by the user; my decision: inside the existing
   Settings window, every key in `GameKeys` remappable, saved per account/character via the existing settings path.
   Design it, then build.
3. **Mail** — original item 22, never answered; needs a plan (server-saved). Decide: send items + gold to a character
   by name, collected at any Keeper; plan first.
4. Not doable without Play: item 3 (snow/rain FPS measurement) — ask the user for Play mode.
5. Then ONE play-test checklist for the whole batch (rules: rebuild/restart table first).

## Manual steps the user must do before playing

1. `cd accounts` then `npm run migrate` (applies 012, and 013 once auto-combat is finished).
2. Restart the character server (announces the map now; storage door).
3. Rebuild the game server build, restart the map server(s).
4. Rebuild the client (new synced values, save fields, windows).

## Next single step

`tools/check-unity-code.sh` — if it fails, the auto-combat partial edit is the cause; read its diff and finish or
repair from there.

## How to resume

```text
Read handoffs/qol-batch-auto-combat-keys-mail.md and continue: first check the auto-combat partial build compiles and read its diff, then finish it, then key remapping and mail. One agent at a time, or work in the main session.
```
