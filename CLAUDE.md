# CLAUDE.md — roguelike-mmo (unwritten)

## Bugfix confidence protocol (ALWAYS)

At the END of every bugfix response, state your confidence that the bug is ALREADY fixed, as a percentage
(0%–100%) on its own final line, e.g. `**Fix confidence: 75%**`. Be honest — reflect whether the fix was
verified live, only reasoned about, or blocked (e.g. Unity unreachable). Do not inflate.

## Second-fix rule: after ONE failed fix, stop hypothesizing — go get GROUND TRUTH

If a fix attempt doesn't resolve the bug, do NOT ship a second fix built on more reasoning about how the
system "probably" works. The next move is to MEASURE: read the real data and replay the real math, then fix
from evidence. Reasoning-only fixes on a bug that already survived one round burn many turns and usually miss
a deeper cause. (This wardrobe bug ate ~4 rounds of plausible-but-wrong fixes; it was cracked in one pass the
moment I decoded the actual save + PNG + FBX bytes and re-ran detection offline — the true cause, a dark-color
gamma mismatch, was invisible to reasoning but obvious in the numbers.)

Ground-truth sources for THIS project (cheap, no Unity needed — a plain Python/Bash script reads them):
- **Save file** = the persisted state that's actually loaded: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\unwritten\unwritten_save.json`.
- **Textures** = decode the PNG bytes (pure-Python zlib/PNG, no PIL) to get exact per-pixel colors/coverage.
- **Meshes/rigs** = parse the binary FBX (zlib-compressed node arrays: Vertices / PolygonVertexIndex / UV) to get real UVs, tris, verts.
- **Scenes/prefabs/.meta** = YAML/text — grep them directly for the actual wired values (renderer names, import flags).
- **Live engine values** = Unity MCP `execute_code` when the bridge is up. A connected MCP bridge beats raising model effort — most stalls here are missing observation, not missing intelligence.

Escalate EFFORT/model only once the evidence is in hand and the remaining hard part is genuinely synthesis —
not as a substitute for looking. Missing information is fixed by looking, not by thinking harder.

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
- **NEVER force-refresh / recompile Unity via MCP** — do NOT call `refresh_unity` (nor poll `read_console` / `editor_state`) at all. This is unconditional, not just for verifying a C# edit: it's slow (each `refresh_unity` blocks ~60s and often times out) and drags the task out. Unity auto-recompiles on its own when the user focuses the editor (or has Auto Refresh off and refreshes manually) — that's their job, not yours. Edit → STOP. Trust the Edit tool (it errors if the change didn't apply). The USER watches the Unity console and reports any compile/log error. If you truly must know whether something compiled, ASK — don't poll. (`execute_code` for a one-off measurement the user asked for is fine; just don't chain it with refresh/console loops.)
- **Prefer editor tools** over manual editor steps; extend `Tools ▸ unwritten ▸ …` for new one-click ops
- **Run it yourself via MCP — don't hand the user a menu click** — when an author-time action can be executed through MCP `execute_code` (asset mutations, one-shot fixes, running an existing editor tool's logic), RUN IT for the user; do NOT reply with a `Tools ▸ unwritten` menu path for them to click. Menu paths are for the user ONLY when MCP can't do it (needs a live Scene-view interaction, Play mode, or the user's own judgment). Separate from the `refresh_unity` ban above — that stays prohibited; this is about not offloading a doable action onto the user.
- **Enemy term is "mobs"** — never "bestiary"
- **Centralize cross-cutting concerns** (full rule in global CLAUDE.md) — Unity primitives to wrap: `Input.GetKeyDown`, `DateTime.Now`, `Random`, direct `PlayerPrefs`. Canonical: `Hotkeys.cs` (typing-guard + `Down/Held/DigitDown`) replaced ~20 raw input sites
- **Single source of truth** (full rule in global CLAUDE.md) — Canonical: the **Vfx Composer's radius ring reads `SkillTickBinding.radius`** (Skill Composer owns it) not a hand-typed copy (`VfxComposerWindow.RefreshSkillRadii`); see also **Pivot = base anchor** below
- **Name by function, not source** (full rule in global CLAUDE.md) — functional: `GlbPropImporter`, `FbxKitPrefabBaker`, `WalkableGround`. Source-specific names allowed only when hardcoded to a pack's folders/quirk: `PxltgrMaterialFixer`, `MobBiomeImporter` (NinjaAdventure dirs)
- **Pivot = base anchor (single source of truth)** — seat objects on the ground by snapping the **PIVOT** (`transform.position`) to the ground hit, NEVER the mesh-bounds bottom / lowest vertex. `Drop Selected to Ground` (`RegionBuilder.cs`) and the generator's `seatByPivot` are canonical; the bounds-based `Drop to Ground` (perched look) is retired. All new placement logic must be pivot-based
- **Bake at author time** — static/never-changing work belongs in generate/editor phase, not Play
- **No comments unless WHY is non-obvious** — well-named identifiers carry the WHAT
- When adding/removing a `Tools ▸ unwritten` menu item, always update `Editor/TOOLS.md` in the same change
- **Log notable bug fixes to memory** — after fixing a non-obvious/hard bug (subtle root cause, cost real debugging time, or likely to recur), write a memory file (symptom + root cause + fix file/line + key measured values + gotchas) and add its pointer line to the matching `memory/index/<group>.md` sub-index (NOT to `MEMORY.md` root — that holds only always-on top-level facts + the group pointers). If no group fits, add a new sub-index + a group pointer in `MEMORY.md`. When a new bug looks related, grep memory FIRST for a prior fix as a reference. Skip trivial/obvious fixes.
- **Handoff unsolved hard bugs** — when a bug ate real debugging time (multiple hypotheses tried) and you're stopping WITHOUT a fix, write a repo-root `DEBUGGING_<bug>_HANDOFF.md` before ending (sibling of "Log notable bug fixes"; that rule = closed bug, this = open bug). Must contain: (1) status = NOT FIXED + "don't trust prior 'it works' claims"; (2) changes still in the working tree (so they're revertible); (3) **what was tried and did NOT work** — the dead-end ledger (highest-value part); (4) proven facts (traces/measurements) kept SEPARATE from hypotheses; (5) the single discriminating test to run next. Lives in the repo, NOT memory — it references uncommitted working-tree state. When a new bug looks related, `ls DEBUGGING_*_HANDOFF.md` FIRST. On resolution: collapse its lesson into a memory (per "Log notable bug fixes") and DELETE the handoff — no stale handoffs at root.