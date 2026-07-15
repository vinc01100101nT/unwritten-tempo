---
name: class-system-design
description: How classes work in unwritten — an active-skill kit layer on top of universal stats/passives
metadata: 
  node_type: memory
  type: project
  originSessionId: 41f6a2ce-05c4-4cb8-aed0-15c3ba6f6c7e
---

Class model locked 2026-06-18. A **class is an identity layer, NOT a stat lock**: stats (STR/AGI/VIT/INT/DEX/LUK) and the [[skill-system-mechanical-model]] TRIO-RING % passives stay **universal** (anyone can build anything). A class = **a kit of active skills + a basic-attack type + a stat affinity**. Replaces the retired "draft reveals your class" idea (passives = build, class = kit). Full design: doc `q171yxv1de9zce12zhzrssr59188ws6t`.

**Implementation pattern (the template for every skill/class):** each active = a small self-contained component on the player that reads its own hotkey, acquires a target (cursor or nearest), gates MP + cooldown, then delegates the effect. `Player3DCaster` (Q bolt), `Player3DBash` (1), `Player3DCharge` (2) are the templates. Skills are **code-defined behaviors, not ScriptableObjects** (consistent with the TRIO-RING "static table over SO" call).

**Reusable hooks built:** `Player3DAttacker.Strike(...)` (heavy always-landing hit reusing the swing→FX→damage pipeline), `MonsterAI3D.Stagger(seconds)` (CC freeze), `MonsterAI3D.Knockback(...)` (AoE shove via `PathAgent3D.Knockback`), `MonsterAI3D.Slow(factor,secs)` (chill), `Player3DController.BeginDash(...)` (charge/dash), `Player3DController.GroundUnderCursor(...)` (ground-target), **`Projectile3D.Spawn(...)`** (reusable traveling billboard projectile → damage on arrival; powers Flam-mob fireball + Mage spells). All player skills **self-bootstrap** via `Player3DController.Awake`; `Build Player` configures their FX/SFX/icons (see [[hotbar-ihotbarskill]]).

**CLASS SWITCHING IS BUILT (2026-06-18):** `enum PlayerClass {Swordsman, Mage}` + `ClassState` (PlayerPrefs). Every active implements `IHotbarSkill.InClass(c)`. `Player3DController` bootstraps BOTH kits; `ApplyClass(c)`/`SetClass(c)` **enables only the chosen class's components** (disabled = no keypress + off the hotbar) and rebuilds the bar. Switch live in the **DevSettings (P) ▸ Class ▸ "Mage (off = Swordsman)"** toggle. So "a class = which kit is enabled."

**Roster:** Swordsman ✅ (1 Bash · 2 Charge · 3 Guard · 4 Warcry) · **Mage ✅ (1 Fireball · 2 Frost Nova · 3 Meteor)** · Archer · Acolyte · Thief · Merchant. Bolt (Q) = universal basic. **How to add a class:** give the player its `IHotbarSkill` components (each returns its class from `InClass`), wire assets in `Build25DSpike.BuildPlayer`. Part of M1 of [[direction-2p5d-pivot]].
