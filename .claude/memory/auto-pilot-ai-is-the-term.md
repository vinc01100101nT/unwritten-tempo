# "Auto-pilot AI" is a creature's fighting; the player's T is "auto-combat" (2026-09-17, split 2026-10-12)

**Auto-pilot AI** is a creature's combat behaviour — pick a target, close, swing, choose a skill (`MonsterAI3D`).
The mode a person hands their character to with **T** was renamed **auto-combat** in the 2026-10-11/12 QoL plan: a
server-owned on/off, three hunt modes, auto-skills, auto-loot and auto-potion, its own window, and the on-screen tag
**AUTO-COMBAT — press T to stop** (`AutoPilotIndicator`, `Player3DController.Order.AutoPilot`, `AutoCombatClient`).
See `docs/2-systems/auto-combat.md`. If the user still wants one word for both, the code names are where to change it.

**How a creature picks a skill, decided by the user on 2026-09-17:** the ULTIMATE first whenever that is off cooldown
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
