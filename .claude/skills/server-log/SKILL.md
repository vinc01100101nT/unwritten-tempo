---
name: server-log
description: Use whenever you play-test, hunt a bug, verify a fix, review a change that writes log lines, or need to know what the game server actually decided — a blow, a death, a kill's loot and experience, a passive firing or being skipped, a buff spent or running out, damage over time, a heal. Covers switching log categories on, reading a slice of the log, the facts that stop wrong reports, and the two scripts that total and check a play-test's log.
---

# The server log

**The game server's log is the record of what the world actually decided.** Blows, deaths, loot, experience,
passives, states and heals are decided on the server, and its log writes each one down with the time to the
millisecond and every unit named by map and spawn_id (`field_15 Snake #3`). A player's machine only knows what it
drew. When a question is "did X happen, and why", read this log before reasoning from source code.

The full list of line shapes, categories and recipes is the guide: `docs/0-process/reading-the-game-log.md`.
Open it when you need a shape you do not see below. This skill is what you must know without opening it.

## 1. Before acting: the categories you need must be ON

Most detail is off by default: `hit`, `passive`, `buff`, `cast`, `command`, `heal`. A run with them off is a wasted
run — the answers were there and switched off.

- **At server start:** `LOG_CATEGORIES=hit,passive,buff,heal` (or `all`) in front of the run command in
  `docs/2-systems/networking/starting-the-network.md`. The server writes `LOG_CATEGORIES applied — …`.
- **While it runs:** the GM chat command `/log hit on`, `/log all on`, `/log` to list — it runs on the server.
- **A player's own machine:** `GameLog.Set(LogCat.Hit, true)` / `GameLog.SetAll(true)` from code.

## 2. Read only what your action wrote

```bash
before=$(wc -c < logs/server.log)
# … do the thing …
tail -c +$before logs/server.log
```

The log is `logs/server.log` at the top of the repo; the run before is `logs/server-prev.log`.

## 3. Facts that stop a wrong report

- **Every line starts `YYYY-MM-DD HH:MM:SS.mmm [category]`.** A line with no date was written by an older server
  build — the new lines do not exist in it. Say so; do not report their absence as a bug.
- **Units are `<map> <name> #<n>`.** Numbers repeat across maps, so always search map and number together. A
  creature keeps its number when it comes back; a player gets a new one per map entered.
- **Around a death the order is: `died` → `loot`/experience/on-kill passive lines → the killing blow's `hit` line**,
  all in the same millisecond. Do not read the hit line after a death as a blow on a corpse.
- **`(after it died)` blows took no health.** Never count them as damage.
- **`with basic attack`** means no skill was resolving: a swing, a bite, an arrow.
- **`rolled` vs `for`:** rolled is before the target's defences; `for` is what came off its health; `[HP now/max]`
  is its health after.
- **A zero-damage blow says why** after `— nothing got through:`. `reason not recorded` is a gap in the code —
  report it.
- **Silence is information, and the log now says which kind:** `earned nothing from X` means the payout ran and
  rolled nothing; no loot line at all after a player's kill means the payout never ran. `skipped <skill> — <reason>`
  means a passive was turned away; neither `fired` nor `skipped` means the skill does not answer that moment.
- **`Parry … : 1 spent` vs `1 ran out`** tells a charge used by a blow from one that expired.
- **Damage over time and heals have their own lines:** `took 4 from Poison by Alpha #1`, `healed 10 by <skill>`. A
  damage-over-time kill is credited to whoever put the ailment on.
- **Not written:** mana changes, and anything a player's machine decided about its own screen. For those, put a
  recorder inside the game.

## 4. After a play-test: the two scripts

```bash
tools/check-server-log.py        # flags what must never happen
tools/server-log-tally.py        # totals per unit: blows, skills, damage, states, passives, heals, experience
```

`check-server-log.py` exits **0** clean · **1** flagged (each flag prints its line) · **3** no hit lines at all (the
category was off) · **4** hit lines in an older build's shape. **3 and 4 are never a clean result** — say which one
you got. Its flags: a kill that paid nobody, a player's kill with no loot line, hurt while invincible, a unit with no
spawn_id, a spawn_id handed out twice, a zero with no reason.

## 5. Quote the line

When a log line answers the question, **that line is the evidence** — quote it with its time in the report, and do
not build an instrument to re-measure what the server already wrote.

## 6. When a change adds or rewords a log line

- Put the map first and name units with `SpawnIds.MapOf(unit)` and `SpawnIds.Label(unit)`.
- A new way for a blow to do nothing returns through `CombatAuthority.NoDamage(reason)` with its words added.
- Build the string only behind `GameLog.On(category)` — these paths run per blow.
- Update the shape in `tools/server_log_lines.py`, re-run both scripts on a sample line, and update the guide in the
  same change.
