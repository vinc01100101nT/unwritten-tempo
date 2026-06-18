# Rules for editing game scripts (`unity/Assets/Scripts/`)

## ⚠️ Performance is a HARD constraint — read `PERFORMANCE.md` before writing runtime C#

This is a 2.5D game with many billboard mobs and auto-combat that keeps the screen full. Code that's
"fine for one object" becomes a frame-time or GC spike at ×N every frame. **`PERFORMANCE.md` (same
folder) is the canonical guide** — its table of reusable systems and its anti-pattern list are required
reading before you add or edit anything that runs per-frame or per-hit.

### The cardinal rule: don't allocate in hot paths
No `new` / `GetComponent` / `Find*` / `Camera.main` / `Sprite.Create` / per-frame `string` interpolation
inside `Update`/`LateUpdate`/`FixedUpdate` or anything that fires per-hit during combat. Pool it, cache
it, or hoist it. **A real example from this codebase:** `Pathfinder3D.FindPath` used to `return new List`
each call; mobs repath constantly, so that alone was **~28.6 KB/frame** of garbage during combat and
caused periodic GC-pause stutters. Fixing it (fill a caller-owned list; reuse all scratch) brought combat
allocation to **~0 B/frame**. Don't reintroduce that class of mistake.

### Before you reinvent something, check if it already exists (see `PERFORMANCE.md` for the full table)
- Camera basis each frame → **`CameraRig`** (never `Camera.main` per object)
- Slicing sprite-sheets → **`SpriteSheetCache`** (never `Sprite.Create` per spawn/mob)
- Floating combat text → **`DamagePopup`** (pooled; never build a `Canvas` per hit)
- Nearest/under-cursor mob → **`MonsterAI3D.Active`** registry + cached **`.Health`**
- Routing → **`Pathfinder3D.FindPath(..., result, ...)`** (fills a caller-owned list, zero-alloc)
- Static prop depth → baked once in `DepthSortRuntime`; only moving units get a per-frame sorter
- Hot collision queries → `Physics.*NonAlloc` with a reused buffer

### Verifying perf changes
There's **no local Play loop** — the dev presses Play. Quick glance: the in-game **`PerfHud`** overlay
(press **F3**) shows `GC/s`, worst-frame peak, FPS, and mob count — `GC/s` should be ~0 during combat.
Precise number: Profiler ▸ **Hierarchy**, watch the **GC Alloc** column per script during auto-combat;
target ~0 B/frame from gameplay scripts. In-Editor frame time is inflated by editor overhead — judge true
frame time from a **Development Build**, not the editor.

### Recurring checkup
Run **`/perf-checkup`** (scan → measure → fix → re-measure → log). Append each result to **`PERF_LOG.md`**
(the history; `PERFORMANCE.md` is the rules). Trigger it after adding any per-frame/per-hit system or before
merging — regressions follow code changes, not the calendar.

## Other house rules
- User is new to Unity / strong in JS-TS — explain C#/Unity by analogy when surfacing changes.
- Prefer one-click editor tools (`Tools ▸ unwritten ▸ …`) over manual editor steps.
- Keep static/never-changing work baked at author/generate time, not in Play.
