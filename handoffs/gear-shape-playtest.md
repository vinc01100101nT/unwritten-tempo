# Handoff — play-test the gear-shape build of 2026-09-13

## Task

Play-test everything built on 2026-09-13 under the action plan's gear-shape task (`MT-3`, ON-GOING): the stat
jumps, melee vs ranged attack, creatures counting their gear's main stats, creature crits, the hit cues on every
unit, creatures' always-physical attacks, and per-copy armour rolling. Then close the task in
`docs/1-planning/action-plan.md`.

## Status

**IN PROGRESS — built, reviewed by reading, NOT PLAYED.** Every change compiles on all three builds
(`tools/check-unity-code.sh`), the pinned arithmetic passes (`tools/check-game-math.sh`), the performance scan and
the asset-wiring sweep are clean, and the editor recompiled with no errors. Four reviewer sub-agents read the code
in stages and every finding they raised is either fixed or listed under "Left as is" below. **Nothing has been
watched happening in a running game — treat every "works" below as a claim to be checked, not a fact.**

## What's done (all in `unity/Assets/Scripts/` unless said otherwise)

- **Stat jumps, shape C** — `UnitStatMath.StatJumpBonus` = ⌊stat/10⌋² + 3×⌊stat/10⌋. STR → melee attack, DEX →
  ranged attack, INT → magic attack, VIT → defence, AGI → evasion, LUK → crit damage (percent). Players and
  creatures. `NextJumpAt` / `JumpGainAt` feed the stat screen.
- **Melee attack / ranged attack** — `UnitStatMath.MeleeAttack` / `RangedAttack` / `PhysicalAttack(WeaponType…)`;
  `WeaponTypes.IsRanged` (Bow, Firearm) is the one test. `Character.Attack` picks by the held weapon (preview-aware
  in `DeriveWith`); `UnitStatBlock.Rederive` picks by the creature's worn weapon. `DerivedSnapshot` carries both.
- **Stat screen** (`ProgressionWindows`) — `MELEE ATK`, `RANGED ATK`, `M.ATK` on its own line, and `next +N at X`
  on each stat row (LUK shows `%`). Equip preview (`ItemTooltip`) shows both attacks.
- **Accessory places removed** — `EquipSlotType` 6 and 7 deleted (never reuse the numbers); gone from the equipment
  screen and the creature table. No item, save or prefab used them (measured).
- **Creatures count gear main stats** — `UnitStatInputs.WithGear`; `UnitStatBlock` feeds every formula with it.
- **Creature crits** — `MonsterAI3D.critChance/critMultiplier` stamped by `UnitStatBlock`; `RollBasicCrit` by
  the shared rule (`UnitStatMath.EffectiveCritChance`); a creature's crit ignores DEF (`Character.TakeDamage` passes
  `crit`); the player's LUK now shields (`Character.CritShieldLuk`). Skill crits also honour the target's LUK
  (`RollPower` takes `targetLuk`). "Always crits" effects still always crit (user's call).
- **Evasion no longer reduces magic damage** — `Combat.MagicGlanceMult` deleted with its two call sites.
- **Skills use their USER's numbers** — `SkillEffectApplier` takes `ICombatUnit caster` everywhere; no fallback to
  `Character.Instance`. New on `ICombatUnit`: `RingBonus`, `SkillRarityLift`, `SwingTime`, `RollSkillDamage`.
  `Rarities.LiftFor` is the one rarity-lift rule. Creatures still carry no skills (`Health.SkillRunners` null).
- **Hit cues owned by the unit hit** — `VfxCue.HitCue(isMagic, crit, attackerHit)`; `Character.LandOnMe` fires it
  for the player (normal / crit / skill, digits stay red); `BasicSwing` fires none (it used to double). Slow /
  Haste / Blind cues fire when that effect STARTS on anyone (`UnitStates.ApplySubStat` returns "started";
  `VfxCue.SubStatStartCue`). NOTE: OnSlow/OnHaste/OnBlind/OnDeath/DoT/Stun/Freeze/Silence have NO vfx bound in
  `VfxCueLibrary.asset` — bind one in the Vfx Cue Composer before expecting to see them.
- **Weapon tooltip** says `MELEE attack` or `RANGED attack`.
- **Corpse beats** — a multi-hit beat on an already-dead creature draws digits + the hit cue/sound and nothing
  else (`SkillEffectApplier.Strike` dead branch, `Health.ShowOverkillBeat`, `CombatAuthority.Land`
  `presentationOnly`, `ApplyHitToMob` skips non-damage effects on a corpse). A basic-attack-resolving strike that
  misses inside a flurry shows MISS and deals nothing (`Health.ApplyDamage` routes `missed` first).
- **GM/dev haste** goes through the sub-stat door; `/speed 1` still cancels it.
- **Creatures' ordinary attack is always PHYSICAL** — `MonsterAI3D.AttackType` / `attackType` and
  `ICombatUnit.BasicIsMagic` deleted everywhere; a creature holding a ranged weapon fires its projectile with the
  crit/miss on the payload (`Projectile3D.HitPayload`). Every creature was already physical (measured).
- **Armour rolling** — `ArmourRoll` (new): a Head/Armor/Garment/Shield copy rolls rarity (weapon odds), defence,
  magic defence and 0–6 distinct main stats inside ranges authored on `Item` (`defenseMin/Max`,
  `magicDefenseMin/Max`, `mainStatCountMin/Max`, `mainStatValueMin/Max`). `EquipmentInstance.rolled/rolledDefense/
  rolledMagicDefense/rolledStats`, `EncodeRoll/DecodeRoll` (8 ints or unrolled), `AddPieceInto` = the one sum
  (rarity lifts DEF/M.DEF by 0/3/9/18/30%, `Rarities.Lifted`; +0/1/2/3/5 per rolled stat, `Rarities.StatExtra`; a
  weapon's ATK/M.ATK lifted by the same ladder). `SaveData.CurrentVersion` 10 → 11 (`equipRolls`, `bagRolls`);
  `BagWire.rolls/wornRolls`. Creatures spawn wearing `CreateRolled` copies (so a creature's WEAPON now rolls a
  random rarity + kit per life too — user not yet asked), drop the exact copy they wore (`LootDropper.wonWorn` →
  `Rewards.PayLoot(winner, items, worn, gold)`), and re-seat fresh copies on respawn (`UnitEquipment.
  ReseatForNewLife`, called from `MobRespawn.Reactivate`). New characters get no starter armour. Tooltip and bag
  column read the copy (`AppendCopyStats`, `InventoryList.StatValue`); the item table's Armor tab edits the ranges.
- **Docs/wiki/plan/memory** updated in place for all of the above; `CLAUDE.md` gained two rules (no play-test
  checklist while anything is open; every reply names its sub-agents).

## Uncommitted working-tree changes (measured 2026-09-13 17:47)

Everything above is uncommitted on `main`: 75 modified files, 2 new (`ArmourRoll.cs` + `.meta`), and the user's own
deletion of the whole `src/` web prototype (18 files, deliberate). The session ends with a commit + push of all of
it, so if you are reading this after that, `git log -1` should show it.

## What's left

1. **The play-test** — the checklist is at the bottom. Rebuild the game server first; it decides every number here.
2. **Save `Scenes/town_0.unity` once** — it still holds `attackType: 0` on the Training_Dummy, so the editor will
   report unsaved changes on open. Saving settles it. (86 creature assets + 86 baked prefabs hold the same leftover
   but do not prompt.)
3. **Ask the user**: should a creature's WEAPON roll a random rarity and kit each life (current behaviour, follows
   "creatures get everything players get"), or always be Common with no kit? Today only the Axolot wears one.
4. **Author the first armour pieces** (`MT-7` in the plan) — no armour `Item` exists yet, so rolling has never
   produced a real piece. Ranges authored all-zero roll DEF 0 with `rolled = true`.
5. **Bind looks** for OnSlow / OnHaste / OnBlind in the Vfx Cue Composer, or those cues fire silently.
6. When everything passes, mark `MT-3` and its `ON-GOING` sub-tasks `[DONE]` in the action plan.

## Left as is (reviewed, judged minor — say so if you disagree)

- On a bystander's screen a corpse beat can count toward THEIR local hit streak for ~0.1 s until the server's
  "dead" arrives (client-only readout).
- A watched creature copy rolls its own local armour, so a hit's on-screen number uses the client's DEF roll, not
  the server's — invisible until creatures wear rolled armour.
- Bag "lift" column / Mob Table show a creature weapon's AUTHORED rarity, not the per-life random one.
- A bare armour `Item` tooltip header prints the authored rarity although copies roll theirs.
- One skill that both raises and lowers one stat shares one pile for both (user: intended).

## Dead ends / gotchas met this session

- **Proven:** `VfxCue.Fire` is a no-op on a joined client — every cue is drawn from the server's announcement.
  Client-side `Fire` calls added "for parity" draw nothing (three were added and then deleted).
- **Proven:** `CombatSfx`'s hit/crit/skillHit sets are EMPTY in `Systems.unity`; the sounds you hear ride the
  cue sequences (`on_hit_skill.asset` etc.).
- **Proven:** creatures have no skills, so "a creature's skill damage" paths are ready but unexercised.
- A first version of the Slow/Haste/Blind cue compared the unit's summed sub-stat total before/after — missed a
  slow landing on a hasted unit; replaced with a per-effect "started" flag.
- Reviewer sub-agents hit the account's session limit once (resets 8 pm Manila); one review was lost and rerun.

## Next single step

Rebuild `builds/server/unwritten-server.app`, restart the map server, then run checklist item 1.

## How to resume

`Read handoffs/gear-shape-playtest.md and continue from "Next single step". Play mode is the user's — hand them the checklist below, then close MT-3 in docs/1-planning/action-plan.md when it passes.`

---

## Play-test checklist

**Before you start:**
1. **Game server build** (`builds/server/unwritten-server.app`): **REBUILD** — every number below is decided there.
2. **Map server process: RESTART** after the rebuild.
3. **Character server: no restart** (it stores the save without reading it; `version` 11 passes its check).
4. **Account service: no restart.**

**Steps (what to press → what should happen → what counts as FAILURE):**
1. Press **C**. See `MELEE ATK`, `RANGED ATK`, `M.ATK` and `next +N at X` beside each stat. FAIL: old `ATK`
   line, or clipped text.
2. `/statpts 20`, then + on a stat sitting at 9. STR 9→10 raises MELEE ATK by 5. FAIL: +1 or +2.
3. Holding the sword, add 1 DEX: RANGED ATK +1, MELEE ATK +0 or +1. FAIL: MELEE ATK +1 every point.
4. `/item item.bow`, equip it, hit a creature, add DEX, hit again: hits grow. FAIL: only STR grows them.
5. Open the bag: no "Accessory 1 / 2" anywhere. FAIL: either appears.
6. Hover the sword → `MELEE attack`; the bow → `RANGED attack`. FAIL: line missing.
7. Fight a physical creature ~1 min: an occasional much bigger hit with the crit sound + effect on you, red digits.
   FAIL: all hits in one narrow band, or a big hit that looks/sounds ordinary.
8. Each normal hit on a creature shows its effect ONCE. FAIL: doubled effect.
9. Stack AGI, take a spell: same damage as at low AGI. FAIL: AGI shrinks spell damage.
10. Use sword skills on a creature: damage as before; a multi-hit skill's later beats on a corpse show digits only.
    FAIL: skill hits drop to 1, or a corpse beat applies a status/buff.
11. `/item item.sword` five times (armour has none authored yet — use the sword): hover each; rarity may differ,
    ATK = 3 lifted by rarity (3/3/3/4/4). FAIL: a Rare+ sword shows ATK 3 with no lift.
12. Once one armour piece is authored with ranges: grant it five times — copies differ; equip → the tooltip's DEF
    change equals the piece's DEF; log out/in → unchanged; drop and pick up → unchanged. FAIL: any re-roll or zero.
