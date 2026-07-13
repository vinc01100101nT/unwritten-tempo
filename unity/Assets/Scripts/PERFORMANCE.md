# Performance Guide — unwritten (2.5D)

**Read this before writing or editing any runtime C# in** `unity/Assets/Scripts/`**.**

Performance is a **hard constraint** for this game (3D world + many billboard mobs + auto-combat that keeps the screen full of action). The frame budget is shared by dozens of mobs, their billboards, depth sorters, pathfinding, and a steady stream of combat FX. Code that's "fine for one object" becomes a frame-time or GC spike when it runs ×N every frame. The rules below are how we keep it smooth.

> **Two enemies:** (1) **per-frame CPU** that scales with object count, and (2) **GC allocations** in hot paths (every `new` in `Update`/per-hit eventually triggers a collection → a stutter). Most wins here are about not doing either.

---

## The golden rules

1. **Don't allocate in hot paths.** No `new` (objects, arrays, `List`, `WaitForSeconds`, lambdas that capture, string interpolation) inside `Update`/`LateUpdate`/`FixedUpdate` or anything that fires per-hit during combat. Pool it, cache it, or hoist it to a field/`static`.
2. **Compute shared things once per frame, not once per object.** If N objects all need the same value (camera forward, time of day), one component computes it and the rest read the cache.
3. **Cache component lookups.** `GetComponent`/`Camera.main`/`Find*` are not free. Resolve in `Awake`/ `Start`, store the reference, reuse it. **Never** `GetComponent` inside a loop or per frame.
4. **Bake static work at author/generate time.** If it never changes at runtime, compute it once when the map is built — not every frame in Play. (See `DepthSortRuntime` static-prop baking.)
5. **Prefer cheap math.** Compare **squared** distances (`sqrMagnitude`) against squared radii instead of `Vector3.Distance` (a `sqrt`). Take a sign/dot instead of an angle.
6. **Don't touch global/render state every frame** unless it changed (`RenderSettings.*`, `Graphics Settings.*`, `Shader.SetGlobal*`, creating `Canvas`es).
7. **Don't add comments next to the line of a real code.** add that comment on the next line.

---

## Established patterns in THIS codebase — reuse them, don't reinvent

| Need | Use | Why |
| --- | --- | --- |
| "Which way is the camera facing on the ground?" | `CameraRig.ForwardXZ` **/** `RightXZ` **/** `Cam` | Computes once per frame, shared by every billboard/sorter/facing solver. Don't call `Camera.main` or recompute the forward per object. |
| Slicing frames from a sprite-sheet at runtime | `SpriteSheetCache.Strip(...)` **/** `Walk4(...)` | `Sprite.Create` allocates. The cache shares one immutable `Sprite[]` across all mobs/FX using the same sheet. Never `Sprite.Create` per-spawn or per-mob. |
| Floating combat numbers / MISS / heal text | `DamagePopup.Spawn` **/** `SpawnText` | Pooled under ONE shared world-space canvas. Don't build per-hit `GameObject`+`Canvas`+`Text`. |
| Persistent per-mob world UI (HP bar + name) | `MobNameplate` | Every mob's plate lives under ONE shared world-space canvas (never a Canvas per mob); each only follows + billboards per frame (no alloc) and rewrites its fill only on HP change. Reuse this for any other always-on per-unit world widget. |
| Find the nearest / under-cursor mob | `MonsterAI3D.Active` registry + the mob's cached `.Health` | No per-frame `FindObjectsByType`; no per-mob `GetComponent<Health>()`. A fixed-size `OverlapSphere` buffer silently drops mobs in dense scenes — don't use it for target scans. |
| Routing on the XZ plane | `Pathfinder3D.FindPath(from, to, radius, result, maxExpansions)` | Fills the **caller-owned** `result` **list** (each agent's reusable `path`) — never returns a new collection. A\* working sets + route scratch are reused statics; `LastOutcome` is a constant string. Mobs pass a small `maxExpansions` (via `PathAgent3D.maxPathExpansions`); only the player needs the full budget. **Verified:** this one change cut combat allocation from \~28.6 KB/frame to \~340 B. |
| Static scenery depth (trees/houses) | **Baked once** in `DepthSortRuntime` (fixed `sortingOrder` from foot-Y) | Only **moving** units carry a per-frame sorter (`CameraDepthSorter`). Never add a per-frame sorter to a static prop. |
| Physics overlap/raycast queries | `*NonAlloc` **variants with a reused buffer** | e.g. `Physics.OverlapSphereNonAlloc(..., overlapBuf, ...)`. No garbage per query. |
| Recycling dead mobs | `MobRespawn` re-enables the same object | Don't `Destroy`/`Instantiate` mobs on a cadence — recycle. |
| Handing state to an on-arrive / on-complete callback (projectiles, delayed hits) | `Projectile3D.HitPayload` + a **cached** `static readonly Action<HitPayload>` handler | A lambda that captures locals allocates a closure **per shot**. Instead the state rides the carrier in a by-value struct and one shared handler reads it back (see `MonsterAI3D.LandRangedHit`, `Player3DAttacker.LandArrow`). Reuse the pattern for any per-event callback. |
| The same expensive question asked by several systems in one frame | **Frame memo**: stamp `Time.frameCount`, compute once, share the result | The cursor pick (raycast + mob snap-scan) was computed up to 3×/frame by `GameCursor3D`, `MobHoverPanel`, and click handlers. A two-field memo (`Player3DController.MonsterUnderCursor`) makes N callers cost one scan. Sibling of rule 2 — this is the idiom for *queries*, `CameraRig` is the idiom for *values*. |

---

## Anti-patterns (these regress us — don't do them)

- ❌ `FindObjectOfType` / `FindObjectsByType` / `GameObject.Find` in `Update` (or any per-frame path). → Resolve once, or keep a registry like `MonsterAI3D.Active`.
- ❌ `GetComponent<T>()` inside a loop or every frame. → Cache it on the component (e.g. `MonsterAI3D.Health`).
- ❌ `Camera.main` per object per frame. → `CameraRig.Cam`.
- ❌ `Sprite.Create` / `new Texture2D` per spawn. → `SpriteSheetCache`.
- ❌ Building a `GameObject` + `Canvas` per UI element that spawns repeatedly. → Pool under one canvas.
- ❌ **Returning a** `new List`**/array from a hot, repeatedly-called API.** → Fill a **caller-owned buffer** passed in (see `Pathfinder3D.FindPath(..., List<Vector2> result)`). This was the single biggest combat allocator — mobs repath constantly, and each call newed several lists.
- ❌ `new WaitForSeconds(x)` every coroutine start. → **Fixed** duration: cache the `WaitForSeconds` in a field (`Health.flashWait`). **Variable** duration (anything scaled by attack speed / per-skill config — a field can't cover it): zero-alloc manual timer, `for (float el = 0f; el < dur; el += Time.deltaTime) yield return null;` (see `Player3DAttacker`; don't name the counter `t` — it collides with the conventional `Health t` parameter, CS0136).
- ❌ A lazy `if (x == null) x = FindFirstObjectByType<T>()` fallback **inside a per-frame path** when `x` can be legitimately ABSENT (system not in this scene, player between scenes). The "runs once" intent silently becomes a `Find` every frame, forever. → Resolve at open/scene-load, or throttle the retry to a cadence (see `DevSettings.EnsureRefs`, 0.5 s).
- ❌ `Vector3.Distance` / `.magnitude` when you only need to compare. → `sqrMagnitude` vs `r*r`.
- ❌ Writing `RenderSettings.*` or other globals every frame. → Only when the value actually changed (see `DayNightLight` early-out).
- ❌ Per-frame `string` interpolation for the inspector/debug. → `#if UNITY_EDITOR` it, or only on change.
- ❌ Setting `Text.text = $"..."` every frame when the value rarely changes. → Cache the last value and only rewrite on change (see `TrainingDummy`). Each assignment both allocates a string AND forces a canvas rebuild.
- ❌ One `Canvas` per HUD widget. Canvases are batch/rebuild boundaries — fewer, shared canvases batch better.
- ❌ A per-frame boundary check (`InRange`, "am I close enough?") that **gates a hot path** with no hysteresis. A target sitting right at the threshold (e.g. a kiting mob at attack-range edge) flips the check true/false every frame, thrashing whatever it gates — re-routing (`StartRoute`/`FindPath`), restarting an animation, or re-firing SFX. → Add **hysteresis**: enter at `range`, stay engaged until `range × k` (see `Player3DAttacker.EngagedWith`); keep the strict check only for the irreversible action (landing the hit).

---

## Checklist before committing runtime code

- [ ] No `new` / `Sprite.Create` / `GetComponent` / `Find*` / `Camera.main` in `Update`/`LateUpdate`/per-hit.

- [ ] Anything that spawns repeatedly (FX, popups, projectiles) is **pooled**, not create-and-destroy.

- [ ] Shared per-frame values (camera basis, clock) read from a cache, not recomputed per object.

- [ ] Distance comparisons use squared values.

- [ ] Static/never-changing work is baked at author/generate time, not in Play.

- [ ] New components cache their sibling references in `Awake`/`Start`.

- [ ] Global render state is written only on change.

---

## How we verify (no local Play loop)

There's **no automated Play loop** — the dev presses **Play** in the Unity Editor to verify. Two tools:

- `PerfHud` (in-game overlay, F3) — the quick glance. Shows `GC/s`, worst-frame peak, FPS, mob count. During combat `GC/s` should be \~0 (a small steady floor is the HUD itself) and peak should stay in bytes, not KB. Use this for the routine check.
- **Profiler ▸ Hierarchy** — the authoritative number. Click a CPU spike → expand `PlayerLoop ▸ UpdateScene ▸ … ▸ BehaviourUpdate` → read **GC Alloc** per script. Target **0 B** from gameplay scripts during a steady fight. The HUD never pollutes these rows (it's its own marker).

Recurring process: the `/perf-checkup` command runs the full loop (scan → measure → fix → re-measure → log). Append every checkup's result to `PERF_LOG.md` (the history; this file is the rules).

Also sanity-check visuals after a change: damage numbers/MISS/heal text appear and face the camera; mobs animate and route around props; depth sorting (behind/in front of trees) is correct; day/night transitions. Judge true frame-time from a **Development Build** — the editor inflates it.

If you add a new pooled/cached system, document it in the table above so the next agent reuses it.