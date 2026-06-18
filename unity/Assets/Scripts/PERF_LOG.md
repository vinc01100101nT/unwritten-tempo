# Performance Log

Running history of perf checkups. **`PERFORMANCE.md` is the rules; this is the history.** Each checkup
appends an entry (newest on top). The next checkup reads the latest entry as its **baseline** — so we
track the trend and catch regressions instead of re-investigating settled code.

Driven by the `/perf-checkup` command. Measure with the in-game **PerfHud** (F3) for the quick glance,
or the Profiler ▸ Hierarchy for the precise per-system number.

## Standard test scenario (keep it the same so numbers compare)

1. Open **NatureStage**, press **Play**.
2. Gather as many mobs as possible around the player, press **T** (auto-combat).
3. Read the numbers:
   - **PerfHud** (top-left): `GC/s` should be ~0 (a small steady floor is the HUD itself); `peak /frame`
     should stay small (hundreds of B, not KB); FPS/ms healthy.
   - **Profiler ▸ Hierarchy** (authoritative): click a green spike → expand
     `PlayerLoop ▸ UpdateScene ▸ … ▸ BehaviourUpdate` → read **GC Alloc** per script. Target **0 B**
     from gameplay scripts (`MonsterAI3D.Update`, `PathAgent3D.Update`, `DamagePopup.Update`, …).
4. Judge true frame-time from a **Development Build**, not the editor (the editor inflates frame time).

## Entry template (copy for each checkup)

```
## YYYY-MM-DD — <one-line summary>
- Mobs in test: <n>
- Combat alloc (BehaviourUpdate GC/frame): <before> → <after>
- Frame time (PlayerLoop, editor): <before> → <after>
- Found: <what the profiler showed>
- Fixed: <files / change>
- New rule added to PERFORMANCE.md? <yes/no — what>
```

---

## 2026-06-18 — Enemy-variety regression caught & fixed (player attack thrash)

Checkup after the M1 enemy-variety work (Bestiary archetypes + `MonsterAI3D` kiting/flee behaviors).

- **Mobs in test:** ~27–28
- **Combat alloc (BehaviourUpdate GC/frame):** **~4.2 MB/s (PerfHud, pre-fix) → ~210 B/frame** (Profiler ▸
  Hierarchy `UpdateScene`; gameplay ≈ baseline 0). `PlayerLoop` total 1.5 KB/frame, of which 1.3 KB is
  `RenderPlayModeViewCameras` (editor-only, 0 in a build).
- **Frame time:** editor-dominated (`EditorLoop` 93% CPU, 0 B GC); `PlayerLoop` ~1.9 ms — healthy.
- **Found:** the regression was **NOT** the new kiting/flee code — `PathAgent3D` repaths are throttled
  (≥0.35 s / 0.6-unit drift) and fill a caller-owned list, and `Sfx` is a pooled voice ring (0 alloc).
  The real cause was the **player attack loop thrashing against a kiting target**: a bare per-frame
  `InRange` decided stand-vs-chase, so at the range edge it flipped every frame — each stand-frame cleared
  `routing`, so each chase-frame re-ran a **full-budget `Player3DController.StartRoute` / `Pathfinder3D.FindPath`**
  (≈ every other frame) and machine-gunned the footstep SFX. New behavior (kiters) **exposed a latent player
  bug**; the profiler — not the static scan — confirmed gameplay scripts sit at ~0 once the thrash stops.
- **Fixed:** `Player3DAttacker.EngagedWith` range **hysteresis** (stand/chase no longer flips at the boundary;
  the strict `InRange` still gates the actual hit, so reach is unchanged) + `Player3DController.Footsteps`
  **persistent cooldown** (no per-frame timer reset). Stops the per-frame re-route + SFX churn.
- **New rule added to PERFORMANCE.md?** yes — hysteresis on per-frame boundary checks that gate a hot path.
- **Minor (not fixed; profiler clean):** `SaveManager` saves synchronously on **every** level-up; rapid
  auto-combat leveling could cause small disk-IO/JSON blips. Throttle if it ever shows in a build.
- **Verdict:** regression resolved; gameplay alloc back to ~0 (210 B/frame editor residual). Confirm true
  frame-time from a Development Build.

---

## 2026-06-18 — Baseline established; killed combat GC churn

First full pass (code review → profiler-driven optimization).

- **Mobs in test:** ~29–32
- **Combat alloc (BehaviourUpdate GC/frame):** **~28.6 KB → ~0 B** (≈340 B residual, all editor-only / a
  test dummy; **0 B in a build**)
- **Frame time (PlayerLoop, editor):** **~26 ms spike → ~6 ms**
- **Found (profiler):** all of the per-frame combat garbage was `MonsterAI3D.Update` (28.3 KB across 29
  mobs) — it came from `Pathfinder3D.FindPath` allocating fresh result `List`s every repath (mobs repath
  constantly while chasing). `DamagePopup` was the *suspected* culprit from code review but profiled at
  0 B once pooled — the profiler corrected the guess.
- **Fixed:**
  - `Pathfinder3D.FindPath` → fills a **caller-owned list**, reuses all A*/scratch collections, constant
    `LastOutcome` strings (callers: `PathAgent3D`, `Player3DController`).
  - `DamagePopup` → **pooled** under one shared world-space canvas (was new GameObject+Canvas+Text/hit).
  - `SpriteSheetCache` → shared sprite slicing (`SlashEffect`, `BillboardAnimator3D`, attack poses).
  - `CameraRig` → camera basis computed once/frame (`CameraDepthSorter`, `Facing3D`).
  - `MonsterAI3D` → cached `.Health`, squared-distance compares; registry-based target scans.
  - Small: `Health` cached `WaitForSeconds`; `DayNightLight` writes globals only on change; `TimeOfDay`
    debug string `#if UNITY_EDITOR`; `TrainingDummy` label only rebuilt on HP change.
  - Added: `PerfHud` (dev overlay), `PERFORMANCE.md`, `CLAUDE.md`, this log.
- **New rules added to PERFORMANCE.md:** "never return a `new List` from a hot API — fill a caller-owned
  buffer"; "don't set `Text.text` every frame".
- **Verdict:** every identified hot path at ~0 alloc. Remaining editor frame-time is editor/rendering
  overhead — confirm real frame-time from a Development Build next time.
