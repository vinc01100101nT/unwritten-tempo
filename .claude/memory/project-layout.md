---
name: project-layout
description: "unwritten Unity project — engine/tech facts, where the code lives, scene & tilemap layout, where units come from"
metadata: 
  node_type: memory
  type: project
  originSessionId: 7e46c3f0-a8b6-4991-952c-5c03f2890bdc
---

Fast orientation for the **unwritten** Unity project so a fresh session doesn't have to re-probe.

## Where the code lives (read this first)
- The conversation **cwd is the React prototype** (`D:\tempo\projects\roguelike-mmo`) — there is NO game C# there. Globbing the cwd for `*.cs` returns nothing (wasted a few calls on boot once).
- Real game scripts: **`D:\Unity3d\unwritten\Assets\Scripts\`** (gameplay) and `…\Assets\Scripts\Editor\` (one-click tools). Edit directly; there is no `unity/` staging copy any more. See [[unity-2d-direction]] and [[editor-tools-menu]].

## Engine / tech facts
- **Unity 6 (6000.4.11f1)**, Personal tier.
- **URP 17.4** with the **2D Renderer** (`Assets/Settings/Renderer2D.asset`; URP global settings asset at project root of Assets). This matters for rendering work — e.g. depth uses the camera's **Custom Transparency Sort Axis**, not the built-in pipeline. See [[ysort-depth]].
- **PPU 16** for all art (Ninja Adventure, CC0). 1 tile = 1 world unit.
- One sorting layer only: **"Default"**. Depth is by the **Y transparency axis** + shared sorting order, not by sorting layers.
- **Obsolete-API warnings (CS0618) are suppressed project-wide** via `Assets\csc.rsp` (`-nowarn:0618` + `0612`). This Unity version flags `FindObjectsByType` / `FindFirstObjectByType` / `FindObjectsSortMode` as obsolete, but the whole codebase uses them as the standard pattern — they're cosmetic noise, so we silence rather than chase the (unknown) replacement. ⚠️ Trade-off: this also hides any *future* genuine `[Obsolete]` warnings; delete `csc.rsp` to re-enable. No `.asmdef` files exist, so the single root `csc.rsp` covers all predefined assemblies (Assembly-CSharp + -Editor). ⚠️ Keep `csc.rsp` flags-only (no `#` comment lines) — a bad line there breaks ALL compilation.
- **No local compile/Play loop available to the agent** — I can't run Unity. Verification is manual: the user presses Play. Don't claim "verified," say "should compile/work, press Play to confirm."

## Scenes & world structure
- Scenes: `Assets/Scenes/Field.unity` (main gameplay — player, NPC, MonsterSpawner, portals) and `Town.unity`. (`Assets/Settings/Scenes/URP2DSceneTemplate.unity` is just a template, ignore.)
- Each gameplay scene has a **Grid** with hand-painted **Tilemaps**: **"Ground"** (floor; always behind) and **"Obstacles"** (trees/houses; carries the obstacle colliders). Those Obstacles colliders are the **walls** the A* pathfinder routes around. World content is **hand-painted by the user** — don't auto-generate it (see [[hand-authored-world-content]]).
- `Bootstrap.cs` is the scene entry point (sets up camera + transparency axis, places the player, attaches `CameraFollow2D`). `buildArena` is OFF in the real tilemap world (the procedural arena is just the hello-world fallback).

## Where units come from (edit the right thing)
- **Player**: an in-scene GameObject named "Player", built by `PlayerBuilder` and wired into `Bootstrap.playerOverride`. SpriteRenderer + PlayerController2D live on the same object.
- **NPCs**: in-scene GameObjects (SpriteRenderer + Interactable), built by `WorldTools.CreateNpc`.
- **Monsters**: **prefabs** in `Assets/Prefabs/Monsters/`, instantiated at runtime by `MonsterSpawner`. To change monster behaviour/stats/visuals, edit the **prefab**, not a scene instance (scene instances may not exist until Play).
