# Performance Guide — unwritten (2.5D)

**Read this before writing or editing any runtime C# in** `unity/Assets/Scripts/`**.**

Performance is a **hard constraint** for this game (3D world + many billboard mobs + auto-combat keeping the screen full of action). The frame budget is shared by dozens of mobs, their billboards, depth sorters, pathfinding, and a steady stream of combat FX. Code that's "fine for one object" becomes a frame-time or GC spike when it runs ×N every frame. The rules below are how we keep it smooth.

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
| A person's chosen outfit colours, on every body drawn for them | `CharacterDye3D` hands the colours to the character shader (`Unwritten/CharacterLit`), which swaps them while drawing; each model's parts are read from `CharacterDyeParts`, saved in the editor | Measured 2026-10-05 on an RTX 4060: a recoloured texture per look was 1 MB of graphics memory for every differently dressed person nearby, with no ceiling; swapping while drawing costs 0.013–0.020 ms per million character pixels, which the screen size caps however many people there are. Finding the parts in game froze a frame for 18.7 ms per model. Never make a texture per person, and never find parts at runtime. |
| Slicing frames from a sprite-sheet at runtime | `SpriteSheetCache.Strip(...)` **/** `Walk4(...)` | `Sprite.Create` allocates. The cache shares one immutable `Sprite[]` across all mobs/FX using the same sheet. Never `Sprite.Create` per-spawn or per-mob. |
| Floating combat numbers / MISS / heal text | `DamagePopup.Spawn` **/** `SpawnText` | Pooled under ONE shared world-space canvas. Don't build per-hit `GameObject`+`Canvas`+`Text`. |
| Floating per-unit world UI (HP bar + name) | `UnitPlate` on `MobNameplate.SharedHost`, owned by `HoverPlate` and `CharacterPlates` | Every floating unit widget hangs under ONE shared world-space canvas (never a Canvas per unit). One widget, `UnitPlate`, draws every plate: the hover plate is ONE of them re-pointed at whichever creature is under the cursor, and `CharacterPlates` keeps one per person on screen (from `NetPlayer.WatchedBodies`, never a scene search) and pools them when people leave. Per frame a plate only follows + billboards (no alloc) and rewrites a fill only when its number changes. Reuse the shared canvas and `UnitPlate` for any other per-unit world widget. |
| Find the nearest / under-cursor mob | `MonsterAI3D.Active` registry + the mob's cached `.Health` | No per-frame `FindObjectsByType`; no per-mob `GetComponent<Health>()`. A fixed-size `OverlapSphere` buffer silently drops mobs in dense scenes — don't use it for target scans. |
| Routing on the XZ plane | `Pathfinder3D.FindPath(from, to, radius, result, maxExpansions)` | Fills the **caller-owned** `result` **list** (each agent's reusable `path`) — never returns a new collection. A\* working sets + route scratch are reused statics; `LastOutcome` is a constant string. Mobs pass a small `maxExpansions` (via `PathAgent3D.maxPathExpansions`); only the player needs the full budget. A goal in an area no route reaches is refused before searching, by the map's connected-area labels (worked out once per map load by `Pathfinder3DConfig`) — without them a cliff-top chase burned the whole budget every repath, 52–57 ms each. **Verified:** this one change cut combat allocation from \~28.6 KB/frame to \~340 B. |
| Static scenery depth (trees/houses) | **Baked once** in `DepthSortRuntime` (fixed `sortingOrder` from foot-Y) | Only **moving** units carry a per-frame sorter (`CameraDepthSorter`). Never add a per-frame sorter to a static prop. |
| Physics overlap/raycast queries | `*NonAlloc` **variants with a reused buffer** | e.g. `Physics.OverlapSphereNonAlloc(..., overlapBuf, ...)`. No garbage per query. |
| Recycling dead mobs | `MobRespawn` re-enables the same object | Don't `Destroy`/`Instantiate` mobs on a cadence — recycle. |
| Enemies near a point in one map (an area skill, a bouncing blade, an ally creature's hunt) | `MonsterAI3D.ActiveIn(map)` + `PlayerUnits.CollectIn(map, list)` | A server holds many maps; walking `Active` walks every creature in all of them, and asking each its `Map` allocates a string per creature. Creatures are filed by map when they switch on and re-filed in `MapManager.PutInMap`; people by `PlayerUnits`. Never walk `Active` for a search that is about one map. |
| Anything in the air (arrows, thrown blades, bouncing skills) | `Flights` (server, pooled entries, walked once a frame) + `FlightPictures` (watchers, one pooled object per flight, flying weapon models pooled per prefab) | One store and one picture for every flight, so nothing allocates per throw and a hop never schedules a coroutine or a closure. |
| A saved character leaving or dying | `SavedCharacters` banks the body by creature id (`PooledMob.returnTo`) | A mob turns back on 1 kill in 10; its body is used again the next time the same creature turns back, never built fresh. |
| "Is this unit my enemy?" / a creature fighting for the players | `Sides.Hostile` + `Sides.NearestPlayerSideCreature` | Every targeting path asks one function. Creatures look for player-side creatures in a small list of their own, never by walking every creature in the world. |
| Handing state to an on-arrive / on-complete callback (projectiles, delayed hits) | `Projectile3D.HitPayload` + a **cached** `static readonly Action<HitPayload>` handler | A lambda that captures locals allocates a closure **per shot**. Instead the state rides the carrier in a by-value struct and one shared handler reads it back. Reuse the pattern for any per-event callback. (Both former users were bow shots; every ranged blow now goes through `Flights`, so this overload currently has no callers.) |
| The same expensive question asked by several systems in one frame | **Frame memo**: stamp `Time.frameCount`, compute once, share the result | The cursor pick (raycast + mob snap-scan) was computed up to 3×/frame by `GameCursor3D`, `UnitStatusPanel`, and click handlers. A two-field memo (`Player3DController.MonsterUnderCursor`) makes N callers cost one scan. Sibling of rule 2 — this is the idiom for *queries*, `CameraRig` is the idiom for *values*. |

---

## Anti-patterns (these regress us — don't do them)

- ❌ `FindObjectOfType` / `FindObjectsByType` / `GameObject.Find` in `Update` (or any per-frame path). → Resolve once, or keep a registry like `MonsterAI3D.Active`.
- ❌ `GetComponent<T>()` inside a loop or every frame. → Cache it on the component (e.g. `MonsterAI3D.Health`).
- ❌ `Camera.main` per object per frame. → `CameraRig.Cam`.
- ❌ `Sprite.Create` / `new Texture2D` per spawn. → `SpriteSheetCache`.
- ❌ Building a `GameObject` + `Canvas` per UI element that spawns repeatedly. → Pool under one canvas.
- ❌ **Returning a** `new List`**/array from a hot, repeatedly-called API.** → Fill a **caller-owned buffer** passed in (see `Pathfinder3D.FindPath(..., List<Vector2> result)`). This was the single biggest combat allocator — mobs repath constantly, and each call newed several lists.
- ❌ `new WaitForSeconds(x)` every coroutine start. → **Fixed** duration: cache the `WaitForSeconds` in a field (`Health.flashWait`). **Variable** duration (anything scaled by attack speed / per-skill config — a field can't cover it): zero-alloc manual timer, `for (float el = 0f; el < dur; el += Time.deltaTime) yield return null;` (see `Player3DAttacker`; don't name the counter `t` — it collides with the conventional `Health t` parameter, CS0136).
- ❌ A lazy `if (x == null) x = FindFirstObjectByType<T>()` fallback **inside a per-frame path** when `x` can be legitimately ABSENT (system not in this scene, player between scenes). The "runs once" intent silently becomes a `Find` every frame, forever. → Resolve at open/scene-load, or throttle the retry to a cadence (see `FootstepTracker`'s `nextFind`, 1 s).
- ❌ `GetComponent<T>() ?? AddComponent<T>()` for a component meant to be **shared per unit**. It can't fail, so a cross-GameObject miss silently becomes a **DUPLICATE** — two instances doing the same per-frame work, with every cached value computed on the wrong one. The player is TWO GameObjects (persistent `Character` + the spawned body carrying `Player3DController`/`StatusController`), so this pattern quietly gave it two `UnitStates` stores: gameplay read one, the buff tray the other. → Resolve the owner **explicitly** (`Character.Instance.States`); a genuine miss should be visible, not papered over. Note rule "cache sibling refs in `Awake`" assumes the sibling is on the SAME GameObject — when it isn't, resolve lazily and **cache the failure path too**, or it degrades into the per-frame lookup below.
- ❌ `Vector3.Distance` / `.magnitude` when you only need to compare. → `sqrMagnitude` vs `r*r`.
- ❌ Writing `RenderSettings.*` or other globals every frame. → Only when the value actually changed (see `DayNightLight` early-out).
- ❌ Per-frame `string` interpolation for the inspector/debug. → `#if UNITY_EDITOR` it, or only on change.
- ❌ Setting `Text.text = $"..."` every frame when the value rarely changes. → Cache the last value and only rewrite on change (see `HoverPlate`). Each assignment both allocates a string AND forces a canvas rebuild.
- ❌ One `Canvas` per HUD widget. Canvases are batch/rebuild boundaries — fewer, shared canvases batch better.
- ❌ **A find-or-create `Ensure()` called from a frame path.** `Foo.Ensure()` searches the whole scene and builds the object if it is missing — it is a BOOT-time call, and the "if it is missing" half hides that from the reader. Call it once (on load, on a weather change, on a scene swap) and hold the reference. **Measured 2026-08-31:** the sky called `CloudShadows.Ensure()` from `LateUpdate`, so every single frame ran a `FindAnyObjectByType` over the entire scene to fetch an object that never changed.
- ❌ **A `magnitude` / `Vector3.Distance` compared against a range.** Same rule as above, listed again because it keeps coming back in routing code: `to.magnitude <= reach` is a square root per agent per frame; `to.sqrMagnitude <= reach * reach` is not.

- ❌ A per-frame boundary check (`InRange`, "am I close enough?") that **gates a hot path** with no hysteresis. A target sitting right at the threshold (e.g. a kiting mob at attack-range edge) flips the check true/false every frame, thrashing whatever it gates — re-routing (`StartRoute`/`FindPath`), restarting an animation, or re-firing SFX. → Add **hysteresis** with the margin INSIDE the range: engage at `range ÷ k`, stay engaged until past `range` (see `UnitOffense.EngageLatch`). Never put the margin outside the range — a target parked between `range` and `range × k` leaves the asker engaged and unable to act, forever. Keep the strict check for the irreversible action (landing the hit).

---

## What counts as "runs once" — the three shapes that are ALLOWED in a frame path

A per-frame method may do an expensive thing as long as it does it once. Three shapes say so, and
the automatic scan below understands exactly these three — write one of them and it stays quiet:

1. **The null cache** — `if (cam == null) cam = CameraRig.Cam;`. Fine when the thing always turns
   up. When it can legitimately be ABSENT forever, this is the anti-pattern above instead: throttle
   the retry (see `FootstepTracker`'s `nextFind`, 1 s) or resolve it at load.
2. **The memo** — `if (map.TryGetValue(k, out var v)) return v;` before building `v`, or
   `while (pool.Count <= i)` before growing a pool. The work happens on the miss and never again.
3. **The change gate** — `if (secs != last) { label.text = …; last = secs; }`. Nothing is written,
   built or allocated unless the value on screen actually moved (see `HoverPlate`, `XpBarHUD`).

Anything genuinely fine that fits none of them gets `// perf-ok: <why>` on the line — the reason
sits with the code, where the next reader can argue with it.

---

## The automatic check

```bash
tools/check-perf-hotpaths.py     # reads every Update/LateUpdate/FixedUpdate/OnGUI in the game
```

It reads four kinds of hot path — the per-frame engine callbacks, the physics contact callbacks, the
LOOPING part of a coroutine, and the per-hit combat path from `Health.ApplyDamage` / `TakeDamage`
outwards — plus the helpers each of those calls, across files. It runs by itself at the end of any
turn that edited a runtime script, reporting only faults in files that turn touched.

**Its silence is not approval.** It cannot see past two calls, cannot tell how often a branch is
really taken, and knows nothing about the GPU, memory or load times. The Profiler is still the
authority; this only catches the mistakes that are visible in the source.

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