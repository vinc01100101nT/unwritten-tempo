# Memory index

Two-level: top-level facts below are always relevant; grouped topics live in `index/<group>.md` sub-indexes — open the matching sub-index when a task touches that area, then the memory file it points to.

## Top-level (always-on)
- [Never widen a snapshot into a verdict](never-widen-a-snapshot-into-a-verdict.md) — today's files prove today only; if the user tested it working, believe them and hunt what CHANGED — never say "it never worked / can't be done".
- [Assets/ reorganized by usage 2026-07-16](assets-reorg-2026-07.md) — numbered buckets 0_Raw/1_Authored/2_Generated/3_Data; OLD paths in older memories are stale — use its mapping.
- [Changes MUST persist in repo, not runtime](changes-must-persist-in-repo.md) — bake in code/saved asset + read back; never leave a change in bare execute_code/runtime state.
- [DIRECTION: RO-style 2.5D](direction-2p5d-pivot.md) — PLAYER+NPC live-3D, MOBS billboard; player LIVE-3D 2026-07-04.
- [Baked-character billboards DELETED](baked-character-billboards-removed.md) — only MOBS billboard now; BakedBillboard8/Facing8/CharacterDef/Roster/bakers gone 2026-07-21.
- [Weapons are 3D, no 2D weapons](weapons-are-3d-no-2d.md) — a weapon Item is real only with a 3D equipModel; kept: Iron Sword + Bow Epic; Katana/Sai/Dawnbreaker retired.
- [NO classes — weapons+skills define gameplay](no-classes-weapons-define-gameplay.md) — class system doc is stale/deleted; identity = equipped weapon's skill kit + ring build; rosters are per-weapon.
- [Ring stats: always LOOP order](ring-stats-canonical-order.md) — list the 10 ring stats as Def→HP→Atk→Accuracy→CastSpeed→MAtk→MP→Evasion→Critical→AttackSpeed (from SkillRing.Ring), never enum order.
- [Unity game lives in unity/ of THIS repo](unity-2d-direction.md) — game C# at `unity/Assets/Scripts/`.
- [unwritten: foundation-first + Ninja art](unwritten-foundation-first.md) — world before skill-draft; CC0 pack, PPU 16.
- [Project layout & tech facts](project-layout.md) — Unity 6 + URP, PPU 16; Field/Town scenes; MonsterSpawner.
- [Persistent global systems layer](persistent-systems-layer.md) — HUD/cursor/player/camera in never-unloaded `Systems.unity`.
- [Performance is a hard constraint — pre-bake](performance-first-prebake.md) — merge static colliders; work at author time.
- [Runtime hot-path perf patterns](perf-runtime-hotpath-patterns.md) — canonical=`PERFORMANCE.md`; reuse CameraRig, pooled DamagePopup.
- [Apply review fixes, don't gatekeep](apply-review-fixes-dont-gatekeep.md) — fix ALL listed findings or label skips explicitly; silent "not worth it" triage reads as refusal.
- [Don't agree reflexively](dont-agree-reflexively.md) — critique on merits even when user says "I like this"; reflexive agreement kills the value of a second opinion.
- [Name knobs concretely, one job, no dead knobs](name-knobs-concretely-one-job.md) — HARD: label every field/effect by the exact thing it changes (never "potency"/"magnitude"); one responsibility each; hide fields that mean nothing in a case.
- [Peer-to-peer GPT replies](peer-to-peer-gpt-replies.md) — when user pastes a GPT design reply, respond in peer voice addressed to GPT, paste-ready, grounded in real code.
- [Skill tooltip voice](skill-tooltip-voice.md) — wiki/doc skill descriptions read as in-game tooltips (table, declarative rules, bold numbers), NOT article prose or designer meta-commentary.
- [Never blame recompilation](assume-compiled-never-blame-recompile.md) — user ALWAYS recompiles; treat code as live+running, diagnose runtime, never suspect stale compile.
- [Find-or-create hides duplicates; fix the CLASS not the instance](find-or-create-hides-duplicates.md) — `GetComponent ?? AddComponent` can't fail so it silently forks a "single source"; one broken member of a uniform system = check ALL members before fixing.
- [A matching function name is NOT the behaviour](existing-function-name-is-not-the-behaviour.md) — user asks for a FEEL; finding `SmoothDamp` ≠ the result exists. Trace position AND rotation to the screen before "already implemented"; when the feel is wrong, suspect the mechanism, not the number.
- [NO editor-tool menus by default](no-editor-tools-by-default.md) — user rejects `Tools ▸ unwritten` tools; ask what the feature needs AT RUNTIME (it's often already live), prefer a GM chat command. OVERRIDES the project "prefer editor tools" rule.

## Grouped sub-indexes (open on demand)
- 🗂 [Character / rig / stance / animation](index/character-rig-stance.md) — Mixamo/Quaternius rigging, stance system, IStancePose, gait, caster grounding (11).
- 🗂 [VFX](index/vfx.md) — `Vfx.PlayAt`/`VfxCue` chokes, composer/tuner, spawn/scale/loop gotchas (7).
- 🗂 [Placement / ground / pivot / collision / pathfinding](index/world-placement.md) — pivot=anchor, feet-pivot, skinWidth floats, A*/crowd, y-sort depth (9).
- 🗂 [Rendering / lighting / reflection / weather](index/rendering-weather.md) — additional-lights loops, planar reflection, nightlight, rain/snow (6).
- 🗂 [Asset / art pipeline](index/asset-pipeline.md) — Hunyuan/Trellis 3D props, style guides, 8-dir sprites, Pxltgr/Kenney (7).
- 🗂 [World authoring / generation / data tables](index/world-authoring.md) — MapRecipe generator, prop palette, world-creator/map-painter, data SOs, portals, ambient life (10).
- 🗂 [Combat / skills / progression / stats](index/combat-skills-progression.md) — cast time, multi-hit/status, trio-ring model, stats, Ring of Progression, charter (15).
- 🗂 [HUD / UI](index/hud-ui.md) — build process, hotbar, progression windows, click-shield, dev-settings (5).
- 🗂 [Workflow / tooling / meta](index/workflow-meta.md) — Unity MCP bridge, chat/GM, Tempo docs/canvas gotchas, extractors, logs, persist protocol, dead-code scan, stale editor-assembly list (11).
- [Mobs get EVERYTHING players get](mobs-get-everything-players-get.md) — HARD LAW: every skill/stat/state/effect on the player must work on mobs; only STANCES are player-only. A player-only gap is a bug, never design.
