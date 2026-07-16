---
name: shadergraph-guids-are-bare-hex
description: "Unity .shadergraph/.shadersubgraph are JSON and reference assets as BARE 32-hex GUIDs, not the YAML `guid:` form — asset-dependency parsers must match both or they miss subgraph/texture deps."
metadata: 
  node_type: memory
  type: reference
  originSessionId: 789f3f05-590b-41f9-8e8f-6f850bd1d7c6
---

When parsing Unity asset files OFFLINE to compute a dependency closure (e.g. a
promote/move-out-of-0_Raw migration script), most assets (`.prefab`, `.mat`,
`.asset`, scenes) are YAML and reference other assets as `guid: <32hex>`. But
`.shadergraph` and `.shadersubgraph` files are **JSON**, and they reference
sub-graphs and default textures as **bare 32-hex tokens** (no `guid: ` prefix).

A parser keyed only on the regex `guid: ([0-9a-f]{32})` will SILENTLY MISS those
shader-graph dependencies. Symptom hit 2026-07-16 promoting VFX out of `0_Raw`:
the moved NamuFX water shaders (`SH_Water_Base`, `SH_Particles`, `SH_Trail`,
`SH_Master_improved`) depend on `MasterShader/Dissolve Function.shadersubgraph`
and `FlipUV.shadersubgraph`, but those were left behind — deleting the leftover
pack would have broken the water shaders. Caught only by re-checking with a
bare-hex matcher before deleting (ground-truth, not the first "SAFE" scan).

Fix pattern: for `.shadergraph`/`.shadersubgraph`, ALSO extract
`(?<![0-9a-f])([0-9a-f]{32})(?![0-9a-f])` and resolve each against the
guid→path index; bare 32-hex node object-ids that aren't real asset guids simply
won't be in the index, so false positives are harmless. Always re-run the
delete-safety scan with this broadened matcher, and verify the MOVED files (not
just kept files) don't reference anything about to be deleted. See
[[changes-must-persist-in-repo]].
