---
name: map-painter-footprint-ignored-authored-scale
description: "map painter drew trees ~3.3× oversized because the manifest exporter measured footprints at localScale=1, discarding each prefab's authored root scale (trees=0.3, houses=1)"
metadata: 
  node_type: memory
  type: project
  originSessionId: eb396ae0-5e42-48a2-80d4-52e71f924e6e
---

**Symptom:** In the map painter, trees rendered enormously oversized vs houses and vs the in-game world (a single tree dominated the map, with a giant white facing-arrow triangle).

**Root cause:** `BrushPaletteWindow.cs` `Footprint(name, scale=1)` did `go.transform.localScale = Vector3.one * scale`, **overwriting the prefab's authored root scale**. Tree prefabs (`unity/Assets/1_Authored/Prefabs/Props/Trees/*.prefab`) are authored at root `m_LocalScale = 0.3`; houses at `1`. So the manifest measured trees at their raw mesh bounds (Deciduous_tree2 = 21.8 u) while the importer places props at the prefab's OWN scale untouched (`MapPainterImporter.cs:400` "the prefab owns its size; nothing here rescales it") → real placed size 21.8×0.3 ≈ 6.5 u. Painter drew ~3.3× too big. This regressed when prop auto-resizing was removed (native-sizing) but the measurement kept forcing scale 1.

**Fix:** `Footprint` now `go.transform.localScale *= Mathf.Max(0.01f, scale)` — measures as PLACED (authored scale × the extra `scale` multiplier; 1 = as-authored). Re-export the manifest (or, as done here, regenerate `prop-manifest.json` by multiplying each object prefab's x,z by its authored root scale and recomputing `footprint`=max). `RenderThumbnail` still forces scale 1 but is fine — uniform scale preserves aspect, so the PNG is unchanged; only the footprint NUMBERS drove the size.

**Gotcha:** `prop-manifest.json` is a generated artifact; the C# fix only affects FUTURE exports — the live painter reads the JSON, so the JSON itself had to be regenerated. Trees/walls authored 0.3, houses 1.0. Related: [[map-painter-pipeline]], [[map-painter-thumbnail-aspect-squash]].
