---
name: afterimage-player-model-and-bakemesh-scale
description: "Afterimage ghosts invisible (twice) then 10x too big — player's rendered model isn't under the player root, BakeMesh already carries the renderer's scale, and a capture made on the screenless server must be sent to every screen."
metadata:
  node_type: memory
  type: project
  originSessionId: d92be9ac-67f4-43c3-bdc8-db6e333ce473
  modified: 2026-10-06T19:23:09.939Z
---

Blade Dance's afterimage (`Afterimage.cs`, new 2026-08-16; the skill is now named Thousand Petals) hit THREE bugs. All are general traps, not skill-specific.

**Bug 1 — nothing rendered at all.** `SkillEffectApplier` passed `unit.Body`, which for the player is `Player3D` (the mover). Measured in `~/Library/Logs/Unity/Editor.log`: 56x `NO SKINNED RENDERERS under "Player3D"`. The player's rendered model is a **SEPARATE object that is NOT parented under the player root** — `Player3DVisual (BaseCharacter)`, built by `Player3DVisualBuilder.Attach` (it never parents it). Mobs/NPCs are fine: `Agent3DVisual.cs:36` keeps the model as a child of the body.
**Reach the player's model with `StanceController.Player.transform`** — a static field registered in `Character3DVisual.Awake` (`Character3DVisual.cs:47` → `StanceController.cs:110`), on the model object itself. Zero scene search. Do NOT add another `FindObjectsByType<Character3DVisual>()`; three sites already hand-roll that (`ChatController.cs:168`, `CharacterWardrobePanel.cs:259`, `FootstepTracker.cs:200`) — all cache after first success, so they're not a per-frame cost, but a 4th would be scattering.

**Bug 2 — ghosts ~10x too big.** `SkinnedMeshRenderer.BakeMesh(mesh)` returns the body **already at world size**; re-applying `src.transform.lossyScale` doubles it. Measured: renderer `lossyScale 10.35`, baked mesh bounds `0.32` across, source renderer world bounds `0.35` — i.e. 1:1. (If the bake had omitted scale it would have read ~0.03.) Fix: `t.localScale = Vector3.one` (`Afterimage.cs`).

**Bug 3 (2026-10-06) — invisible on EVERY screen, after the server took over running skills.** `SkillEffectApplier` runs only on the game server, which has no screen, and nothing told any player's machine. A first fix had the server send an "afterimage here" message; the user rejected it ("no reason for the server to command the client to render those effects"). Final shape: the applier draws NOTHING and passes a `RelocationLook` (skill id + binding + effect index + the cast's pile readings) through `TeleportTo`/`GlideTo`; each screen reads `afterimageSeconds`/`afterimageTint`/`casterOpacity` from its own `SkillCatalog` copy — the owner in `Player3DController.ShowLook`, watchers in `WatchedMove.Tell`. The same pass fixed moves looking slow to watchers: a hop lasts 0.03–0.06 s and the 10-a-second position report + catch-up ease reached at most 65% of its speed (offline replay), so every scripted move (glide, blink, creature knockback) is said once as a `MoveSaid` SyncVar on `NetPlayer`/`NetMob` and drawn by `WatchedMove` with `GlideEase`; it then waits at the end until a report lands nearer the end than the start (else the old report drags it back). `CharacterFade` now dresses any number of bodies.

Third time this project shipped a double-applied character scale — see the "COPY EXACTLY, never re-derive" rule in global CLAUDE.md. When cloning a working thing, copy its resolved values; never re-fit.

**Method that cracked it:** the fix guess-space had 4 candidates, so a temporary `[Afterimage]` probe logged baked bounds / lossyScale / position / source bounds per capture. Read straight out of `~/Library/Logs/Unity/Editor.log` — no Unity bridge, no asking the user to paste. That log is a first-class ground-truth source for this project.

Related: [[player-two-gameobjects-one-store]], [[mobs-get-everything-players-get]].
