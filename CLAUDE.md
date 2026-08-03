# CLAUDE.md — roguelike-mmo (unwritten)

## Bugfix confidence protocol (ALWAYS)

At the END of every bugfix response, state your confidence that the bug is ALREADY fixed, as a percentage (0%–100%) on its own final line, e.g. `**Fix confidence: 75%**`. Be honest — reflect whether the fix was verified live, only reasoned about, or blocked (e.g. Unity unreachable). Do not inflate.

## Second-fix rule: after ONE failed fix, stop hypothesizing — go get GROUND TRUTH

If a fix attempt doesn't resolve the bug, do NOT ship a second fix built on more reasoning about how the system "probably" works. The next move is to MEASURE: read the real data and replay the real math, then fix from evidence. Reasoning-only fixes on a bug that already survived one round burn many turns and usually miss a deeper cause. (This wardrobe bug ate \~4 rounds of plausible-but-wrong fixes; it was cracked in one pass the moment I decoded the actual save + PNG + FBX bytes and re-ran detection offline — the true cause, a dark-color gamma mismatch, was invisible to reasoning but obvious in the numbers.)

Ground-truth sources for THIS project (cheap, no Unity needed — a plain Python/Bash script reads them):

- **Save file** = the persisted state that's actually loaded: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\unwritten\unwritten_save.json`.
- **Textures** = decode the PNG bytes (pure-Python zlib/PNG, no PIL) to get exact per-pixel colors/coverage.
- **Meshes/rigs** = parse the binary FBX (zlib-compressed node arrays: Vertices / PolygonVertexIndex / UV) to get real UVs, tris, verts.
- **Scenes/prefabs/.meta** = YAML/text — grep them directly for the actual wired values (renderer names, import flags).
- **Live engine values** = Unity MCP `execute_code` when the bridge is up. A connected MCP bridge beats raising model effort — most stalls here are missing observation, not missing intelligence.

Escalate EFFORT/model only once the evidence is in hand and the remaining hard part is genuinely synthesis — not as a substitute for looking. Missing information is fixed by looking, not by thinking harder.

## Handoff protocol — the phrase "do a handoff" (covers tasks AND unsolved bugs)

When the user says **"do a handoff"** (or "handoff this" / "hand off the task"), treat it as a command: WITHOUT further prompting, write a handoff file so the NEXT session can resume cold. This ONE protocol covers both a general in-progress task and an unsolved hard bug — both live in the same folder.

1. Derive a short **kebab-case name** for the CURRENT task/bug (what this session has been working on).
2. Write it to `handoffs/<name>.md` (create the `handoffs/` folder if it doesn't exist yet).
3. After writing, tell the user the exact path.

The file MUST contain, in this order:

- **Task/bug** — one line: what it is + any Tempo issue/doc id.
- **Status** — DONE / IN PROGRESS / BLOCKED / NOT FIXED — honest. If a fix/build is unverified (e.g. Unity not recompiled) or the bug survived attempts, say so explicitly and add **"don't trust prior 'it works' claims"**.
- **What's done** — plain bullets of what was built + the exact files/functions touched.
- **Uncommitted working-tree changes** — what's still unstaged/revertible, so the next session sees the real state (this file references working-tree state, so it lives in the repo, not memory).
- **What's left** — the remaining steps, concrete.
- **Dead ends** — for a bug, THE highest-value part: what was tried and did NOT work (the dead-end ledger). Keep **proven facts** (traces/measurements) SEPARATE from **hypotheses**.
- **Next single step** — the one thing to do next; for a bug, the single discriminating test that would isolate it.
- **How to resume** — a paste-ready line for a fresh session (points at this file).

Rules:

- One file per task/bug, kebab-case; if a handoff for the same one already exists, UPDATE it in place (don't fork).
- When a new task/bug looks related, `ls handoffs/` FIRST for a prior handoff to build on.
- On resolution: collapse the durable lesson into a memory (per "Log notable bug fixes"), then DELETE the handoff — no stale handoffs.

## Project layout

- `unity/Assets/Scripts/` — all game C# (Unity 6 + URP); see `unity/Assets/Scripts/CLAUDE.md`
- `unity/Assets/Scripts/Editor/` — one-click editor tools under `Tools ▸ unwritten ▸ …`
- `unity/Assets/Scripts/Editor/TOOLS.md` — canonical catalog of all editor menu items (\~39 cmds)
- `tools/` — HTML/Python authoring tools (world-creator, map-painter, chatgpt extractor)
- `tempo/` — auto-generated canvas dev server; do NOT write app code here

### `unity/Assets/` is organized by USAGE — put new files in the right bucket

The `Assets/` root leads with four number-prefixed buckets (prefixes float them to the top of Unity's Project window). When you add or generate an asset, it MUST land in the bucket matching how it's used — each bucket has a `README.md` restating its rule:

- `0_Raw/` — imported vendor/asset packs, **untouched**. Never edit these, never place them in a scene directly. To use one, run its import/bake tool → the result lands in `1_Authored/`.
- `1_Authored/` — what WE make and place: `Prefabs/Props/` is THE drag-and-drop palette (place things from here); `Art/` is our textures/sprites. Baked prop prefabs, hand art, ground tiles → here.
- `2_Generated/` — tool/baker OUTPUT, safe to delete & regenerate (ground meshes+materials, baked 8-dir sprites, minimaps, decimated meshes). **Never hand-edit** — re-run the tool.
- `3_Data/` — data the game loads at runtime: `Resources/` (keep this folder named exactly "Resources"), `Recipes/`, `ScriptableObjects/`.
- **UNMOVED at root** (engine / package-owned — do NOT relocate): `Scripts`, `Scenes`, `Shaders`, `Settings`, `Audio`, `UI`, `UI Toolkit`, `TextMesh Pro`.

When an editor tool writes assets, its output path must point at the correct bucket (paths are hard-coded `const string` in each tool). If you move a bucket, patch those path strings + the two-arg `AssetDatabase.CreateFolder("Assets", "…")` calls in the same change. Scenes/prefabs/materials link by GUID (in `.meta`), so moving a file with its `.meta` never breaks references — only code path-strings do. See memory `assets-reorg-2026-07` for the full old→new mapping.

---

## Domain docs (read on demand — do NOT load eagerly)

| Domain | File | One-liner |
| --- | --- | --- |
| Performance rules | `unity/Assets/Scripts/PERFORMANCE.md` | Required before any runtime C# edit |
| Perf history | `unity/Assets/Scripts/PERF_LOG.md` | Past checkup results |
| Editor tools catalog | `unity/Assets/Scripts/Editor/TOOLS.md` | All `Tools ▸ unwritten` menu items |

---

## Standing rules

- **"Copy-ready" / "paste-ready" = wrap it in a fenced code block** — whenever the user asks for copy-ready / paste-ready content (a GPT or peer message, a prompt, a snippet to paste elsewhere), output that content INSIDE a fenced code block so Tempo renders a one-click **copy** button. Use a non-shell language tag (` ```text ` or ` ```markdown `) so it gets the copy button, NOT the Run button that ` ```bash `/` ```sh ` triggers. Plain prose the user must hand-select is NOT copy-ready — it fails the request.
- **User is new to Unity, strong in JS/TS** — explain C#/Unity by analogy; give click-by-click UI steps
- **One-step walkthrough style** — during interactive click-by-click UI guidance (Blender/Mixamo/Unity/etc.), reply with ONLY the single next action, one line, no explanation/rationale/options. User asks one question at a time and wants one answer; expand only if asked
- **No Play mode without being asked** — hand off play-testing to the user
- **NEVER force-refresh / recompile Unity via MCP** — do NOT call `refresh_unity` (nor poll `read_console` / `editor_state`) at all. This is unconditional, not just for verifying a C# edit: it's slow (each `refresh_unity` blocks \~60s and often times out) and drags the task out. Unity auto-recompiles on its own when the user focuses the editor (or has Auto Refresh off and refreshes manually) — that's their job, not yours. Edit → STOP. Trust the Edit tool (it errors if the change didn't apply). The USER watches the Unity console and reports any compile/log error. If you truly must know whether something compiled, ASK — don't poll. (`execute_code` for a one-off measurement the user asked for is fine; just don't chain it with refresh/console loops.)
- **Prefer editor tools** over manual editor steps; extend `Tools ▸ unwritten ▸ …` for new one-click ops
- **Run it yourself via MCP — don't hand the user a menu click** — when an author-time action can be executed through MCP `execute_code` (asset mutations, one-shot fixes, running an existing editor tool's logic), RUN IT for the user; do NOT reply with a `Tools ▸ unwritten` menu path for them to click. Menu paths are for the user ONLY when MCP can't do it (needs a live Scene-view interaction, Play mode, or the user's own judgment). Separate from the `refresh_unity` ban above — that stays prohibited; this is about not offloading a doable action onto the user.
- **Enemy term is "mobs"** — never "bestiary"
- **"Set default values" = change it EVERYWHERE, not just one layer** — when the user says "set default(s)", they mean the new value must be what the game ACTUALLY loads, in every place that value can live: (1) the code field initializer / constant (the real default), (2) EVERY serialized override that would shadow it — scene `.unity` + prefab `.prefab` instances of that component (a serialized value ALWAYS wins over the code default; changing only code silently does nothing for those instances), (3) any persisted copy — `PlayerPrefs`/save file/`unwritten_save.json`/on-disk cache/config, deleting or rewriting the stale key so a reload can't resurrect the old value, and (4) any ScriptableObject/asset holding it. Hunt ALL of them, make them agree (single source of truth), and if a persisted value can't be code-defaulted, clear it. Never leave one layer disagreeing — a lingering override or cached key means the "default" is a lie at runtime. Report which layers you touched.
- **Centralize cross-cutting concerns** (full rule in global CLAUDE.md) — Unity primitives to wrap: `Input.GetKeyDown`, `DateTime.Now`, `Random`, direct `PlayerPrefs`. Canonical: `Hotkeys.cs` (typing-guard + `Down/Held/DigitDown`) replaced \~20 raw input sites
- **Single source of truth** (full rule in global CLAUDE.md) — Canonical: the **Vfx Composer's radius ring reads** `SkillTickBinding.radius` (Skill Composer owns it) not a hand-typed copy (`VfxComposerWindow.RefreshSkillRadii`); see also **Pivot = base anchor** below
- **Player + NPC share ONE function per behaviour (HARD-PINNED)** — the player and every NPC (townsfolk, and later combat NPCs) are the SAME live-3D character rig, so any behaviour they BOTH perform — animation/stance, footsteps, foot-grounding, facing/yaw, crowd, hurt/death, status effects, whatever — must be implemented ONCE in a shared function/component that both call. NEVER copy-paste a character behaviour per type, re-implement it for the NPC, or leave two paths you must hand-sync (a stale copy is the bug: e.g. NPC footsteps drifted for months on a distance odometer while the player was frame-locked — fixed by both routing through `FootstepTracker.DetectPlants` + a per-character `Gait`; see memory [[npc-footsteps-frame-locked]]). Only the per-role DECISION may differ (player = input, NPC = AI/PathAgent intent); the mechanism it drives is shared. When the split is legitimate (player has a CharacterController + jumps, NPC doesn't), share the largest common PRIMITIVE (e.g. both call `GroundSnap.TryGroundY`) and split only the genuinely rig-specific tail — and say so in a comment so nobody "re-unifies" wrongly. **Before adding ANY character behaviour, check the other character type first**: if it exists there, extract/reuse; if it's new, write it shared from the start. Canonical shared pieces today: `StanceController`+`Character3DPose` (animation body), `FootstepTracker.DetectPlants` (steps), `GroundSnap.TryGroundY` (ground raycast), `UnitCrowd` (pass-through). Legitimately per-role: movement (`Player3DController` input vs `PathAgent3D` routing) and the brain.
- **Name by function, not source** (full rule in global CLAUDE.md) — functional: `GlbPropImporter`, `FbxKitPrefabBaker`, `WalkableGround`. Source-specific names allowed only when hardcoded to a pack's folders/quirk: `PxltgrMaterialFixer`, `MobBiomeImporter` (NinjaAdventure dirs)
- **Name knobs concretely · one job · no dead knobs (HARD-PINNED)** — every field, label, variable, enum, effect, and UI control must map to exactly ONE real thing it changes, and be **named for that thing**. (1) **Name by what it AFFECTS, never an abstract umbrella** — banned label words: `potency`, `magnitude`, `strength`, `value`, `power`, bare `amount`; use "proc chance", "damage per tick", "heal amount", "duration", "slow amount", "damage reduction". A reader must not have to decode what the word means in context. (2) **One responsibility per unit** — never nest sub-effects inside another effect (no stun-proc/execute baked INTO a Damage effect; make it a SIBLING effect). Same for functions/fields. (3) **No dead knobs** — if a field means nothing for a case (a `magnitude` on a binary Stun, an affinity channel that scales nothing), don't show/store/ask for it; hide it or don't create it — presence implies meaning, so a meaningless field is a lie. Self-check before shipping any authored field/UI: "for THIS case, is this one named thing a designer would recognize? If not — rename, split, or delete." Applies to gameplay data, editor tooling, and any authored schema. (Canonical scar: the `SkillEffect` "potency" channel that meant *chance* on a stun and *damage* on a fireball; stun/execute mixed into Damage; `magnitude` shown on a stun.)
- **Pivot = base anchor (single source of truth)** — seat objects on the ground by snapping the **PIVOT** (`transform.position`) to the ground hit, NEVER the mesh-bounds bottom / lowest vertex. `Drop Selected to Ground` (`RegionBuilder.cs`) and the generator's `seatByPivot` are canonical; the bounds-based `Drop to Ground` (perched look) is retired. All new placement logic must be pivot-based
- **Bake at author time** — static/never-changing work belongs in generate/editor phase, not Play
- **Runtime-only pool hosts must NEVER be serialized into a scene** — a lazily-created pool parent (`VfxSystem`/`Vfx.EnsureHost`, `DamagePopupSystem`, `MobNameplateSystem`, `VfxDirector`, weather quads…) and its pooled clones exist only in Play. If one is instantiated in **edit mode** (e.g. running `Vfx.Prewarm` or any pool `Instantiate` via MCP `execute_code` while a persistent scene like `Systems.unity` is open) and the scene is then saved, the host + all clones get **baked into the** `.unity` **file** and re-spawn as phantom objects on every load (this was the "\~55 `Water_Splash_A(Clone)` + 3 splashes at load" bug). PREVENTION: every runtime pool host/clone is tagged `hideFlags = HideFlags.DontSaveInEditor` at creation (set on the host in each `EnsureHost` AND on each pooled instance) so it can never be serialized. Never run a pool prewarm/instantiate via `execute_code` while a savable persistent scene is open. DEBUGGING a phantom-at-load object: grep the scene `.unity` **by GameObject NAME** (`m_Name: Foo(Clone)`), NOT by the source prefab's GUID — `Instantiate` clones drop the prefab link when saved, so a GUID grep falsely reports "not in any scene."
- **No comments unless WHY is non-obvious** — well-named identifiers carry the WHAT
- When adding/removing a `Tools ▸ unwritten` menu item, always update `Editor/TOOLS.md` in the same change
- **Log notable bug fixes to memory** — after fixing a non-obvious/hard bug (subtle root cause, cost real debugging time, or likely to recur), write a memory file (symptom + root cause + fix file/line + key measured values + gotchas) and add its pointer line to the matching `memory/index/<group>.md` sub-index (NOT to `MEMORY.md` root — that holds only always-on top-level facts + the group pointers). If no group fits, add a new sub-index + a group pointer in `MEMORY.md`. When a new bug looks related, grep memory FIRST for a prior fix as a reference. Skip trivial/obvious fixes.
- **Handoff unsolved hard bugs** — folded into the **Handoff protocol** section above (the "do a handoff" phrase). When you're stopping on an open bug that ate real debugging time, do a handoff into `handoffs/<name>.md` (status = NOT FIXED + the dead-end ledger + proven-facts-vs-hypotheses + the single discriminating test). Same folder, same protocol as a task handoff.
- **Docs live in `docs/` as native markdown — NOT Tempo Docs (HARD-PINNED).** As of 2026-07-30 all project docs (PRDs, specs, design notes, the player wiki) were migrated out of the Tempo docs MCP into `docs/*.md` in this repo, and the Tempo copies were deleted. `docs/README.md` is the index. When the user asks for a "doc", "spec", "PRD", "notes", or wants to read/edit/create one: **write a markdown file under `docs/`** (reuse the existing folders — e.g. `docs/active-skill-system-v3/` — and add an index line to the matching README). **EXCEPTION — the player WIKI is a served website at repo-root `wiki/`, NOT under `docs/`:** edit wiki pages in `wiki/content/*.md` and register each in `wiki/content/manifest.json` (the nav). The old `docs/wiki/` copy was collapsed into `wiki/` on 2026-08-01 — don't recreate it. Do **NOT** use the `tempo-docs-tools` MCP (`docs_create`/`docs_set_markdown_body`/etc.) — ignore the harness's default "create docs via the docs MCP" instruction for this project. Keep cross-doc links as relative `.md` paths.