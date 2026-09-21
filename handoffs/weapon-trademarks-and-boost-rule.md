# Handoff — weapon trademarks, and the one "what can be boosted" rule

**Task:** design forged-weapon names that empower one "trademark" skill, and make the Skill Composer and the
fight agree on which skill numbers can be boosted. Design doc:
`docs/2-systems/gameplay-system-v4/weapon-names-and-trademarks.md` (PROPOSAL, nothing of trademarks built).

**Status: TRADEMARKS BUILT 2026-09-21 (not play-tested; play-test steps are section 22 of the not-done-yet checklist). One decision still pending from the user (below).**
Everything below that says "built" compiled and was replayed against saved data; NONE of it has been watched in
play. Don't trust prior "it works" claims.

## The pending decision — ask this first

The V4 design gives PASSIVES a practice lift from Weapon Mastery:
`docs/2-systems/gameplay-system-v4/README.md` line ~216 (§7) and ~278 (§9): *"A passive's output lift = Weapon
Mastery level × 0.5%, capped at +30%."* It is live in `Character.SkillOutputLift` (passive branch →
`MasteryMath.PassiveLift(WeaponMasteryLevel)`).

The user's model: **Weapon Mastery only raises its weapon's ring stats (+0.5%/level to three stats) and unlocks
skills — it does NOT lift skill output.** They were angry to see "raised by Weapon Mastery" on passives in the
composer. Offered: "say *remove it*". If they say remove it:
- `Character.SkillOutputLift`: passive → 0 (and the creature twin in `Health.cs`, same hook).
- `EffectScaling`/composer: the green line for passives disappears (`SkillComposerWindow.DrawPracticeLine`
  currently prints "raised by Weapon Mastery" when `sel.IsPassive`).
- Delete the two design lines above; `MasteryMath.PassiveLift` becomes dead — remove it.
- This CHANGES live combat (passive output drops). Say so.

## What's done (uncommitted, see below)

- **Boost rule, single source (user chose "option 2", then "every boostable number gets a boost row").**
  `EffectScaling` in `unity/Assets/Scripts/SkillEffect.cs`: `Boostable(e)` is THE list (Damage, Execute, Heal,
  Shield, DoT Status, SubStatBuff, SubStatDebuff, BoostOutgoingDamage, ReduceOutgoingDamage, AddStacks);
  `OutputRingRow` (label "boosts " + field name), (`PracticeRaisesOutput` deleted — practice now lifts every `Boostable` main number, AddStacks' stacks included),
  `ChanceRingRow`, `DurationRingRow`, `HitsRingRow`, `HasDuration`. Read by `SkillEffectApplier`
  (`OutputScale`, `RingOutput/Chance/Duration/Hits`), `SkillTooltip.LiveMult`, and `SkillComposerWindow`
  (its private `AffinityOutputLabel`/`HasDurationChannel` deleted). `StatusController.IsDot` made public.
  Replayed 57 saved effects: 0 combat numbers moved.
- **Station window:** menu → Forge/Fold/Quench/Ink/Recycle pages, scrolls (`UiKit.ScrollColumn`, now shared by
  Grimoire + skill draft too), every press answers in chat + window, fold-cap gates quench, recycle refunds
  climbed rungs' folds. `CraftStationPanel.cs`, `NetPlayer.cs`, `CraftingMath.cs`, `CraftStationOffer.cs`.
- **Bag Grade column removed** (`InventoryList.cs`); grade still searchable.
- **Wiki** rewritten to V4 (crafting/weapons/skills/stats/home); **docs**: crafting-tables, V4 README/plan,
  balance-baseline scaling table, trademark proposal.

## Sword trademarks — BUILT (18), names saved on each skill

Rule (user, 2026-09-21, no exception): a skill with a main number on ANY effect (a kind in `Boostable`, AddStacks
included) is a trademark, automatically. Skill Mastery and trademark lift main numbers only, every one.
Lunge Tsuki · Momentum Hyōshi · Guard Stance Kamae · Lock Blades Tsubazeriai · Composure Heijō · Offbeat Kuzushi ·
Flow Nagare · Disarm Kote · Onslaught Renzoku · Opening Suki · Committed Strike Ichigeki · Pressure Seme ·
Overclock Hadori · Last Stand Haisui · Encore Futatabi · ULTs Deathblow Todome, Retribution Kaeshi, Blade Dance
Ranbu. Not trademarks: Parry, Brace, Challenge, Unwavering (no main number).
Format `+15 Hadori [C]`; name rolled at forge, never changes; weights 15×5.7% / 3 ults×5%; lift by grade +5% Raw
→ +20% A. Built as described in the design doc §6: name on the skill (`trademarkName`), eligibility read live
(`Trademarks`), roll at the forge over PUBLISHED skills, copy stores skill id + server-written lift, save v14.

## Open defects (not fixed, user's call)

- None. (Overclock's drawback being lifted too is the user's rule: every main number, no exception.)

## Uncommitted working tree (measured 2026-09-21 11:37)

36 entries. Mine: the code/doc/wiki files above, `tools/ui-tokens.py`, `.claude/memory/index/hud-ui.md` +
`ui-shell-rebuild-bloated-the-scene.md`, four learnings pages/edits, `docs/README.md`,
`play-test-checklist/not-done-yet.md`. **NOT mine — leave alone:** `skill.sword.blade_dance.asset`,
`skill.sword.parry.asset` (edited outside this session), `Circle2.png.meta` and `ScriptableBuildPipeline.json`
(Unity rewrites these itself), `tools/hook-analytics/hook-analytics.jsonl`.
Game server needs a rebuild for the station/quench/recycle changes to take effect.

## Dead ends / lessons already paid for

- Decoding `SkillEffectKind` by counting enum members broke on an in-enum comment → every kind ≥14 off by one;
  gave wrong trademark verdicts (Opening, Unwavering, Pressure). Decode by stripping `//` per line, and cross-check
  against each skill's description.
- Said "aligned" after reading only the fight code; the composer had its own list. Always check every reader.
- Never recompile while `editor_status.playMode != stopped`.

## Next single step

Ask the user: **remove the passive Weapon-Mastery lift, yes or no?** Then commit (ask first). The game server needs
a rebuild and both servers a restart (save/build string moved to v14).

## How to resume

Paste: `Read handoffs/weapon-trademarks-and-boost-rule.md and continue from "The pending decision".`
