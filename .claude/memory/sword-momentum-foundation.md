---
name: sword-momentum-foundation
description: "Passives must be COMPOSABLE (Skill Composer data), not hardcoded C#; combat trigger bus built; Momentum-as-class was reverted"
metadata: 
  node_type: memory
  type: project
  originSessionId: 681de88d-d838-4839-91af-8bb635d30acc
---

**Sword-kit / passive-system implementation direction (2026-08-04):**

**Passives are AUTHORED DATA, not code.** The V3 docs mandate every passive (Momentum, Encore, Perfect Form, Pressure…) be composed in the **Skill Composer** as a `SkillDef` with a trigger — *"event-triggered via the trigger system Momentum proves."* A first pass hardcoded Momentum as a bespoke `Momentum.cs` class; that was WRONG and was fully reverted. Do NOT build passives as C# classes.

**Kept (the right spine):** `CombatEvents.cs` — central combat trigger bus (`BasicAttackLanded(Health, bool generated)`, `MobKilled(MonsterAI3D)`), emitted from `Player3DAttacker.LandBasicHit` (inside `dealt>0`) and `Health.Die()` (when a `MonsterAI3D`). Composable passives subscribe here.

**The composable architecture (grounded in real code):**
1. `SkillTrigger` enum + `SkillDef.trigger` (default `OnCast`); `IsPassive => trigger != OnCast`.
2. `SkillRunner` (already one per `SkillDef` via `Player3DController.EnsureComposedSkills`) gains a passive mode: subscribe its trigger → run its effects through `SkillEffectApplier`. Reuses the one effect path.
3. Grant routing: weapon equip (`Character.cs:688`) → `grantedSkillIds` → `HotbarBindings.SetWeaponKit`. Split it so passives→a granted-passive set (enable their runners), actives→bar, ult→ult slot. Passives ride the SAME `grantedSkillIds` (DRY, no new Item field).
4. One new engine piece: **stacking Buff** (today `Character.BuffDamage` is single-instance) — add `stacking`/`stackCap`/`owner` to `SkillEffect` (State System doc) + a FIFO stack list in Character summing into `DamageBuffMult`. Momentum = authored `Buff(Damage,+3%,Stacking·Private·no-cap,4s)`.

**Already implemented, docs were stale (now corrected):** aspd cap (`Character.cs:88` `MaxAttackSpeedMult=3f`) and the ult-charge meter. `Character.Invulnerable` gates physical+magic but NOT `TakeTrueDamage` (Bladedance DoT-pause needs that). Full plan: `handoffs/sword-skill-implementation.md`. See [[no-classes-weapons-define-gameplay]], [[name-knobs-concretely-one-job]].
