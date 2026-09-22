# "Auto-pilot AI" is the term for both a creature's fighting and the player's T (2026-09-17)

The user's word, set on 2026-09-17: **auto-pilot AI** covers a creature's combat behaviour AND the mode a person
hands their character to with **T**. They are one idea — pick a target, close, swing, choose a skill — so they get
one name. "Auto-combat" is retired; the on-screen tag reads **AUTO-PILOT — press S to stop**
(`AutoPilotIndicator`, `Player3DController.Order.AutoPilot`).

**How it picks a skill, decided by the user in the same breath:** the ULTIMATE first whenever that is off cooldown
and affordable, then one of the ready ordinary actives at random. Since 2026-09-22 a creature that summons casts its
SUMMON before either, whenever it is ready and not everything it may have out is out (see [[creature-kinds-and-world-spawns]]). It never saves mana for a bigger skill and never
reaches for the most expensive one it can pay for. Measured 2026-09-16: a level 3–5 creature holds 17–23 MP, so no
40-MP ultimate is affordable below about level 11 — seeing only cheap skills on a low creature is correct.

**Still to come, named but not built:** deliberate skill COMBOS — an authored sequence the auto-pilot AI plays in
order. Nothing about combos exists yet.

**A creature's cast is no longer gated on biting reach.** `MonsterAI3D` weighs its skills whenever it is chasing
and its own `nextCastTry` clock is due (one look per swing interval, so a field of them costs nothing per frame);
each skill is then judged by its OWN `castRange` through `Reaches`. Before this, a creature had to be inside its
bite range before it was even asked, so a Lunge with a range of 8 could only ever fire from under 1.

See [[creatures-cast-weapon-kit]], [[every-skill-moment-is-shared]].
