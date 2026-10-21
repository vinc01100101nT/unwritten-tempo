---
name: combat-multihit-status
description: "Multi-hit skills are worked out ONCE per target (RO-style) and shown as pieces; every skill tick's blows travel as one HitBatch message"
metadata:
  node_type: memory
  type: project
  originSessionId: 0ef68d6e-0ec8-4ede-9860-5ef602f27d9c
  modified: 2026-10-21T05:55:11.549Z
---

Rebuilt 2026-10-21 at the user's instruction ("it should compute once, not per hit… overkill popup will still show,
just like ragnarok"). `MultiHitBurst.cs` is deleted.

- **A Damage effect with hitCount N > 1** (`SkillEffectApplier.FlurryOnce`): ONE roll per target, multiplied by N
  (`strikeScale`), applied as ONE blow — so one parry charge, one brace charge, one block roll and soft defence once
  cover the whole flurry (the user chose "one cast = one blow"). Health comes off at cast. Chance effects still roll
  once per hit index; a target already dead takes the numbers only.
- **Shown as pieces**: while `BlowBatch.Split = N`, the announcement cuts the blow into N equal pieces (remainder on
  the last), each with its own server-side rising total (`StrikeTotals.Add`) and delay index; the client plays them at
  `BlowBatch.PieceInterval` (0.2s) from `GameNetwork.Update → BlowBatch.TickPlayback`. Combat log writes the whole blow
  once (piece 0); later pieces carry log −1.
- **Batching**: `SkillRunner.OnTick` opens `BlowBatch.Begin()` around every tick's burst; every `AnnounceBlowOnMob`
  / `AnnounceBlowOnPlayer` inside is collected and sent once per watching connection as `GameNetwork.HitBatch`.
- **Passives fired mid-strike** run inside `SkillEffectApplier.StrikeIsolation` (PassiveEvents.Fire), so they never
  inherit the strike's scale, crit bonus or piece count.
- Repeats (`repeatCount`, Sun Shower's 5 shots) are NOT multi-hit — each beat is its own strike/basic attack.

**Ground-target reticle:** `GroundTargeter.cs` (reusable) — still the placed-AoE aim. See [[class-system-design]],
[[hotbar-ihotbarskill]].
