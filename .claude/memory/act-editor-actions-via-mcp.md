# Run editor actions YOURSELF via MCP — never make the user click Tools ▸ unwritten

**Hard user directive (2026-07-06, stated angrily and explicitly):** the live connection to Unity is OPEN and I am NOT the user's hands. Do NOT ask them to run a `Tools ▸ unwritten ▸ …` menu item, add a component, find a GameObject, rebuild a library, or tweak an asset. DO IT myself.

**Tool names moved 2026-08-20** — the connection is the Unity CLI now, not the retired CoplayDev bridge (see
[[unity-mcp-bridge]]). `execute_code` → **`eval`**, `execute_menu_item` → **`menu`**, `read_console` →
**`get_console_logs`**, `refresh_unity` → **`recompile`**. The directive is unchanged; only the names did.

## How
- Prefer `eval` calling the **dialog-free `*Core()`** method (e.g. `EffectLibraryBuilder.RebuildCore()`), NOT the `[MenuItem]` wrapper — the wrapper often shows `EditorUtility.DisplayDialog`, a MODAL that can HANG the editor. Fall back to `menu` only when there's no dialog.
- `eval` compiles against the **already-compiled assemblies**, so asset/library edits run even when my just-edited `.cs` hasn't recompiled yet. Use that to sidestep compile-order: register/patch assets directly instead of waiting on a code edit to compile.
  - Worked example: registered NamuFX splash prefabs by loading `Assets/Resources/EffectLibrary.asset`, adding `EffectDef{id,prefab}` entries, `SetDirty`+`SaveAssets`+`EffectCatalog.Reload()` — bypassed the uncompiled `Roots[]` edit entirely.

## Still true (does NOT loosen)
- NEVER force a `recompile`, never poll `get_console_logs`/`recompile_status` in a loop. Runtime-C# compilation stays the user's focus-triggered job. (Its speed on the CLI is UNMEASURED — the ban was written for the old bridge's ~60s stall and stands until someone times it.)
- Only fall back to asking the user when the action truly needs **Play mode** or a **human visual judgement** (e.g. "does this splash look good").

Rule also written into project `CLAUDE.md` standing rules (next to the "NEVER force-refresh" bullet).

## Repeat-offense note (2026-07-06 — re-violated SAME DAY this was written)
Told the user to "Add Component in the Inspector" for a `WaterStep` scale knob, AND piled `WaterStep` onto
`Sun` — breaking BOTH this memory and `feature-knobs-own-object.md` in one task. Root cause: acted on a Unity
placement / "where's the knob" task WITHOUT grepping memory first. **Trigger to internalize:** any Unity task
about (a) *where a component lives* or (b) *making the user click/add/find something* → GREP MEMORY FIRST,
then do it myself. Fix applied live: running C# in the editor created a `WaterFX` object, moved
WetGround+PlanarReflection there via `ComponentUtility.CopyComponent`/`PasteComponentAsNew` (preserves tuned
values), added `WaterStep`, saved `Systems.unity`.
