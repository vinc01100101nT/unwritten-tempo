---
name: unity-editor-assembly-source-list-stale
description: A new Editor script's menu item never appears — Unity's editor-assembly source list can go stale for days; diagnose via Library/Bee .rsp, never by trusting a refresh.
metadata:
  type: project
---

**Symptom (2026-08-13):** added `Assets/Scripts/Editor/CharacterCastTools.cs` with four `[MenuItem("Tools/unwritten/Mobs & NPCs/…")]` entries. The menu items **never appeared**, through repeated user refreshes and recompiles. New RUNTIME scripts added in the same minutes compiled fine.

**Root cause — measured, not guessed:** Unity keeps a per-assembly compiler input list at
`unity/Library/Bee/artifacts/<hash>.dag/Assembly-CSharp{,-Editor}.rsp`.

- `Assembly-CSharp.rsp` — regenerated that day, listed **229** files = the 229 non-Editor `.cs` on disk. ✅
- `Assembly-CSharp-Editor.rsp` — **two days stale**, listed **89** files while **90** `.cs` sat in `Assets/Scripts/Editor/`. ❌

Unity rebuilt the DLL (contents changed) but **reused the stale file list**, so the new file was never compiled into any assembly. A plain refresh does not fix this — Unity believes the list is unchanged.

**Ruled out (don't re-check these):** wrong project path (verified via `ps` + Editor.log), GUID collision (`grep` the guid across `*.meta` — unique), malformed `.meta` (all 90 editor metas are minimal `fileFormatVersion + guid`, mine identical), import errors (none in Editor.log or the AssetImportWorker logs), missing asmdef (project has none).

**Diagnosis recipe (fast, no Unity needed):**
```bash
cd unity/Library/Bee/artifacts/*.dag
ls -la Assembly-CSharp.rsp Assembly-CSharp-Editor.rsp   # mtimes tell you which list is stale
grep -c '\.cs' Assembly-CSharp-Editor.rsp               # compare against: find Assets/Scripts/Editor -name '*.cs' | wc -l
grep -c '<NewClassName>' Assembly-CSharp-Editor.rsp     # 0 = Unity isn't compiling it
strings ../../../ScriptAssemblies/Assembly-CSharp-Editor.dll | grep -c '<NewClassName>'
```
A DLL **mtime alone is not evidence** the new file was included — check the `.rsp` and the DLL contents.

**Fix:** right-click the file in Unity's Project window ▸ **Reimport**, or restart Unity (rebuilds both lists). **Better fix: don't need the editor tool at all** — see [[no-editor-tools-by-default]]; the real resolution here was deleting the tool and reading what the feature needed from the live runtime scene instead.

Related: [[assume-compiled-never-blame-recompile]] (still true for RUNTIME scripts — this stale-list failure is editor-assembly-specific and must be proven from the `.rsp`, never assumed).
