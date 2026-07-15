# Planar reflection clip-plane sign fix (billboards/props cut at the waterline)

Fixed 2026-06-27. Symptom: in the wet-ground **planar reflection**, raising the DevSettings
**Reflection ▸ Water level offset** above ~**-0.03** clipped the BOTTOM of every reflected object
(trees, houses, AND the billboard character). At -0.03 nothing was cut; anything higher ate the
bases. (First mis-reported as "only the character is half" — it was actually everything.)

## Root cause
`PlanarReflection.cs` (`CameraSpacePlane`) built the reflection camera's **oblique near-clip plane**
with the WRONG SIGN: `offsetPos = pos + normal * clipPlaneOffset` (normal = up) pushed the clip plane
**0.03 ABOVE** the water, even though the field's tooltip says "Pushes the clip plane slightly below
the water so the shoreline doesn't shimmer." So the clip plane sat at `waterHeight + clipPlaneOffset`
and ate `clipPlaneOffset` (0.03) off every reflected base. The user's -0.03 offset was only canceling it.

## Fix
One-char sign flip: `offsetPos = pos - normal * clipPlaneOffset`. Now the clip sits BELOW the water
(matches the tooltip; the small below-water push is the intended anti-shimmer slack). At
**Water level offset = 0** nothing is cut and there's ~0.03 of headroom before any cutting starts.

## Key live values (read via Unity MCP `execute_code`, NOT play-driven — game was already in Play)
- `PlanarReflection`: clip plane = `waterHeight + clipPlaneOffset`; `followGround` ON so
  `waterHeight = groundUnderPlayer + groundOffset`; `clipPlaneOffset` default 0.03.
- DevSettings "Water level offset" = `refl.groundOffset`; "Water level (manual)" = `refl.waterHeight`
  (only used when Auto water level off); "Auto water level" = `refl.followGround`.
- Ground under player ≈ Y0; trees/houses/character all base at ≈ Y0, so the clip plane parks right at
  their feet — why a tiny upward push cut everything.
- Player billboard: sprite is a **center-pivot** quad on a child lifted by `feetOffset` (BakedBillboard8);
  visible art ran ~Y0→Y0.62 with the quad bottom (transparent padding) below Y0. (Not the cause here,
  but the reason a center-pivot billboard CAN get sliced if the clip ever rises into its body.)

## Gotchas / workflow learned
- **Script edits do NOT recompile while in Play mode** — Unity defers until you stop Play.
- Added a one-click **Tools ▸ unwritten ▸ Debug ▸ Recompile Scripts** (`Ctrl/Cmd+Shift+R`,
  `Editor/RecompileTool.cs`) = `AssetDatabase.Refresh()` + `CompilationPipeline.RequestScriptCompilation()`.
- MCP recompile from chat: `refresh_unity(compile=request, mode=force, scope=scripts)`; check
  `EditorApplication.isPlaying/isCompiling` via `execute_code`; `mcpforunity://editor_state` resource URI
  did NOT resolve — query state through `execute_code` instead.
- Reflection only draws inside the puddle mask (`wet > 0.001`) in `WetGround.shader`; it samples the
  mirror RT at the SAME screen UV (no waterline flip; `_ReflFlipY` default 0).
