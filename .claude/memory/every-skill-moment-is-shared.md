# Every skill moment is raised in shared code — creatures answer all of them (2026-09-17)

**The gap.** Four of the ten skill moments were raised in `Character.cs` and NOWHERE else, so only a person could
ever raise them: `OnParry`, `OnDamageReceived`, `OnLowHP`, and (for a creature) both swing moments. A creature
holding Last Stand or Composure held a skill that could never fire. `Health.ApplyLeech` opened with
`attacker as Character` and returned when it was not one, so a creature that dealt damage also raised no
`OnDamageDealt`, drank no life steal and leeched no mana.

**Where each one lives now — one place each, and both damage doors call it:**

- `PassiveEvents.HitLanded(victim, hpBefore, attacker)` raises `OnDamageReceived` and the `OnLowHP` falling edge.
  `PassiveEvents.LowHpFrac` (0.3) is the single owner of the threshold. Called from `Character.ApplyHitToHP` and
  from all four of `Health`'s hit intakes. Damage over time reaches neither side, same as before.
- `UnitMitigation.TryParry(ICombatUnit unit, ICombatUnit attacker)` spends the charge AND raises `OnParry`.
- `MonsterAI3D.LandOneSwing` routes a creature's bite through `BasicSwing.Land`, which is where
  `OnBasicAttackStart` and `OnBasicAttackDealt` are raised. It used to roll crit/accuracy/damage inline —
  identical numbers, but announcing nothing.
- `Health.ApplyLeech` now takes `ICombatUnit` throughout. `ICombatUnit` gained `LifeStealPct` and `LeechHeal`.

**`DamageInfo.source` (the `MonsterAI3D`-typed handle) is GONE.** `DamageInfo.attacker` (`ICombatUnit`) is the only
one. The old one was creature-only, so a blow from another PERSON reached the player's parry, reflect and
damage-received moments with nobody named, and a creature's SKILL strike named nobody either.

**A WATCHED COPY NOW ANSWERS NOTHING:** `PassiveEvents.Fire` returns immediately when `actor.IsWatchedCopy`.
Note what that covers — on a client, `NetPlayer.TakeTheServersWord` marks the local player's OWN `Character` as a
watched copy, so a client no longer runs its own passives at all. The server runs them and announces the cast.
Anything that wants a local reaction to a moment must be driven by the server's announcement, not by a local fire.

See [[creatures-cast-weapon-kit]], [[whole-skill-runs-on-the-server]], [[mobs-get-everything-players-get]].
