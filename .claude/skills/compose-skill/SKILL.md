---
name: compose-skill
description: Use when brainstorming, designing, composing, authoring, editing or reviewing a game SKILL for this project — a weapon's whole pool of new skills, a new active or passive, a change to an existing skill's effects/cost/cooldown/triggers, or anything touching the Skill Composer or the skill .asset files under Resources/Skills. Covers the design rules for a new pool (Kashira naming, lanes, ultimate builds, no trash passives, points budget, caps), which side an effect lands on, the pricing rules, and the mandatory verification pass. A skill's look (VFX) is the linked skill-vfx skill.
---

# Composing a skill

A skill is a `SkillDef` asset in `unity/Assets/3_Data/Resources/Skills/`. Every skill in that folder is loaded at runtime, so an asset saved there is live — there is no separate registration step.

**Nothing in this file is a memorized value.** Every enum below is serialized BY INT and positional, so read the real list before you type a number:

```bash
grep -n "enum SkillEffectKind" -A 200 unity/Assets/Scripts/SkillEffect.cs   # kinds, in order
grep -n "enum SubStat" -A 30 unity/Assets/Scripts/SkillEffect.cs            # sub-stats, in order
grep -n "enum SkillTrigger" -A 60 unity/Assets/Scripts/SkillDef.cs          # triggers, in order
grep -n "enum StateId" -A 60 unity/Assets/Scripts/UnitStates.cs             # piles, in order
```

## 0. Brainstorming a weapon's pool — the rules to design inside

Learned designing the Bow's pool with the user (2026-10-21). Read these before proposing a single skill.

- **Names come from the Kashira's style picture** (`docs/3-world/story-setting-canon.md`, The Fourteen Kashira):
  every skill is a numbered form of that style, built only from its picture (the Bow's Passing Cloud = cloud, sky,
  rain and the weather that moves them). Real English, nature only, never a material or a machine. **No name may repeat
  another weapon's skill, a creature's name, a Kashira's style name (Wildfire is the Axe's) or an item's name (Quench
  Oil)** — grep `data docs wiki/content unity/Assets/3_Data` for each one. A name also becomes a weapon's name when it
  rolls as a trademark, so it must read as one.
- **Elements combine by the chart** (`Element.cs`, level 1): Fire beats Earth, Earth beats Wind, Wind beats Water,
  Water beats Fire; Fire–Wind and Earth–Water are neutral. A skill mixing two elements must make sense on it — never
  burn and freeze on one target (water puts out fire). Each element leaves one mark and **only the element that beats
  a mark cashes it in** (the Staff: Water puts out Burn, Wind scatters Chill, Earth grounds Static, Fire melts Stone).
- **The boring test — every skill gets at least one yes, an ultimate two:** does it READ the fight (the target's
  state, crowd, your piles), LEAVE something another skill uses (a mark, a pile, or a stat another skill reads), make
  you CHOOSE when (now or wait, cash in or let it run — aiming a shot does not count), or PAY OFF at an end (breaks,
  expires, kills)? Write "reads: ___ / leaves: ___" under each idea; both blank = redesign it. **A read must vary
  between casts:** "makes a mark, then spends it" in one cast, or a Staff combination that reads the orbs it was cast
  from, gives the same output every time and fails the test (both proposed and rejected on 2026-10-22).
- **Audit the pool yourself before showing it:** two skills doing one job (count the skills per job — more than two is
  a duplicate), a part that changes nothing, and whether the build that MAXES a skill can trigger its main mechanic on
  its own (a pure-Earth build has no water to freeze with). Fix, then present.
- **A level only grows OUTPUT numbers** — damage, a damage-over-time's damage, a sub-stat change, a charge's cut. It
  never grows a duration, a range or a size, so "this part grows its range" is a part that does nothing.
- **Any hit that costs health breaks a freeze** (damage over time does not) — never design a skill that freezes and
  then hits again; a run of beats chills to 2 at most. Every mark and stack needs a written cap.
- **Every skill keeps to the weapon's job** (the identity sentence in `weapon-roster-12-identities.md`) **and stays out
  of other weapons' lanes**: stun and knockback are the Warhammer's, pull the Spear's, taunt the Fist's, slowing a whole
  area the Staff's (same doc, §3–4).
- **Three ultimates, each defining a separate build** — ideally one per mastery stat of the weapon (the Bow: attack
  speed → Cloudburst, evasion → Wandering Sky, accuracy → Thunderhead). Mix their shapes: one or two may grow from a
  stack pile, never all three. Each ultimate gets one feeder that specialises in it while still helping the other two.
- **No trash passives.** A passive that fires on a general action (a basic attack landing, a kill, taking damage) and
  only builds a pile with no effect of its own is trash. Either give the pile its own effect (Gathering Clouds: +1%
  accuracy per stack) or tie the passive to one named skill (Petrichor: kills made by Cloudburst).
- **A passive answers a MOMENT that varies with the fight, never a fixed event.** "When you cast Cloudburst, its rain
  lasts 5 s longer" fires every cast, the same every time, so it is just a bigger Cloudburst wearing a passive's name —
  fold it into the skill (a longer base, or growth per level) instead. The test: *does how often it fires, or how much
  it gives, depend on what happens in the fight* (hits landed, enemies in reach, kills, dodges, health)? No ⇒ it is not a
  passive. The same goes for rebalancing: never fix a weak passive by moving it onto its skill's own cast (rejected
  2026-10-24).
- **Points budget:** a weapon gives 70 mastery points; the pool's max levels should total about twice that (~50%; the
  Sword is 54%). Max level per skill is the `skill-tree` skill's 10 / 5 / 1 rule — work it out while brainstorming,
  because a short pool shows up here first.
- **Caps, summed at the top.** Every power source has a cap. Add up every buff on a capped stat at max level with the
  best trademark (+5% per level after the first, up to +20% from an A-grade trademark, one trademark per weapon) plus
  the existing gear and consumables, and keep the total under the cap — speed under 250%, slows inside the 30% floor.
  A buff past the cap is wasted mana, time and points.
- **Ask what the user's words mean** when they reuse one of yours ("volley", "set") — see
  `docs/5-ai-learnings/the-users-word-is-not-my-word.md`. And list the ideas before writing numbers; the numbers
  are a separate pass the user asks for.

### What already exists — reuse it, never build a second one

Added for the Bow (2026-10-21). Each is a field or kind you author; read its tooltip before using it.

- **A new weapon needs its colour saved** in `Resources/WeaponColors.asset` (the colour picker on its group in the
  Skill Composer). Without it every tile of that weapon is grey in game.
- **Triggers:** `OnDodge` — an attack aimed at you missed (Slipstream, Wandering Sky's counter). `OnKill` with
  `triggerSkillId` = kills made by that skill only (Petrichor, Second Bloom).
- **Charges:** `BoostBasicAttackDamage` — the next N basic attacks hit harder, never a skill strike (Rain Shadow);
  `SureHit` — the next basic attack cannot miss (Slipstream). **Modifier:** `ResetCooldown` of a named skill (Fair Weather).
- **Damage growth on a strike:** `damageMultPerAccuracyOver` (hit chance above 100%, at most 100 points —
  Thunderhead), `damageMultPerSpeedBonus` (movement-speed bonus, moving or standing still — Silver Lining), `critChanceBonus` (this
  strike only — Hailstone), plus the older per-metre and per-point-given-up.
- **Delivery:** ground `AroundCaster` — a splash centred on the caster, no aim, follows them each beat (Cloudburst).
- **Runs:** `repeatWhilePileHeld` + `repeatMaxSeconds` — a run that lasts while its own pile is held, so an Extend
  Duration lengthens it, under a ceiling (Cloudburst + Petrichor); `repeatBonusSkillId` — when the run ends, read a
  pile ONCE, spend it, play one extra beat per stack (it never refills itself). `showRepeatsTotalDamage` totals the
  whole cast (a new cast starts from zero); `repeatTotalPerBeat` totals each beat alone.
- **Skill flags:** `statesEndWhenHit` — every pile the skill gives ends when a hit costs health (Wandering Sky);
  `companionOf` — a hidden skill learned with, and at the level of, another (the counter-arrow, for a moment the
  parent's body cannot answer); `weaponSkillAnimation` — the skill plays one of the held weapon's two skill animations (First / Second).
- **Effects:** a Carrier step can `carrierDrawsArrow` (the bow's arrow model) and `carrierArc` with a launch angle.

Added for the Staff (2026-10-22):

- **Orbs and combinations:** `freeFirstLevel` (known at level 1 by everyone — the orbs and Release), `orbCombo`
  (three letters F/E/W/A — a combination cast through Release, max level 0, never learned), `isOrbRelease`. Effect
  kind `AddOrb` with `orb` = which orb. On a combination's effect, `orb` = whose level lifts that part.
- **Marks:** effect kinds `Chill` and `Static` (stacks-per-apply; `magnitude` = each stack's slow / attack-speed cut,
  grown by the effect's orb; `stackLimit` caps Chill below the freeze), `CopyMark` (`markKind` Chill or Burn,
  `copyRadius`; never past the source's own count), `ExtendMarks` (`markKind`; `seconds` longer, `magnitude` stronger).
  Stone = `ReduceOutgoingDamage` from a Staff skill (capped 5 charges, 30%). Every mark turns its unit's armour into
  its element at level 1 (Burn Fire, Chill/freeze Water, Static Wind, Stone Earth; the newest mark wins). A freeze lasts
  10s and any hit that costs health breaks it.
- **Cash-ins on a Damage effect:** `cashMark` + `damageMultPerMark` (Burn, Chill, Static, Stone), `burnPayoff` (Burn);
  `keepsMark` counts the mark without removing it (the ultimates). A part with `damageMult` 0 is a payoff only: with no
  mark it lands no blow at all.
- **Gate on any effect:** `onlyIfMarked` — lands only on units that carried that mark when the tick reached them (read
  before the tick's cash-in ran); `stackPerMark` — one stack per mark carried. `ResetCooldown` +
  `resetsLastCombination` resets the last combination cast, ultimates and their shared wait included (Weathering).
- **None of these Staff fields has a Skill Composer control yet** — author them in the asset files.
- **Growth on a strike:** `critChanceBonusVsSlowed`, `damageMultPerBurnSecond` (at most 10s),
  `damageMultPerMaxMana` (at most +2).
- **Runs:** `repeatScaleWithCastSpeed` + `repeatMaxCount` — more beats the faster you cast, under a ceiling.
- **Rebuilding onto an existing skill asset keeps every field you did not write** — reset them all (the old Meteor
  kept its orange name colour). See `docs/5-ai-learnings/rebuilding-onto-an-existing-asset-keeps-its-old-values.md`.

Added 2026-10-24:

- **Heal as a share of max HP:** `healShareOfMaxHP` on a Heal effect makes its amount a share of the healed unit's
  max HP (0.1 = 10%) instead of flat HP (Still Air). Use it for any heal meant to matter past the first levels —
  a flat heal, regen or burn is worth less every level the character gains.

### Many hits — two different things, never confuse them

- **`hitCount` above 1 on a Damage effect = a MULTI-HIT, worked out ONCE per target**: one roll, one blow through
  defence — so one parry charge, one brace charge or one block stops the whole of it — shown as that many pieces with
  one rising total. Even with `resolveAsBasicAttack` it is ONE swing: one accuracy roll, one crit roll, one charge.
  Chance effects still roll once per hit.
- **A REPEAT (`repeatCount`, `repeatScaleWithAttackSpeed`, a pile-held run) = separate strikes**, each its own roll,
  its own defence, its own parry. With `resolveAsBasicAttack` every beat is a real basic attack: its own accuracy,
  crit and damage, and it fires every "basic attack lands" passive (Sun Shower's 5 shots, Cloudburst's volleys).
- Pick by the design: one heavy flurry that a single parry should answer → hitCount; a stream of shots where each can
  miss and each feeds on-hit passives → repeats.

## 1. WHO an effect lands on — say it, never assume it

**Every effect answers `side` outright: `Caster` or `Target`. There is no third option, deliberately.**

There used to be an `Auto` meaning "this KIND's traditional side", and it shipped a broken skill: sub-stat changes were traditionally SELF, so an effect authored to "cut THEIR attack speed" and left on Auto cut **the player's own** attack speed — saved clean, passed the data checker, and looked right until someone read the "To the caster" heading in the composer (Scattered Petals, 2026-08-24). Auto was deleted the same day and all 46 authored sides were rewritten to what they already resolved to, so no skill changed behaviour.

**So the rule is now enforced by the data model rather than by discipline** — but the reading still matters, because **no checker can flag the wrong side**: every side is legal for every kind, and a self-inflicted debuff is a real design (Squall deliberately doubles its own incoming damage). PROVE it per skill before you report anything:

```bash
.claude/skills/compose-skill/audit-effect-sides.py sword
```

It prints who every effect lands on, reading both enums out of the C# at run time. Read each row and ask "is that where I meant it?"

**Do not reintroduce a per-kind default.** A side that resolves differently depending on the effect's kind is unreadable off the asset, and that is exactly what turned an authored enemy debuff into a self-inflicted one.

## 2. Where it lives, and the naming rule

- Folder: `unity/Assets/3_Data/Resources/Skills/` — nothing else is loaded.
- **The FILE NAME must equal the** `id` **field.** The composer builds the path from the id (`{id}.asset`) and deliberately never uniquifies the filename, so a mismatch means the composer writes a second asset next to yours.
- Convention for the id: `skill.<weapon>.<name>` for a weapon skill (`skill.sword.parry`), `skill.<name>` for the shared/legacy ones.
- Every asset needs a `.meta` beside it with a unique guid.
- `published: 1` = it can roll onto a dropped weapon. `published: 0` = composed but unreachable.
- `vfx` must be set — `SkillRunner.Activate` refuses to run a skill whose vfx is null. Reuse what the sibling skills use rather than inventing an effect (`grep '^ vfx:' skill.sword.*.asset` shows the two in play).

## 3. Prices and cooldowns are RULES, not taste

`docs/2-systems/active-skill-system-v3/skill-numbers-balance-baseline.md` owns them. Read it; the short form:

- **Passives are free** — 0 mana, 0 cooldown, always. The build slot is the price.
- **Mana by step:** reaction 8 · standard 12 · heavy 16 · ultimate 40.
- **Cooldown ≥ 1.5 × the skill's own duration**, so an effect can never be permanently up. Instant skills take their step's band (\~5s reaction, \~6s standard, \~8s heavy). Ultimates are 45–60s.
- A window (seconds) and a count of charges are different numbers and are never expressed in terms of each other.

The weapon's own roster doc (e.g. `docs/2-systems/active-skill-system-v3/sword-skill-roster.md`) owns what the skill IS, including its identity bans. Read it before designing — Sword, for instance, may never have crit or AoE.

## 4. Mandatory verification — none of these are optional

```bash
tools/check-skill-data.py                              # the data: gates, piles, triggers, effect wiring
.claude/skills/compose-skill/audit-effect-sides.py     # who each effect lands on
tools/check-unity-code.sh                              # only if you touched C#
```

`check-skill-data.py` prints how many skills it read — confirm that number includes yours, because a clean run over a file it never opened looks identical to a clean run over one it did.

**What the checkers CANNOT catch, so you must:**

- **Which side an effect lands on** — every side is legal, so nothing can flag the wrong one. That is the whole reason the audit above exists.
- **Whether the numbers mean what you intended** — a 0.3 that should have been 3, a duration that should have been charges.
- **Whether it plays well.** A clean run means Unity will load it, never that it is fun or balanced.
- **When a passive really fires.** The moment is `trigger`, but the skill that must CAUSE it is `triggerSkillId` on
  the same entry (Second Bloom: a kill — made by Thousand Petals). The generated table in `data/skills.md` prints only
  the moment, so always read the asset's `triggers:` before saying when a passive fires.

## 5. Finish the job

- **Read the asset back off disk** and confirm the values landed. Never report a value you only wrote.
- **Update the weapon's roster doc** once the work is final (the project rule: no doc edits while a change is still being built or tested) — those docs state which skills are composed, and a stale "designed, not composed" line is a lie the next reader will act on.
- The player wiki (`wiki/content/*.json`) describes skills to players; update it when a skill becomes real.
- The editor needs a refresh or recompile to load new assets and code. Trigger it yourself (the old ban was lifted
  2026-08-26), but only when Play mode is stopped — read `editor_status` first.

## 6. Authoring in the editor

The window is **Tools ▸ unwritten ▸ Composers ▸ Skill Composer**. Use it when the user is driving. When you are composing on their behalf, editing the `.asset` files directly is fine and faster — they are plain YAML — but then the verification pass in §4 is what stands in for the window's own guardrails.

Related windows: **Tools ▸ unwritten ▸ Catalogs ▸ Skill Channels & Effects** (what every effect kind and channel means) and **Tools ▸ unwritten ▸ Composers ▸ Vfx Composer**.

## 7. A skill's look (VFX) — the `skill-vfx` skill

**Everything about what a skill LOOKS like lives in the `skill-vfx` skill — load it before choosing, sizing, importing
or fixing any effect.** It owns the never-make-your-own-VFX rule, sizes in this world, rendering before use, importing
a pack, and fixing a broken effect at the smallest place first. The three rules below stay here because they are
authored on the skill itself, not on its look.

- **Every ground-aimed area skill gets a cast circle, automatically** (rule set by the user, 2026-10-23): any skill
  aimed at a point whose tick hits an area there shows the default circle while it casts, sized to its largest area
  radius (`SkillDef.CastCircle`). Leave `castCircleId` empty; set only `castCircleTint` to the skill's element colour.
  Fill `castCircleId` only to use a different circle.
- **An effect that plays for a long time before or while it hits** (a dome, a flood) lands its plain hit as a
  multi-hit: `hitCount` 10 with `damageMult` divided by 10 — the same total, worked out once at the first hit and
  shown as 10 pieces. Never `resolveAsBasicAttack` for this.
- **A weapon's skill animation:** `hasReleaseStance` + `weaponSkillAnimation` plays one of the two skill animations
  the held weapon owns — `First` (1) or `Second` (2), whichever the skill is mapped to. Each is one clip: up to its mark
  (its Timing in the Stance Composer) is held through the cast time, the rest plays when the skill fires at the clip's
  own speed; an instant skill plays all of it. Never give one to a dash or to a passive — it roots the caster for the
  animation's length.
