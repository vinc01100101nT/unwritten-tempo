---
name: hand-authored-world-content
description: "User builds game world/level content (towns, maps, decoration) by hand — don't auto-generate it"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: a3d51154-5cde-4c73-891a-be917b8927ac
---

On **unwritten** (Unity 2D), Claude built an auto **"Build Town"** editor tool twice to place houses + NPCs from the Ninja Adventure tileset. Both looked messy; the user said to **remove all of it** and that they'll **paint/place the Town by hand**.

**Why:** world/level content (town layout, house placement, decoration) is a hand-authoring/art task. Procedurally assembling it from a tileset that isn't authored as whole-object sprites produces junk, and the user wants creative control over how their world looks.

**How to apply:** for world-building tasks (towns, maps, decoration, level layout), do NOT write generators that auto-place art. Instead help with **setup** — slice tilesets into Tile assets, create Tile Palettes, build prefabs, fix import settings, write one-click component/prefab builders — and give click-by-click painting guidance, then let the user place/paint it themselves. Auto-builders are welcome for **systems/components** (player rig, dialogue UI, portals, spawn points), NOT for aesthetic/level content. When a task says "make a Town," default to enabling manual authoring unless the user explicitly asks to generate it. Related: [[unwritten-foundation-first]], [[user-new-to-unity]].

**Refinement (2026-06-14):** user now wants to **automate base terrain/map creation** via a **design-time editor tool** (`Tools ▸ unwritten ▸ Generate Map`) that **bakes a persistent map** — NOT runtime/roguelike on-the-fly generation. Key distinctions from the failed "Build Town" tool: this is **tile-level terrain** (noise/BSP → Rule Tiles for correct edges, write via `SetTilesBlock` batch API), not auto-placing whole-object art. **Towns/key areas stay hand-authored** (a town = a special chunk the generator leaves alone). Agreed scalability architecture: **world = grid of ~64×64-tile chunks, streamed additively** (`WorldStreamer` keeps a 3×3 ring loaded), `CompositeCollider2D` on Obstacles, per-chunk spawners, object pooling; design data in chunks from day one even if loaded all at once. So: tile/terrain auto-gen = OK now; whole-object art auto-placement = still no.

**Refinement 2 (2026-06-14):** scope now explicitly includes auto-generating **towns** (the first generator theme), reconciled with the no-art-auto-placement rule via **tile-stamp templates** — the user hand-paints a few houses, the tool captures each as a `HouseTemplate`, and the generator only automates PLACEMENT + roads + decor + spawns. The principle holds: **ART stays hand-authored, LAYOUT is automated.** First layout = radial/plaza village. Tracked as VIN-13; detail in [[map-generator-tool]].