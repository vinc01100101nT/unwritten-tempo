---
status: "BUILT 2026-09-23 — not play-tested. Decided with the user on 2026-09-23."
owner: vince
last-updated: 2026-09-23
tags:
  - systems-combat-design
---

# Timed Effects Survive Logout

**A timed effect ends only on death.** Every buff, debuff, barrier, charge, damage-over-time and delayed Brace damage
a character carries is written into its save with the **seconds it had left**, and put back — clock resuming where it
stopped — when the character logs back in or arrives on a map another server holds. **The clock is paused while the
character is away**: a converter with 3:12 left still has 3:12.

## What happens where

| Moment | What happens |
| --- | --- |
| Log out, disconnect, signed in elsewhere, clean server shutdown | saved with the time left |
| Walk into a map another server holds | saved at the door, paused, restored on arrival |
| Walk into a map the same server holds | unchanged — nothing is saved, the clocks keep running |
| Death | everything cleared at revive, as always; a save written while dead brings nothing back |
| Server crash | whatever the last save caught — a buff starting or ending marks the character to be saved at the next 15-second beat |

## The rules

- **Only the server restores.** A player's machine is told its states by the server and never reads them from the save.
- **Restored before health and mana are clamped**, so a max-health or max-mana buff is part of the maxima and a
  character saved above their plain maximum keeps that health.
- **A Taunt is never kept** — whoever taunted will not exist when the save is read back, and a taunt nobody holds would
  block every skill for its whole window.
- **Damage over time comes back with nobody to credit** for a kill it makes.
- **Delayed Brace damage is still owed**, including debt that came due at the moment of saving — it lands as soon as
  the character is back.
- **A pile from a skill that no longer exists is dropped.**
- **Saved by name, one stack at a time** — each stack keeps its own time, strength and amount; the totals are rebuilt.
  A state renamed or removed from the game simply drops out of old saves.
- Saved from **save v15** on; the save today is v19 and the build string `unwritten-character-v28`. A save older than v15 carries none, which loads as "nothing
  running".
- The server log writes one `buff` line per pile put back: `… restored from the save, <seconds>s left`.

## Where it lives in the code

`TimedEffectSave` (the pause arithmetic, plain functions, pinned by the game-maths tests),
`SaveData.timedEffects`, `UnitStates.CaptureForSave` / `RestoreFromSave`, `Character.CaptureTimedEffects` /
`RestoreTimedEffects`.

## Related

- [State System](../active-skill-system-v3/state-system-buffs-debuffs-combo-marks.md) — how stacks expire.
- [Elements & Converters](elements-and-converters.md)
- [Gameplay System — V4](README.md)
