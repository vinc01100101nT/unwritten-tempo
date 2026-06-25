# SESSION_STATE — resume here (keep SHORT; git holds history)

*branch* `experiment/3d-world` *· dials: **Opus 4.8 always** + assess EFFORT only*

## Protocols
- **Effort**: model ALWAYS Opus 4.8 (never assess/print model). Assess EFFORT only; auto-GO Low/Medium; PAUSE >Medium until user GO. Tag `**EFFORT: X**`.
- **Checkpoint**: OVERWRITE this file (never append); prune landed; keep short.

## 🎨 DESIGN (parallel, no code): "unwritten" brand sheet + StatPanel stat icons
GPT generates the logo/sheet; I give feedback + draft GPT prompts. **All decisions saved in Skill System doc §15** (`q17as16xh2gt1g058kemazjar588tvc8`): ring nodes = the 10 derived stats (logo == §5b ten-stat set); color by 3 archetype families (🔴Might Def/HP/Atk · 🔵Magic MAtk/MP/CastSpd · 🟢Finesse Accuracy/Evasion/Critical/AtkSpd; gold=neutral path); icons silhouette-first; fixing MATK vs Critical sparkle clash. ⏭️ User pastes GPT's next iteration → check crit icon + colors landed. Presentation only, no code/math impact.

## 🎯 ACTIVE (this session — COMMITTING NOW): player feel, free-fall, billboard shadows
Visible character = the **baked 8-dir billboard** (`BakedPlayerVisual` + `BakedBillboard8`), NOT the Sscary path (Sscary to be deleted; its dead Fall code can go with it).

- **Tunable defaults**: camera (`CameraFollow3D` + `Systems.unity` + `Build25DSpike`) zoomStep 1 / minHeight 0 / minDistance 1 / maxDistance 4 / distance 4 / lookAtHeight 0. Player moveSpeed 1.5, stepInterval 0.42, walkStepInterval 0.62. Baked `feetOffset` 0.17 (default + `BaseCharacter.prefab`; BlueSoldier prefabs still 0.45).
- **Free-fall**: `Player3DController.ApplyGravityAndMove` accumulates `vy` (real air time off ledges), resets on land; `fallAnimSpeed` (1.5) gates `falling`. Pose reuses **Jump** clip tail via `BakedBillboard8.PlayFall`; `BakedPlayerVisual.fallPosePhase` (0.5) picks the window.
- **Billboard shadows**: `SunRig.cs` (cached light dir) + `BillboardShadow.cs` = a SINGLE `ShadowsOnly` proxy that turns to **fully face the light in 3D** (`SunRig.ShadowFacing`) — stands up for a low sun (streak), lies flat for an overhead sun (contact shadow under feet). One morphing caster → works at every angle incl. top-down, never separates, and grounds the character. Distance-culled (32). Self-bootstrapped on mobs (`BillboardAnimator3D.Awake`) + baked player (`BakedBillboard8.Awake`). **Ground-blob decal approach was tried then REMOVED** (a static blob separates from the swinging cast shadow) — don't reintroduce it.
- **P-panel**: **Shadows** cat (Billboard shadows / cull dist 32) + Player "Fall pose @ speed" + Baked "Fall pose phase".
- **Mob/combat polish**: `UnitScale.MobRelative` default **0.30**. `MobNameplate` — `showName` (default OFF, name only in hover panel) + `hideAtFullHealth` (default ON: bar shows only once damaged, widget lazily built on first show); `BossBanner` opts both back in. Plain left-click a mob → `BeginAttack` (target-lock + `TargetIndicator` above it + auto-swing); click ground clears it. `GameCursor3D` shows the sword (`tool_sword_a.png`) when hovering a living mob.

⏭️ Verify in Play: walk off a ledge (fall pose), rotate sun side-on & top-down (shadow persists), tune via P. New `SunRig.cs`/`BillboardShadow.cs` need `.meta` from Unity's next import.

## ✅ Landed earlier: baked-character pipeline
Baked Quaternius chars ground/animate/play actions (Die>Attack>Jump>Dash>PickUp>Hurt-when-idle>loco). Re-bake (uncheck "Skip already baked") after any `SpriteBakeCore`/`BatchCharacterBaker` change. 8192 tex cap → 128 frames @64px max per strip.

## 💤 PENDING: Map Painter Phase 3 + Prontera
ONE map → JSON → Unity import ([[map-painter-pipeline]]). Phase 1+2 done; **Phase 3 importer NOT STARTED** — read `map.json` → bake (ground→GroundBlend, objects→`PropPlacement`+yaw, walls→`BuildPerimeterWall`). BLOCKED on a real painted `map.json` to lock schema.

## Gotchas
- **No local Play loop** — user presses Play/Bake/Generate to verify; I can't compile here (paste recompile errors).
- World ground standard: Y0 floor, Ground layer; custom shaders (WetGround/GroundBlend/FoliageWind) need Always-Included before a real build.
