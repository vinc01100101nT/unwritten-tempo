# Memory index

Two-level: top-level facts below are always relevant; grouped topics live in `index/<group>.md` sub-indexes — open the matching sub-index when a task touches that area, then the memory file it points to.

Keep the always-on list SHORT (~10). A line earns its place only if it changes what I do on almost ANY task; a fact that matters only inside one area belongs in that area's sub-index, and a rule already enforced by a check or written in CLAUDE.md does not need a second home here.

## Top-level (always-on)
- [THE FOUR — anti-invention protocol](the-five-anti-invention.md) — an invention disguises itself as continuity: the contract must list what STAYS, a meaning-branch is a design decision, a question gets an answer NOT an edit, ask once then decide. No "new words" report section (removed 2026-10-04).
- [Never widen a snapshot into a verdict](never-widen-a-snapshot-into-a-verdict.md) — today's files prove today only; if the user tested it working, believe them and hunt what CHANGED — never say "it never worked / can't be done".
- [Delete scaffolding EVERY response](delete-scaffolding-every-response.md) — probes, before-copies, copied checkers, /tmp files: delete once the edit is verified, same reply; never carry them over.
- [Changes MUST persist in repo, not runtime](changes-must-persist-in-repo.md) — bake in code/saved asset + read back; never leave a change in bare execute_code/runtime state.
- [Never blame recompilation](assume-compiled-never-blame-recompile.md) — user ALWAYS recompiles; treat code as live+running, diagnose runtime, never suspect stale compile.
- [Find-or-create hides duplicates; fix the CLASS not the instance](find-or-create-hides-duplicates.md) — `GetComponent ?? AddComponent` can't fail so it silently forks a "single source"; one broken member of a uniform system = check ALL members before fixing.
- [A matching function name is NOT the behaviour](existing-function-name-is-not-the-behaviour.md) — user asks for a FEEL; finding `SmoothDamp` ≠ the result exists. Trace position AND rotation to the screen before "already implemented"; when the feel is wrong, suspect the mechanism, not the number.
- [Apply review fixes, don't gatekeep](apply-review-fixes-dont-gatekeep.md) — fix ALL listed findings or label skips explicitly; silent "not worth it" triage reads as refusal.
- [Don't agree reflexively](dont-agree-reflexively.md) — critique on merits even when user says "I like this"; reflexive agreement kills the value of a second opinion.
- [No number walls in decisions](no-number-walls-in-decisions.md) — relaying a design result: two or three plain sentences, make the calls myself, at most one question; never several tables of figures.
- [Just DO it, don't instruct](just-do-it-dont-instruct.md) — if a command can do it, RUN it; never hand over menu paths, keyboard shortcuts, or key symbols. "Where is X" means SHOW me.
- [Edit big docs by SLICE, never a whole read](edit-big-docs-by-slice-never-whole-read.md) — any doc over ~300 lines: grep the heading, read ~40 lines, edit that slice (or sed from the shell); a full read only for a full sweep.
- [NO editor-tool menus by default](no-editor-tools-by-default.md) — user rejects `Tools ▸ unwritten` tools; ask what the feature needs AT RUNTIME (it's often already live), prefer a GM chat command. OVERRIDES the project "prefer editor tools" rule.
- [The game is ALWAYS ONLINE](the-game-is-always-online.md) — no offline/single-machine mode exists; never build, fix or offer a "no server" path. The dead no-server branches still in the code are not evidence it is supported.
- [The server NEVER draws](server-never-draws.md) — HARD LAW 2026-10-07: the server sends data only (state, events, which effect fired); every picture is chosen and drawn by the client from its own data. Never forward a "draw this" message.
- [The world is a SANDBOX](sandbox-world-no-linear-progression.md) — HARD LAW 2026-09-26: no linear progression; creatures live wherever they make sense, maps mix levels and elements, a creature's properties are its own.
- [No typed sub-stats on ANY unit](no-typed-sub-stats-on-any-unit.md) — HARD LAW 2026-10-02: every sub-stat (defence, armour cut, speed, reach, swing timing, regen, leech…) is computed from level/primaries/gear/ring/shared spec; a typeable field is a bug. One exception the user made 2026-10-14: a creature's own movement speed (0 to 2).
- [Mobs get EVERYTHING players get](mobs-get-everything-players-get.md) — HARD LAW: every skill/stat/state/effect on the player must work on mobs; only STANCES are player-only. A player-only gap is a bug, never design.
- [Copy the existing mechanism](copy-the-existing-mechanism.md) — a new track (mastery, an XP bar) is earned and shown exactly like XP/ring already are; never a bespoke formula or special case.
- [DIRECTION: RO-style 2.5D](direction-2p5d-pivot.md) — PLAYER+NPC live-3D, MOBS billboard; player LIVE-3D 2026-07-04.

## Grouped sub-indexes (open on demand)
- 🗂 [Character / rig / stance / animation](index/character-rig-stance.md) — Mixamo/Quaternius rigging, stance system, IStancePose, gait, caster grounding.
- 🗂 [VFX](index/vfx.md) — `Vfx.PlayAt`/`VfxCue` chokes, composer/tuner, spawn/scale/loop gotchas.
- 🗂 [Placement / ground / pivot / collision / pathfinding](index/world-placement.md) — pivot=anchor, feet-pivot, skinWidth floats, A*/crowd, y-sort depth.
- 🗂 [Rendering / lighting / reflection / weather](index/rendering-weather.md) — additional-lights loops, planar reflection, nightlight, rain/snow.
- 🗂 [Asset / art pipeline](index/asset-pipeline.md) — Hunyuan/Trellis 3D props, style guides, 8-dir sprites, Pxltgr/Kenney, the Assets/ bucket mapping.
- 🗂 [World authoring / generation / data tables](index/world-authoring.md) — MapRecipe generator, prop palette, world-creator/map-painter, data SOs, portals, ambient life, scene layering.
- 🗂 [Combat / skills / progression / stats](index/combat-skills-progression.md) — cast time, multi-hit/status, trio-ring model, stats, Ring of Progression, charter, weapons-define-gameplay.
- 🗂 [HUD / UI](index/hud-ui.md) — build process, hotbar, progression windows, click-shield, dev-settings.
- 🗂 [Performance](index/performance.md) — the hard constraint: pre-bake at author time, runtime hot-path patterns; canonical doc is `PERFORMANCE.md`.
- 🗂 [Workflow / tooling / meta](index/workflow-meta.md) — Unity MCP bridge, chat/GM, Tempo docs/canvas gotchas, extractors, logs, persist protocol, dead-code scan, repo layout, writing voice.
- [Absence hook: reply only the fix](absence-hook-reply-only-the-fix.md) — when the absence-claim stop hook fires, send just the corrected sentence, never the whole reply.
