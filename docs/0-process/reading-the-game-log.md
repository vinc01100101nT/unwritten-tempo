---
status:
owner: vince
last-updated: 2026-09-15
tags:
  - process
---

# Reading the Game Log

**The standing way to find out what the game actually did.** Every meaningful moment writes one line. Each line
starts with the time to the millisecond, belongs to a named category, and names every unit by the map it is in and
its spawn_id — so two snakes in one field are two different names. You switch categories on and off to decide what
you are told, and two scripts read the whole log for you after a play-test.

Use it whenever you are play-testing, hunting a bug, or driving the game through a tester. It is written for
people and for every agent role alike: the tester, the debugger, the reviewer, and the main session.

---

## Where the logs are

| Machine | File | Run before it |
| --- | --- | --- |
| The game server | `logs/server.log` at the top of this repo | `logs/server-prev.log` |
| A client build | `tools/platform.sh player-log` — `~/Library/Logs/DefaultCompany/unwritten/Player.log` on the Mac, `AppData/LocalLow/DefaultCompany/unwritten/Player.log` in your user folder on Windows | `Player-prev.log` |
| The Unity editor | `tools/platform.sh editor-log` — `~/Library/Logs/Unity/Editor.log` on the Mac, `AppData/Local/Unity/Editor/Editor.log` in your user folder on Windows | — |

**The server's log is the one that knows.** Blows, deaths, passives, buffs and loot are decided on the server, so
only its log can say what really happened. A player's machine draws what it is told and its log only says what it
drew. The server's log is only kept when the server is started with the run command in
[Starting the Network](../2-systems/networking/starting-the-network.md).

---

## What a line looks like

```
<time> [<category>] <map> <unit> <what happened>
```

- **Time** — `YYYY-MM-DD HH:MM:SS.mmm` in the machine's own time zone. On the server it is that machine's own clock; a
  player's machine counts from the time the server gave it on arrival. Two lines 0.250 apart happened a quarter of a
  second apart, and the date keeps a play-test that runs past midnight in order.
- **Category** — the word in square brackets. Filter on it.
- **Map** — the scene name of the map the line is about, e.g. `lst_fild01`, `lastline`.
- **Unit** — the unit's name, a space, `#`, and its spawn_id: `Snake #3`, `Alpha #1`, `Training_Dummy #4`.

Lines about the server itself (start-up, connections) have no map or unit. Unity's own start-up text, printed
before any game code runs, has no time and no category.

### Every line that names a unit

| Category | Shape |
| --- | --- |
| `spawn` | `<map> <name> #<n> (<mob id>) appeared` — a creature or townsperson came into the map (a townsperson's name is `Townsfolk_<their name>`, and their model name is in the brackets). A townsperson's line goes on: `at x <x>, y <y>, z <z> — ground y <g>, <gap> above it`, or `NO GROUND FOUND under it` when it was placed before its map's ground existed. The player's own log (`Player.log`, category `client`) writes the same for the body it draws: `<map> townsperson '<name>' drawn at x …, y …, z … — ground y …` — side by side, the two say which machine put a townsperson in the air |
| `spawn` | `map '<map>' given <n> spawn area(s), <m> creature(s).` — the server read this map's rows from the world spawn list as it loaded it |
| `spawn` | `map '<map>' given <n> townsperson(s).` — the server read this map's people from the townsfolk list as it loaded it |
| `spawn` | `townsperson '<name>' in '<map>' has no model — left out.` — a row of the townsfolk list names no body, so that person was not made |
| `server` | `<player> bought <n> <item id> from '<shop>' for <g> gold`, ending `(<f> free, a tutorial's first run)` when a tutorial's first run gave some of them free / `<player> sold <n> <item id> to '<shop>' for <g> gold` / `<player> bought back <n> <item id> (copy <id>) at '<shop>' for <g> gold` (no copy part for a stack) — a shop deal the server let through · `<player>'s oldest kept sale, <n> <item id> (copy <id>), is gone for good: more than 10 kept` — an eleventh sale pushed the oldest off the buy-back list |
| `server` | `<player> set their save point at the Keeper in '<map>'` (the town only — no spot is kept) · `<player> paid <g> gold for a Keeper's trip from '<map>' to '<map>' (<n> doors)` · `<player>'s Keeper trip to '<map>' did not happen — <g> gold handed back` — the Keeper's Save Point and Teleport, as the server let them through or undid them |
| `server` | `<player> is storing <n> <item id> (copy <id>) — move <move id>` / `<player> is taking <n> <item id> (copy <id>) out of storage — move <move id>` (no copy part for a stack) — a storage move started; `storage <store or take> of <n> <item id> (copy <id>) for character <id> written down` — the save that carried it landed · `storage <store or take> of <n> <item id> for character <id> was refused (<why>) — sending the save again without it` — the character server would not make the move, and wrote nothing · `storage <store or take> of <n> <item id> for character <id> was let go — the save that carried it will never be written` · `could not read <player>'s storage — <why>` |
| `server` | `<player> asked to walk through a door to '<map>' from '<map>' at <where> — there is no such door within 3 m of them. Refused.` — a map change was asked for with no door of the world layout in reach · `GM <player> walked through a door to '<map>' from '<map>' with no door of the world layout in reach — let through` — a game master, on a door the layout does not list (the test maps) |
| `server` | `<player> said they are in '<map>' but the server has them in '<map>' — refused.` — their machine named a map other than the one the server put them in; the server's stands · `<player> was sent into '<map>' with no arrival point the server can name — their next position is taken as read` — a door or `/tp` with no arrival point named, the one trip still taken on trust · `<player> did not report arriving where the server sent them in '<map>' <where> within 2s — standing them there` — after a trip the server ordered, their machine had the map and still had not reported the spot (said once per trip; the stand-there repeats every 2 s) |
| `server` | `<player> is going back to their save point (<item id>)` (no brackets when no item paid) · `<player>'s trip did not happen — <item id> given back` — the wing that paid for a trip that failed, or one owed to somebody who fell first · `<unit> found nowhere to land on a random spot of '<map>'` — a wing's trip, or a Fly Wing with no walkable cell to land on |
| `server` | `<map> <player> picked up <item> — it had lain <s>s`, ending ` and fell for <unit>` when it was somebody else's kill · `<map> <player> could not pick up <item> — <why>` (`it is <d> away and the reach is <r>`, or `it is still kept for <unit>`; one a second at most) · `<map> <player> had no room: <sentence>` — a pickup or an auto-loot refused because a bag tab is full or it is too heavy, in the sentence the player was shown (a craft refused for the same reason is written as that craft's refusal) |
| `server` | `<map> <item> vanished untaken after <s>s — it fell for <player>` — a kill's drop nobody picked up before it ran out |
| `server` | `<map> <player> put down <item>` — dropped from their bag onto the floor |
| `server` | `<map> <player> equipped <item> (<slot>)`, ending ` in place of <item>` when it replaced one · `<map> <player> took off <item> (<slot>)` |
| `server` | `<map> <player> used <item> [HP <now>/<max>] — <n> left` — a potion or food eaten; the health is after it took effect |
| `server` | `<map> <player> changed their hotbar: set slot <n> to <entry>, cleared slot <n>` — only the slots that moved |
| `server` | `<map> <player> spent a skill point on <skill id>` · `<map> <player> was refused a skill point on <skill id> — <why>` |
| `server` | `<map> <player> crafted: <sentence>` · `<map> <player> was refused a craft: <sentence>` — every forge, fold, quench, ink, recycle and merge press, in the same sentence the player was shown |
| `server` | `<map> <player> started talking to <townsperson id>` · `… stopped talking to <townsperson id>` |
| `server` | `<map> <player> accepted quest <quest id> from <townsperson id>` · `… started following quest <quest id>` / `stopped following any quest` · `… pressed Play on tutorial <quest id> — <Started / NotMet / …>` · `… reached step <i> of <n> (<step id>) of quest <quest id>` — the gap between two step lines is how long the step took |
| `server` | `<map> <player> reached level <n>` · `<map> <player> was killed by <unit> at level <n>` — a player's death is written here even with `hit` off |
| `server` | `<map> <player> said: <text>` — a line said in the room's chat; a private message is never written |
| `server` | `<name> (<player id>) stopped playing after <m>m <s>s — level <a> → <b>; <k> kill(s) for <xp> experience and <g> gold; died <d> time(s); <f> thing(s) fell for them, <v> vanished untaken; picked up <p>; finished <q> quest(s); maps: <map>, …` — one line per visit, written when they leave the game; walking through doors on the same map server is the same visit |
| `spawn` | `<map> <name> #<n> came back` — a creature that died is alive again, same body, same number |
| `spawn` | `<map> <name> #<n> (<player id>) entered` — a player is now in this map |
| `spawn` | `<map> <name> #<n> got up [HP <now>/<max>]` — a player who died is standing again, same number |
| `hit` | `<map> <attacker> hit <target> with <skill> for <dealt> (rolled <rolled>)` — then `(critical)`, `(after it died)`, `[HP <now>/<max>]` and `— nothing got through: <reason>` when they apply, in that order |
| `hit` | `<map> <attacker> MISSED <target> with <skill>` |
| `hit` | `<map> <unit> died — killed by <unit>` — or `killed by nobody` when no attacker was recorded |
| `hit` | `<map> <unit> took <amount> from <cause> by <unit> [HP <now>/<max>]` — damage that is not a blow: `Poison`, `Bleed`, `Burn`, `reflect`, `brace debt`, `GM kill`. `by <unit>` appears when the damage is credited to somebody — an ailment's ticks are credited to whoever last put it on, so a kill by poison pays the poisoner |
| `hit` | `<map> CANNOT REACH <target> — it is <distance> away and this weapon reaches <reach>` |
| `hit` | `<map> <thrower> blade <n> thrown at <target> (hop 0)` · `… blade <n> jumps to <target> (hop <k>)` · `… blade <n> flies back[ — <why>]` · `… blade <n> ends — <why>` — one flight in the air: an arrow, a thrown shuriken or a bouncing skill. `<n>` ties a flight's lines together; each hop's blow is its own `hit` line. `<why>` is `out of jumps`, `nobody left to jump to`, `the thrower died`, `the thrower changed weapon`, `the thrower changed map`, `the thrower is leaving`, `the thrower left`, `the thrower is gone`, `the thrower died and came back` or `nobody near the first hit` |
| `heal` | `<map> <unit> healed <amount> by <skill> [HP <now>/<max>]` — `by life steal` for life steal; no `by` for regeneration and `/heal` |
| `buff` | `<map> <state> x<stacks> on <unit> from <skill id> for <seconds>s` — a state arriving or growing |
| `buff` | `<map> <state> x<stacks> on <unit> from <skill id> restored from the save, <seconds>s left` — a state a character was carrying when it logged out or crossed to another server, back on it with the time it had left |
| `buff` | `<map> <state> on <unit> from <skill id>: <n> <how>, <left> left` — a state losing stacks; `<how>` is `spent`, `ran out`, `used up` (a shield barrier broken by damage), `dropped over the stack limit`, `removed` or `cleared` (every state at once, as the unit starts a new life) |
| `passive` | `<map> <unit> fired <skill id> on <trigger> at <target>, <distance> units away` |
| `passive` | `<map> <unit> skipped <skill id> on <trigger> — <reason>` — `not switched on`, `target <d> units away, cast range <r>`, `already part of this chain`, `its required state is not met` |
| `cast` | `<map> <unit> casts <skill>` · `<map> <unit> could not cast <skill> — <reason>` · `<map> <unit> weighed its skills: <skill> <ready or reason>; … — picked <skill>` or `— none ready, swinging` (a creature, every active and ultimate its weapon gave it — written when it picks a skill, and a `none ready` line only when the reasons changed since its last one, never once per swing) |
| `loot` | `<map> <unit> (<player id>) earned <gold> Zeny and dropped <items> for them from <unit>` — the Zeny went into their purse, the items onto the ground, held for them; several of one item are one pile, written `Iron ×7`; either half is left out when there is none, and `earned nothing from <unit>` when both are; every kill a player earns writes one, except a summoned creature's, which pays nothing and writes nothing |
| `loot` | `<map> <unit> (<player id>) earned <amount> experience from <unit> — combo <n>`, then `, +<m> from it` when the run of kills added some — the kill's experience, on its own line, after the world event's rate and the run's bonus; `<n>` is the run this kill brought them to |
| `loot` | `<map> <unit> turned back into <character id> (<NeverFights / Ally / Rebel>) for <player>[, who was paid <n> gold — combo <c>[, +<m> from it]]` — a beaten creature turned back into its character; a never-fights one's gold also writes the `earned <gold> Zeny … from <unit>` line above, from the character. A death itself pays no Zeny, so a kill's line names items only |
| `server` | `<map> <unit> invincibility on` / `off` — written every time `/invuln` is run |
| `server` | `<name> (<player id>) entered the world — <n> player(s) here` · `player left — connection <n> — <name> (<player id>)` · `reviving <name> (<player id>), who was dead.` |

**`with`** names the skill whose effects were resolving when the blow landed — a cast, a passive, a flurry beat — or
`basic attack` for a swing, a bite or an arrow. **`rolled`** is the blow as it arrived, before the target's defence,
element, shield barrier or brace touched it. **`dealt`** is what came off its health; the gap between the two is what
the target's protection took. **`[HP]`** is the target's health after the blow.

**`(after it died)`** marks a blow that reached a unit already dead — a flurry beat, or a swing or arrow already on
its way. It prints the number that was shown on screen, but took no health. Never add it to damage totals; the
tally script leaves it out.

**Around a death, the lines come out in this order.** A unit dies inside the blow that kills it, so its `died` line —
and the `loot` and on-kill `passive` lines that death sets off — are written BEFORE the `hit` line of the killing
blow, in the same millisecond. The first `hit` on a unit after its `died` line that is not marked `(after it died)` is
the blow that killed it.

### Why a blow did nothing

A `hit` line that dealt 0 says why, after `— nothing got through:`

| Words in the line | What happened |
| --- | --- |
| `parried` | The target spent a parry charge and the blow was erased |
| `blocked` | The target blocked it, with block chance a skill granted |
| `soaked by a shield barrier` | A Shield skill's barrier ate all of it |
| `held back by a brace` | A Brace charge held it back — the damage is owed and comes due when the brace ends |
| `immune to that element` | The target's element, at its level, fully resists the blow's element |
| `healed by its element` | The target's element, at its level, turns the blow into a heal — the `heal` line beside it says how much it gained |
| `invincible` | The target has GM invincibility on (`/invuln`) |
| `dodged` | The target was dashing through it |
| `already dead` | The target was dead when the blow was resolved |
| `reason not recorded` | A path that returns nothing without saying why — **report it**, it is a gap to close |

A miss is its own line (`MISSED`), not a zero-damage hit.

---

## Spawn_ids

A spawn_id is a number that tells units apart **inside one map's lines**. It is not the mob id (every snake shares
one) and not the player id.

- **Counted per map.** Every map has its own count, starting at 1 when the server starts.
- **Never restarts while the server runs.** A map nobody is in is let go and brought back later; its count carries
  on, so `lst_fild01 Snake #3` never means two different snakes in one log. A server restart starts every count again.
- **A creature keeps its number through death.** A snake that dies and comes back is the same body, and says
  `came back` with the number it had. A creature that is new gets the next number.
- **A map that is let go takes its creatures with it.** When it is brought back its creatures are new, with new
  numbers.
- **A player gets a number each time they enter a map.** Dying and getting up in the same map keeps it. Walking into
  another map, or reconnecting, gives a new one. The `entered` line carries their player id, which is what joins
  their numbers across maps.
- **The player id** is the id of their row in the `character` table. Look one up:
  `psql "$(grep '^DATABASE_URL=' settings.env | cut -d= -f2-)" -A -t -c "select id, name from character where deleted_at is null;"`
- **Townsfolk** get a number on their `appeared` line and nowhere else — they take no blows.
- **Only the server hands them out.** A player's machine logs units by plain name with no number.

---

## The categories

`loot` is on by default beside the first seven, because a kill's pay-out is the only record of itself — nothing is
left lying in the world to look at afterwards. The six below it are off by default and cost nothing until switched
on.

| Category | What it writes | Default |
| --- | --- | --- |
| `unwritten` | Start-up and general engine-side notes | on |
| `server` | The machine that owns the world, talking about itself — and every choice a player makes, from picking something up to leaving | on |
| `net` | Messages crossing between machines | on |
| `client` | A player's own machine | on |
| `map` | Maps loading, handing over, being let go | on |
| `spawn` | A unit appearing, coming back, entering a map, or a player getting up, with its spawn_id | on |
| `census` | A routine head-count — nothing writes it today | on |
| `loot` | What a kill paid, who it went to, and which unit paid it | on |
| `hit` | A blow landing or being turned away and why, damage that is not a blow, and a unit dying and who killed it | **off** |
| `passive` | A passive skill firing — or skipped, and why | **off** |
| `buff` | A state arriving, growing, losing stacks or going | **off** |
| `cast` | A cast accepted, or refused with its reason | **off** |
| `command` | Every command run | **off** |
| `heal` | A unit gaining health, and which skill gave it | **off** |

---

## Switching them

**The server, from the start command** — no typing after every restart. Put `LOG_CATEGORIES=` in front of the run
command in [Starting the Network](../2-systems/networking/starting-the-network.md):

```
LOG_CATEGORIES=hit,passive,buff,cast   # these on, on top of the defaults
LOG_CATEGORIES=all                     # everything
```

It only switches categories on. The server writes `LOG_CATEGORIES applied — …` listing every category's state, and a
word that is not a category is refused in a warning that names it.

**The server, while it runs — in the in-game chat window, GM only:**

```
/log                      every category and whether it is on
/log hit on               switch one on
/log spawn off            silence one
/log all on               everything
/log all off              quiet — warnings and errors still print
/log reset                back to the defaults
```

The command is carried to the server and run there, and the answer comes back into the chat log.

**A player's own machine — from code.** A tester does this; it needs no chat window:

```csharp
GameLog.SetAll(true);
GameLog.Set(LogCat.Hit, true);
GameLog.Set(LogCat.Spawn, false);
```

A tester normally wants both: the server's switches for what was decided, and its own for what was drawn.

---

## The two scripts

Run them on the server's log after a play-test. Both need `hit` switched on while playing, and both say so when the
log has no hit lines — an empty result from a log with nothing in it is not a clean result. Both read the line shapes
from one shared file, `tools/server_log_lines.py`, so a line whose wording changes is changed there once.

**`tools/server-log-tally.py` — totals per unit.**

```
tools/server-log-tally.py                                  # logs/server.log
tools/server-log-tally.py logs/server-prev.log             # the run before
tools/server-log-tally.py --map lst_fild01                   # one map only
tools/server-log-tally.py --since "2026-09-15 23:27:00"    # only from this moment on
tools/server-log-tally.py --since 23:27:00                 # the same, on the date of the log's first game line
```

For every attacker the columns are: `swings` (every hit and miss line), `miss` and `miss%`, `hits` (blows that took
health), `0 dmg` (blows that landed but took nothing), `crits`, `dealt`, `rolled`, and `dead` (blows that reached a
unit already dead, left out of every other column). Under each row: the damage it dealt with each skill, and the
reasons its zero-damage blows got nowhere. Then the damage every unit took from blows, the damage it took from
everything else by cause and by whom, what it was healed and by what (needs `heal`), what each state did — times
granted, stacks spent, ran out, used up (needs `buff`) — what each passive did — times fired, times skipped for each
reason (needs `passive`) — the experience each player earned (needs `loot`), and who killed what. Given a log written
by an older server build, it says so instead of printing empty totals.

**`tools/check-server-log.py` — flags what must never happen.**

| Exit | Meaning |
| --- | --- |
| 0 | Hit lines were read and nothing was flagged |
| 1 | Something was flagged — each flag prints its line number and the line |
| 2 | No log file at that path |
| 3 | The log has no hit lines at all, so there was nothing to check — **not** a clean result |
| 4 | The log has hit lines, but none in a shape these checks can read — it was written by an older build of the server. Rebuild and restart it. **Not** a clean result |

| Flag | What it means |
| --- | --- |
| `a kill that paid nobody` | A unit died `killed by nobody` although something had cost it health in that life, the killing blow included |
| `a player's kill with no loot line` | A creature killed by a player wrote no loot line in that life. Every kill a player earns writes one, `earned nothing` included, so silence means the payout never ran. Only checked when the log has loot lines at all — the script says when it could not |
| `a zero with no reason` | A blow that dealt nothing says `reason not recorded` — a way of turning a blow away that does not yet say why |
| `hurt while invincible` | A blow dealt damage to a player whose invincibility was on |
| `a unit with no spawn_id` | A hit, miss or death line named a unit without `#number` |
| `a spawn_id handed out twice` | Two units appeared or entered with the same map and the same number |

---

## Recipes

**Read only what one action wrote.** Record the log's size first, then read from there:

```bash
before=$(wc -c < logs/server.log)
# … do the thing …
tail -c +$before logs/server.log
```

**"Did that blow land, and on which one?"** — `grep '\[hit\]'` the slice. The line names both units by number, what
was rolled, what was dealt, and why a zero was a zero.

**"Did it really die, who got the kill, and did it come back?"** — the `died — killed by` line, then the same
`<map> <name> #<n>` on a `came back` line.

**"How far apart were those two things?"** — subtract the two times. Everything on one line of the log is stamped
the moment the server did it.

**"Everything that happened to one unit."** — `grep 'lst_fild01 Snake #3'` (map and number together, since numbers
repeat across maps).

**"Everything one player did across maps."** — find their `entered` lines by player id, note the `<map> <name> #<n>`
each gave, and grep for those.

**"How did a play-tester play?"** — start from their `stopped playing after` line: it sums the visit in one line. Then
read their `[server]` lines in order — picked up, equipped, used, crafted, talked, quest steps, deaths, what they said —
which are on by default and tell the visit as a story. Things that `vanished untaken` against `picked up 0` means they
never found how to loot; a quest step with a long gap before the next is where they got stuck.

**"Is this mob's damage right?"** — `tools/server-log-tally.py --map <map>` and compare rolled against dealt per
attacker; `(critical)` counts are in the same row.

**"Which skill did that damage?"** — the `with` on the hit line, or the per-skill rows under the attacker in the
tally.

**"Did that kill pay, and what?"** — the `loot` line ending `from <name> #<n>`, just after that unit's `died` line. `earned nothing` means the payout ran and rolled nothing; no line at all is the bug the checker flags.

**"Why didn't my passive fire?"** — switch `passive` on and look for `skipped <skill id>`: the reason is on the line.
A passive with neither a `fired` nor a `skipped` line was never asked, which means its skill does not answer that
moment.

**"Was the parry charge used, or did it run out?"** — `buff` lines for `Parry`: `spent` means a blow used it, `ran
out` means its time ended first.

---

## What the log does not write

- **Mana** — no change to a unit's mana writes a line.
- **Numbers on a player's machine** — its log names units without spawn_ids and never knows why a blow did nothing.
- **What a player looked at** — a window opened, a tooltip read, a thing hovered: those happen only on their own
  machine, and the server is never told.

When a question needs one of these, put a recorder inside the game instead — see the related page below.

---

## Three rules that make it trustworthy

**A warning or an error always prints, whatever the switches say.** Categories decide how much routine detail you
get; they never hide a problem.

**An off category costs nothing.** The check is one bit test, and every line that builds a string sits behind it:

```csharp
if (GameLog.On(LogCat.Hit)) GameLog.Info(LogCat.Hit, string.Concat(name, " hit for ", dmg.ToString()));
```

**The log says what it is not telling you.** When the server starts, and again every time somebody enters the world,
it writes which categories are switched off and how to switch them on. That line lands at the tail of the log right
as a tester starts reading, and it prints even with every category off.

---

## Adding to it

**A new category:** one entry in the category list and one word beside it in the log's code. The start-of-world
notice, `/log` and `LOG_CATEGORIES` all read that list. Name it for one kind of thing that happens — `hit`, not
`combat`.

**A new line that names a unit:** put the map first and name units through the spawn_id helpers — `SpawnIds.MapOf`
and `SpawnIds.Label` — so the line matches every other one and the scripts can read it. A unit named any other way
shows up as `a unit with no spawn_id` in the checker.

**A new way for a blow to do nothing:** return through `CombatAuthority.NoDamage` with a reason, and add its words to
the table above. A path that returns a bare zero shows `reason not recorded`.

---

## Related

- The **`server-log` skill** (`.claude/skills/server-log/SKILL.md`) — the short version every tester, debugger and
  reviewer is given when it starts: switching categories on, reading a slice, the facts that stop a wrong report, and
  the two scripts. It points here for line shapes rather than repeating them.
- [Starting the Network](../2-systems/networking/starting-the-network.md) — standing the world up, the run command,
  and `LOG_CATEGORIES`.
- [Put the Recorder Inside — Don't Stop the World](../5-ai-learnings/put-the-recorder-inside-dont-stop-the-world.md)
  — for what the log does not write.
- [Querying the Database](../1-planning/querying-the-database.md) — reading a character row by the player id a line
  carries.
