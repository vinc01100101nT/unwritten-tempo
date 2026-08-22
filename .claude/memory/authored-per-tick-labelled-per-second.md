---
name: authored-per-tick-labelled-per-second
description: Heal-over-time was authored per TICK while every label said per second — the 0.8s interval meant 25% more healing than any screen admitted.
metadata: 
  node_type: memory
  type: project
  originSessionId: 07e15ed6-d553-4a48-b0f8-d0b4e9b21691
---

The regen effect took its number as HP **per tick** and ticked every 0.8s, while the tooltip and the
composer both said "HP/sec". A 6 authored as "6 HP/sec" actually healed 7.5 HP/sec. Fixed 2026-08-23
by making health regen a real sub-stat stated per second (mirroring the mana trickle) and converting
the one authored skill so the healing is byte-identical and only the label changed.

**Why:** nobody could catch it by reading either side alone — the code was self-consistent and the
label was self-consistent. It only appears when you compare the authored unit against the tick
interval.

**How to apply:** whenever an effect has BOTH a cadence and a per-application amount, check the unit
in the label against the unit in the code. Prefer per-second in the data so the interval stays an
implementation detail nobody has to know. Same family as [[dead-stat-channel-displayed-only]] — a
screen that confidently states a wrong number.
