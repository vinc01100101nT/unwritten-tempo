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
(new-to-Unity explain-by-analogy, prefer editor tools, bake at author time — see project `CLAUDE.md` standing rules; not repeated here.)

## Throwaway "quick fix" editor tools — tag them so they're purgeable

When you add an editor tool that exists only to patch a one-off problem (a temporary repair, a migration
helper, a spike) — i.e. something we'll want to delete once it's served its purpose — **tag it** so a future
"clean up the quick fixes" pass is a grep, not a guess:

1. Put a single-line marker on its own line directly above the class:
   `// QUICKFIX <yyyy-mm-dd> — <why it exists>; delete after <the condition that makes it obsolete>.`
2. Park its menu under the **`Tools/unwritten/Quick Fixes/…`** submenu (not the main menu), e.g.
   `[MenuItem("Tools/unwritten/Quick Fixes/<name>")]`.

To purge later: `grep -rl "QUICKFIX" Assets/Scripts/Editor/`, delete each hit **plus its `.meta`**, then
re-check references first (a "spike" can quietly become load-bearing — `RigBuilder25D` is the cautionary
tale: it was self-labelled "throwaway" as `Build25DSpike`, became the shared rig-builder used by ~15 files,
and was renamed to reflect that). Do **not** tag recurring,
idempotent maintenance tools that you re-run on every re-import (e.g. `Fix Pxltgr Materials`) — those are
keepers, not quick fixes.
