# ⚡ Performance Checkup — My Routine

> My recurring routine to keep **unwritten** smooth. The whole point: catch performance problems early, with almost no effort, so the game never gets slow without me noticing.

## 🎯 The 30-second version

1. Open chat and type `**/perf-checkup**`
2. In Unity, press **Play** → gather a bunch of mobs → press **T** (auto-combat)
3. Look at the **PerfHud** box (top-left of the game view, toggle with **F3**)
4. Tell the AI what the `**GC/s**` and `**peak**` numbers say
5. If they're ~0 → 🎉 done. If they spiked → the AI walks me through the Profiler and fixes it.

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

- Open the **NatureStage** scene, press **Play** (▶ at the top).
- Walk around and gather **as many monsters as I can** near the player.
- Press **T** to turn on **auto-combat** (the player fights automatically).

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

I don't need to read the two `.md` files — the AI does. They're listed so I know they exist.

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
