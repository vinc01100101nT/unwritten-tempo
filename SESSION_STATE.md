# SESSION_STATE — resume here (keep SHORT; git holds history)

*branch* `experiment/3d-world` *· dials: **Opus 4.8 always** + assess EFFORT only*

## Protocols
- **Effort**: model ALWAYS Opus 4.8 (never assess/print model). Assess EFFORT only; auto-GO Low/Medium; PAUSE >Medium until user GO. Tag `**EFFORT: X**`.
- **Checkpoint**: OVERWRITE this file (never append); prune landed; keep short.

## 🎯 ACTIVE: Baked-character animation → game wiring (this session, UNCOMMITTED)
Goal: the baked 8-dir Quaternius chars (via **Use Baked Character** / `BakedPlayerVisual`) ground correctly, animate smoothly, and play real game actions. Pipeline context in memory **[[bake-8dir-sprites-pipeline]]**.

**DONE (code, needs user re-bake to see visuals):**
- **Grounding**: `BakedPlayerVisual.GroundedPos` drops visual to capsule BOTTOM (`cc.center.y - height/2`; player transform sits ~0.25 above floor). `SpriteBakeCore.GroundingFeetOffset()` auto-measures feet row from baked `_S` strip → `feetOffset = 0.5 - feetFromBottom`, scale-independent. `BakedBillboard8.SetFeetOffset()` live setter.
- **Frame cap (fixes INVISIBLE chars)**: strips wider than the 8192 import cap got downscaled (height too) → fixed-cell slicing OOB → null sprites. `SpriteBakeCore.MaxTexSize=8192` + `FrameCap` (=128 @64px) clamps `nFrames`.
- **Jump head-cut FIX**: real cause = sizing ignored the 30° camera PITCH. On-screen height = `y·cos + depth·sin` > raw `b.size.y`, so apex pose overflowed at padding 1. `SpriteBakeCore` now frames in SCREEN space (projection-aware, worst-case across 8 yaws via `radius=max(x,z)`) + centres on bounds (dropped the headroom-eating bottom margin; grounding still measured). Also union-bounds across poses. **Needs re-bake.**
- **Anim FPS**: editable Idle/Walk/Run/Other in `BatchCharacterBaker` — **default 24** (= source rate, FBX FrameRate/TimeMode=11). FPS = baked frame count AND playback rate.
- **Action wiring** (`BakedPlayerVisual` priority override, facing-only so movement can't cancel): Die > Attack(`attacker.Swinging`) > Jump(`IsAirborne`) > Dash(`Dashing`) > PickUp(timed) > **Hurt ONLY when idle** (`speed<walkThreshold`, lowest) > Idle/Walk/Run.
- **Attack = full clip per swing, damage at END, sped to attack-speed**: `Player3DAttacker.SwingTime = cooldown*MotionScale`; `SwingThenHit` waits SwingTime then damages (Swinging stays true throughout); DPS unchanged. `BakedBillboard8.NaturalDuration()` + `BakedPlayerVisual` sets Attack speed = `natural/SwingTime` so the whole clip plays once in the window.
- **Movement LOCKED during swing**: `Player3DController` early-returns (gravity only) while `attacker.Swinging` — no WASD/click/order/jump/dash. (Between swings a 1-frame window lets held WASD break out after the swing.)
- **Click-to-pickup (auto-collect REMOVED)**: `WorldPickup.OnTriggerEnter` gone, `Collect()` public + `OnCollected` static event. Controller: click a pickup → `BeginPickup` walks to it (`pendingPickup`), `TryCollectPending` collects within `collectRadius`, then `pickupLockUntil` locks WASD/clicks 0.6s (gravity only) while PickUp gesture plays. `PickupUnderCursor` raycast in `ReadInput`.
- **Reflection gap fix**: was the MIRROR PLANE. `PlanarReflection.followGround` (default on) raycasts Ground under player → `waterHeight`; `groundOffset` nudge.
- **DevSettings knobs**: Baked Character→Feet offset; Reflection→Auto water level + offset + narrowed manual; World→Ground height (Y).

Files: `Editor/SpriteBakeCore.cs`, `Editor/BatchCharacterBaker.cs`, `BakedBillboard8.cs`, `BakedPlayerVisual.cs`, `Player3DController.cs`, `Player3DAttacker.cs`, `WorldPickup.cs`, `PlanarReflection.cs`, `DevSettings.cs`.

## ⏭️ NEXT
1. **User must RE-BAKE** (Bake All Characters, **uncheck "Skip already baked"**) — current on-disk bake is the broken 45fps one (invisible) AND predates the jump-framing fix. Then Use Baked Character → Play to verify: grounding, jump apex (head intact), full attack anim + damage-at-end + movement lock, click-to-pickup, idle-only hurt.
2. Untriggered actions baked + idle (use later): Cast, Victory, Defeat, StandUp, Sit, Punch, Shoot, Roll, WalkCarry, RunCarry.
3. Future: promote `BakedPlayerVisual` logic into `Player3DController` for a PERMANENT avatar (currently a QA-bridge swap). Grid sprite-sheet layout would lift the 128-frame cap if long high-fps clips ever needed.

## 💤 PENDING (uncommitted, prior session): Map Painter Phase 3 + Prontera
Hand-author ONE map → JSON → Unity import. Design in memory **[[map-painter-pipeline]]**. Phase 1 (BrushPalette SO + `Brush Palette…` window, manifest exported) + Phase 2 (`tools/map-painter/index.html` painter) DONE. **Phase 3 importer NOT STARTED** — read `map.json` → bake (ground→GroundBlend, objects→exact prefab via `PropPlacement` + yaw, walls via `BuildPerimeterWall`). BLOCKED on user sending a real painted `map.json` to lock schema. Prontera: `unity/Assets/Prontera.asset` bakes via re-added `MapRecipeEditor.cs` (Grid + PerimeterWall + Cardinal path rules in recipe engine).

## Gotchas
- **No local Play loop** — user presses Play / Bakes / clicks Generate to verify. I can't compile here; on recompile error, user pastes it.
- Re-bake required after ANY baker change (FPS, cap, grounding) — frame count + feetOffset are baked at author time. Uncheck "Skip already baked".
- 8192 texture cap → single-row strips limited to 128 frames @64px (down/shrink → invisible if exceeded). Grid layout = future lift.
- Char "elevation vs reflection" residual: user said LEAVE IT (deprioritized). Reflection auto-follows ground now.
- World ground standard: Y0 floor, Ground layer; seat props bounds-bottom (PropPlacement). Custom shaders (WetGround/GroundBlend/FoliageWind) need Always-Included before a real build.
