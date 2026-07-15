---
name: stance-system
description: Stance Composer + StanceController — data-authored held poses + single choke point that drives BakedBillboard8; player migrated onto it
metadata: 
  node_type: memory
  type: project
  originSessionId: 2498931d-2de8-46cf-9b5e-885b7bbe2f91
---

Character "stance" system — the pose sibling of [[vfx-cue-system]] (cue = fire-and-forget event→VFX; stance = HELD pose state).

**Authoring:** `Tools ▸ unwritten ▸ Art ▸ Stance Composer` (`StanceComposerWindow.cs`) → `Resources/StanceLibrary.asset`. Table like the Vfx Cue Composer: EVERY pose is a `StanceId` row (APPEND-ONLY) → baked `CharacterAction` + `StanceSpeedSource` + × mult + priority + `StancePlayMode` + fallPhase. Groups: **Held** (Combat/Guard/Casting/Sit), **Locomotion** (Idle/Walk/Run), **Action one-shots** (Attack/Jump/Roll/Hurt/PickUp/Die/Fall). Resolved by `StanceCatalog` (array-by-int, `Reload()` on save), mirrors `VfxCueCatalog`.

**Stat-driven fps (the key design):** a stance NEVER stores a stat/window (SSOT). `StanceSpeedSource` names the OWNER to read each frame: AttackSpeed→`Character.AttackSpeedMult`, CastSpeed→`CastSpeedMult`, MoveGait→`Player3DController.GaitSpeedMult`, **FitWindow**→clip `NaturalDuration`/runtime-window (window fed at drive time: Attack=`SwingTime`, Roll=dashLen), Baked→none. Applied = baked action fps × source × speedMult (`SetActionSpeed`). ASPD buff re-paces every AttackSpeed/FitWindow-attack stance from one place.

**Runtime = single choke point** `StanceController.cs` (attach next to `BakedBillboard8`). Nothing else calls `baked.Play` for a unit that has one. Two layers, highest first: (1) **action stance** (`DriveStance(id, window)`/`ClearAction`) — transient the brain drives one at a time, carrying the runtime FitWindow seconds; (2) **held stance** (`Push`/`Pop`/`Set`, priority-resolved). Fallback: `selfDrive=true` (NPC) → `ResumeSelfDrive` ONCE; `selfDrive=false` (player) → play the authored Idle stance. `StancePlayMode.FallTail` → `PlayFall` tail-loop (Fall reuses Jump clip).

**Held stances need a caller (they're inert alone).** Push them via `StanceController.Player` (static handle, set by `BakedPlayerVisual.BecomePlayer()`; mirrors `Character.Instance`). Wired: **Casting** = `SkillRunner.CastWindup` Push / `EndCast` Pop (the one teardown choke point). Combat/Guard/Sit not wired yet (need `Player3DAttacker.EngagedWith` etc.). SkillRunner is on the PLAYER object; the StanceController is on the VISUAL object → the static handle bridges them.

**Player migrated:** `BakedPlayerVisual` keeps its brain (grounding/death-ease/hooks/facing/loco-selection) but routes EVERY drive through `stance.DriveStance/ClearAction` — zero direct `baked.Play` left. Auto-adds `StanceController`, `selfDrive=false`, `SetGaitSource(player)`. Retired its `fallPosePhase` field → Fall binding's `fallPhase` is the owner (DevSettings knob repointed to `StanceCatalog.Get(Fall).fallPhase`).

⚠️ Gotchas:
- **FitWindow window stays RUNTIME, never authored.** The row only declares source=FitWindow; the seconds (SwingTime/dashLen) flow via `DriveStance(id, window)`. Authoring a constant would hand-copy cooldown+clipLen → SSOT break.
- **`StanceDefaults.For(id)` is the SINGLE owner of defaults** — shared by the composer (seed new row) AND `StanceCatalog` (synthesizes any id the asset hasn't saved yet), so combat animates even before opening+saving the library. Don't fork this map.
- **Execution order pinned (now explicit attrs):** `BakedPlayerVisual` −100 → `StanceController` −50 → `BakedBillboard8` 0. Same-frame, 0-lag. Don't reorder.
- **Fallback `selfDrive` must `ResumeSelfDrive` ONCE** (it re-bases movement sampling; per-frame → speed ~0 → stuck Idle). Guarded by `driving`.
- New `.cs` need `refresh_unity scope=all force`.
