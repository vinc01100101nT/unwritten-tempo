---
name: asset-move-breaks-hardcoded-tool-paths
description: "Moving assets out of 0_Raw breaks hardcoded string paths in editor build-tools (GUIDs survive, path strings don't) — and Setup Systems Scene SAVES OVER the scene, so a stale path silently wipes the player."
metadata: 
  node_type: memory
  type: project
  originSessionId: db4a6bcd-1754-4809-8c2d-7d3c3f116c2b
---

**Symptom:** After promoting Quaternius chars out of `0_Raw/Quat` into `1_Authored/Art/Characters`, the player was fine in Play — until the user ran **Setup Systems Scene**, then the player VANISHED and the good `Systems.unity` was overwritten with a playerless one.

**Root cause (two compounding):**
1. **GUID refs survive a move (meta moved too), but hardcoded STRING paths in code do NOT.** Editor build-tools store folder addresses as `const string` (e.g. `Player3DVisualBuilder.RiggedDir = "Assets/0_Raw/Quat/Rigged"`, `HumanoidClipResolver.ClipDir`, `PlayerModelSwap.RiggedDir`, `BulkHumanoidRig.DefaultFolder`, `BatchCharacterBaker.fbxFolder`). Moving the assets left these pointing at deleted folders → `File.Exists(modelPath)` false → `Attach()` returns null → no player visual.
2. **`SystemsSceneBuilder` (Setup Systems Scene) does `EditorSceneManager.SaveScene(sys, SystemsPath)` — it OVERWRITES `Systems.unity` on disk** after a full `NewScene`+rebuild. So the broken (playerless) rebuild got saved over the good scene. No verify, no backup.

**Fix applied:** repointed all stale `0_Raw/Quat` paths → `1_Authored/Art/Characters/{Models,Animations,Animators}` in 6 files. Recovery for the wiped scene: `git checkout -- unity/Assets/Scenes/Systems.unity` (committed version's GUIDs still resolve).

**Rules for next time (BOTH):**
- **After ANY asset move/promotion, grep code for the OLD path string** (`grep -rn "0_Raw/<Pack>" --include=*.cs`) and repoint every hit — same as you'd update GUID refs. This is the sibling gap to [[raw-fs-move-purges-unity-asset]] and the NinjaAdventure move (which had ~10 code path sites).
- **7 tools call `SaveScene` and can silently overwrite a good scene with a broken build** (SystemsSceneBuilder, RecipeGenerator, RegionBuilder, SandboxBuilder, WorldCreator, MapPainterImporter, MinimapBaker). They need: (a) ABORT-before-save if a critical asset is missing, (b) auto-backup the scene file before overwrite. NOT to be deleted — SystemsSceneBuilder/`RigBuilder25D` is load-bearing (31 dependents = the core build pipeline).
