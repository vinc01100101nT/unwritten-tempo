# CLAUDE.md — roguelike-mmo (unwritten)

## Project layout

- `unity/Assets/Scripts/` — all game C# (Unity 6 + URP); see `unity/Assets/Scripts/CLAUDE.md`
- `unity/Assets/Scripts/Editor/` — one-click editor tools under `Tools ▸ unwritten ▸ …`
- `unity/Assets/Scripts/Editor/TOOLS.md` — canonical catalog of all editor menu items (\~39 cmds)
- `tools/` — HTML/Python authoring tools (world-creator, map-painter, chatgpt extractor)
- `tempo/` — auto-generated canvas dev server; do NOT write app code here

---

## Domain docs (read on demand — do NOT load eagerly)

| Domain | File | One-liner |
| --- | --- | --- |
| Performance rules | `unity/Assets/Scripts/PERFORMANCE.md` | Required before any runtime C# edit |
| Perf history | `unity/Assets/Scripts/PERF_LOG.md` | Past checkup results |
| Editor tools catalog | `unity/Assets/Scripts/Editor/TOOLS.md` | All `Tools ▸ unwritten` menu items |

---

## Standing rules

- **User is new to Unity, strong in JS/TS** — explain C#/Unity by analogy; give click-by-click UI steps
- **One-step walkthrough style** — during interactive click-by-click UI guidance (Blender/Mixamo/Unity/etc.), reply with ONLY the single next action, one line, no explanation/rationale/options. User asks one question at a time and wants one answer; expand only if asked
- **No Play mode without being asked** — hand off play-testing to the user
- **NEVER recompile / refresh / poll the Unity console to verify a C# edit** — do NOT call `refresh_unity`, `read_console`, or poll `editor_state` after editing scripts. It's slow (each `refresh_unity` blocks ~60s and often times out) and drags the task out. Edit → STOP. Trust the Edit tool (it errors if the change didn't apply). The USER watches the Unity console and reports any compile/log error. If you truly must know whether something compiled, ASK — don't poll. (`execute_code` for a one-off measurement the user asked for is fine; just don't chain it with refresh/console loops.)
- **Prefer editor tools** over manual editor steps; extend `Tools ▸ unwritten ▸ …` for new one-click ops
- **Enemy term is "mobs"** — never "bestiary"
- **Centralize cross-cutting concerns** (full rule in global CLAUDE.md) — Unity primitives to wrap: `Input.GetKeyDown`, `DateTime.Now`, `Random`, direct `PlayerPrefs`. Canonical: `Hotkeys.cs` (typing-guard + `Down/Held/DigitDown`) replaced ~20 raw input sites
- **Single source of truth** (full rule in global CLAUDE.md) — Canonical: the **Vfx Composer's radius ring reads `SkillTickBinding.radius`** (Skill Composer owns it) not a hand-typed copy (`VfxComposerWindow.RefreshSkillRadii`); see also **Pivot = base anchor** below
- **Name by function, not source** (full rule in global CLAUDE.md) — functional: `GlbPropImporter`, `FbxKitPrefabBaker`, `WalkableGround`. Source-specific names allowed only when hardcoded to a pack's folders/quirk: `PxltgrMaterialFixer`, `MobBiomeImporter` (NinjaAdventure dirs)
- **Pivot = base anchor (single source of truth)** — seat objects on the ground by snapping the **PIVOT** (`transform.position`) to the ground hit, NEVER the mesh-bounds bottom / lowest vertex. `Drop Selected to Ground` (`RegionBuilder.cs`) and the generator's `seatByPivot` are canonical; the bounds-based `Drop to Ground` (perched look) is retired. All new placement logic must be pivot-based
- **Bake at author time** — static/never-changing work belongs in generate/editor phase, not Play
- **No comments unless WHY is non-obvious** — well-named identifiers carry the WHAT
- When adding/removing a `Tools ▸ unwritten` menu item, always update `Editor/TOOLS.md` in the same change
- **Log notable bug fixes to memory** — after fixing a non-obvious/hard bug (subtle root cause, cost real debugging time, or likely to recur), write a memory file (symptom + root cause + fix file/line + key measured values + gotchas) and index it in `MEMORY.md`. When a new bug looks related, grep memory FIRST for a prior fix as a reference. Skip trivial/obvious fixes.
- **Handoff unsolved hard bugs** — when a bug ate real debugging time (multiple hypotheses tried) and you're stopping WITHOUT a fix, write a repo-root `DEBUGGING_<bug>_HANDOFF.md` before ending (sibling of "Log notable bug fixes"; that rule = closed bug, this = open bug). Must contain: (1) status = NOT FIXED + "don't trust prior 'it works' claims"; (2) changes still in the working tree (so they're revertible); (3) **what was tried and did NOT work** — the dead-end ledger (highest-value part); (4) proven facts (traces/measurements) kept SEPARATE from hypotheses; (5) the single discriminating test to run next. Lives in the repo, NOT memory — it references uncommitted working-tree state. When a new bug looks related, `ls DEBUGGING_*_HANDOFF.md` FIRST. On resolution: collapse its lesson into a memory (per "Log notable bug fixes") and DELETE the handoff — no stale handoffs at root.