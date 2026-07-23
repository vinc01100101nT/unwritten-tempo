# Handoff: projectile skill shows NO flying projectile on cast

**Task/bug:** After this session's targeting consolidation + projectile rework, casting a projectile skill (`skill.ember_bolt`) produces **no flying projectile**. No Tempo issue id.

**Status:** NOT FIXED — handoff only (user asked for a debug handoff, not a fix). None of this session's projectile work has been Play-tested; **don't trust any prior "it compiles / should work" claims.**

## What's relevant that was done this session (all UNCOMMITTED)
The whole targeting/projectile refactor is in the working tree, uncommitted. Projectile-specific pieces:
- `VfxDirector.cs` — Carrier "along-flight" ticks: a Carrier with `tickCount > 1` now schedules its spread ticks at LAUNCH and tracks the live mover via a new `tickFollow[]` array; the drain loop sets `tickPoint[i] = tickFollow[i].position` each frame. Single-tick carriers keep arrival behavior.
- `Projectile3D.cs` — `SpawnVfx` changed from `void` → returns the mover `Transform` (so the runner can track it).
- `SkillRunner.cs` — `PlayFan(n, spread)` fans N copies of the VFX around the aim when `def.IsProjectileAim && projectileCount > 1`; `OnTick` Ground routes Projectile→AoE-at-tick-point (the hard-coded per-frame pierce scan was REMOVED).
- `SkillDef.cs` — `IsProjectileAim` = "the referenced VfxSequence has a Carrier step"; skill-level `projectileCount`/`projectileSpread`; per-tick Projectile delivery + its fields REMOVED (delivery is now Point/Aoe only).
- `GroundTargeter.cs` — NEW line-arrow aim preview (`UpdateArrow`, a `LineRenderer` with `new Material(Shader.Find("Sprites/Default"))`) shown when `projectilePreview` (i.e. `IsProjectileAim`) is true.

## PROVEN FACTS (measured from the assets this session — trustworthy)
- ember_bolt's VFX = `Resources/VfxSequences/skill_projectile_fire.asset`. Step 0 "fire" **IS a Carrier** (`kind: 2`), `effectId: fire.magic-3`, `tickId: projectile_fire`, `spreadTicks: 1`, `tickCount: 3`, `tickInterval: 0.28`, `carrierFromCaster: 1`, `carrierScatter: 0.511`, `carrierSpeed: 6.9`, `carrierFaceTravel: 1`, `carrierMaxLife: 3`.
- `fire.magic-3` **EXISTS** in `Resources/EffectLibrary.asset` (line 232). → NOT a missing-effect problem.
- Skill tick (`skill.ember_bolt.asset`): `tickId: projectile_fire`, `targetMode: 2` (Ground), `groundDelivery: 0` (**Point**), effect `kind: 0` (Damage), hits 5. Skill-level `projectileCount: 1`, `projectileSpread: 0`, `targetingMigrated: 1`.
- Therefore `IsProjectileAim` should be TRUE (VFX has a Carrier) → the cast opens `GroundTargeter` with the new line-arrow preview; `projectileCount: 1` → `PlayFan` NOT taken, plain `VfxDirector.Play`.

## HYPOTHESES (UNVERIFIED — need the Unity console)
Ranked, all point at THIS session's new code:
- **H_A (most likely): the new line-arrow preview throws during aim → the cast never confirms → nothing plays.** `GroundTargeter.UpdateArrow` runs BEFORE the left-click confirm check; if `Shader.Find("Sprites/Default")` returns null (URP) → `new Material(null)` throws, or any LineRenderer setup errors, `Update` throws every frame and the confirm click is never processed. Symptom would match "cast does nothing, no projectile."
- **H_B: `VfxDirector`/`VfxSequenceRunner.Update` throws** on the new `tickFollow[i]` path (e.g. array not sized, or the alongFlight branch), so the sequence never renders the carrier.
- **H_C: the Carrier just isn't spawning/rendering** despite the code (SpawnVfx return-type change side-effect, or the attach of `fire.magic-3` failing at runtime). Less likely — the spawn code itself is unchanged.

## Ruled out (dead ends)
- Missing effect — `fire.magic-3` exists.
- Fan bug — `projectileCount: 1`, so the fan path isn't even exercised.
- Recompile — per project rule, assume the user recompiled; do NOT blame stale compile.

## SEPARATE bug found (NOT the cause of "no projectile", but flag to user)
The tick's `groundDelivery: 0` = **Point**, not AoE. `OnTick` routes Point → `targets = Player`, so the Damage effect would land on the **caster** (self-damage) with no mob gather. A projectile skill's tick should be **AoE** (small radius) so the along-flight ticks hit mobs. The composer even lets you pick Point here with a projectile VFX — worth guarding/UX-flagging.

## Next single step (the discriminating test)
Press Play, cast ember_bolt, and **read the Unity console**. An exception's stack trace decides it:
- exception in `GroundTargeter.Update`/`UpdateArrow` → **H_A** (fix: guard the material/shader, or build the arrow safely / lazily).
- NRE in `VfxDirector`/`VfxSequenceRunner.Update` → **H_B**.
- NO exception but still invisible → check whether a `Projectile3D(Vfx)` GameObject is spawned during the cast (it should be) and whether the `fire.magic-3` Attach actually renders (H_C).

## How to resume
`Read handoffs/projectile-not-appearing.md` — then get the Unity console output from a cast (that single log picks H_A vs H_B vs H_C). Start by eyeballing `GroundTargeter.UpdateArrow` (newest, most suspect) and `VfxDirector` `tickFollow`.

**Not fixed — 0% confidence (untouched by design; this is a handoff).**
