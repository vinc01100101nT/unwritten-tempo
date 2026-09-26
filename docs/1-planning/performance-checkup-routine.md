---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - planning-production
---

# ⚡ Performance Checkup — My Routine

> My recurring routine to keep **unwritten** smooth. The whole point: catch performance problems early, with almost no effort, so the game never gets slow without me noticing.

## 🎯 The 30-second version

1. **Start the server first** — there is no offline game any more, so pressing Play with nothing running gets me nowhere. See [Starting the Network](starting-the-network.md).
2. Open chat and type `**/perf-checkup**`
3. In Unity, open a **field** map, press **Play**, sign in → gather a bunch of mobs → press **T** (the auto-pilot AI)
4. Look at the **PerfHud** box (top-left of the game view, toggle with **F3**)
5. Tell the AI what the `**GC/s**` and `**peak**` numbers say
6. If they're ~0 → 🎉 done. If they spiked → the AI walks me through the Profiler and fixes it.

That's it. The AI does the thinking (scanning code, diagnosing, fixing, logging). I just run the game and report numbers.

---

## 🕐 When to run it

Don't just wait for the calendar — **performance problems come from new code, not from time passing.** Run a checkup:

- ✅ **Every ~3 days** as a habit (it's cheap now).
- ✅ **Right after adding anything that runs every frame or every hit** — a new mob behavior, a new visual effect, a new UI element that updates live. *This is the most important trigger.*
- ✅ **Before merging a branch.**
- ✅ Any time the game **feels** stuttery.

---

## ✅ The routine (step by step)

**1. Start the checkup**
In the AI chat, type `**/perf-checkup**`. The AI reads the rules + history and tells me what to do. (It already knows the plan below — this list is just so *I* remember.)

**2. Run the stress test in Unity**

- Make sure the **database, the sign-in service, the character server and the game server are running** — Play waits to be let into a world and will sit there otherwise, and without the character server nobody can be let in at all. [Starting the Network](starting-the-network.md) is the runbook.
- In the **Project** window open `Assets/Scenes/`, double-click any **field** map (e.g. `lst_fild01`), then press **Play** (▶ at the top). Play always boots the persistent `Systems` scene and loads whichever map I had open, so I never press Play *on* `Systems` directly.
- Sign in and pick a character at the door.
- Walk around and gather **as many monsters as I can** near the player.
- Press **T** to hand the fight to the **auto-pilot AI** (your character fights on its own).

> ⚠️ **The old instruction here said "open the NatureStage scene".** There is no such scene — it belonged to an asset pack that was removed. The maps on disk are Lastline (`lastline`) and the three-region world's maps, each named by its id (`lst_fild01`, `hlt_dun01`, `pennaryl` …).

**3. Read the PerfHud overlay** (top-left corner of the Game view)

- It shows up automatically in Play. Press **F3** to hide/show it, **F4** to reset the peak.
- Watch these two lines during the fight:
  - `**GC/s**` — how much garbage is being made per second.
  - `**peak**` — the worst single frame.

**4. Report to the AI**
Tell it the `GC/s` and `peak` values (or screenshot the PerfHud). 

**5. If something looks bad**, the AI will ask me to open the **Profiler** (Window ▸ Analysis ▸ Profiler) and click a spike — it guides me click-by-click from there, fixes the code, and asks me to re-test.

**6. Done** — the AI writes the result into the history log (`PERF_LOG.md`) so next time we know the baseline.

---

## 🟢 How to read PerfHud (is it good or bad?)

> **The one rule to remember:** during combat, `**GC/s` should be basically 0.** Garbage = the game pausing to clean it up = stutter. If garbage is ~0, the game stays smooth.

---

## 🧠 Why this matters (the simple mental model)

Every time code creates a new object in a fast loop, it becomes "garbage." Unity's **Garbage Collector** has to stop the game for a few milliseconds to clean it up — and that pause is a visible **stutter**. So the goal isn't "use less memory," it's **"don't create garbage during gameplay."** PerfHud's `GC/s` is literally a garbage meter. (Same idea as a JS engine's GC pausing to sweep — but in a game a pause is a hitch you can feel.)

---

## 💻 Editor vs real build (important!)

The numbers **inside the Unity Editor are slower than the real game** — the editor adds its own overhead. So:

- **GC/garbage** in the editor → trustworthy. Use PerfHud / Profiler as-is.
- **Frame time / FPS** in the editor → inflated. To judge the *real* speed, make a **Development Build**: `File ▸ Build Settings ▸ tick "Development Build" + "Autoconnect Profiler" ▸ Build And Run`, then profile that.

---

## 📍 Where everything lives (reference)

I don't need to read these — the AI does. They're listed so I know they exist:

- `unity/Assets/Scripts/PERFORMANCE.md` — the rules the AI checks new code against.
- `unity/Assets/Scripts/PERF_LOG.md` — the history log every checkup's result is written into.

---

## 🧾 What the AI handles for me

So I don't feel like I have to remember the technical stuff — when I run `/perf-checkup`, the AI automatically:

- reads the rules + the last checkup's numbers,
- scans my recent code changes for known performance mistakes,
- tells me exactly what to measure,
- fixes only what the Profiler actually proves is a problem,
- has me re-test to confirm,
- and writes the result into the history log.

**My only job:** type `/perf-checkup`, press Play, gather mobs, press T, and read the PerfHud numbers. 👍

---

### 📌 First baseline (2026-06-18)

After the first optimization pass: combat garbage went from **~28.6 KB/frame → ~0**, and frame spikes from **~26 ms → ~6 ms**. That's the "healthy" baseline to compare future checkups against.

---

## Related

- [Starting the Network](starting-the-network.md) — Play needs a running world now; this is how to get one.
- [Roadmap to Launch](roadmap-to-launch.md) — where the perf habit sits in the plan.
- [Active Skill System — Implementation Notes](../2-systems/active-skill-system-v3/implementation-notes.md) — the per-hit and per-frame paths most likely to make garbage.
- [Unified Unit Stats](../2-systems/active-skill-system-v3/unified-unit-stats.md) — what every mob in the stress test is computing.
- [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md) — the per-hit and per-frame rules the networked half has to obey too.
