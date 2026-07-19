---
name: active-skill-archetype-ring-mapping
description: LOCKED backbone of the active-skill system (2026-07-18) — 6 archetype verbs mapped hard/soft onto the 10-stat ring; doc q170cz40rsxav6kq0r6pe431bn89me24
metadata: 
  node_type: memory
  type: project
  originSessionId: fac25a01-392c-4c20-bc03-6d0fb54d8d63
---

**The backbone for building active skills, locked 2026-07-18** (user + Claude brainstorm; seeded by a GPT proposal). Lives in Tempo doc `q170cz40rsxav6kq0r6pe431bn89me24` ("Active Skill System — Fresh Brainstorm (ring-harmonized)"), child of the [[active-skill-design-charter]]. The old Swordsman-track roster was **deleted** — this replaces it.

**Core idea:** active skills are NOT a separate track — they're an expression of where you steered the [[skill-system-mechanical-model]] stat ring. GPT's 6 element-verbs were kept as **battlefield-role archetypes** (elements deferred to pure flavor-skin) and mapped onto the ring.

**The 6 archetype verbs (coherence litmus — a skill failing its verb belongs to another archetype):**
- Consume/Burst, Accelerate/Tempo, Endure/Fortress, Restore/Sustain, Empower/Buff (LIGHT), Corrupt/Curse (DARK).

**Ring-mapping — HARD home (stat scales the archetype's numbers = node identity) vs SOFT home (utility archetype gets a potency bonus at a pole, owns no node):**
- Consume/Burst → Atk, MAtk, Critical, Accuracy (damage-agnostic; MAtk's HARD id is Burst)
- Accelerate/Tempo → AtkSpd, CastSpeed
- Endure/Fortress → Defense, Evasion, HP
- Restore/Sustain → HP, MP (pools)
- Empower/Buff → SOFT-rides the **Defense pole (top = Light)**
- Corrupt/Curse → SOFT-rides the **MAtk pole (bottom = Dark)**

Poles Defense↔MAtk are opposite on the ring = the Light/Dark axis falls out of geometry. Soft=soft: any build can slot/cast a buff/curse, the pole only grants potency (keeps Charter's no-forced-filler rule).

**Content matrix:** each archetype × 4 categories (Attack / Utility / Buff / Ultimate). "Ultimate" is a TIER FLAG within a category, not its own category.

**Open (flagged, not blockers):** Burst is widest (4 nodes) → most skills; HP is the one split node (Endure+Restore); magic arc (MAtk) is verb-dense (nuke+heal+buff+curse gravitate to INT) vs raw-stat physical arc.

**FINAL ARCHITECTURE — 3-brain consensus reached 2026-07-18** (Claude+GPT+user, doc convergence log entry (d)). The hard/soft ring-map above is superseded by a weighted-affinity model + a clean ownership stack:
- **Effect instance** owns SCALING — affinity is per output-channel (damage / burn-duration / shield / knockback…), weighted (e.g. Meteor damage = 70% MAtk/20% CastSpeed/10% MP; burn-duration = 70% MP). Maps to a NEW field on `SkillEffect` (SkillDef is already `bindings → List<SkillEffect>`). Effect TYPE gives an overridable default.
- **Skill** owns COMPOSITION (which effects it fires).
- **Tag** (the 6 archetype verbs) owns IDENTITY only — litmus, signature weakness, authored synergy motifs, category, UI. NEVER in the damage math (a tag can't own a damage-stat: Burst spans both Atk & MAtk).
- **Template** (per tag × damage-type) speeds authoring — defaults only, never constrains (a Holy skill scaling off Defense is legal).
- **Affinity Heatmap** (renamed from "gravity") = DERIVED analytics only; never drives gameplay; NOT shown as authored numbers (would be false). Only each skill's own affinity shows on its card.

Balance pillars: **signature weakness** per archetype (every gain pays a cost) via TWO levers — encounter variety (primary) + resource pressure (always-on backup). **Synergy = cross-tag ONLY**, authored MOTIFS each with a "gameplay sentence" (Restore→Burst = "bank resources to unleash"), never same-tag, never arbitrary.

**MVP MATRIX v1 drafted + committed to the doc (2026-07-18):** 6 skills, one per archetype — Ember Bolt (Burst/Attack/base, renamed fireball) + Meteor (PROMOTED to Burst/Attack/ULTIMATE, reuses existing skill.meteor VFX), Blink Step (Tempo/Utility), Bulwark (Fortress/Buff), Mending Tide (Sustain/Buff), War Banner (Empower/Buff/Light), Hex Field (Corrupt/Utility/Dark). Motif package demoed: Hex Field softens → Meteor executes. **Ultimate = a TIER** (not a category) on any skill: charge-meter gated (fills in combat) + one dedicated ult slot; adds `tier`+`chargeCost` to SkillDef, meter+slot to hotbar — no new architecture.

**Next step (build):** author the 5 missing VfxSequences (Meteor done; Ember Bolt likely reuses an existing bolt/spark), then composed `SkillDef` assets under `Resources/Skills/`; add per-effect affinity field to `SkillEffect` + `tier`/`chargeCost` to `SkillDef` + ult meter/slot. VfxSequence per skill = the real bottleneck ([[legacy-skills-retired-composed-pool]]).
