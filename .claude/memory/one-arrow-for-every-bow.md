---
name: one-arrow-for-every-bow
description: Everything that flies (arrow, thrown shuriken, bouncing skill) goes through ONE server flight store and one picture; legs carry leave/arrive times on the shared clock
metadata:
  type: project
---

# Everything that flies — one server store, one picture (rebuilt 2026-10-06)

**One shape for every unit's ranged attack and every bouncing skill:**

- `Flights` (server) is THE store for anything in the air: a bow/sniper arrow, a thrown shuriken (3 jumps, nearest
  enemy not hit within 2.5 of the last, 75% kept per jump, then home to the hand), and a Single Target skill tick with
  `bounceJumps > 0`. Numbers live in `FlightMath` (speed 7 for everything, `MaxJumps` 12, the throw rules), pinned
  in the game-maths checks. Each hop is decided ON ARRIVAL (thrower still here / same life / same map / not leaving /
  still holding the same weapon for a basic throw; target alive / same life / same map / hostile).
- A person's ranged attack: `GameNetwork.OnHitRequest` judges reach AND map at the throw, then `Flights.ThrowBasic`.
  A creature's: `MonsterAI3D` calls the same `Flights.ThrowBasic` (its old one-shot arrow slot is gone). Hops land
  through the existing doors: `BasicSwing.Land` (scaled), a creature's `LandSwing` (on-hit ailment onto the unit
  struck), or `SkillEffectApplier.ApplyBurst(..., casterPass: false, hop: share)` for a skill hop.
- The LOOK: `GameNetwork.AnnounceFlightLeg` → `FlightLeg` message (thrower, blade id, target, departs/arrives on
  `NetworkTime`, look byte, effect id, map) → `FlightPictures` draws ONE object per flight, time-locked to the leg.
  `Arrow` now holds only the streak look. A thrown weapon empties the hand (`WeaponSocket.HideForFlight`, counted per
  body, given back however the flight ends). `ArrowLoosed`, `AnnounceArrow`, `Arrow.Show/FlightSeconds` and
  `Projectile3D.SpawnLine` were deleted.
- **Gotcha (fixed 2026-10-06):** the local player's 3D model is a SEPARATE root object riding the avatar, so a search
  under `Character.Body` finds no `WeaponSocket` — the throw fell back to the streak look and the hand never emptied.
  Always resolve a body's hand through `WeaponSocket.Of(drawnBody)` (`Player3DBody` marks the local hand with
  `WeaponSocket.MarkMine`). The flying model is laid flat and spun about the vertical.
- Creatures are also filed by map (`MonsterAI3D.ActiveIn`, re-filed in `MapManager.PutInMap`); area gathers and hop
  searches read only the caster's map.

**Still true from 2026-09-17:** the old arrow-sprite fields were deleted as dead knobs; their values still sit in
`Systems.unity`, `lastline.unity` and the 86 mob prefabs until each is saved once.

See [[every-skill-moment-is-shared]], [[the-game-is-always-online]].
