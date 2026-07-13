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

## 2026-07-14 — Zero-alloc sweep + dead-code purge; PerfHud at/below the known editor floor

Checkup after a full-codebase review session: alloc fixes applied first, then measured.

- **Mobs in test:** 32 (gathered), then 17 after an auto-attack (T) wipe; rain active in the second read.
- **Combat alloc:** PerfHud only this run — `GC/s 3.3 MB / peak 150.5 KB` @32 mobs, `GC/s 2.6 MB /
  peak 354.6 KB` @17 post-wipe. Both at/below the documented editor floor (baseline 2026-06-20 read
  `4.5 MB/s / 666 KB` while Hierarchy proved gameplay ~0), so **no regression signal**. The higher
  post-wipe `peak` is max-since-F4 catching one-time transients (mass DeathDissolve/respawn/loot from
  the wipe + rain starting), not steady-state churn. **Profiler ▸ Hierarchy rows were NOT captured** —
  no authoritative per-script 0 B this run; capture them next checkup after an F4 reset mid-fight.
- **Frame time:** 9.5–9.6 ms at 104–106 FPS in-editor (editor-inflated) — healthy.
- **Found (static scan):** the 12 unchecked commits since the last entry (humanoid migration, stances,
  3D weapons, palette customization) grep clean — dye re-bakes are event-time, stance/pose tables are
  Awake-built. Review-level allocators found and fixed before measuring (below).
- **Fixed (pre-measure, this session):**
  - `Player3DAttacker` swing/strike + `SkillRunner` repeat-cycles → manual timer loops (was
    `new WaitForSeconds` per swing/cast); `PlayerHurtFlash` → cached wait (the `Health.flashWait` pattern).
  - `Projectile3D` → `HitPayload` + cached static handlers (`MonsterAI3D.LandRangedHit`,
    `Player3DAttacker.LandArrow`); no more closure per ranged shot.
  - `Player3DController.MonsterUnderCursor` → frame-memoized (was up to 3 raycast+snap-scans per frame
    across GameCursor3D / MobHoverPanel / click handlers).
  - `DevSettings.EnsureRefs` → 0.5 s cadence while open (was per-frame `FindFirstObjectByType` for any
    system absent from the scene).
  - `Hotkeys` now owns the dual input-backend reads + `CancelDown()`; ~10 per-key wrappers and 10
    scattered Escape reads collapsed into it.
  - Dead code deleted: `Character3DDriver`, `ClickMarker`, `DevGui`, `FriendlyUnit`, `SpriteFactory`,
    `PlayerInteractor`, `Interactable` (+ WorldTools `Create NPC` tool). `MultiHitBurst` kept (planned
    Meteor multi-hit revival); `DialogueUI`/`InteractPrompt` kept as dormant reusable UI halves.
- **New rule added to PERFORMANCE.md?** no — every fix applies existing rules.
- **Verdict:** no regression; the alloc fixes are in and the editor-floor numbers match the known-clean
  baseline. Next checkup: F4-reset mid-fight (past the transients) and capture Profiler ▸ Hierarchy for
  the authoritative per-script 0 B, including a ranged-caster pack to exercise the new projectile path.

---

## 2026-06-20 — RO-combat systems checkup: clean (PerfHud editor-inflation clarified)

Checkup after the RO-combat batch (animated NinjaGreen player, `MobNameplate`, `StatusController`,
`MonsterAI3D` element/on-hit-status, `Element`) + the aim-first single-target skill (`UnitTargeter`,
`Player3DSoulStrike`).

- **Mobs in test:** 34 (NatureStage, gathered)
- **Combat alloc (BehaviourUpdate GC/frame):** **~0 / ≈1.4 KB `UpdateScene`** (Profiler ▸ Hierarchy,
  authoritative). Flat graph, **no spikes**. Of `PlayerLoop` 2.7 KB/frame, ~1.3 KB is editor-only
  `RenderPlayModeViewCameras` + `PerfHud.OnGUI`/`GUIUtility.BeginGUI` IMGUI (0 in a build).
- **PerfHud read:** showed `GC/s 4.5 MB`, `peak 666 KB/frame` — **editor/Profiler inflation, NOT gameplay**.
  PerfHud sums the `"GC Allocated In Frame"` recorder, which includes editor game-view IMGUI **and the open
  Profiler window** (a heavy allocator while recording). `peak` is max-since-F4 and stuck from a one-time
  transient (scene load / first nameplate builds / DamagePopup pool fill). The Hierarchy is the truth → clean.
- **Frame time:** editor-dominated (FPS 95–117 in-editor). Judge true frame time from a Development Build.
- **Found:** the three new ×N-per-frame systems are all allocation-free in steady state —
  `MobNameplate.LateUpdate` only writes transforms + rewrites its HP fill **on HP change** (no per-frame
  canvas rebuild); `StatusController.Update` early-outs unless a DoT is live; `MonsterAI3D` changes are in the
  combat/Awake path (the ranged-caster lambda allocates a closure only per projectile, not per frame).
  `UnitTargeter` runs only while aiming (single instance) and reuses the magnetised `MonsterUnderCursor`
  pick + registry scan — no per-frame `GetComponent`/alloc.
- **Fixed:** nothing — no regression. (Profiler corrected the PerfHud-suspected culprit, as the process warns.)
- **Also surfaced (not perf):** "No cameras rendering" recovery = re-run **Tools ▸ unwritten ▸ Setup Systems
  Scene** (the Spike Camera lives in Systems.unity; a disabled cam / stale URP renderer index blanks the view).
- **New rule added to PERFORMANCE.md?** no — the "editor inflates allocation; trust Hierarchy over PerfHud"
  caveat is already documented. Reinforced here as a worked example.
- **Verdict:** gameplay alloc ~0; the RO-combat + targeting additions did not regress the budget. Confirm true
  frame-time from a Development Build.

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
