# Caster facing bug — handoff (UNRESOLVED)

## The bug

When the player casts a skill, the caster should turn to face the cast point (except self-target). It still does **not** visibly face the target. User cast Sanctuary to the north; the character body visibly faced the wrong way (left / not-north).

## Current status: NOT FIXED

The 3D model's **body visibly points the wrong way**, even though every logged number says the object's rotation is correct. My changes did not fix what the user actually sees. Do not trust my "it works" claims — the user's screen is the ground truth and it shows the body mis-aimed.

## Files I changed (all still in the working tree, uncommitted)

### 1. `unity/Assets/Scripts/Player3DController.cs` — functional change, kept

- Added fields near `worldFacing` (\~line 198):
  - `float faceHoldUntil;`
  - `const float FaceHoldTime = 0.35f;`
- `FaceWorldPoint(Vector3 point)` now **returns** `bool` (true = it turned; false = self-cast on own feet), and sets `worldFacing` + `faceHoldUntil = Time.time + FaceHoldTime`.
- Guarded the two writers that were clobbering the cast heading:
  - normal-move (\~line 464): `if (moving && Time.time >= faceHoldUntil) worldFacing = move;`
  - `FaceTarget()` (auto-combat): early-return `if (Time.time < faceHoldUntil) return;`

### 2. `unity/Assets/Scripts/SkillRunner.cs` — functional change, kept

- Added `bool castFacedTarget;` and `const float MinFacePlant = 0.3f;`
- `StartCastOrPlay()`: `castFacedTarget = body != null && body.FaceWorldPoint(lastCtx.target);`
- `PlayCycles()`: directional casts now root the caster: `float lockDur = castFacedTarget ? Mathf.Max(releaseDur, MinFacePlant) : releaseDur;if (lockDur > 0f) StatusController.Player.LockActions(lockDur);`(self-casts exempt — no forced facing/root)

### 3. `unity/Assets/Scripts/Character3DPose.cs` — functional change, kept (UNTESTED)

- Added `public float yawOffset = 0f;`
- `Update()` LookRotation now: `Quaternion look = Quaternion.LookRotation(facing) * Quaternion.Euler(0f, yawOffset, 0f);`
- **This was never verified in Play** — the user stopped before testing a yawOffset value.

### 4. `unity/Assets/Scripts/Character3DVisual.cs` — NO net change

- Only had a temporary `Debug.Log`, since removed. Functionally identical to original.

### 5. `unity/Assets/Scripts/GroundTargeter.cs` — NO net change

- Only had a temporary `Debug.Log` at confirm, since removed. Functionally identical to original.

## What the runtime traces PROVED (facts, not theory)

- The player runtime visual is the **live-3D model** (`Character3DVisual` + `Character3DPose`, built by `Player3DVisualBuilder`, Mixamo Humanoid `BaseCharacter.fbx`), NOT the billboard.
- `Character3DVisual.LateUpdate` feeds facing via `PlayerStanceBrain.Decide` → `pose.SetFacing(player.WorldFacing)`.
- Logged every frame: `player.WorldFacing == d.facing == Character3DPose.transform.forward` — the object's `transform.forward` **does** end up pointing at the cast direction, and holds it (after the plant-lock fix). Example cast: `FaceWorldPoint wf=(-0.52,0,0.98)`, model `forward` settled at the normalized `(-0.47,0,0.88)` and stayed.
- Ground reticle lands where the cursor aims. Confirm trace: `cursorScreen=(301,516) point=(2.97,0,-1.78) player=(1.06,0,-1.82) dir=(1.00,0,0.02) camFwd=(1.00,0,0.06)` — dir ≈ camFwd (screen-up), so the point is correct for the aim.

## The unresolved contradiction (where the next session should start)

Numbers say `Character3DPose.transform.forward` = the cast direction. The user's screen says the **body faces elsewhere**. Therefore the visible mesh is **rotated relative to the transform whose rotation we set** — the `Character3DPose` object turns correctly, but the skinned mesh under it does not visually align with `transform.forward`.

### Leading hypothesis (unconfirmed)

Mesh yaw-offset: the Mixamo/Quaternius model's visible front is not local +Z. `yawOffset` in `Character3DPose` (change #3) is meant to correct this, but:

- It was **never tuned/tested**.
- If true, **walking would also be visibly mis-aimed** — this was never confirmed with the user. If walking looks correct but casting doesn't, the yaw-offset theory is WRONG and something else rotates the mesh only during/after a cast.

### Things NOT yet checked (do these first)

1. Ask/observe: when the player **walks** toward a clear world direction, does the BODY face travel correctly? If yes → it is NOT a static mesh offset; look for a cast-time rotation source instead.
2. Inspect the instantiated `Player3DVisual (...)` hierarchy in Play: is the `SkinnedMeshRenderer` on a **child with a non-zero local Y rotation**? Compare that child's world-forward to the `Character3DPose` object's `transform.forward`.
3. Check whether the **cast/release animation clip has root rotation** (Mixamo clips often bake root motion/rotation). `anim.applyRootMotion` is set false in `Player3DVisualBuilder`, but a state with write-defaults could still rotate. The controller is built `writeDefaultsOff: true` — verify the cast/release states actually have WD off.
4. Confirm which object the user is even looking at — rule out a second visible renderer.

## How to revert my changes if desired

`git diff` then `git checkout -- unity/Assets/Scripts/Player3DController.cs unity/Assets/Scripts/SkillRunner.cs unity/Assets/Scripts/Character3DPose.cs`(the other two files have no net change). The plant-lock (SkillRunner) and the `worldFacing` hold (Player3DController) are correct improvements regardless of the visual bug — keep them unless they cause other issues.