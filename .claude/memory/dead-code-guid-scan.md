---
name: dead-code-guid-scan
description: How to PROVE a Unity script is dead (2-step scan) + the false-positive traps; used for the 2026-07-14 purge
metadata: 
  node_type: memory
  type: project
  originSessionId: 03a09163-330e-4068-a637-2b6b0e261990
---

To claim a runtime script is dead, BOTH checks must pass (a MonoBehaviour with zero code refs can still be wired in a scene):
1. **Code refs** — word-boundary grep of the type name across ALL `.cs` (runtime + Editor).
2. **Scene/prefab/asset refs** — take the guid from the script's `.meta`, grep every `.unity`/`.prefab`/`.asset` under `Assets/` for it.

**False-positive traps (each one bit during the 2026-07-14 sweep):**
- Filename ≠ class name: `Player3DController.Traversal.cs` is a **partial class**; `SkillMod.cs` holds 5 differently-named types; `HotbarDragBind.cs` holds 3. Scan by DECLARED type names, not filenames.
- A "superseded" claim needs path-checking: `MultiHitBurst` looked superseded by `SkillContext.HitCount`, but that lives in the single-target melee path only — the AoE path never had its features (snapshot targeting + RO combo-total popups). Verify which pipeline actually replaced what before calling it dead.
- Doc-comment `<see cref>` mentions don't keep code alive but DO confuse later greps — rewrite them when deleting the referenced class.

Also delete the `.meta` next to each deleted `.cs`. Purge list from that sweep: Character3DDriver, ClickMarker, DevGui, FriendlyUnit, SpriteFactory, PlayerInteractor, Interactable (+ WorldTools CreateNpc menu item + TOOLS.md row). See [[dormant-kept-systems]] for what was deliberately spared.
