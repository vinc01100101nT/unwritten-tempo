---
name: runtime-material-on-prefab-null-pink
description: "Assigning an in-memory `new Material(...)` to a PREFAB asset serializes as a null slot → prop renders pink; persist the material as a .mat asset first."
metadata: 
  node_type: memory
  type: project
  originSessionId: 3ac89ae2-20f7-417e-8287-01dd86911958
---

**Symptom:** Ran `Tools ▸ unwritten ▸ World ▸ Sway Selected Tree` on the **Oak_Tree PREFAB** (selected in the
Project window, not a scene instance) → the prefab thumbnail + all instances turned **pink/magenta**.

**Ground truth:** the prefab's renderer `m_Materials` was `- {fileID: 0}` (a NULL reference), NOT a
missing-shader material. Pink here = empty material slot, not a broken shader.

**Root cause:** the tool builds its sway material with `new Material(_foliageWind)` — an **in-memory** object
with no asset backing (`GetSwayMaterial` in `RecipeGenerator.cs`). That's fine for a SCENE object (the material
gets embedded in the `.unity` file), but assigning it to a **prefab asset** and saving the prefab writes a
reference to a material that has no on-disk home → serializes as `fileID: 0` (null) → pink. General rule:
**a material written into a prefab/asset must itself be a persisted asset**, or the slot serializes to null.

**Fix (`RecipeGenerator.SwaySelectedTree`):** detect a prefab-asset target with `EditorUtility.IsPersistent(go)`;
when so, run each sway material through new `PersistSwayMaterial(m)` (saves/replaces a `.mat` under
`Assets/Prefabs/Props/Materials/<name>.mat`, deterministic path = idempotent) BEFORE assigning, then
`PrefabUtility.SavePrefabAsset(go)`. Scene objects keep the cheap in-memory clone as before.

**Recovery of an already-pinked prefab:** re-running the tool does NOT fix it — the loop does
`if (src == null ...) continue;` so a null slot is skipped. Restore the material by editing the prefab YAML:
set `m_Materials: - {fileID: 2100000, guid: <mat-guid>, type: 2}` (Oak_Tree's original = `Oak_Tree_0.mat`,
guid `a5bdfe900aead904787be29320322262`). fileID `2100000` = the main object of a `.mat` asset.

Same trap applies to any editor tool that assigns a runtime-created Material/Mesh to a persisted asset. See
also [[changes-must-persist-in-repo]].
