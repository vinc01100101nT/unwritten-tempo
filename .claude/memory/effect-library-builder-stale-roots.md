---
name: effect-library-builder-stale-roots
description: Rebuild Effect Library silently added nothing for months — all 5 scan roots pointed at folders removed in the assets reorg; now one live root at 1_Authored/Prefabs/VFX.
metadata: 
  node_type: memory
  type: project
  originSessionId: ce85d402-48e3-442a-8ebe-97c3526710b9
---

**Symptom (2026-08-11):** a new VFX prefab never appeared in the Vfx Composer's effect dropdown, and
running **Tools ▸ unwritten ▸ Art ▸ Rebuild Effect Library** did not add it either.

**Root cause:** `EffectLibraryBuilder.Roots` still listed the five PRE-reorg folders
(`Assets/1_Authored/Art/VFX`, three `0_Raw/Hovl…`/`Eric VFX…`, `0_Raw/NamuFX/…`) — **every one of them
gone** after [[assets-reorg-2026-07]] consolidated all effect prefabs into
`Assets/1_Authored/Prefabs/VFX/<Category>/`. `RebuildCore` early-returns "No prefab folders found" when
`missing.Count == Roots.Length`, so it reported nothing and added nothing. The 105 existing entries kept
working because `EffectLibrary.asset` matches entries by **prefab reference (GUID), not path** — which is
exactly why nobody noticed the builder had been dead since the reorg.

**Fix** (`Editor/EffectLibraryBuilder.cs`): ONE root —
`new ScanRoot("Assets/1_Authored/Prefabs/VFX", byFirstSubfolder: true, extraDrop: NoiseTokens)` — plus
`CategoryPrefix` remapped to the REAL folder names (`Hits`→hit, `Slashes`→slash, `MagicCircles`→circle,
`Auras`→aura, `Shields`→shield, `Portals`→portal, `Sparks`→spark, `Environment`→env). Without that remap
new drops would generate `hits.*`/`slashes.*` and clash with the established `hit.*`/`slash.*` ids.
`"magiccircle"` was folded into the global `NoiseTokens`. Verified: rebuild added exactly
`fire.footprint`, 105 existing entries unchanged.

**Gotchas / lessons:**
- A GUID-keyed registry **masks a dead path-based scanner**. When "the tool does nothing", check whether
  its input folders still EXIST before touching its logic (see [[dont-agree-reflexively]] on measuring).
- Verifying a scanner offline by replaying its own naming logic in Python (over the real folders) predicted
  the exact added ids + collision set before Unity ever ran it — cheap and it matched.
- Editing prefab YAML by hand: **preserve the exact indentation**. Rewriting
  `    maxNumParticles:` at 2 spaces silently corrupts the file for Unity; always read the line back.
- Unity re-serializes an open prefab and can overwrite external edits — read the file fresh before each
  edit batch and assert the expected old value. See [[changes-must-persist-in-repo]].
