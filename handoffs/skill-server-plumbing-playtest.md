# Handoff — play-testing the skill & server plumbing changes of 2026-09-03

## Task

Play-test eleven changed game files from 2026-09-03. All of them are server and skill plumbing, and **the
intended result of every single one is that NOTHING LOOKS DIFFERENT** — any visible difference is a failure.

## Status

**IN PROGRESS.** The code is written, compiles clean on all three builds, and the highest-risk parts have been
watched working in a real play session. About half the checklist is still unrun.

Four tester runs were spent getting here: two died on account usage limits, one was cancelled mid-run, and the
fourth completed and produced everything in the "passed" list below.

**Nothing in this file is a reasoned claim.** Every PASS was watched happening in a running game. Anything not
watched is listed as unrun or inconclusive, never as working.

---

## What was changed today, and what it risks

1. **Skill components are built ON DEMAND** — the first time a skill is pressed, or the first time one of the
   player's passives fires — instead of all 42 being built the moment a character is made. A person now joins
   costing zero skill components instead of 42. *Risk: a dead slot, or a passive that never fires.*
2. **Passive dispatch asks only the skills that authored a moment**, using an index built when a character's
   skill list is bound. A landed basic swing went from **126 checks to 4**. *Risk: a passive that stops firing.*
3. **Casting looks a skill up directly** instead of comparing names one at a time.
4. **Buff updates no longer carry the buff's two-letter tag or the skill's name** — the player's machine reads
   both off the skill asset — and the hover sentence is sent **once**, when the buff first appears, rather than on
   every update. *Risk: a blank buff tile, or hover text that vanishes.*
5. **Distance-based interest management** now limits who receives what. Its range separates MAPS (they sit 2000
   apart) but is far wider than any one map (largest is 120x64), so people in the same map are unaffected.
6. **The server remembers who is playing on each connection** instead of searching for it on every request —
   seven call sites, including both hot ones a basic swing passes through.
7. **Players are filed by which map they are in**, so a creature only scans its own map's people instead of every
   player in the world twice a second. A player is re-filed when they change map.
8. **The buff push no longer copies its list into a throwaway array** on every change.
9. **BUG FIX: a departing player's character was never written down.** The disconnect path let go of the fighter
   *before* asking for the write — which destroys the character — so the write found nothing, gave up without
   sending a request, and reported failure. Every player lost everything since their last periodic write. The
   write is now asked for first. **This one is confirmed fixed by observation** (see below).

---

## What has been TESTED and PASSED — watched, not reasoned

- **A1** — the hotbar draws correctly on arrival: three bound slots with their letters, colours and mana costs;
  the rest genuinely empty. No blank tiles. On arriving, 42 skill positions held with only **3 built**.
- **A2** — a bound key fires on the FIRST press. Key 2 and key 3 both. An empty key does nothing and breaks nothing.
- **A3** — the ultimate fires **when given a target**. An earlier "did not fire" was purely the absence of anything
  to target, not a fault.
- **A4 — THE BIG ONE.** A never-used skill was put into an empty slot: built components went **3 → 4**, the tile
  drew its letters and cost, and it **fired on the first press** with its correct 9-second cooldown. Building on
  demand works.
- **A7** — a second press during cooldown is refused. Also observed: a second press during a wind-up is refused
  while the cooldown still reads zero.
- **C1 — both halves, frozen.** The buff tile ON SCREEN drew the letters "Br", a timer "4", and a stack badge
  "x5", with a screen capture to show it. The value behind it agreed. **The blank-tile regression is cleared.**
- **C3** — the hover card draws its full text.
- **C4 — PARTIAL ONLY.** The text survived hovering, hiding, pressing another skill, and hovering again — but the
  other skill granted no buff of its own, so *"while other buffs come and go"* was never actually met. **The
  send-once change is NOT cleared.**
- **D1** — basic attacks land on every swing; a 20-health creature died in two swings.
- **D4, first half** — died, respawned at full health at the map's start point, and skills fired on the new body.
- **E3** — returning to the first map, 31 live creatures, nearest 0.3 units.
- **F1** — signed out and back in: the bar came back exactly as left, including a skill added mid-session, and all
  four ticked passives. **Progress persisted.**
- **The save fix is confirmed by the server's own log:** zero "could NOT be written down" lines in this run,
  against three in the previous one.
- **Mana is spent correctly** — settled with a timestamping probe: 242 → 230 on a 12-mana press, refilling over
  ~8 seconds. An earlier "mana unchanged" reading was the tester's read-back arriving after the refill. **Do not
  re-investigate.**

---

## What is still UNRUN

- **A5** equip a different weapon and use a skill it grants · **A6** use a consumable · **A8** cast with too
  little mana (must be refused)
- **B1–B2 — INCONCLUSIVE, not failed.** A ticked passive on a killing blow produced no component and no buff. The
  tester could not establish whether that passive is meant to run on the player's machine or on the server, and
  refused to guess. **Settle this first among the passives.**
- **B3** parry · **B4** crossing the low-health line · **B5** tick a passive on mid-session then trigger it ·
  **B6** untick one and confirm it stops
- **C2** a buff from a skill with no authored two-letter tag must fall back to the skill's NAME · **C4** properly,
  with another buff genuinely coming and going · **C5** expire and recast · **C6** Brace's red debt tile carrying
  the same letters as the armed one · **C7** stacking to several charges and spending them
- **D2** cast + swing + item in quick succession · **D3** loot pickup updating the bag
- **E1 — UNOBSERVED, not failed.** No creature was watched chasing. The player could barely be moved (see dead
  ends), so the situation could never be set up properly, and the character's health never changed all session.
- **E2 — COULD NOT RUN.** See dead ends. **E4** dying near a creature and it stopping.
- **Everything needing two players at once** — cross-map visibility, two people in one map, a creature choosing
  between two people. **The editor is ONE client.** This needs the built client running alongside, which nothing
  here has ever driven.

---

## Dead ends and hard-won facts

### PROVEN — measured, do not re-derive

- **E2 could not be tested, and the failure is NOT change 7.** The portal leads to a map **this server is not
  holding**, so the destination had zero creatures. The server says so itself: *"Alpha says they are in
  'field_10', which this server is not holding — they will be filed under 'field_11' and seen by the wrong
  people. Add it to the map list."* That warning was written on 2026-09-02, before any of today's work, and the
  OLD code would have behaved identically because it filtered on the same map name. **To test E2 at all, the
  server must be started holding the map the portal actually leads to.**
- **Unity's Auto Refresh is ON here, in the mode that refreshes during Play** (`kAutoRefreshMode = 1`). A note
  claiming it was permanently off was wrong and cost a whole tester run: a file edit triggered a 6.4-second code
  reload mid-play, the player's character handle went to nothing, two components threw ~120 errors per second,
  and in ~8 seconds that filled the 1000-entry console and **erased every earlier line**. **Never edit any file
  while anything is playing.**
- **The freeze technique works** — pausing play and then capturing the composited screen. Two refinements the
  first user of it learned: **pause a few frames AFTER the action, not in the same instant** (pausing immediately
  freezes before the interface has drawn), and **every unpause costs a disconnect and reconnect** — measured four
  times out of four to the millisecond, because pausing stops the heartbeat and the server times the connection out.
- **The editor cannot reach the world unless `CHAR_SERVER_URL` is set to `http://127.0.0.1:8081`** in the editor
  process. Without it sign-in fails and Play mode ends itself, which reads as a crash.
- **The ordinary object search cannot see the sign-in screen** — it is marked "don't save". Use the search that
  includes every loaded object. And *found* is not *showing*: an inactive spare object at the world origin
  reported as "a creature 1.2 units away".

### HYPOTHESES — observed once, cause NOT established

- **A character can arrive dead.** Two minutes after entering with full health it read 0 of 486 and the death
  screen came up, with no attacker near and no damage seen. It looks like health from a previous death arriving
  late and overwriting the live value. **Not proven.**
- **The player can barely be moved.** Both step-by-step walking and the game's own glide-to-a-point were cancelled
  within a frame or two — about 2 units in 25 seconds. This is why E1 could not be set up. **Cause unknown.**
- **Dropped money has no picture.** A real console error: *"no sprite called 'GoldCoin' in Resources — Zeny
  dropped in a shared world will lie on the ground with nothing to show for it."* Unrelated to today's changes.

---

## Uncommitted working-tree changes — re-measured at 2026-09-03 17:52

Nothing is committed. Everything below is unstaged and revertible.

**Game code (11 files, the whole of today's work):** `Character.cs`, `GameNetwork.cs`, `NetPlayer.cs`,
`PassiveEvents.cs`, `Player3DController.cs`, `PlayerFighter.cs`, `PlayerSkills.cs`, `PlayerUnits.cs`,
`SkillRunner.cs`, `UnitStates.cs` — plus `.claude/skills/tester/SKILL.md`.

**Docs and rules:** `CLAUDE.md` (the AI-learnings page cap was removed at the user's instruction),
`docs/5-ai-learnings/README.md`, `docs/5-ai-learnings/never-reload-under-a-live-session.md`, and three NEW
learnings pages — `a-comment-promising-an-order-is-where-the-bug-is.md`,
`a-value-you-just-changed-reads-stale.md`, `give-the-whole-list-not-a-trickle.md`.

**Memory:** `MEMORY.md`, `index/workflow-meta.md`, a corrected `unity-auto-refresh-off.md`, and a new
`scaling-fixes-are-never-deferred.md`.

**Machine-level, outside this repo:** the global `~/.claude/CLAUDE.md` also had the page cap removed.

**Untouched by hand:** `tools/hook-analytics/*.json*` are written by the hooks themselves.

---

## Next single step

**Settle B1/B2 — does a passive fire on a landed basic attack and on a kill?** It is the only item that came back
inconclusive rather than passed or unrun, it is the direct target of change 2, and it is the one remaining place a
silent regression could be hiding. Tick a passive that answers a landed basic attack, land one, and read whether a
component was built for it and whether its effect landed — freezing the moment, since anything it grants is short.

Then work down: C2, C4 properly, C5–C7, A5, A6, A8, D2, D3, B3–B6.

## How to resume

Paste this into a fresh session:

```text
Read handoffs/skill-server-plumbing-playtest.md and resume the play-test from the "Next single step" section.
Before spawning a tester: check the Unity editor is open and idle, that CHAR_SERVER_URL is set to
http://127.0.0.1:8081 in the editor process, and that the map server is running a build that includes the
current source (rebuild with tools/build-game.sh server if not). Do not edit any file while a play session is
running — auto-refresh is ON here and it will destroy the run.
```
