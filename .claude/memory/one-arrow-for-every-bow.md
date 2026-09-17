# One arrow for every bow — the server times it, every machine draws it (2026-09-17)

**Before:** there was no arrow at all. The player's `arrowSheet` was null in `Systems.unity` and all 86 creature
prefabs had `projectileSheet: {fileID: 0}`, and a null sheet meant "the blow lands instantly, draw nothing". A
creature's bow branch was gated on that sheet, so bow creatures fell through to the instant melee blow.

**Now, one shape for every unit that holds a bow:**

- `Arrow` owns what an arrow IS: `Speed = 14` units/sec (the player's old authored value; the creature's was 9),
  `FlightSeconds(from, to)`, and `Show(...)`, which flies a **drawn line** through `Projectile3D.SpawnLine` —
  the pooled projectile gained a `LineRenderer` used whenever no sprite sheet is given. Replace the look here and
  every bow in the game changes with it.
- The SERVER times the blow. A person's bow: `GameNetwork.OnHitRequest` schedules `pendingBlows` at
  `now + Arrow.FlightSeconds`. A creature's: `MonsterAI3D` holds one `arrowAt`/`arrowLandsAt` slot (one shot in
  flight, because the swing cadence paces it) and lands it through `LandOneSwing`.
- The LOOK is announced: `GameNetwork.AnnounceArrow` → the `ArrowLoosed` message → every machine watching the
  shooter draws the same flight. The shooter's own machine draws it from that announcement too — it no longer
  flies one of its own.

**Deleted as dead knobs:** `Player3DAttacker.arrowSheet/arrowFrameSize/arrowFrames/arrowFps/arrowSpeed/arrowScale`
and `MonsterAI3D.projectileSheet/projFrameSize/projFrames/projFps/projSpeed/projScale`. Their values still sit in
`Systems.unity`, `town_0.unity` and the 86 mob prefabs, so those files report unsaved changes once.
`Projectile3D.HitPayload` and its payload `Spawn` overload lost their only two callers and now have none.

See [[every-skill-moment-is-shared]], [[the-game-is-always-online]].
