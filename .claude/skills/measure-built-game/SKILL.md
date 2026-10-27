---
name: measure-built-game
description: Use when an ACCURATE frame rate, stutter, load hitch or graphics-card cost is wanted — "the real number", "in the build", checking whether an editor stutter is real, or confirming a performance fix. Quick looks stay in the editor. Covers building the client, starting it in a window with the control link on, driving the character, the perf command, finding square loads in the game's log, and stopping what you started.
---

# Measuring the built game

**Quick measures happen in the editor; the accurate number comes from the built game.** An editor reading is fine
for a fast look and for finding WHERE time goes, and it is reported as an editor number. When the user wants the
accurate number — or an editor reading shows a stutter or hitch — measure in the built client, driven through its
control link, as below.

*(Measured 2026-10-27, same world, same spots: the editor showed 11–13% of frames over 25 ms and a 100 ms frame each
time a square of ground finished loading. The built game showed 0–2 frames over 25 ms per 600, and 25 square loads
with no frame over 36 ms. A whole round was spent hunting an editor stutter that does not exist in the game. The
editor also slows itself whenever it is not the window in front.)*

## 1. A client build that holds the change

- The client is `builds/client/unwritten.exe`. Compare its date with the change you are measuring; if it is older,
  it must be rebuilt: `tools/build-game.sh client` (about 15 minutes; a plain build, never `--dev`, so it
  matches what players run). **A build only runs when the user's message says to build** (the order gate).
- The game server, the character server and the account service must be running — the user starts them. Check,
  never start: `tools/platform.sh port-info 7777`, `8081`, `8080`. If the change touches anything the server runs,
  the game server needs its own rebuild and restart too — say so instead of measuring a world the change never
  reached.

## 2. Start it in a window, with the control link on

`game_launch` starts the client with NO graphics — it draws nothing, so its frame times mean nothing. Start it
yourself in a window (it appears on the user's screen, so the user must have agreed to that):

```bash
builds/client/unwritten.exe -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -agent-port 7790 -logFile logs/agent-client.log &
```

It answers on port 7790 within seconds; the `game` tool then talks to it as it would to one it launched. Sign in
with the test account and play its character: `login alpha@test.local testpass123`, then `play 1`. Say the window
size in the report — the graphics card's cost grows with it.

## 3. Read frame times: `perf`

- `perf nocap` lifts the frame cap (60) and restarts the samples; `perf cap` puts the cap back.
- `perf` reports the last 600 frames: average, median, 99th percentile and slowest frame, the same for the graphics
  card, and how many frames took over 25 ms and over 50 ms.
- **Standing still:** `perf nocap; wait 10; perf`.
- **Walking:** 600 frames is about 8 seconds, so walk in short stretches with a fresh sample each:
  `perf nocap; move <x> <y>; perf`. A walk longer than that keeps only its last 8 seconds.
- **A reading under about 5 ms is suspect** — it came twice, once pressed against a town wall and once at the
  world's edge. Check where the character stands (`ground`) before believing it.

## 4. Square loads: the game's own log

`log Map on` makes the client write every square of ground it finishes loading to `logs/agent-client.log`:

```bash
grep -a "chunk " logs/agent-client.log | tail -30
```

Each line gives the time, which square, and how long it took to arrive. Squares loaded during a stretch whose
slowest frame stayed low did not hitch.

## 5. Driving

- The screen's position `at (x, y)` is the world's x and z.
- `move` ends on arrival or after 30 s ("still walking"). If the position stops changing, the character is stuck:
  `ground` lists what it is pressed against; `stop`, then go back the way it came and route over open ground or
  roads. A stuck spot is a finding — report it with its position and what it was touching.

## 6. Stop what you started

`perf cap`, then stop the client **by its port** — never by name, the user may have their own game open:

```bash
tools/platform.sh stop-port 7790
```

Confirm the user's servers still show the same start times. Report each reading with where it was taken, standing
or walking, the window size, the frame and graphics-card averages, and the slow-frame counts.
