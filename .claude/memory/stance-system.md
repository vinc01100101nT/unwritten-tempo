---
name: stance-system
description: Stance Composer + StanceController — data-authored held poses + single choke point that drives the live-3D pose sink; every pose it settles on is broadcast to watchers
metadata: 
  node_type: memory
  type: project
  originSessionId: 2498931d-2de8-46cf-9b5e-885b7bbe2f91
---

Character "stance" system — the pose sibling of [[vfx-cue-system]] (cue = fire-and-forget event→VFX; stance = HELD pose state).

**Authoring:** `Tools ▸ unwritten ▸ Art ▸ Stance Composer` (`StanceComposerWindow.cs`) → `Resources/StanceLibrary.asset`. Table like the Vfx Cue Composer: EVERY pose is a `StanceId` row (APPEND-ONLY) → baked `CharacterAction` + `StanceSpeedSource` + × mult + priority + `StancePlayMode` + fallPhase. Groups: **Held** (Combat/Guard/Casting/Sit), **Locomotion** (Idle/Walk/Run/Sprint), **Action one-shots** (Attack/Jump/Roll/Hurt/PickUp/Die/Fall). Resolved by `StanceCatalog` (array-by-int, `Reload()` on save), mirrors `VfxCueCatalog`.

**Stat-driven fps (the key design):** a stance NEVER stores a stat/window (SSOT). `StanceSpeedSource` names the OWNER to read each frame: **FitWindow**→clip `NaturalDuration`/runtime-window (window fed at drive time: Attack=`SwingTime`, Roll=dashLen), Baked→none. Applied = baked action fps × source × speedMult (`SetActionSpeed`). ASPD buff re-paces every FitWindow attack stance from one place. (AttackSpeed, CastSpeed and MoveGait were RETIRED as sources — ints 1, 2 and 3 kept dead. Walk/Run/Sprint take the leg pace by BEING walk cycles, never from a speed source: locked to ground speed — see [[gait-speed-sync]].)

**Runtime = single choke point** `StanceController.cs`. It drives the `IStancePose` sink (`Character3DPose`); nothing else plays a pose for a unit that has one. Two layers, highest first: (1) **action stance** (`DriveStance(id, window)`/`ClearAction`) — transient the brain drives one at a time, carrying the runtime FitWindow seconds; (2) **held stance** (`Push`/`Pop`/`Set`, priority-resolved). Fallback: `selfDrive=true` (NPC) → `ResumeSelfDrive` ONCE; `selfDrive=false` (player) → play the authored Idle stance. `StancePlayMode.FallTail` → `PlayFall` tail-loop (Fall reuses Jump clip).

**Held stances need a caller (they're inert alone).** Push them via `StanceController.Player` (static handle, set by `Character3DVisual.BecomePlayer()`; mirrors `Character.Instance`). Wired: **Casting** = `SkillRunner.CastWindup` Push / `EndCast` Pop (the one teardown choke point). Combat/Guard/Sit not wired yet (need `Player3DAttacker.EngagedWith` etc.). SkillRunner is on the PLAYER object; the StanceController is on the VISUAL object → the static handle bridges them.

**Player migrated:** `Character3DVisual` keeps its brain (grounding/death-ease/hooks/facing/loco-selection) but routes EVERY drive through `stance.DriveStance/ClearAction` — zero direct `baked.Play` left. Auto-adds `StanceController`, `selfDrive=false`. Retired its `fallPosePhase` field → Fall binding's `fallPhase` is the owner (DevSettings knob repointed to `StanceCatalog.Get(Fall).fallPhase`).

**EVERY POSE TRAVELS TO WATCHERS (2026-09-07), reported from this choke point and nowhere else.** `LateUpdate`
change-gates what it settled on and calls `GameNetwork.ReportDrivenStance` / `ReportHeldStance`; `PlayActionFor`
reports a one-shot beat via `ReportActionBegan`. Only `StanceController.Player` reports (a watched body would echo
what it was told). The server relays via SyncVars on `NetPlayer`, and `RemotePlayer3DVisual.DriveRoleAction`
applies the driven pose while `NetPlayer.ShowTheirHeldStance` pushes the held one.
**Two stances are deliberately NOT reported and adding them would double them:** locomotion (each machine picks the
gear from the speed it is told) and the weapon swings (the server announces those from the weapon IT holds). The
test is `StanceCatalog.WatchersMustBeTold`, backed by `WeaponStances.IsBasicAttack` — so a NEW stance is reported by
default, which is the safe way round. Sprint is the one gear that is told rather than measured, via
`NetPlayer.CmdSprinting`: a hasted run and a sprint are both simply fast and no threshold separates them.

⚠️ Gotchas:
- **FitWindow window stays RUNTIME, never authored.** The row only declares source=FitWindow; the seconds (SwingTime/dashLen) flow via `DriveStance(id, window)`. Authoring a constant would hand-copy cooldown+clipLen → SSOT break.
- **`StanceDefaults.For(id)` is the SINGLE owner of defaults** — shared by the composer (seed new row) AND `StanceCatalog` (synthesizes any id the asset hasn't saved yet), so combat animates even before opening+saving the library. Don't fork this map.
- **Execution order pinned (now explicit attrs):** the visual brain (`Character3DVisual`, `RemotePlayer3DVisual`) −100 → `StanceController` −50 → the pose sink 0. Same-frame, 0-lag. Don't reorder.
- **Fallback `selfDrive` must `ResumeSelfDrive` ONCE** (it re-bases movement sampling; per-frame → speed ~0 → stuck Idle). Guarded by `driving`.
- New `.cs` need `refresh_unity scope=all force`.
