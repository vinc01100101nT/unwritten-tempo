# Handoff — tutorials rework (built) and the gold economy (planning)

**Task:** two linked pieces from the 2026-10-07 session. (1) Tutorials: unlock conditions, free first runs, replays,
and a new healing tutorial — BUILT. (2) Gold: every creature drops gold, saved never-fights characters pay ×3, and
material/gear sale prices tied to farming time — PLANNED, a decision pending, no code. Design doc for (1):
`docs/2-systems/quests-and-tutorials.md` (rewritten in place this session).

Background that started it: play-tester "patches" (character `c44bd863-…`) played 16 minutes on 2026-10-06 23:34–23:50,
died 9 times, and mistook the blue mana bar over their head for health (respawn puts you on 10% HP). The fold bonus fix
for "creatures too strong" (every fold ×1.8) is DONE and COMMITTED (`eb7f694e`, `CraftingMath.cs` whole-hundredths fold
values) — not part of this handoff.

## Status

- **Tutorials: BUILT, NOT PLAY-TESTED, NOT COMMITTED.** Compiles (offline checker + editor recompile clean), checkers
  clean, verified in the live editor by calling `QuestLog` directly (see below). **Migration `011` NOT applied** to the
  live database — the character server will fail to save/read quest rows until it is. Don't trust "it works" until a
  play-test after the migration and restarts.
- **Gold economy: IN PROGRESS (planning).** Waiting on the user's pick of a gold curve. No gold code written.

## What's done (tutorials)

Decided by the user 2026-10-07:
- Every tutorial has an **Unlock Condition** (`QuestDef.unlockCondition`, enum `QuestUnlock { None, EnterWorld,
  ReturnToSavePoint }` — replaced the old `givenAutomatically` bool; both quest assets rewritten). The Tutorial tab lists
  EVERY tutorial from the start; a locked one shows "Unlock Condition: …" and no Play. A tutorial auto-plays the moment
  it unlocks (followed, taking over; the one it took over from is saved on its record as `resume` and followed again
  when it is done). Stop still works.
- **First run of a tutorial takes nothing its steps use up and hands out nothing.** Saved per record in a piece list
  (`QuestPieceWire`: `i` item, `c` the run's copy, `g` free forges, `n` free folds, `b` free purchases), used up by
  the press itself (`QuestLog.TakeFreeForge/TakeFreeFold/TakeFreeBuys`, called in `NetPlayer.CmdForge/CmdFold/CmdBuy`).
  Free lasts until first finished. Station rows/Fold button say "Free"; shop row says "N free, then Xg each" (server
  lists `QuestStore.FreeForges` / `FreeBuys`; the fold offer carries `foldFree`).
- **Replays:** Play on a finished tutorial replays from step 1, full bill, no reward again; holding steps count only
  copies made during the replay (`QuestLog.HeldFor`). DB keeps `finished_at` set; `replaying` column.
- **New tutorial `tutorial.heal-up` "Low on Health"** (`Resources/Quests/TutorialHealUp.asset`, title is Claude's
  wording — user may rename): unlocks in `GameNetwork.RespawnOnServer` ("Return to last saved point"). Step 1 Buy a
  Minor Healing Potion (new objective kind `Buy`, counted like Kill; free count = potions to reach 80% from the health
  you came back on, using the potion's written heal of 30 → 2 at level 1 in the +5 set). Step 2 `Health` ≥ 80% (new
  kind; judged when health changes via `NetPlayer.HealthMoved`, subscribed in `PlayerFighter`, only while
  `QuestLog.WatchesHealth`). New guide spots `OwnHealthBar` (framed with NO dark — `GuideOverlay` projects a
  world-space plate through the camera) and `ShopBuyButton`. New block `NoneLeft` ("buy more at a shop").
- **Starting kit is empty** (`StarterKit.cs`: no items, 0 gold). Existing quest rows get an empty piece list from the
  migration (they had the old kit).
- Shared character-build string bumped **v20 → v21** (`MapServerLink.cs`, `charserver/MapServers.cs`).
- Character server: `charserver/QuestRows.cs` validates/writes/reads `r`, `u`, `a`; migration
  `accounts/sql/011_character_quest_runs.sql` adds `replaying`, `resume_quest`, `pieces`.
- Checkers: `tools/check-quest-data.py` (unlock condition, Buy/Health rules, shop spot; enum parser now strips comments
  before splitting) and `tools/prove-quest-data-check.py` (fold case found by kind; 3 new planted cases) — prover passes.
  `tools/game-math-tests/Checks.cs` pins `HealthMet/HealthNeeded/UsesToReach/BuyBill/HealOfOneUse`.
- Docs rewritten in place: quests-and-tutorials, implementation-plan item 8, roadmap quest count, game-log buy line,
  docs index.
- Live-editor check done: all 3 quests load with no fault; potion heal 30; `Unlock(ReturnToSavePoint, 7, 76)` → 2 free
  potions; free forge once then refused; free fold only on the run's copy; "Buy 10" with 2 free → 2 free; saved rows
  carry the new fields.

## Uncommitted working-tree changes (measured 2026-10-07 22:22 GMT+8)

Modified: `charserver/MapServers.cs`, `charserver/QuestRows.cs`, `docs/0-process/reading-the-game-log.md`,
`docs/1-planning/roadmap-to-launch.md`, `docs/2-systems/gameplay-system-v4/implementation-plan.md`,
`docs/2-systems/quests-and-tutorials.md`, `docs/README.md`, `tools/check-quest-data.py`,
`tools/game-math-tests/Checks.cs`, `tools/prove-quest-data-check.py`,
`unity/Assets/3_Data/Resources/Quests/{SideLastlinePloplings,TutorialFirstGear}.asset`, and in `unity/Assets/Scripts/`:
`Character.cs CharacterPlates.cs CraftStationOffer.cs CraftStationPanel.cs GameNetwork.cs GuideMarker.cs
GuideOverlay.cs MapServerLink.cs NetPlayer.cs PlayerFighter.cs QuestCatalog.cs QuestDef.cs QuestLog.cs QuestMath.cs
QuestStore.cs QuestsWindow.cs ShopPanel.cs StarterKit.cs UnitPlate.cs WorldShops.cs`.
Untracked: `accounts/sql/011_character_quest_runs.sql`, `unity/Assets/3_Data/Resources/Quests/TutorialHealUp.asset`
(+ `.meta`). 33 files, +1140 / −213. All of it is this session's tutorial work.

## What's left

**Tutorials (before a play-test):**
1. Apply the migration — `cd accounts && npm run migrate` (ASK the user first: it writes their live DB).
2. User restarts the character server, rebuilds the game server build, restarts the map server (v21 on both sides).
3. Play-test checklist only once the gold work is also done (project rule: one checklist when nothing is left open).
   Unverified: the frame position over the health bar on screen; the full save → DB → reload of the new fields.

**Gold economy (planning → build):**
- **Pending user decision: the gold-per-kill curve.** The user rejected gold following kill experience (level 99 would
  pay 110,030). Offered, each ±20% roll, elite ×10 / shadow ×20, from spawn level: `10 × level` (Claude's pick:
  990 at 99, an A-grade quench = 3.5 kills), `10 × level^1.25` (3,123 at 99), `10 × level^1.5` (9,850 at 99) — or fit a
  curve to a target the user names. Also still unanswered: should a creature that levels up mid-fight (from damage it
  deals) heal to full (`MonsterAI3D.GainCombatXp`)?
- Settled for the plan (user or red team): saved never-fights pays 3× its creature's gold roll ON TOP of the kill gold,
  replacing tiers 20/40/60 (remove `GoldTier`, `MonsterDef.goldMin/goldMax` — warn about 98 creature + 91 saved-
  character assets carrying the old values); gold priced by SPAWN level (creature level grows uncapped from hits);
  material price = 1/7 × gold-per-hour × hours to farm one (7 drop rolls per normal kill), merged Ink/Salt tiers capped
  at 10× the tier below; crafted gear sells for 1/7 of its provable bill via ONE function shared with recycling (sent
  per copy by the server); free tutorial gear sells and recycles normally (user: "it already consumed their time");
  shop sale price stays half the buy price; purse widened to 64-bit everywhere with no `(int)` casts (incl.
  `QuestMath.CanPay`, `NetPlayer` sell's float step); a purse-only message instead of a whole-bag push per kill.
- Red team (2026-10-07) also flagged: rebels (~6.2% of normal kills) drop elite loot and pay elite gold — the price
  simulation must include them; gold sinks (A fold 350, A quench 3,500, forge ≤800) are small.
- After the user picks: resume the planner (agent `a2ba10f…`, it holds the fight/farming simulation) to recompute
  prices with rebels + merge cap + chosen curve, then the red team (agent `ae05dd8…`) attacks it, THEN build. Resume
  agents, never respawn fresh (global rule).

## Dead ends / proven facts

- Proven: respawn health is 10% of max (`SavePoint.RespawnHealth`); a Minor Healing Potion's skill heals a flat 30;
  kill experience = `0.31 × L² × e^(0.02L)` (min 2), level cost = `6.25 × L^1.23 × e^(0.098L + 0.021 × max(0,L−90)²)`;
  reaching 99 = 409,648,693 experience = 26,842 same-level kills.
- Dead end: a creature sub-stat scaler (divide creature stats by 3) was planned then dropped by the user in favour of
  fold ×1.8.
- Gotcha: Python `open().read()` + `write(newline='')` silently converted two CRLF files to LF; restored. Check line
  endings after scripted edits.
- The dead-while-saved path (sign out from the death window) deliberately does NOT unlock the healing tutorial — it
  unlocks on the next real "Return to last saved point".

## Next single step

Ask the user which gold curve (or a level-99 target), then resume the planner with it.

## How to resume

```text
Read handoffs/tutorials-and-gold-economy.md and continue: get the user's gold-curve pick, resume the planner and red team for the gold plan, and remind the user the tutorial migration (accounts/sql/011) is unapplied and nothing is committed.
```
