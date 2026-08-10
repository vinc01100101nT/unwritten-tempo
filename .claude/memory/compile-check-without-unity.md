---
name: compile-check-without-unity
description: "How to type-check all of unity/Assets/Scripts offline using Unity's bundled .NET 8 SDK — catches C# errors without touching the Unity editor."
metadata: 
  node_type: memory
  type: reference
  originSessionId: 036aac99-29a9-45db-bff5-8157919fb418
---

C# edits in this project can be **compile-checked offline**, without Unity and without violating the `refresh_unity` ban. Unity ships a full .NET 8 SDK:

`/Applications/Unity/Hub/Editor/6000.5.4f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet`

Build a throwaway `netstandard2.1` csproj in the scratchpad with `EnableDefaultCompileItems=false`, `<Compile Include=".../Assets/Scripts/**/*.cs" />` and these references:

- `.../Scripting/Managed/UnityEngine/*.dll` (the 173 engine modules — this glob also supplies the `UnityEditor.*Module` assemblies, so do **NOT** also reference `Managed/UnityEditor.dll` or every `MenuItem`/`EditorWindow` becomes CS0433 "exists in both")
- from the project's own `unity/Library/ScriptAssemblies/`: `UnityEngine.UI.dll`, `Unity.TextMeshPro.dll`, `Unity.RenderPipelines.Universal.Runtime.dll`, `Unity.RenderPipelines.Core.Runtime.dll`, `Unity.Addressables.dll`, `Unity.ResourceManager.dll`, `MCPForUnity.Runtime.dll`
- `NoWarn` the Unity-normal noise: CS0169;CS0414;CS0649;CS0108;CS0114;CS0162;CS0219;CS0618;CS0672

**Known baseline: 11 errors**, all environmental, none real — `Light2D` (URP-2D package not referenced), `PlayerModelSwap`/`PerfHud` (types outside `Assets/Scripts`), `WeatherSequenceLibrary.InvalidateCache`, and `GetInstanceID` obsolete-as-error (CS0619 under newer ref DLLs). Exclude `Editor/MeshDecimateWindow.cs` + `Editor/MobPrefabBaker.cs` (need UnityMeshSimplifier / UnityEditor.AddressableAssets). Anything ABOVE 11 is yours.

**Why it matters:** on 2026-08-10 this caught nine `tier` references across four files I hadn't opened during the tier→level removal, and would otherwise have shipped a broken tree the user had to find by hand. Worth the ~6 tool calls to set up on any multi-file refactor.

Pair it with the other habit that paid off the same session: **replay authoring math offline in Python** with the real constants before trusting it — that's how the stat-point budget guard's "untouched stats get crushed" bug was caught pre-ship. See [[skill-effects-caster-vs-target-passes]].
