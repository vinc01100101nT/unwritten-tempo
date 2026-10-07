# Handoff — thrown Shuriken: the flying weapon copy is invisible

**Task/bug:** holding a Shuriken with DEX > STR (its attack is the THROW), the player's own weapon vanishes from the hand
and the bounce damage lands on several creatures, but **no spinning flying weapon is seen** in the air. Expected: the 3D
model in the hand flies, spins flat, bounces between enemies and returns to the hand.

**Status: NOT FIXED.** Two fixes went in; neither has been seen working. **Don't trust prior "it works" claims.**
Tested by the user on their MAC client (Tailscale to this Windows machine's map server, port 7777). The user cannot
provide the Mac `Player.log`.

## What's done (the feature, built 2026-10-06)
- **Server flight store** `unity/Assets/Scripts/Flights.cs` — `ThrowBasic` (person: from `GameNetwork.OnHitRequest`
  ranged branch, reach + map judged at the throw; creature: from `MonsterAI3D` ranged swing), `StartSkillBounce`
  (from `SkillRunner.OnTick` for Single Target ticks with `bounceJumps > 0`), `Tick` (from `GameNetwork.Update`), each
  hop decided on arrival, landing through `BasicSwing.Land` / `MonsterAI3D.LandSwing` / `SkillEffectApplier.ApplyBurst(casterPass:false, hop:)`.
  Look chosen at `Flights.ThrowBasic`: `Look.HeldWeapon` when `WeaponTypes.ThrowsItself(HeldWeapon)` (Shuriken), else `Look.Streak`.
- **Numbers** `FlightMath.cs` (speed 14 u/s, 3 jumps, 2.5 range, 75% kept, `MaxJumps` 12), pinned in `tools/game-math-tests/Checks.cs`.
- **Network** `GameNetwork.FlightLeg` message + `AnnounceFlightLeg` (server) + `OnFlightLeg` (client). Old
  `ArrowLoosed`/`AnnounceArrow`/`Arrow.Show`/`Projectile3D.SpawnLine` deleted; `Arrow.cs` keeps only the streak look.
- **Client picture** `FlightPictures.cs` — `FlightPictures.Leg` → one pooled `FlightPicture` per flight. `Begin`:
  `WeaponSocket.Of(thrower)`; if `look == HeldWeapon && hand.ShownModel != null` it rents a copy of the prefab
  (`RentModel` → `Instantiate`), parents it to the picture, localRotation (90,0,0), localScale = `hand.Mounted.lossyScale`,
  start = `hand.Mounted.position`, calls `hand.HideForFlight()`; else falls back to `Look.Streak`. `Update`: t from
  `NetworkTime.time` vs leg departs/arrives, Lerp start→`UnitAnchor.Center(target)`, spins the picture 1440°/s about
  world up; hides visuals while waiting after an arrival (`ShowVisuals(false)`), finishes on the home leg or after a
  0.6 s grace.
- **Hand** `WeaponSocket.cs` — `HideForFlight`/`ShowAfterFlight`/`ResetFlights` (count of blades out), `ShownModel`,
  `Mounted`, `OnMyAvatar`/`MarkMine`/`Of(drawnBody)`; `Player3DBody.cs` calls `MarkMine` (the local player's model is a
  SEPARATE root object, not a child of the avatar). `NetMob.PutBodyBack` resets the count.
- Also this session (all built, uncommitted, unplayed): Sniper magic crit + magic shot (`BasicSwing.LandMagicShot`,
  `WeaponTypes.MagicCanCrit/ShootsMagic`, `StrikeMath`), Extra Bounce Jumps sub-stat, bounce settings on
  `SkillTickBinding`, new effects (Spend All Mana, Drop Health To One, Drain Mana, strike growth), shoe words Anchored /
  Wayfarer, shadow look tool (Mob Table) + fixed looks + ids renamed to `mob.shadow.<kashira>`, world map (Hazemere,
  Luminhal city, Narrowbend, Iron Gate gone), creatures filed by map (`MonsterAI3D.ActiveIn`), flurry mastery-lift fix,
  launcher shows its platform manifest's version, action plan thinned. See memories `one-arrow-for-every-bow`,
  `publish-one-platform-leaves-other-stale`, `flurry-beats-lost-mastery-lift`.

## Uncommitted working-tree changes (measured 2026-10-06 22:21)
360 entries: 187 modified, 62 deleted, 111 untracked — this session's work plus earlier sessions' uncommitted work
(nothing committed since `c850fb60`). The 62 deletions are the 14 shadow creature + prefab files renamed (old names
deleted, new `Shadow_<Kashira>` untracked) and the 3 shields removed in the previous session. Bug-relevant files:
`Flights.cs`, `FlightMath.cs`, `FlightPictures.cs`, `StrikeMath.cs` (untracked); `Arrow.cs`, `GameNetwork.cs`,
`MonsterAI3D.cs`, `Player3DBody.cs`, `Projectile3D.cs`, `WeaponSocket.cs` (modified).

## What's left
- Make the flying copy visible on a real client, then play-test the whole batch (checklist in the 2026-10-06
  session's last report: throw, swung Shuriken, die mid-flight, bow, Sniper, shoe words, shadow looks, Mob Table tool,
  world map, Lastline untouched, Skill Composer).
- Builds: server rebuilt 21:49 and map server restarted 21:50; version 2026.10.06-2144 published for Mac (Windows
  publish state not re-checked). The launcher change needs `tools/build-launcher.sh` + a publish.

## Dead ends
**Proven facts (measured):**
- Fix 1 (the hand lookup): the local player's 3D model is a separate root object, so the old
  `thrower.GetComponentInChildren<WeaponSocket>()` found nothing and the picture fell back to the white streak, hand
  never emptied. Fixed with `WeaponSocket.Of` + `MarkMine`.
- The user's first retest still showed the streak because the Mac never got the fix: `publish-update.py` with no flags
  published only Windows; the Mac manifest stayed at 18:07 while the launcher printed the shared 2001 version.
- After republishing (2144, Mac DLL contains `MarkMine`/`OnMyAvatar`): the streak is gone and the HAND EMPTIES — so the
  `HeldWeapon` branch ran, the copy was created and `HideForFlight` ran (`FlightPictures.cs:114-128` is its only caller).
- Bounce damage works (several creatures take numbers).
- The debugger ruled out from code/assets: renderer/layer/material (Sword2_3_3 prefab, layer 0, opaque URP Lit, camera
  mask all), scale (copy = hand copy's lossyScale ≈ 0.203 → weapon ≈ 0.25 u long vs a 0.327 u tall body — derived
  from assets, not measured live), position (start = hand, end = target centre), and the client clock hiding legs on
  arrival (Mirror clamps the client timeline to ≤ the newest snapshot, `SnapshotInterpolation.cs:125-138`).

**Hypotheses (NOT measured):**
- Drawn but effectively invisible: a dark 0.25 u mesh moving 14 u/s (≈ its own length per frame at 60 fps), legs of
  0.07–0.25 s, spinning 1440°/s — the old streak was bright and 0.7 u long.
- The picture ends immediately (clock or `Finish` path) — would show as the hand refilling almost at once.
- The client timeline doesn't advance — the hand would never refill.

## Next single step
Ask the user ONE thing: after a throw, how long does the hand stay empty, and does the weapon come back?
(~0.5–1 s then back = visibility problem → slow the flight / add a trail; instantly back = the picture ends at once;
never back = clock). In parallel have them type `/log hit on` in chat before throwing so `logs/server.log` records
`blade N thrown at / jumps to / flies back` with real leg timings. If still unclear, a temporary client log line in
`FlightPicture.Update` (copy active, world position, lossyScale, `NetworkTime.time` vs departs/arrives) read in the
editor console during the user's own Play-mode throw.

## How to resume
```text
Read handoffs/thrown-shuriken-flying-weapon-invisible.md and continue the thrown-Shuriken flying-weapon bug from its "Next single step".
```
