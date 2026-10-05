---
name: other-players-default-colours
description: "Other players drawn in their model's default colours — the creation dye never left the owner's machine; fixed 2026-10-05 by a server-written look SyncVar"
metadata:
  node_type: memory
  type: project
  originSessionId: 5b6ae276-cdfd-4393-9712-24ee9115b18a
  modified: 2026-10-05T08:22:28.127Z
---

**Symptom (2026-10-05, lastline):** every other player showed their model's default colours; your own character showed your chosen colours, but only on your own screen.

**Root cause:** `CharacterDye3D` only ever bound to `Character.Instance` (this machine's own character), and the body `NetPlayer.EnsureBody` builds for another person got model + worn gear only — no colours travelled, nothing recoloured it. The map server already HAD them: the character server hands over the whole progress document (`modelName`, `dyeSources`, `dyeTargets`) in `MapServerLink.Expected.progress`.

**Fix:** `NetPlayer.DressOnServer(progress)` writes `modelName` AND a `look` SyncVar (`CharacterDye3D.PackLook`: `model\nsrc>dst,…`) from the handed-over progress; `JoinRequest.modelName` was removed, so a client no longer states its own model. Watching machines call `CharacterDye3D.AddShowing(pose.gameObject, look)` — a show-only dye that never binds to the local character (a static flag set across `AddComponent` so `OnEnable` cannot bind first). Same component, one `ShowDye` path for both.

**Recolouring is done while drawing (2026-10-05, measured before choosing):** every character material uses `Unwritten/CharacterLit` (URP Lit copied, plus `ApplyDye` in `CharacterLitInput.hlsl`); `CharacterDye3D` hands `_DyeCount`/`_DyeSrc0..7`/`_DyeDst0..7` through a property block. A baked texture per look was 1 MB of graphics memory per differently dressed person nearby; the draw-time swap is 0.013–0.020 ms per million character pixels on an RTX 4060. Pixel test: undyed render identical to URP Lit (0 of 63,301 pixels differ), dyed within 1/255 of the old bake. Parts are saved in the editor (`CharacterDyePartsBaker.BakeAll()` → `Resources/CharacterDyeParts.asset`), removing an 18.7 ms first-appearance freeze per model; the asset-rules check fails a model with no entry. The materials are set by `CharacterMaterialFixer.OnPreprocessMaterialDescription` (`OnPostprocessMaterial` never runs for these description-imported models, so the anti-shine finish — _Smoothness 0, _Metallic 0 — had never reached 51 of 52 models; it now runs in the description step too, user-approved 2026-10-05, same shader and keywords so no draw cost). The shader has NO `CustomEditor`: URP's Lit inspector re-enables the motion-vector pass on any shader it does not know by name.

**Gotcha when testing in edit mode:** a non-ExecuteAlways component gets no `Awake` from `AddComponent` outside Play, so a live-editor test must invoke `Awake` by reflection. A first render in a preview scene can come out empty while shaders compile in the background — set `ShaderUtil.allowAsyncCompilation = false` for the test.

Related: [[mobs-get-everything-players-get]]
