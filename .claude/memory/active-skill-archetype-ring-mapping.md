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

**Next step:** GPT v2 critique packet is in the doc awaiting its reply; then fill the archetype×category matrix, MVP ≈ 1 skill per archetype first (each needs a hand-authored VfxSequence — the real bottleneck, see [[legacy-skills-retired-composed-pool]]).
