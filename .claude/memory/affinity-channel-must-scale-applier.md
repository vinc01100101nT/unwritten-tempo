---
name: affinity-channel-must-scale-applier
description: "If the Skill Composer shows an effect a 'boosts X' affinity channel, the applier MUST scale that output (ScaledMagnitude), or it's a dead knob."
metadata: 
  node_type: memory
  type: project
  originSessionId: 396ce70e-7a14-4979-830d-2a8a5f5e5e77
---

An effect's affinity channel is a DEAD KNOB unless the applier actually scales the matching output. `SkillComposerWindow.AffinityOutputLabel` decides which "boosts X" channel an effect shows the author; `SkillEffectApplier` must then pass `ScaledMagnitude(e, caster)` (not raw `e.magnitude`) to that effect's sink for the knob to do anything.

**Scar (2026-07-24):** Guard and Vulnerability showed "boosts DAMAGE REDUCTION" / "boosts DAMAGE TAKEN" in the composer but the applier passed **raw** `e.magnitude` — the affinity did nothing. Fixed: `BeginGuard` and `BeginVulnerable` now take `ScaledMagnitude(...)`. (Guard's fraction is `Clamp01`'d in `Character`, so scaling past 1 is safe.)

**How to apply:** when adding/reviewing an effect, cross-check `AffinityOutputLabel` (channel shown?) against the applier call (scaled?). The **Tools ▸ unwritten ▸ Catalogs ▸ Skill Channels & Effects** window flags this — a ⚠ in its "ring scales" column means composer-shows-but-runtime-ignores. Related: [[name-knobs-concretely-one-job]].
