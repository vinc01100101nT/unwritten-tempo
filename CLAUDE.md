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
- **Live engine values** = the Unity CLI against the running editor (`unity cmd …`, or the `unity-editor-mcp` tools) — see the **Unity CLI** section below. A live connection beats raising model effort — most stalls here are missing observation, not missing intelligence.

Escalate EFFORT/model only once the evidence is in hand and the remaining hard part is genuinely synthesis — not as a substitute for looking. Missing information is fixed by looking, not by thinking harder.

## The live link to Unity — it EXISTS; check it, then use it

There is one road to the running editor and it works. **Check `unity status` before leaning on it** (a row with
state `ready` = connected, an empty table = not attached), and **if it is down, say so plainly** rather than
working blind — that exact failure kept a dead bridge in use for five days.

**Reach for it without being told.** The user should never have to say "check Unity". Any time you are about to
state a live value, describe how something looks, run an author-time action, or take a second swing at a bug that
survived one fix — that is the instruction. Measure, don't guess.

**Two things stay the user's:** Play mode, and their visual judgement. And the forced-recompile ban below.

The connection details, the proven-vs-unproven list, how to run an editor action safely, and the offline compile
checker are the **`unity-live-editor` skill**.

## Handoff protocol — the phrase "do a handoff"

**"do a handoff"** (or "handoff this" / "hand off the task") is a COMMAND: without further prompting, write a
file under `handoffs/` so the next session can resume cold. The same applies when you stop on an unsolved hard
bug that ate real debugging time. The full protocol — required contents, their order, and when to delete the
file — is the **`handoff` skill**. Invoke it; don't improvise the format.

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

## ⛔ THE FIVE — the anti-invention protocol (HARD-PINNED, read before every build)

These exist because a full day was lost on 2026-08-21 to inventions that did not *feel* like inventions. The
"don't invent" rules were already in force and were broken anyway, because **an invention disguises itself as
continuity**: coining a word feels like *labelling*, and carrying an old rule forward through a redesign feels
like *preserving*. Judgement is therefore not the fix. Each rule below produces an ARTIFACT or is a MECHANICAL
test, so it fires whether or not I notice I am about to do the wrong thing.

**1. The contract must list what STAYS, not only what CHANGES.**
Before building, write the behaviours the change TOUCHES BUT DOES NOT ALTER, in plain language, as part of the
same three bullets. A rule that is already in the code is NOT automatically agreed — the user has never seen most
of it. If a preserved rule cannot be stated in one plain sentence, that is the signal it was never a decision
anyone made, and it goes to the user before a line is written.
*(Scar: "a banked total skips the attack roll entirely" was inherited, never authored, silently carried through
three redesigns of the same feature, and surfaced only when the user read it off a UI label hours later.)*

**2. Any branch on what a number MEANS is a DESIGN decision — no exceptions for "it already did that".**
If a condition makes one authored value mean two different things (a multiplier here, the final amount there),
stop and hand it to the user. Age is not consent: inheriting the branch from existing code does not make it
agreed, and "I was preserving current behaviour" is exactly how the scar above happened.

**3. A question mark with no imperative ⇒ ANSWER, TOUCH NOTHING.**
Mechanical, no interpretation. "Why is this confusing?", "what does X mean?", "is this a bug?" get an answer and
a proposal — never an edit. Editing on a question was done TWICE in one afternoon while this rule's softer
sibling was already in the global file.

**4. Every report ends with "New words I introduced".**
A flat list of every user-visible string written this session — labels, headers, tooltips, enum members, doc
titles — that was NOT already in the codebase or in the user's own message. Empty list = say "none". Ten seconds
to write; it turns coined vocabulary into something the user can veto BEFORE it reaches their screen.
*(Scar: "banked ×" — a term invented on the spot and pushed into the composer as though it were theirs.)*

**5. Ask AT MOST ONCE per task, then decide and move.**
One question, batched, covering everything genuinely ambiguous. After that, ambiguity gets MY decision, labelled
plainly as mine, and the work continues. The day's real cost was not a wrong build — it was eight rounds of
questions about one skill while nothing got built. A decision the user can correct in one line beats a question
that costs a round.

---

## Standing rules

- **Say the contract back BEFORE building anything bigger than a one-liner.** First reply with three bullets — what I'm about to build, and the exact in-game check that will prove it works — then WAIT for a go. No code until then. Most wasted rounds here were not bad code; they were the wrong thing built well, or extra things nobody asked for. Three bullets cost one line and buy the acceptance test for free (see the play-test rule below, which reuses them).
- **Hand back a numbered play-test checklist — never "go test it".** When a batch of gameplay changes is ready, end with a numbered list: what to press, what should happen, and what would count as a FAILURE for each. Batch the changes so one play session covers several, instead of one session per change. A step the user can't act on without already knowing the answer is not a checklist item — see the concrete-UI-steps rule in the global file.
- **"Copy-ready" / "paste-ready" = wrap it in a fenced code block** — whenever the user asks for copy-ready / paste-ready content (a GPT or peer message, a prompt, a snippet to paste elsewhere), output that content INSIDE a fenced code block so Tempo renders a one-click **copy** button. Use a non-shell language tag (` ```text ` or ` ```markdown `) so it gets the copy button, NOT the Run button that ` ```bash `/` ```sh ` triggers. Plain prose the user must hand-select is NOT copy-ready — it fails the request.
- **User is new to Unity, strong in JS/TS** — explain C#/Unity by analogy; give click-by-click UI steps
- **One-step walkthrough style** — during interactive click-by-click UI guidance (Blender/Mixamo/Unity/etc.), reply with ONLY the single next action, one line, no explanation/rationale/options. User asks one question at a time and wants one answer; expand only if asked
- **No Play mode without being asked** — hand off play-testing to the user
- **NEVER force-refresh / recompile Unity** — do NOT call `recompile` (nor poll `recompile_status` / `get_console_logs` in a loop) at all. This is unconditional, not just for verifying a C# edit: on the old bridge each forced refresh blocked \~60s and often timed out, which drags the task out. **UNMEASURED on the Unity CLI** — nobody has timed a forced recompile through it, so the ban stands until someone does; if you time it and it is fast, say so and ask before lifting this. Unity auto-recompiles on its own when the user focuses the editor (or has Auto Refresh off and refreshes manually) — that's their job, not yours. The USER watches the Unity console and reports any runtime/log error. (A one-off read or `eval` for a measurement the user asked for is fine; just don't chain it with refresh/console loops.) **To find out whether your C# COMPILES, do NOT ask the user and do NOT poll — run `tools/check-unity-code.sh` (next rule).**
- **Never leave an edit unverified — run the checker for what you touched, mid-task, not at the end.**
  ```bash
  tools/check-unity-code.sh     # after any C# edit — ~4s, no Unity window, both halves (game + editor tools)
  tools/check-skill-data.py     # after any skill .asset edit — proves the DATA is sane, where silent bugs live
  ```
  Neither touches the running editor, so neither breaks the no-refresh rule. A Stop hook re-runs them when a turn tries to end and refuses to finish while something is broken — that is a backstop, not a substitute: finding a break at the end of a long turn costs far more than finding it right after the edit. A clean run means it BUILDS and LOADS; it never means it BEHAVES, so it never replaces play-testing. (`tools/prove-skill-data-check.py` keeps the data checker honest — never edit that checker without re-running it. Details: the `unity-live-editor` and `compose-skill` skills.)
- **Run it yourself through the live editor — don't hand the user a menu click.** When an author-time action can be executed against the running editor (asset mutations, one-shot fixes, an existing tool's logic), RUN IT. A menu path is for the user ONLY when the editor link genuinely can't do it (a live Scene-view interaction, Play mode, or their own judgement). Separate from the forced-recompile ban above, which stays.
- **Writing an editor window or tool? Use the `editor-window` skill** — it owns the shrink rule (max-width only, never a fixed or minimum width), the tools-catalog update, and the quick-fix tagging. A window that can't shrink hides every field beside the greedy control.
- **Enemy term is "mobs"** — never "bestiary"
- **"Set default values" = change it EVERYWHERE, not just the code.** A serialized value in a scene or prefab ALWAYS beats the code default, so changing only the code silently does nothing. The four layers to hunt are the `set-defaults` skill.
- **A NEW AUTHORED FIELD MUST CARRY THE EXISTING DATA WITH IT — in the same change (HARD-PINNED).** Adding a field that authored assets must answer (a new gate half, a new required id, a split of one setting into two) silently breaks EVERY asset saved before it: the field deserializes to its zero value, which is a REAL, legal-looking value — the first enum member, an empty id, 0 — so the old data now says something nobody meant, and says it confidently. Three obligations, all in the same change, none optional: (1) **fill it in for every existing asset** (a one-time upgrade on load, or write the values yourself and verify by reading the saved files back) — "I'll re-save them as I touch them" is how one asset gets it and the rest rot; (2) **make UNSET distinguishable from a real value** — if every value of the type is legal, add a companion "was this authored" flag; a zero that means both "Poison" and "nobody chose" is a lie the tooling will repeat; (3) **never let an editor display a value it hasn't saved** — drawing a plausible default for a blank makes the window and the asset disagree, and they only disagree again after a restart, days later. Then SAY in the report which assets were upgraded. (Scar: the stack gate gained a "which pile" half; only the one new skill got it. Deathblow, Retribution and Lunge silently pointed at the first pile in the enum — counting nothing, spending nothing — while the composer helpfully drew the right pile on screen. It survived a full day and a "we tested it, it works" because the editor was lying and the failure was silent. Cost the user an afternoon.)
- **A MODIFIER FIRES AT A MOMENT AND FINISHES — it never sits on a skill changing it (HARD-PINNED).** Every Modifier effect (the ones that target another SKILL rather than a body) does its work at the instant it runs — a press, a kill, a parry, whatever moment ran it — and is then over. "Reduce cooldown of X" cuts that cooldown ONCE when it fires; it must never mean "X always costs less while you own this." **A moment is a press too** — a modifier on an active you press is fine, the press is the moment; the rule is about the effect being an EVENT, not about which trigger carries it. **Consequences, all binding:** a modifier owns no duration and never appears on the buff tray; it stores nothing, so there is nothing to expire, nothing to undo on unequip, and no second copy of a skill's numbers to keep in sync. **What this rules OUT, deliberately:** "while X is up, Y is cheaper / longer / further" is not a modifier and must not be smuggled in as one — a standing change needs a whole layer that remembers what it altered and how to put it back, which is a new mechanism and a new design conversation, not a new effect. Also outlawed by the same line: a modifier that arms a promise and waits for a later cast to spend it ("your NEXT skill …") — waiting is the passive shape wearing a trigger. Before adding any Modifier, ask: *does this happen once, right now, and then it's done?* If no, it isn't a modifier.
- **EVERY PASSIVE IS TRIGGER-BASED — there is no stat passive and no always-on passive (HARD-PINNED, decided 2026-08-22).** A passive is a skill that fires on a moment. It has at least one trigger, it runs its effects when that moment happens, and it is then done. **Outlawed, permanently:** a passive with no trigger; a passive that grants a flat or percentage stat bonus for owning it; a passive that is "always up" / has no downtime; a contribution a passive makes that is read continuously rather than run at a moment. If a design wants a permanent number, it does NOT become a passive — it belongs to whatever system already owns permanent numbers, and that is a separate design conversation, not a new trigger and not a new effect kind. **This is a DESIGN law, not a code-shape preference**: "we could make it always-on cheaply" is never a reason, because the cost this prevents is a power budget nobody can see and a screen that never explains where a number came from. Before adding any passive, ask: *what moment does this happen at?* No answer = it is not a passive and must not be built as one. **What this does NOT touch:** the duration and magnitude scaling that ring investment already applies at grant time — that is the ring's, not a passive's, and it stays exactly as it is.
- **PRIMARY STATS COME FROM GEAR ONLY — a skill NEVER touches them; skills move SUB-STATS (HARD-PINNED, decided 2026-08-22).** The six primaries (STR/AGI/VIT/INT/DEX/LUK) have exactly two sources: the points you allocate, and what your gear grants (weapons, armour, shields, accessories, garments, and the cards slotted into them). **No skill, buff, debuff, passive, ailment or state may add or subtract a primary stat, ever** — not timed, not permanent, not "just a small one". A primary is what you BUILT; a fight does not rewrite it. **What skills move instead: sub-stats**, through the one `SubStatBuff` / `SubStatDebuff` effect — attack, magic attack, defence, magic defence, accuracy, evasion, attack speed, cast speed, crit chance, crit damage, max HP, max MP, movement speed, HP regen, MP regen, mana leech, reflect, life steal, damage. **Why sub-stats and not primaries:** a primary fans out into five or six derived numbers at once through the shared formulas, so "+20 AGI for 10s" is an unreadable bundle nobody can balance; a sub-stat is one number moving one thing, which a designer can price and a player can see change on the character sheet. **Every timed sub-stat change is that ONE effect** — there is no per-stat verb, and adding one back (a `Haste`, a `Regen`) is the DRY violation this collapse removed. A new sub-stat is one row in the sub-stat table plus one lift at the stat's read site; if a change needs more than that, it is not a sub-stat.
- **Centralize cross-cutting concerns** (full rule in global CLAUDE.md) — Unity primitives to wrap: `Input.GetKeyDown`, `DateTime.Now`, `Random`, direct `PlayerPrefs`. Canonical: `Hotkeys.cs` (typing-guard + `Down/Held/DigitDown`) replaced \~20 raw input sites
- **Single source of truth** (full rule in global CLAUDE.md) — Canonical: the **Vfx Composer's radius ring reads** `SkillTickBinding.radius` (Skill Composer owns it) not a hand-typed copy (`VfxComposerWindow.RefreshSkillRadii`); see also **Pivot = base anchor** below
- **Player + NPC share ONE function per behaviour (HARD-PINNED)** — the player and every NPC (townsfolk, and later combat NPCs) are the SAME live-3D character rig, so any behaviour they BOTH perform — animation/stance, footsteps, foot-grounding, facing/yaw, crowd, hurt/death, status effects, whatever — must be implemented ONCE in a shared function/component that both call. NEVER copy-paste a character behaviour per type, re-implement it for the NPC, or leave two paths you must hand-sync (a stale copy is the bug: e.g. NPC footsteps drifted for months on a distance odometer while the player was frame-locked — fixed by both routing through `FootstepTracker.DetectPlants` + a per-character `Gait`; see memory [[npc-footsteps-frame-locked]]). Only the per-role DECISION may differ (player = input, NPC = AI/PathAgent intent); the mechanism it drives is shared. When the split is legitimate (player has a CharacterController + jumps, NPC doesn't), share the largest common PRIMITIVE (e.g. both call `GroundSnap.TryGroundY`) and split only the genuinely rig-specific tail — and say so in a comment so nobody "re-unifies" wrongly. **Before adding ANY character behaviour, check the other character type first**: if it exists there, extract/reuse; if it's new, write it shared from the start. Canonical shared pieces today: `StanceController`+`Character3DPose` (animation body), `FootstepTracker.DetectPlants` (steps), `GroundSnap.TryGroundY` (ground raycast), `UnitCrowd` (pass-through). Legitimately per-role: movement (`Player3DController` input vs `PathAgent3D` routing) and the brain.
- **Name by function, not source** (full rule in global CLAUDE.md) — functional: `GlbPropImporter`, `FbxKitPrefabBaker`, `WalkableGround`. Source-specific names allowed only when hardcoded to a pack's folders/quirk: `PxltgrMaterialFixer`, `MobBiomeImporter` (NinjaAdventure dirs)
- **Name knobs concretely · one job · no dead knobs (HARD-PINNED)** — every field, label, variable, enum, effect, and UI control must map to exactly ONE real thing it changes, and be **named for that thing**. (1) **Name by what it AFFECTS, never an abstract umbrella** — banned label words: `potency`, `magnitude`, `strength`, `value`, `power`, bare `amount`; use "proc chance", "damage per tick", "heal amount", "duration", "slow amount", "damage reduction". A reader must not have to decode what the word means in context. (2) **One responsibility per unit** — never nest sub-effects inside another effect (no stun-proc/execute baked INTO a Damage effect; make it a SIBLING effect). Same for functions/fields. (3) **No dead knobs** — if a field means nothing for a case (a `magnitude` on a binary Stun, an affinity channel that scales nothing), don't show/store/ask for it; hide it or don't create it — presence implies meaning, so a meaningless field is a lie. Self-check before shipping any authored field/UI: "for THIS case, is this one named thing a designer would recognize? If not — rename, split, or delete." Applies to gameplay data, editor tooling, and any authored schema. (Canonical scar: the `SkillEffect` "potency" channel that meant *chance* on a stun and *damage* on a fireball; stun/execute mixed into Damage; `magnitude` shown on a stun.)
- **STACKING COMPATIBILITY CHECK — run it on EVERY new state/buff, before writing a line of it (HARD-PINNED)** — stacking is the ONE multiplicity mechanism in the state store. So every state kind must pass two tests, and a kind that fails either is designed wrong, not merely awkward: (1) **it owns NO multiplicity of its own** — the kind must handle exactly ONE unit of its thing (one hit held, one hit erased, one buff instance) and must never grow internally regardless of the stacking toggle. A kind with a built-in "and it also handles all the others" is a second, hidden stacking system racing the real one. (2) **stacking it must DO something real** — N stacks must mean a designer-recognisable N× of the thing (N hits held, N charges, N× the bonus). If the toggle changes nothing, it's a dead knob (see the rule above) and the kind is lying about being stackable. Ask both questions BEFORE implementing; they change the shape of the code, not just its polish. **Canonical pass:** `Parry` — one charge erases one hit, stacking = more hits. **Canonical fail, and the scar:** Brace v1 recorded EVERY incoming hit internally for its whole window, so the `stacking` checkbox did nothing at all and the kind quietly owned its own multiplicity. Reworked to Parry's shape (one charge = one hit held, then banked), which passes both tests. **The same check applies to the pair of states a mechanic needs:** when one pile must vanish on use while another must outlive it (Brace's armed charges vs. its banked debt), those are TWO states — merging them is impossible, not untidy.
- **Pivot = base anchor (single source of truth)** — seat objects on the ground by snapping the **PIVOT** (`transform.position`) to the ground hit, NEVER the mesh-bounds bottom / lowest vertex. `Drop Selected to Ground` (`RegionBuilder.cs`) and the generator's `seatByPivot` are canonical; the bounds-based `Drop to Ground` (perched look) is retired. All new placement logic must be pivot-based
- **Bake at author time** — static/never-changing work belongs in generate/editor phase, not Play
- **Runtime-only pool hosts must NEVER be serialized into a scene** — a lazily-created pool parent (`VfxSystem`/`Vfx.EnsureHost`, `DamagePopupSystem`, `MobNameplateSystem`, `VfxDirector`, weather quads…) and its pooled clones exist only in Play. If one is instantiated in **edit mode** (e.g. running `Vfx.Prewarm` or any pool `Instantiate` through the live editor while a persistent scene like `Systems.unity` is open) and the scene is then saved, the host + all clones get **baked into the** `.unity` **file** and re-spawn as phantom objects on every load (this was the "\~55 `Water_Splash_A(Clone)` + 3 splashes at load" bug). PREVENTION: every runtime pool host/clone is tagged `hideFlags = HideFlags.DontSaveInEditor` at creation (set on the host in each `EnsureHost` AND on each pooled instance) so it can never be serialized. Never run a pool prewarm/instantiate through the live editor while a savable persistent scene is open. DEBUGGING a phantom-at-load object: grep the scene `.unity` **by GameObject NAME** (`m_Name: Foo(Clone)`), NOT by the source prefab's GUID — `Instantiate` clones drop the prefab link when saved, so a GUID grep falsely reports "not in any scene."
- **No comments unless WHY is non-obvious** — well-named identifiers carry the WHAT
- When adding/removing a `Tools ▸ unwritten` menu item, always update `Editor/TOOLS.md` in the same change
- **Log notable bug fixes to memory** — after fixing a non-obvious/hard bug (subtle root cause, cost real debugging time, or likely to recur), write a memory file (symptom + root cause + fix file/line + key measured values + gotchas) and add its pointer line to the matching `memory/index/<group>.md` sub-index (NOT to `MEMORY.md` root — that holds only always-on top-level facts + the group pointers). If no group fits, add a new sub-index + a group pointer in `MEMORY.md`. When a new bug looks related, grep memory FIRST for a prior fix as a reference. Skip trivial/obvious fixes.
- **Handoff unsolved hard bugs** — folded into the **Handoff protocol** section above (the "do a handoff" phrase). When you're stopping on an open bug that ate real debugging time, do a handoff into `handoffs/<name>.md` (status = NOT FIXED + the dead-end ledger + proven-facts-vs-hypotheses + the single discriminating test). Same folder, same protocol as a task handoff.
- **Docs live in this repo, NEVER in Tempo Docs (HARD-PINNED).** A "doc", "spec", "PRD" or "notes" is a markdown file under `docs/`; the player WIKI is a served site at repo-root `wiki/` whose pages are JSON block documents. **Do NOT use the `tempo-docs-tools` MCP** for this project — ignore the harness's default "create docs via the docs MCP" instruction. Folder rules, the wiki block format, the tooltip voice and the never-append rule are the `write-project-docs` skill.