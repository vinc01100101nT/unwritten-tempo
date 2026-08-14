---
name: compile-check-without-unity
description: "Run tools/check-unity-code.sh to compile-check all game C# offline in ~4s — no Unity window, no refresh_unity, catches errors in BOTH the game and the editor tools."
metadata: 
  node_type: memory
  type: reference
  originSessionId: 036aac99-29a9-45db-bff5-8157919fb418
---

**There is a committed script — just run it. Do NOT hand-build a csproj (the old way, below).**

```bash
tools/check-unity-code.sh            # both halves (default)
tools/check-unity-code.sh runtime    # game scripts only
tools/check-unity-code.sh editor     # editor tools only
```

~4s. Needs no Unity window and does NOT violate the `refresh_unity` ban — it never touches the running editor. Unity ships its own C# compiler in the app bundle and every reference library is already on disk, so it runs the SAME check Unity would and prints real errors with exact file + line. Exit **0** = clean, **1** = compile errors (printed), **2** = setup problem (it names what's missing; needs `unity/Library/ScriptAssemblies/`, present on any project Unity has opened once).

Verified to **CATCH** deliberate errors in both halves, not merely to report "clean" — a checker that can only say "fine" is worse than none.

**It checks BOTH assemblies, which matters:** the game (`Assets/Scripts`) and the editor tools (`Assets/Scripts/Editor`, the `Tools ▸ unwritten` windows). An editor-only mistake is otherwise invisible until someone opens that window.

**Two setup traps it already solves** (added 2026-08-13 after both bit me):
- Missing the .NET **compatibility stand-ins** (`NetStandard/compat/2.1.0/shims/netstandard/*` and `.../netfx/*`, which supply `mscorlib`) makes ordinary `List<>`/`Func<>`/`Enum` code report *"type is defined in an assembly that is not referenced"* — reads as your bug, isn't.
- `UnityEditor.dll` lives at `Managed/UnityEngine/UnityEditor.dll`, already covered by the engine-module glob.

There is **no known-error baseline any more** — a clean run means zero errors, full stop. (The old hand-rolled csproj had 11 environmental errors you had to remember to ignore; that's gone.)

A clean run means Unity will BUILD it. It does **not** mean the game behaves correctly — it never replaces play-testing.

Pair it with the other habit that keeps paying: **replay the math offline in Python with the real constants** before trusting a change — that's how the buff-encoding and attack-order changes were proven damage-identical before anyone pressed Play. See [[magnitude-always-bonus-fraction]] and [[attacker-owns-the-accuracy-roll]].
