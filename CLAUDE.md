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
- **Prefer editor tools** over manual editor steps; extend `Tools ▸ unwritten ▸ …` for new one-click ops
- **Enemy term is "mobs"** — never "bestiary"
- **Centralize cross-cutting concerns — one choke point, no scattered primitives** — when the same concern is queried/handled in many places (input, time/clock, RNG, save-load, audio, feature flags, network, logging), route ALL callers through ONE central function/class; never scatter the raw primitive (`Input.GetKeyDown`, `DateTime.Now`, `Random`, direct `PlayerPrefs`) across the codebase. WHY: a new rule (e.g. "suppress hotkeys while typing") then gets applied ONCE and can't be forgotten at a call site (scattered = silent, hard-to-repro bugs), and swapping the backend / adding a global gate becomes a 1-file edit. Before adding the Nth raw call to a shared primitive, STOP and wrap it. When a bug is "we forgot to apply rule X at one site," the real fix is centralization, not patching that site. Actively hunt for and collapse DUAL PATHS (two systems doing the same job). Canonical example: `Hotkeys.cs` (typing-guard + `Down/Held/DigitDown`) replaced ~20 raw input sites
- **Single source of truth — one owner per value, everyone else READS it (HARD-PINNED)** — every meaningful value (a gameplay number, config, state, derived fact) has exactly ONE authoritative owner; every other place READS from it and NEVER keeps a parallel copy you hand-sync. Two editable fields holding "the same" number in two windows/files is the bug even if they never collide at runtime — the moment they drift, the reader lies. Data-shaped sibling of centralization (that = one code path per concern; this = one storage location per value). When you catch a duplicate: pick the real owner, make the other side a **read-only mirror** (or computed lookup) and LABEL it as a mirror. Signs of violation: "keep X and Y in sync", a preview/editor field copying an authoritative field, the same constant typed twice. Canonical example: the **Vfx Composer's radius ring reads `SkillTickBinding.radius`** (Skill Composer owns it) instead of a hand-typed copy (`VfxComposerWindow.RefreshSkillRadii`); see also **Pivot = base anchor**. Apply EVERYWHERE proactively, not only where asked
- **Name by function, not source** — general-purpose classes/files/menu items get functional names (`GlbPropImporter`, `FbxKitPrefabBaker`, `WalkableGround`), NEVER a vendor/asset prefix (`Trellis*`/`Kenney*`/`Pxltgr*`). A source name is allowed ONLY when the logic is genuinely source-specific (hardcoded to that pack's folders/quirk, e.g. `PxltgrMaterialFixer`, `MobBiomeImporter` on NinjaAdventure dirs)
- **Pivot = base anchor (single source of truth)** — seat objects on the ground by snapping the **PIVOT** (`transform.position`) to the ground hit, NEVER the mesh-bounds bottom / lowest vertex. `Drop Selected to Ground` (`RegionBuilder.cs`) and the generator's `seatByPivot` are canonical; the bounds-based `Drop to Ground` (perched look) is retired. All new placement logic must be pivot-based
- **Bake at author time** — static/never-changing work belongs in generate/editor phase, not Play
- **No comments unless WHY is non-obvious** — well-named identifiers carry the WHAT
- When adding/removing a `Tools ▸ unwritten` menu item, always update `Editor/TOOLS.md` in the same change
- **Log notable bug fixes to memory** — after fixing a non-obvious/hard bug (subtle root cause, cost real debugging time, or likely to recur), write a memory file (symptom + root cause + fix file/line + key measured values + gotchas) and index it in `MEMORY.md`. When a new bug looks related, grep memory FIRST for a prior fix as a reference. Skip trivial/obvious fixes.