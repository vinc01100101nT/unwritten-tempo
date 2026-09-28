---
name: wardrobe-dye-voronoi-match
description: Wardrobe per-part dye recolored wrong/multiple parts — fixed with nearest-part (Voronoi) shader match fed ALL parts
metadata: 
  node_type: memory
  type: project
  originSessionId: 9dfc1ee7-8011-4ad5-814a-b5feb9352794
---

**Symptom:** In the in-game Wardrobe (`O` key — the window was removed 2026-09-28; the same dye shader and part detection now serve only character creation's colour mixer), recoloring most cloth parts did nothing, while one swatch (a light color) recolored many parts at once.

**Root cause:** `AtlasRecolor.shader` matched each pixel to the **first** source part within a tolerance ball (`_Tol`=0.12 → radius ~0.21) and only *dyed* parts were fed to the shader. Cloth parts are close navy shades, so their tolerance balls overlapped: the first match ate later parts' pixels (they "did nothing"), and a single dyed part's wide ball swallowed neighbor regions that had no swatch claiming them (one swatch "changed many parts").

**Fix:**
- `AtlasRecolor.shader` frag — assign each pixel to its **nearest** listed source (Voronoi, argmin over `_Src`), recolor only if that nearest part is dyed (`_Dst != _Src`); else pass through. No first-match `break`.
- `CharacterDye3D.Apply()` — feed **every** detected part as `_Src` (via `DetectableParts()`), with `_Dst = TargetFor(part)`; undyed parts map to themselves so they claim (and pass through) their own pixels. Still skips all work when nothing is dyed (`sources.Count == 0`).

**Why it works:** every part owns a Voronoi cell, so no dyed part can bleed into a neighbor that has its own swatch — "one part at a time." `_Tol`=0.12 is now safe (only bounds how far unlisted skin/eye pixels get grabbed; those are far from cloth). Apply is off the hot path (dye-change/load only; `DetectableParts` caches). Relies on `sources ⊆ detected` (kept true by `RetainParts`) so `TargetFor` lookups hit.

**Phantom swatches (colors not worn):** detection sampled per-vertex UVs (seams bled neighbor swatches) and, when the mesh wasn't Read/Write, scanned the WHOLE per-character texture. Fix in `DetectableParts`: sample each **triangle centroid, area-weighted**, keep only swatches ≥2% of worn surface (`BuildParts` now uses `minFrac`, `Tally` weighted float). Requires mesh `isReadable` — `Tools ▸ unwritten ▸ Rigs ▸ Enable Read-Write on Character Models` (or flip `isReadable: 1` in each `Assets/Art/Quat/Rigged/*.fbx.meta`). Each Quaternius character has its OWN texture (`Rigged/textures/<Name>.png`) — atlas is NOT shared across different characters.

**Cross-model dye carryover (a color from another character):** the dye map is color-keyed (`sourceHex→targetHex`) with no model identity, so a `navy→green` dye authored on one character bled onto another navy character (e.g. BlueSoldier, whose texture has no green). Player model IS swapped at runtime in-editor via `PlayerModelSwap`→`Player3DVisualBuilder.Attach` (adds a fresh `CharacterDye3D` per swap), so carryover flows only through the persistent `Character` SSOT. Fix: stamp the dye with a **model signature** (`Character.DyeModel` + `SaveData.outfitDyeModel`). `CharacterDye3D.PullFromCharacter` shows the body's plain colours when the stamp ≠ its `ModelSignature()` — it NEVER erases the character's dye (fixed 2026-09-26: two bodies are often bound to one `Character` at once — the creation preview beside the Systems world body, or the old body still standing while `Player3DBody.Wear` loads the chosen one at sign-in — and the erase let the wrong body wipe the right one's colours, so every non-BaseCharacter creation saved `dyeSources: []`). SECOND cause of "always BaseCharacter in game", same day: `SaveManager.BeginLoad` runs at `PlayAs`, while `GameRoot.Quiet` has every Systems root (Player3D included) switched off until the server lets them in, so `Player3DBody.Wear` found no active `Player3DController` and did nothing silently. Fix: `ApplyAppearance` only sets `bodyToWear`; `SaveManager.Update` wears once `character.isActiveAndEnabled`. Do NOT instead delay the whole load: the progress GET calls `LetGoOfEverywhereAsync` and kicks the session that just joined (proven in the text client). `Wear` now logs `wearing '<model>'` / `could not put on …`. `SetDye` takes `(sources, targets, model)`.

⚠️ **Signature MUST key on the worn TEXTURE name, not the mesh name.** Quaternius bodies share one mesh across characters (BlueSoldier_Female & Soldier_Female are byte-identical geometry — confirmed via FBX strings — while Goblin differs) so a mesh-name key COLLIDES and the dye still bleeds. Each character has its own `Rigged/textures/<Name>.png`, so `ModelSignature()` joins the sorted distinct `material.GetTexture(TexIdFor)`/`mainTexture` names. An un-stamped legacy dye (null stamp) fails the match too, so it shows plain until the first recolour replaces the whole map with a stamped one.

**FINAL root cause of "dyed part does nothing / neighbor over-applies" (PROVEN, 2026-07-13):** the shader's
`pow(x,0.4545)/pow(x,2.2)` gamma approximation vs Unity's TRUE piecewise sRGB curve diverges worst on DARK
colors: a sampled `#1F1F1F` reconstructed to ~`#242424`, which is CLOSER to the neighboring ink `#272727`
(8/255 away) than to its own source → under Voronoi nearest-match, `#1F1F1F`'s pixels were all claimed by
`#272727` (green dye on `#1F1F1F` rendered nowhere; blue on `#272727` grabbed both). Verified numerically:
old error at #1F = 0.0207 > 0.0107 gap; true curve error = 0. Fix: exact piecewise sRGB in
`AtlasRecolor.shader` (`LinToGamma`/`GammaToLin` now `lerp(12.92c, 1.055c^(1/2.4)−.055, step(.0031308,c))`
and inverse). Quaternius atlases carry near-black "ink" (#1F1F1F ≈ 23% worn surface on BlueSoldier_Female) —
any two dark parts are byte-close, so EXACT reconstruction is mandatory; never reintroduce pow-approx.

**Debug method that cracked it (reuse!):** read the actual save
(`%USERPROFILE%\AppData\LocalLow\DefaultCompany\unwritten\unwritten_save.json` — dye hex pairs + stamp are
ground truth), then pure-Python decode the atlas PNG + parse the FBX binary (UV/tris/verts, zlib node arrays)
and re-run detection's exact math offline — reproduces the wardrobe swatch list byte-for-byte without Unity.
Script kept at scratchpad `inspect_bluesoldier.py` pattern.

Guards added same pass: `EnsureParts` only calls `RetainParts` when detection non-empty; `DetectableParts`
never caches an empty scan (both prevent wiping saved dye on a transient not-ready mesh).

Related: [[persistent-systems-layer]] (dye is a computed view of `Character` SSOT).
