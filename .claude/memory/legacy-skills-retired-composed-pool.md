---
name: legacy-skills-retired-composed-pool
description: 2026-07-02 legacy Player3D* skill classes RETIRED from the pipeline; skill pool is now data-driven composed SkillDefs only (+ new Grimoire J window)
metadata: 
  node_type: memory
  type: project
  originSessionId: 6a87aa0c-d538-472a-b039-f8f0856e8359
---

Migration 2026-07-02 (user: "clear all legacy skills + Grimoire window", full-migration option): the hand-written
`Player3D{Bash,Charge,Guard,Warcry,Caster,Fireball,FrostNova,Meteor,SoulStrike}` actives are RETIRED from the
pipeline. The active-skill pool is now purely DATA — composed `SkillDef` assets (Skill Composer output) under
`Resources/Skills`, each interpreted by a `SkillRunner`.

**What changed (files):**
- `SkillCatalog.OrderedIds()` — new; ids ordered by slotOrder then id (cached, cleared on Reload).
- `SkillRoster.AllIds` — was a hardcoded `string[]` of 9 legacy ids; now `=> SkillCatalog.OrderedIds()`. So the
  ring-unlock/ownership/hotbar-binding progression keys off composed skills automatically.
- `PlayerSkills.EnsureCache` — now maps a `SkillRunner` by its `def.id` (legacy IModifiableSkill path kept as fallback).
- `Player3DController.Awake` — legacy `AddComponent<Player3D*>` block replaced with `EnsureComposedSkills()`
  (attaches one `SkillRunner` per `SkillCatalog.All` def, then `PlayerSkills.Refresh()`).
- `Build25DSpike` (Build Player) — legacy skill AddComponent+SFX+icon blocks deleted; only `Player3DAttacker`
  (basic auto-attack) stays baked. `caster` field on the controller is null-guarded, so removal is safe.
- NEW `SkillBookPanel.cs` = the **Grimoire (J)** HUD window: lists every composed `SkillDef` (icon/name/MP/CD),
  drag or click to bind to the hotbar via the existing `SkillDragSource`/`HotbarBindings.BindToFirstEmpty`.
  Added to the canvas in `UIShellBuilder` — **must run `Tools ▸ unwritten ▸ …` Build UI Shell** to appear on
  already-built scenes.

**FULL DELETION (2026-07-02, user follow-up "remove them entirely, even in code"):** all 9 `Player3D*.cs`
skill files + `.meta` were DELETED (only `Player3DAttacker` = basic auto-attack + `Player3DController` remain).
Refs excised: `DevSettings` (caster field + EnsureRefs lookup + the 4 "Bolt" tuning knobs), `Player3DController`
(caster field/GetComponent + the whole cast-root `if (caster.Casting)` block), `SkillDefTools.ResolvePlayer`
(Player3DMeteor fallback), and all `<see cref="Player3D*"/>` doc comments (Character/CastBar/GuardShieldFX/
IHotbarSkill/VfxSequenceTools). `CastBar` + `GuardShieldFX` + `Character.BeginGuard` + `Player3DController.BeginDash`
KEPT as generic reusable primitives (now caller-less; a future composed cast/guard/dash skill can drive them).
0 compile errors. Only a historical mention left in `PERF_LOG.md` (a log, intentionally untouched).

**IMPORTANT gotchas:**
- **Save-format shift**: old hotbar/owned ids ("meteor","bash",…) are dropped on load — `SkillRoster.Restore` already
  validated via `IsKnownId`; `HotbarBindings.Restore` now ALSO filters through `SkillRoster.IsKnownId` (added 2026-07-02)
  so stale legacy bindings can't linger as dead slots. New meteor id is `skill.meteor`.
- **After the delete, the OLD baked player GameObject in Systems.unity has missing-script placeholders** where the
  legacy components were. Fix = re-run **Setup Systems Scene** (`Tools ▸ unwritten ▸ Scene Setup`) → it calls
  `Build25DSpike.BuildPlayer` and recreates the player fresh. Then **Build UI Shell** (same menu) for the J window,
  and SAVE Systems.unity. Both must be done in the Systems scene (that's where GameUICanvas + the player live).
- **Only ONE composed skill exists today**: `skill.meteor` (needs `Resources/VfxSequences/meteor.asset`). So the whole
  game skill pool = Meteor until more are authored in the Skill Composer. Build more via `Tools ▸ unwritten ▸ Skills`.
- Grimoire (J) binds ANY composed skill (bypasses ring ownership) — it's the library/authoring browser; the K menu
  stays the progression-gated bind path. Mild dual-path, intentional per the ask ("find ALL composer skills + bind").

Related: [[skill-system-mechanical-model]], [[mob-item-map-data-tables]], [[hotbar-ihotbarskill]], [[vfx-composer-window]].
