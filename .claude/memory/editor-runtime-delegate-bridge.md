---
name: editor-runtime-delegate-bridge
description: "Runtime code can't call an Editor-folder class; bridge it with a delegate the editor fills on load."
metadata: 
  node_type: memory
  type: project
  originSessionId: f3418117-4ed2-480c-be8f-88f921c62116
---

Unity default assemblies reference **one way only: `Assembly-CSharp-Editor` → `Assembly-CSharp`** (editor sees runtime, never the reverse). So a **runtime** script can NEVER name a type defined in an `Editor/` folder — not even inside `#if UNITY_EDITOR` (that guard enables the `UnityEditor` *namespace* like `AssetDatabase`, but does NOT grant access to the Editor *assembly*). Direct call = compile error.

**Bridge with a delegate (editor reaches DOWN to runtime):**
1. Runtime side declares an empty hook: `public static System.Func<...> Attacher;`
2. Editor side fills it on load: `[InitializeOnLoadMethod] static void Hook() { PlayerModelSwap.Attacher = Attach; }`
3. Runtime calls `Attacher(...)` — never names the editor type. One implementation, no duplication (SSOT).

**Live example:** `PlayerModelSwap` (runtime, `Scripts/PlayerModelSwap.cs`) is the in-game editor-Play character swapper; it invokes `Player3DVisualBuilder.Attach` (Editor asm) via `Attacher`, wired by `Player3DVisualBuilder.HookRuntimeModelSwap`.

**Why:** any "gameplay needs to trigger an editor-only builder/importer" hits this wall; the instinct to just `#if UNITY_EDITOR` the call fails with a confusing compile error. Reach for this delegate flip first. See also [[act-editor-actions-via-mcp]], [[humanoid-import-type-tpose]].
