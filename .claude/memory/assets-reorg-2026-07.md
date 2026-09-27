---
name: assets-reorg-2026-07
description: "2026-07-16 Assets/ reorganized by usage into numbered buckets 0_Raw/1_Authored/2_Generated/3_Data; OLD paths in older memories are stale"
metadata: 
  node_type: memory
  type: project
  originSessionId: 574d081b-a96f-44b8-9d8c-9a3de632933f
---

On 2026-07-16 `unity/Assets/` was reorganized by INTENDED USAGE into **number-prefixed buckets** (prefixes sort them to the top of Unity's Project window). Any OLDER memory/doc naming a pre-reorg path is stale — translate via this mapping:

- **`0_Raw/`** — vendor packs, untouched: `Art/Pxltgr`, `Art/NinjaAdventure`, `Art/Quat`, `Art/Hovl Studio`, `Art/KenneyCursorPack`, `Blink`, `Eric VFX Studio`, `NamuFX`, `Phoenix3D`, `20 Ground Material Sets MAN MADE`, `Standard Assets (Mobile)` → `Assets/0_Raw/<pack>` (e.g. `Assets/0_Raw/Quat/Rigged`).
- **`1_Authored/`** — stuff we make/place: `Prefabs/` → `Assets/1_Authored/Prefabs/` (drag-drop palette still at `…/Prefabs/Props`); `Art/` → `Assets/1_Authored/Art/` (incl. `GroundTiles/`, since 2026-09-24 the ONE home of every ground tile texture — the old `…/Props/Grounds/` tile prefabs were removed).
- **`2_Generated/`** — tool output, regenerable: `Materials/Generated`→`2_Generated/Materials`, `Meshes/Generated`→`2_Generated/Meshes`, `Art/BakedChars`→`2_Generated/BakedChars`, decimated→`2_Generated/Decimated`.
- **`3_Data/`** — runtime data: `Resources/`→`Assets/3_Data/Resources/` (still named "Resources" so `Resources.Load` is unaffected), `Recipes/` (incl. TownRecipe/FieldRecipe/DungeonRecipe/Prontera that were loose at root)→`3_Data/Recipes`, `ScriptableObjects/` (incl. BrushPalette.asset)→`3_Data/ScriptableObjects`.
- **Loose root .assets:** BgmTrack.asset → `Assets/Audio/`.
- **UNTOUCHED at root** (package-dependent / engine — do NOT move): `TextMesh Pro`, `UI Toolkit`, `Standard Assets` stayed via… actually Standard Assets moved to 0_Raw; but `TextMesh Pro`, `UI Toolkit`, `Settings`, `Shaders`, `Scenes`, `Scripts`, `Audio`, `UI` all stay at root.

**Why:** discoverability by lifecycle — `0_Raw`=never edit/place, `1_Authored`=ours (the drag-drop palette), `2_Generated`=regenerable, `3_Data`=runtime tables. Each bucket has a `README.md` stating its rule. All hard-coded C# path strings (~130), 2 python tools, optimize-glb.sh, prop-manifest.json, TOOLS.md were patched in the same pass. Scenes/prefabs/materials link by GUID (in `.meta`) so they were unaffected; only code path-strings needed patching.

**How to apply:** when a memory or doc names a pre-reorg path, translate via the mapping instead of trusting it; unpatched stragglers may exist in old memory files only (code + TOOLS.md were swept clean). Related: [[fantasy-town-pxltgr]], [[mixamo-batch-autorig-api]] predate this.
