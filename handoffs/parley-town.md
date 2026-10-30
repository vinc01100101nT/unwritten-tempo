# Handoff — Parley, the second town piece, and the world extension around it

**Task:** extend the seamless world north of Lastline with the Sword town Parley (theme Blossom Wind) — first the town
piece, then the 35 squares of rows 12–16 (x 17–23) around it. Also done this session: the Geyser stuck-spot fix and the
river links. No Tempo issue/doc id. Related, older handoff: `handoffs/seamless-world.md` (the world as a whole).

## Status

**IN PROGRESS.** Parley is built and walkable in the editor; the 35 squares around it are built (84 squares + 2 towns).
**The game server must be rebuilt and restarted** — the ground (Parley and the world chunks) was rebuilt again on
2026-10-28 for the bridge underside. The user has not play-tested since that rebuild, so **don't trust prior "it works"
claims** beyond the checks listed under "Verified".

## What's done

- **Geyser stuck spot** — `GroundSnap.SurfaceNearestY` (new); `GroundTargeter.ClampToRange` re-reads the ground at the
  clamped spot; `SkillEffectApplier` Teleport/Dash seats the destination on the ground nearest its height.
- **River links** — `tools/world-layout/build_world_layout.py` `RIVER_LINKS` (river 2→0 and 0→1), bed only falls, bridges
  where roads cross. Preview now lives at `data/world-layout.html`; `tools/check-before-done.py` groups `world-layout`
  and `town-plans` re-run both tools at the end of a turn that edited them.
- **Town plan tool** — `tools/town-plan/build_town_plan.py` writes `data/towns/parley.json` + `parley.html`
  (the preview the user approved). Layout: Petal Stream canal round the island (Duelist's Ring), Petal Square circle,
  slanted streets (Blossom Road, High Street, Sword Lane, Willow Walk, Island Lane, Orchard Lane, Mill Row, Weaver's
  Lane), tavern with tables/chairs, Petal Grove orchard, bank trees, 6 bridges, 2 ground mounds over the canal ends,
  6 floating-leaf patches, trees/bushes on stone sit in soil pits with a curb, never on bare paving.
- **Town builder** — `unity/Assets/Scripts/Editor/TownPieceBuilder.cs` (new) builds `1_Authored/Prefabs/Towns/Parley.prefab`
  from the plan: one ground mesh (ONEGROUND) with smooth curved curbs + top stone band, sunk canal with sloped banks,
  each bridge model's own triangles joined into the ground twice — once as the model, once facing the other way so the open-bottomed model is drawn and solid from below (drawn with the bridge's own material, solid as ground — no separate bridge object), footprint lift so nothing snags under a bridge edge, mounds, blossom tint on
  bushes and leaves. Curb faces turn toward the low side by the field's slope across their own triangle; where low
  paving meets the stone band along water a face closes the step. Every build replaces the piece; Parley's plan is now edited in the town editor (tools/town-editor).
- **Shared pieces** — `BridgeDecks.cs` (new, lifted out of `SeamlessWorldBuilder`, both builders use it; `AddModel` lays
  the bridge model into the ground mesh, both facings, for town and world bridges alike);
  `GroundBlendBuilder.LayPlotCurbed` (curved curbs; the endless-loop bug from `List<int>.Find` returning 0 is fixed).
- **Under-bridge runtime fixes (2026-10-28)** — `GroundSnap.TryFeetY` (new): a body's drawn feet look for ground from
  one body height above the feet, not 2 units (player `Character3DVisual`, every other body `Agent3DVisual`), so a body
  under a bridge is no longer drawn on its top. `GroundSnap.SurfaceNearestY` walks the stacked surfaces one cast at a
  time (one cast sees one hit per collider, and the ground is one collider); `GroundDrop`, `QuestArrows` (arrows + pin)
  and `WeaponSocket` now use it. `CameraFollow3D.ClearOfGround`: one ray from the framed point stops the camera the near
  clip distance in front of the ground, so it can no longer sit inside a bank or a bridge.
- **World builder** — `SeamlessWorldBuilder` takes every town under `Prefabs/Towns/`, lays roads to each town's gates,
  blends each rim; squares wholly under a town are still built (no ground child) so no edge walls appear inside a town.
- **Kit prefabs** — 21 `Props/Outdoor/*_LOD0.prefab` had a child scale of 0.3 on top of the kit's own → set to 1.
- **Know-how** — the world-authoring skill (layout and town decisions, builders, rebuild commands, checks); memory `list-find-returns-zero`.

## Verified (watched in Play, test account Alpha)

South gate entry, curbs and bands, falling petals, Old Bridge crossed north→south, island via the east-arm bridge, west
mound bridge crossed, only the 4 outer world edge walls remain. Last session's final walks: past the Old Bridge's
north-east bank to (-3.49, 184.33); across the east mound bridge to (-6.05, 189.59). After the curb/bridge fix
(2026-10-28): wrong-facing curb walls 118 → 0, the water-band step closed, open water under every arch, all 6 town
bridges solid at their sides and not blocked for the route finder, Alpha walked to the far side of a bridge. Round 2
(2026-10-28): every bridge model is part of the ground (downward rays hit it on all 6 town bridges), curb band stretched
stripes 514 → 0, Tavern LOD levels aligned (Alpha stopped by its drawn walls). Lastline's 49 squares compared identical
after each world rebuild. Round 3 (2026-10-28, Alpha in Play, Old Bridge at town (6.68, 12.84)): on the river bed under
it the drawn body sits on the bed (7.32), not the top (8.28); upward rays meet the underside at 3,009 of 3,011 spots
under the 6 bridges (the 2 misses are single points on a bank rim); walking up the bank toward either end stops under
the bridge at -0.05; walking along the river under it passes freely; the camera stays under the bridge, out of the bank;
the top view is unchanged. Not watched: the world square's bridge, a creature under a bridge.

## Uncommitted working-tree changes (measured 2026-10-28, 361 entries)

Everything above is uncommitted: the editor and runtime C# files named above (two of them new), the two tools, `data/towns/`, `data/world-layout.*`,
the kit prefabs, `Prefabs/Towns/Parley*`, `2_Generated/Materials/`, all regenerated world chunks and meshes, the memory
files. Also modified and NOT from this task: `3_Data/Resources/Fonts/DejaVuSansCondensed SDF.asset` (leave it alone).

## What's left

1. User play-test of Parley after rebuilding + restarting **the game server** (ground changed; the character server and
   account service need nothing) — including walking under a bridge and swinging the camera low there.
   From below, a bridge shows its walkway's own paving and moss (the model's top seen from under it), not separate stone.
2. The Keeper-per-map problem (deferred by the user: "easy thing").
3. Known, not fixed: the pathfinder may route a walker hugging a bridge's side; waders can walk through the canal.
4. Docs and wiki not touched (rule: only once the user calls the work final). Patch notes go in `launcher/news.json` on push.
5. Next towns: each needs its own plan + preview and the user's go before building; every town wholly unique, themed by
   its weapon.

## Dead ends (don't repeat)

- **Proven:** curb faces turned by sampling the field 0.02 off their middle faced the wrong way at tight bends (blue
  sky holes). Walls hung under each deck's sides (to stop snagging) closed the arch like a striped wall — replaced by
  the bridge model's own solid shape. Diagonal-cut curbs left hollow triangle outlines → replaced by the curved curb field. A flat-topped mound
  made cliff faces at its edge → shaped to rise under the bridge's back half, top = deck mid height. The invisible walls
  were the world's edge walls round a square skipped for lying inside Parley — not tree trunks (that first diagnosis was
  wrong). A player stuck beside a bridge was the deck edge overhanging the bank → footprint lift (its later deck side walls were replaced by the model's own collider).
- A hand-built underside (per slice of the model, its lowest point across its width, plus side walls) left holes and a
  jagged ceiling: the model's piers and end blocks drag a slice's lowest point far below its walkway. Replaced by laying
  every model triangle a second time, facing the other way.
- Unity froze twice on `List<int>.Find` returning 0 for "no match" (an endless walk) — use `foreach` with an explicit -1.

## Next single step

Ask the user to rebuild and restart the game server, then play-test Parley from the south gate.

## How to resume

```text
Read handoffs/parley-town.md and continue the Parley / world-extension work from "What's left".
```
