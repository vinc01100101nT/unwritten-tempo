# ☀️ WAKE_UP — Town Map Generator (VIN-13) · ✅ PER-PROP COLLISION + PERFORMANCE PRE-BAKE (Play-confirmed)

**For:** vince · **Date:** 2026-06-16 · **Built by:** interactive session

> ✅ Everything below is **compiled + Play-confirmed by the dev**. The generator bakes clean towns; props have **authorable per-prop collision** (Auto/Custom/None, half-cell resolution); collision + visuals are **pre-baked at generate time** (no on-Play generation). Collision now blocks across the whole map (the last two bugs are fixed).

---

## ▶ RESUME AFTER `/clear` (read this first)

**Safe to** `/clear` — nothing critical is only in chat. Source of truth = the **code on disk** (`D:\Unity3d\unwritten\Assets\Scripts\`) + this file + auto-memory (`performance-first-prebake`, `ysort-depth`, `map-generator-tool`, `floor-autotile-ruletiles`) + the Tempo **workflow doc / VIN-13**.

**To resume in a fresh session, paste this:**

> `Resume unwritten (VIN-13 Town Map Generator). Read WAKE_UP.md. The generator + per-prop collision + performance pre-bake are DONE and Play-confirmed. Real game code is at D:\Unity3d\unwritten\Assets\Scripts\. Continue with whatever I point at next.`

**Map of everything:**

- **Spec** → Tempo doc *Procedural Town Map Generator — Spec* (`q178ppzhynajt660nadeh6ras988n88a`). **Workflow how-to** → Tempo doc *Town Map Generator — Workflow (Beginner Guide)* (`q171zzcsyy34n52cga9v1zn95d88sn9g`, nested under the spec). **Tracker** → issue **VIN-13**.
- **Code** → `D:\Unity3d\unwritten\Assets\Scripts\MapGen\*` (runtime) + `…\Editor\*` (tools). Key decisions in memories `performance-first-prebake`, `ysort-depth`, `map-generator-tool`.

**Future-me note:** ALWAYS keep this RESUME block at the top when you rewrite WAKE_UP.md.

---

## ⚡ NEW HARD RULE this session — performance first (memory `performance-first-prebake`)

The dev's words: *"a 2D game that lags is a bad game."* Protect **GPU / CPU / RAM** in every design choice. **Prefer pre-bake over on-Play.** Merge/keep static colliders cheap; don't sort static props every frame; move work to author/generate time. When proposing an approach, state its cost vs alternatives.

## What works now (this session's deliverables)

1. **Per-prop collision is authorable** on `PropTemplate`:
   - `collisionMode` = **Auto** (buildings full footprint, trees bottom-row trunk) / **Custom** / **None** (off, keeps cells). Default Auto.
   - **Half-cell resolution** — a 3×3 prop edits as a **6×6 grid** in the Inspector (`PropTemplateEditor`). Click half-cells to paint Custom (auto-switches to Custom); or paint a `Collision` capture layer (full cells → 4 half-cells). `Clear → Auto` / `Fill footprint → Custom` buttons.
2. **Everything is PRE-BAKED at Generate time** (no on-Play generation):
   - Visuals → static foot-sorted prop objects with **fixed `sortingOrder`** (computed once), **no per-frame `YDepthSorter`** on statics (only moving units sort each frame).
   - Collision → one `Collision` GameObject (Static `Rigidbody2D` + **plain run-merged `BoxCollider2D`**, half-cell sized; green outlines; Pathfinder wall).
   - `DepthSortRuntime`'s on-Play bake is now a guarded **fallback** that no-ops for pre-baked scenes; **on-Play collider generation REMOVED**.
3. **Generate Town** is fixed (two editor crashes gone): the additive-untitled-scene error and the `EndLayoutGroup` GUI error.
4. **Hand-painted maps** → `Tools ▸ unwritten ▸ Bake Map (props + collision)` (name-derived collision: building=full, tree=trunk). **`Bake Buildings to Objects` retired** → now redirects to Bake Map (no slow path).

## ⭐ The 4 bugs squashed this session (don't repeat the hunts)

1. **Generate threw "Cannot create a new scene additively with an untitled scene unsaved."** A leftover untitled preview (orphaned across a domain reload) wasn't closed. Fix: close **every** loaded untitled scene first; use `NewSceneMode.Single` when the only scene left is untitled. (`MapGeneratorWindow.Generate`)
2. **`EndLayoutGroup: BeginLayoutGroup must be called first`.** Generate/Save did scene + dialog work *inside* `OnGUI`, corrupting IMGUI's layout stack. Fix: defer both via `EditorApplication.delayCall` + `Repaint()`.
3. **Player passed through everything (no collision).** I'd used a `CompositeCollider2D` — it shows outlines in the editor but loads with **empty geometry at Play**. Fix: **plain static `BoxCollider2D`** (run-merged), no composite. ⚠️ Don't reintroduce a scene-baked composite for collision.
4. **Collision only in the lower-left quarter.** `TownLayout.AddCollision` ran HALF-cell coords through `In()`, a FULL-cell bounds check, dropping everything past the midpoint. Fix: bounds-check against the half-grid (`< width*2 && < height*2`).

## ⚠️ Migration notes (tell the dev if they reopen old content)

- **Old saved towns** relied on the removed on-Play collider gen → **re-Generate** them (or for hand-painted, run **Bake Map**).
- **Custom props authored before the half-cell switch** have old full-cell `collisionCells` that now read as half-cells → **re-paint** their collision. (Auto props are fine — they re-derive.)
- **Bake Map is hand-painted-maps only** — it derives collision by tilemap NAME and can't see per-prop Custom modes. For per-prop collision, capture a PropTemplate + use **Generate Town**.

## Files touched (this session) — all under `D:\Unity3d\unwritten\Assets\Scripts\`

- **Runtime (`MapGen/`, root):** `PropTemplate.cs` (collisionMode + half-cell `collisionCells` + `CollisionFootprint()` + `HalfCellsOf()`), `TownLayout.cs` (`collision` half-cell set + `AddCollision` half-grid bounds), `TownGenerator.cs` (Stamp + boundary feed `collision`), `DepthSortRuntime.cs` (no on-Play colliders; fixed static sort order; public `BakeObstacles()`; skip Collision layer).
- **Editor:** `CollisionLayerTools.cs` *(new — plain run-merged static boxes)*, `PropTemplateEditor.cs` *(new — 2× half-cell grid)*, `MapBaker.cs` *(new — hand-painted bake)*, `PropTemplateTools.cs` (capture peels Collision layer; stamp materializes collision; no visual-layer colliders), `MapGeneratorWindow.cs` (untitled-scene + delayCall fixes; no visual colliders; materialize `collision`; pre-bake visuals), `BuildingBakeTools.cs` (gutted → redirects to Bake Map).

## What to tell me next (menu)

- **More generator features** — roadside buildings, building rotation to face roads, field/cave themes.
- **The open terrain decision** (still unpicked): patches vs paths look identical (same Dirt RuleTile) — A) DarkDirt patches, B) decor/tall-grass variety, C) leave merged.
- **Bake & play-test** a saved town end-to-end (wire Portal Target Scenes, walk it).
- **Next perf lever** (deferred): combine each prop's per-cell SpriteRenderers into one mesh to cut draw calls (URP's SRP batcher already merges same-atlas sprites, so likely not urgent).
- **Switch tracks** — Phase F death/respawn screen, or Phase G skill-draft (VIN-1/3/4/5/6).
