---
name: changes-must-persist-in-repo
description: "Every change must persist in a repo-tracked source (code/asset on disk), never only in runtime/editor state"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 5714cf56-d6c8-4726-b548-4d80e11edcad
---

When making a change, ALWAYS land it in a place that persists across domain reloads / Play / editor restarts — a repo-tracked source file or a saved-and-verified asset. NEVER leave a change living only in runtime state or a bare `execute_code` mutation.

**Why:** the user was (rightly) furious when a value set via `execute_code` on a ScriptableObject didn't survive — it looked done but evaporated. An ephemeral edit is worse than no edit: it wastes their playtest and erodes trust.

**How to apply:**
- Prefer a CODE-level source of truth (a field default, a constant) so the value is baked in the repo and survives asset rebuilds/resets. See [[single-source-of-truth-principle]].
- If you must write an asset at runtime: `SetDirty` + `SaveAssetIfDirty`/`SaveAssets`, then READ IT BACK from disk to prove it stuck — don't assume.
- Check whether any builder/rebuild tool re-generates the asset and would clobber the field ([[editor-tools-menu]]); make the tool preserve it.
- Canonical case: `EffectDef.groundRingRadius` (cast-circle sizing) — persisted via code default `1.5f` in `EffectLibrary.cs` AND the on-disk `EffectLibrary.asset`, not just an `execute_code` poke.
