# Handoff — RO-cloned attack animations + dual-wield off-hand hit

**Task:** give every weapon its own attack animation cloned from a Ragnarok Online sprite, wire them into the game,
and make Katar and Axe land a second (off-hand) hit per basic swing. No Tempo issue/doc id.

**Status:** BUILT, NOT PLAY-TESTED. The attack animations are approved by the user (from the videos) and wired into
the game; the off-hand hit is built with the red team's fixes (2026-10-09). What remains is one play-test of everything.

## What's done

- **Standard process, set by the user 2026-10-09:** every character animation is cloned from the matching RO sprite.
  Method + both editor scripts: skill `.claude/skills/clone-ro-sprite-animation/SKILL.md` (new). Standing rule line
  added to `CLAUDE.md` (next to the weapon-model rule). Memory: `.claude/memory/attack-animations-from-ro-sprites.md`.
- **12 humanoid clips** in `unity/Assets/1_Authored/Art/Characters/Animations/` (all new, untracked), 60 poses/s,
  no-overshoot curves (ClampedAuto), strike pose at 70% of each clip (the server's `CharacterSpec.hitTiming`):
  `AttackOneHandSword` (the Katar's right-hand outward slash and forward step, right hand only — user, 2026-10-09), `AttackAxe` (Blacksmith chop, right at 70% then mirrored left at 90%),
  `AttackMace` = Warhammer (Blacksmith slam), `AttackSpear` (Knight thrust), `AttackKnuckle` = Fist (Monk punch),
  `AttackDaggerStab` (Assassin stab), `AttackKatar` (Assassin, right slash 70% / left slash 90%),
  `AttackShurikenSwing` (Ninja), `AttackSniper` (Gunslinger), `AttackStaffSwing` (Wizard), `AttackWand` (Priest body, feet,
  left arm and timing; its right arm and the wand take the staff's swing since 2026-10-10, when the Trellis wand —
  117% of body height — went through the torso in the Priest's chop),
  `AttackScytheSweep` (Lord Knight spin — stand-in, no RO scythe). Locked by the user, NOT to be remade and never
  to be offered for a rework (2026-10-09, repeated 2026-10-10): Greatsword (keeps current `AttackSword` clip), Bow,
  Shuriken throw.
- Free arm fixed on Dagger/Fist/Staff/Axe/Scythe (was aimed behind the body → elbow folded wrong; user saw "broken
  arm"). Elbow pass after the fix: every bent elbow points down or outward.
- Weapons point along each swing (skill step 4b): the 11 weapon clips were rebuilt so the hand turns the weapon — the
  spear and dagger stay aimed at the target, cuts lead with the edge, the Sniper's bow (left hand) stands upright;
  the Fist clip is unchanged. Sample videos for the user to approve:
  `D:\kimodo\out\ro_refs\videos\<weapon>\<weapon>_attack.mp4` (knight body, the Trellis models, all re-rendered
  2026-10-10). Spear/dagger/sniper keys carry `rw`/`re` in their key tables.
- Second round of animation fixes (user, 2026-10-09): staff and wand gripped a third of the way up from the plain
  end (prefab grips), the staff swing moved into an upright plane right of the body, the edge kept on the line of the
  cut, Spear two-handed, Sniper held as a right-hand rifle at its trigger grip (the videos draw the item's rifle, `sniper_vael`), pair weapons
  aimed in both hands, and the weapon kept out of the body — all in skill step 4b; key tables in the skill's `keys/`.
- Warhammer attack held in TWO hands (user, 2026-10-09; the item stays a one-hand weapon type in game): wind-up changed
  to the hammer upright in front of the chest, because the chibi arms cannot both reach a raise over the shoulder.
- **Outside the repo** (not scaffolding, keep): `D:\kimodo\out\ro_refs\` — `attack_references.png` (the user-approved
  sprite picks), `clones\*_vs_sprite.png` (sprite rows over our frames; dagger/fist/staff/axe/scythe pictures predate
  the free-arm fix — `clones\free_arm_fixed_dagger_fist_scythe.png` shows the fix), `keys\tool.py` (slice sprite rows /
  generate the skill's scripts from a json / compose the comparison; it expects a json + RO sheets next to it, sheets
  re-downloadable via the skill), `offhand-plan.md`, `videos\<weapon>\` (one folder per weapon, Trellis models only). The key-pose tables' home is now the skill's
  `keys\` folder (in the repo); the `keys\*.json` copies here are older duplicates — edit the skill's.
- Kimodo (NVIDIA text-to-motion) installed at `D:\kimodo` (own venv, models cached in `~/.cache/huggingface`); the user
  judged it worse than sprite cloning — keep only for long full-body motion.
- Learnings: `docs/5-ai-learnings/measure-what-the-user-sees.md` (new, indexed); body-position note added to
  `a-character-stays-in-t-pose-outside-play.md`; posing note in `.claude/skills/import-weapon-models/SKILL.md`.
- Removed earlier this session (slam test leftovers, untracked): NinjaShuriken25Pack, Shuriken_20 prefab, AttackSlam
  fbx/anim.

## User decisions already made (do not re-ask)

- Dual-wield weapons: **Katar and Axe** (`WeaponType.Katar`, `WeaponType.Axe`). Off-hand = **50%** power, a **full hit of
  its own** (own miss, crit, on-hit, procs), both hits as fractions of swing time (attack speed scales them). Creatures
  get the same. Memory: `.claude/memory/dual-wield-offhand-decision.md`.

## Uncommitted working-tree changes (measured 2026-10-09 02:03)

From THIS session: `CLAUDE.md`, `.claude/skills/import-weapon-models/SKILL.md`, `.claude/skills/clone-ro-sprite-animation/`
(new), `.claude/memory/*` (2 new files + 2 index lines), `docs/5-ai-learnings/README.md`,
`docs/5-ai-learnings/a-character-stays-in-t-pose-outside-play.md`, `docs/5-ai-learnings/measure-what-the-user-sees.md`
(new), the 12 `Attack*.anim` (+ .meta) above.

NOT from this session (another session or the user — leave alone): `tools/check-asset-rules.py`,
`tools/sprite-sheet-to-weapon.py`, `unity/Assets/2_Generated/README.md`, `unity/Assets/2_Generated/Weapons/`,
`unity/Assets/ASSETS.md`, `unity/Assets/Scripts/Editor/PixelArtImportPostprocessor.cs`,
`unity/Assets/Scenes/Systems.unity` (19 dead stat fields dropped, saved 2026-10-08 21:22 before this work),
`tools/hook-analytics/hook-analytics.jsonl` (hook log).

## What's left

1. **Answered 2026-10-09: per hit.** Each hand's hit uses up its own charge and counts in the run of hits, like every
   other second hit (the execute bonus spends its own charge). No special case to build.
2. **Revise the plan** (`D:\kimodo\out\ro_refs\offhand-plan.md`) with the red team's fixes:
   - store the held weapon (or an equip counter) on each `PendingBlow` and drop the blow if it changed
     (`GameNetwork.cs` ~1752 struct, ~1859 add, ~1888 `LandTheBlow`) — also closes the existing single-hit hole;
   - `LandTheBlow` refuses a dead victim (creature path already does, `MonsterAI3D.cs` ~739);
   - ONE plain function returns a weapon's contacts (fraction, power), shared by players and creatures; off-hand
     fraction clamped strictly after the main one, ≤ 1;
   - creature: second pending slot, cleared in `OnDisable` (`MonsterAI3D.cs` ~376), re-checked like the main one;
   - land due blows in time order; remove by swap-with-last;
   - new `StanceId`s (append-only) must join `StanceDefaults` attack case (`StanceLibrary.cs` ~208),
     the Stance Composer rows (`StanceComposerWindow.cs` ~160) and `WeaponStances.IsBasicAttack`;
   - left-hand copy in `WeaponSocket` handled in remount/destroy/hide-on-throw/silhouette (`WeaponSocket.cs` ~23/60/103/121),
     keyed by weapon type (bows already grip left-handed).
3. **The off-hand hit is built (2026-10-09)**, see "Next single step". **The clips are wired (done 2026-10-09):** new stance ids
   `AttackOneHandSword`, `AttackAxe`, `AttackMace`, `AttackKatar`, `AttackKnuckle`, `AttackWand` (appended, in
   `StanceDefaults`, the Stance Composer rows and `IsBasicAttack`); `WeaponStances.AttackFor` maps each weapon to its
   own (TwoHandSword and TwoHandAxe stay on `AttackSword`); Dagger/Staff/Lance/Rifle/Scythe/Shuriken stances point at
   the new clips; controller rebuilt (same ID, 12 states added, none removed). The server picks the stance and sends it,
   so the game server build must be rebuilt for other players and creatures to show them.
4. **Done 2026-10-09:** one pair weapon fills both hands — `WeaponTypes.IsDualWield` (Katar, Axe) read by `WeaponSocket`,
   which mounts the mirror copy (and hides/destroys it with the main one). Not play-tested yet.
5. Checks: `tools/check-unity-code.sh`, `check-client-math.py`, console; then a play-test checklist incl. which servers
   to rebuild/restart (server code changes → game server build rebuild + map server restart).

## Dead ends (proven)

- Kimodo text prompts for short exact moves: never matched the sprite (3 versions tried for the Katar).
- Fitting a straight-overhead move to our chibi body: impossible to show the arms from the front — shoulders 10.2% of
  body height from centre vs head half-width 15.7%; raised wrist reaches 87.5% vs head top 99%. Raise arms out to the side.
- Reading a sprite from one view only: a forward slash came out aimed at the camera. Always two views.
- Posing a whole body from muscles alone (no RootT/RootQ): feet float up to 14% of body height.

## Next single step

Hand the user the play-test checklist (game server rebuild + map server restart first). The off-hand hit: one plain
rule `SwingContacts.Of` (WeaponType.cs, pinned in tools/game-math-tests) read by the server's person swing
(GameNetwork pending blows: power + the weapon copy that started the swing; due blows land in time order; a dead
target or a swapped weapon drops the blow) and by MonsterAI3D (second pending contact, cleared on disable);
`CharacterSpec.offHandHitTiming` 0.9 and `offHandPower` 0.5, written into CharacterSpec.asset.

## How to resume

```text
Read handoffs/ro-attack-animations-and-dual-wield.md and continue from "Next single step".
```
