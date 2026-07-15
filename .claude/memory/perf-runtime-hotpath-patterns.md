# Runtime hot-path performance patterns (2.5D) — canonical guide in-repo

**Canonical, always-current doc: `unity/Assets/Scripts/PERFORMANCE.md`** — read it before writing/editing
any runtime C#. This memory note is just the pointer + the reusable systems so future sessions don't
reinvent them.

Established 2026-06-18 during a perf code review of the 2.5D game. The review found the runtime was
clean architecturally but had **combat-time GC churn** and **per-object redundant work**. Fixes landed,
plus shared utilities every new system should reuse:

- **`CameraRig`** (`CameraRig.cs`) — camera horizontal basis (`ForwardXZ`/`RightXZ`/`Cam`) computed ONCE
  per frame, shared by all billboards/sorters. Never call `Camera.main` or recompute camera-forward
  per object. Used by `CameraDepthSorter` + `Facing3D`.
- **`SpriteSheetCache`** (`SpriteSheetCache.cs`) — `Strip(sheet,size,count)` (bottom-row strip: FX +
  player attack poses) and `Walk4(sheet,cell,frames)` (4-dir mob walk). Shares one `Sprite[]`; never
  `Sprite.Create` per-spawn/per-mob. Used by `SlashEffect`, `BillboardAnimator3D`, `Player3DController`.
- **`DamagePopup`** — now POOLED under ONE shared world-space canvas (was: new GameObject+Canvas+Text
  per hit). Public API unchanged (`Spawn`/`SpawnText`).
- **`MonsterAI3D.Active` registry + cached `MonsterAI3D.Health`** — target scans (`NearestMonsterWithin`,
  `MonsterUnderCursor`) read the registry and the cached Health; no per-frame `FindObjectsByType` or
  per-mob `GetComponent`.
- **`Pathfinder3D`** — A* working sets are reused statics (no per-query alloc); `FindPath` takes an
  optional `maxExpansions`; mobs pass a small `PathAgent3D.maxPathExpansions` (~2500), player uses full.
- Low-level rules baked in: squared-distance compares (`MonsterAI3D`), cached `WaitForSeconds`
  (`Health.Flash`), global render writes only on change (`DayNightLight`), `#if UNITY_EDITOR` debug
  strings (`TimeOfDay`).

Golden rules: no `new`/`GetComponent`/`Find*`/`Camera.main`/`Sprite.Create` in Update or per-hit; pool
repeated spawns; compute shared per-frame values once; bake static work at author time; cheap math.

Verify = user presses Play; watch Profiler **GC Alloc** (~0 B/frame in a steady fight) during auto-combat.

## Recurring checkup tooling (set up 2026-06-18)
The user wants periodic perf checkups. Tooling exists — USE it, don't reinvent:
- **`/perf-checkup`** slash command (`.claude/commands/perf-checkup.md`) — runs the measure-driven loop
  (read docs+log → static scan recent diff → user measures → fix only proven issues → re-measure → log).
- **`PerfHud`** (`PerfHud.cs`, F3 in Play, editor/dev-build only) — on-screen `GC/s`, peak frame alloc,
  FPS, mob count. The quick "is it ~0?" glance so checkups don't need a full Profiler dig.
- **`PERF_LOG.md`** (next to PERFORMANCE.md) — append-only history; the latest entry is the baseline to
  compare against. Standard test scenario is documented there (NatureStage → gather mobs → T).
Trigger checkups after adding any per-frame/per-hit system or before merging — not purely on a calendar.
