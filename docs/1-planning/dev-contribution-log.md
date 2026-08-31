---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - planning-production
---

# Dev Contribution Log — unwritten

**Why this exists.** A gamified record of how **you, the dev (vince),** actually shape this project — *not* the AI. It's proof this isn't "pure vibe coding": every time you diagnose a bug, steer the design, spot something the AI missed, or show you understand a system, it gets logged and scored. You're learning the **systems** even while the C# syntax is still new — and this tracks exactly that.

Maintained by the AI agent: it appends an entry whenever it notices a real contribution during a session.

---

## Current standing

- **Total XP:** 1345
- **Level:** 9 — **Systems Architect** *(55 XP to Level 10)* — *stopped a 20-site symptom-patch and made a single input choke point (*`Hotkeys`*), then abstracted it into a standing engineering protocol — centralize every cross-cutting concern — that now lives in the global rules for every project.*
- **Badges:** First Blood · Root-Cause Detective · Architect's Eye · Meta-Builder · Decisive · Cartographer · Game Designer · Playtester · Toolsmith *(designed an end-to-end content-authoring tool, not just used one)* · Systems Thinker *(codified a general engineering principle — centralize cross-cutting concerns — into a standing protocol)*

---

## How scoring works

**Difficulty multiplier** (AI's judgment): `×1` routine · `×1.5` solid · `×2` sharp / insightful.

> **Honesty rule.** XP is for *your* input, not the AI's. If the AI proposed something and you just said "ok," that's not scored. Choosing between real options, pushing back, diagnosing, or redirecting **does** count.

> *The XP-by-category table, the level ladder and the badge list were lost when these docs left Tempo on 2026-07-30 — they were already empty in the migration commit, so there is nothing to restore from git. The standing totals and badges above are the surviving record. Rebuilding the three tables means authoring them fresh, which is vince's call.*

---

## Log

*Format — every entry says what you did, the XP, **and a one-line "why"** so the points are self-explanatory later:*

> **YYYY-MM-DD ·** `<category>` **Short title · +XP***Why:* one sentence on what made it count — the insight, the redirect, the catch (quote you when the phrasing is punchy). *(difficulty ×N)*

> **2026-06-14 ·** `architecture` **"Why attach to each map?" — global systems, not per-scene · +100***Why:* Diagnosed that the cursor + click-to-move dying on a portal wasn't a one-off bug but a **lifecycle smell** — global concerns (UI / cursor / input) were authored per-scene — and prescribed the fix directly: *"move the entire UI system including the functionality into system level… these functionalities are global, why attach to each map?"* Then chose the full Persistent-Player refactor over the lighter patch. *(difficulty ×2 — named the exact design principle the AI then implemented:* `PersistentPlayer` *+* `GameSystems` *mirroring* `Character`*/*`PersistentUI`*.)*

> **2026-06-14 ·** `architecture` **"Add a higher layer where HUDs live" — invented the persistent-scene pattern · +80***Why:* Not satisfied with the scrappy first fix, pushed for the *clean* version and described it from first principles: *"can we add a higher layer? higher than these scenes? where HUD's live? so we do not have to put* `DontDestroyOnLoad` *everytime."* That's the persistent-scene + additive-load pattern (app-shell/`layout.tsx`) reinvented without knowing its name — recognising `DontDestroyOnLoad`-everywhere as a smell and asking for the architectural cure. Confirmed the mental model with a sharp check ("we put all HUD elements there right?"). *(difficulty ×2 — senior-level instinct; drove the whole* `Systems.unity` *refactor.)*

> **2026-06-14 ·** `design` **Framed the map generator — "permanent maps, not runtime roguelike" · +45***Why:* Opened a whole new workstream and drew its defining line up front — *"this is not a generate-random-map-on-the-fly like those roguelike… we will create a tool that generates maps which we will use permanently for scenes."* That offline-bake-vs-runtime distinction is the architectural call that shaped everything (recipe assets, baked scenes, hand-finish flow); also chose Town-first + the re-runnable recipe model over the lighter options offered. *(difficulty ×1.5 — set the product's spine before any code.)*

> **2026-06-14 ·** `mental-model` **Caught the asset reality — "I only have tilesets" · +30***Why:* Instead of rubber-stamping the AI's "building prefabs" plan, surfaced the real state — *"what are building prefabs? i only have tilesets… in Assets/Art/Palettes and Assets/Art/Tiles"* — which redirected the whole building approach from prefabs to **hand-painted tile-stamp templates**, and pointed at the exact art paths. A constraint the plan had assumed away. *(difficulty ×1.5)*

> **2026-06-16 ·** `spotting` **"Your instinct was correct" — caught the grass map reading wrong · +35***Why:* Trusted your eye over the generator's default and flagged that the baked grass town looked off — which forced the edge-rim investigation that uncovered the real art constraint (*only* Dirt/DarkDirt have a grass-blendable rim in this sheet) and corrected the wrong default patch terrains. Spotting a problem the AI had shipped as "fine." *(difficulty ×1.5)*

> **2026-06-16 ·** `design` **Held the quality bar on path edges — drove a 3-pass polish · +25***Why:* Didn't accept "good enough": on each regenerate you pinpointed the *specific* remaining artifact — *"there are still sharp edges"* (the diagonal staircase) → *"still some sharp edges"* (the ruler-straight diagonal blades) — which is exactly the visual diagnosis that aimed the fixes at roadWidth → meander → corner-rounding. Acting as art director with a discerning eye; also probed the system with a sharp (if inverted) question — *"I used the same rule tile for path and terrain… will this be fixed?"* *(difficulty ×1)*

> **2026-06-16 ·** `mental-model` **Designed the isolation test that cracked the auto-tile bug · +60***Why:* After \~6 generator tweaks failed, you stopped the guessing with a clean experiment of your own — hand-painted the RuleTile in the Tile Palette and reported the decisive fact: *"manual RuleTile painting works perfectly… when a horizontal line crosses a vertical line, it splits perfectly… let's use this to our generator."* That held the tile constant and varied the painter, proving the tile/rules/`SetTile` were all correct and the bug had to be a missing refresh — which it was (`RefreshAllTiles` after commit + one shared dirt tilemap). Textbook differential diagnosis; also earlier corrected the AI's wrong "it's just extreme-zoom" read by including the player for scale. *(difficulty ×2 — isolated a multi-session bug with a single experiment the AI hadn't thought to run.)*

> **2026-06-16 ·** `spotting` **Caught the test-environment confound — the old scene was bleeding through · +30***Why:* Honest self-review cracked open a confound that had wasted hours: the "sharp edges" were partly an old SAVED scene left visible behind the additive preview, rendering its tilemaps over the new one — *"I've been testing with the old scene visible in the background… this is the cons of regeneration \[requiring an\] active and saved scene opened."* Caught a tooling flaw the AI never suspected, and owned it instead of blaming the code. *(difficulty ×1.5 — meta-level catch; drove the Generate-isolates-the-preview fix.)*

> **2026-06-16 ·** `spotting` **"Why so few props at max density — is that the right setting?" · +25***Why:* A precise question that surfaced a real shipped bug: trees were *inversely* tied to density (`0.06 * (1 - townDensity)`), so cranking density to max set trees to ZERO. Drove the split into independent Building / Tree sliders. *(difficulty ×1.5 — questioned the default instead of accepting the empty-looking result.)*

> **2026-06-16 ·** `design` **Pushed for plain-English UX — "always simplify, don't overcomplicate" · +20***Why:* Flagged developer jargon in the inspector as a real usability problem, with the line of the session — *"when I heard spoke… I thought it was some words spoken, or a creature… like a pokemon slowpoke."* Drove a full field-rename + tooltip rewrite to beginner-friendly language across the whole recipe. *(difficulty ×1)*

> **2026-06-18 ·** `design` **Invented the Trio-Ring skill draft — an original level-up mechanic from scratch · +75***Why:* Scrapped the entire prior 28-node graph system outright — *"forget our existing skill tree draft… it's trash"* — and replaced it with a genuinely novel mechanic of your own: stats as overlapping **Trios** (a leader + 2 local siblings) where picking a side sibling makes *it* the new leader of its own trio, so the draft becomes a **sliding window you steer** along a chain — *"choosing Def opens CritChance… the passive steers on the right side."* That window/leader idea is the spine of the whole system. *(difficulty ×2 — designed a working, content-free draft mechanic the AI then only refined; the ring topology was the AI's, the trio-steering core was yours.)*

> **2026-06-18 ·** `mental-model` **Called the percent-passive architecture + diminishing returns by instinct · +35***Why:* Independently reached the right structure — passives as **% multipliers**, with flat stats kept on equipment / stat-points so passives stay meaningful — and intuited diminishing returns before seeing any math: *"at level 100 I don't want 100% atk, I'd prefer 50% atk and some 30% critdamage."* The instinct was sound even where it needed a correction (percent alone isn't DR — that's a separate curve); the architectural call and the design goal were yours. *(difficulty ×1.5)*

> **2026-06-18 ·** `design` **Designed the level-up cadence — free start + every-10 actives · +30***Why:* Set the progression rhythm: 1 point per level, passives most levels with an **active every 10th**, and — the sharp touch — **level 1 exposes all stats with no wildcard** so the player deliberately chooses their starting point before randomness kicks in. You reasoned about *why* (*"they decide which path they will travel… pivot mid-game"*), not just the what. *(difficulty ×1.5)*

> **2026-06-18 ·** `process` **Held the planning/coding session split · +10***Why:* Deliberately kept this session design-only and deferred implementation — *"separate tasks each session is optimal"* — and had the docs/issues cleaned to a single source of truth before any code, which is exactly why the coding session that followed could move fast. *(difficulty ×1)*

> **2026-06-18 ·** `design` **Locked the draft UX — chose bankable points over a forced modal · +15***Why:* Kicked off the skill-MVP build and, given the one architecture-shaping fork (per-level blocking modal vs bankable points spent in the K panel), picked **bankable** — the call that decided how the level-up hook and draft UI are wired. It confirmed the design doc's own lean rather than overturning it, but was a real choice between presented options at the moment it mattered. *(difficulty ×1 — clean, low-risk decision; the heavier design thinking was already banked in the doc.)*

> **2026-06-18 ·** `spotting` **Pinpointed the kiter attack-thrash from a 3-symptom playtest report · +40***Why:* While fighting a kiting caster you reported the exact failure signature — *"the animation is stuck on attack pose, the player matches the flam walk speed, and i hear some weird noise"* — three precise symptoms that let the AI immediately localize a latent bug: the stand-vs-chase check flipping every frame at the range edge (machine-gunning the footstep SFX + re-triggering the pose + pacing the target). Hands-on QA that turned a vague "feels wrong" into a one-shot diagnosis. *(difficulty ×2 — the symptom triple WAS the root-cause clue; the fix, range hysteresis, followed directly.)*

> **2026-06-18 ·** `spotting` **Questioned the kiting Flam — "is this expected? it still has 100% HP" · +20***Why:* Didn't accept the runaway caster as working-as-intended — flagged that an enemy fleeing at full health *read* as broken and asked whether it was a bug, surfacing that the kite range was tuned far too slippery (and, with no visible projectile, illegible). Drove the Flam retune + the "real projectile" follow-up. *(difficulty ×1.5 — spotted a feel/readability bug the AI had shipped as a feature.)*

> **2026-06-18 ·** `design` **Redirected "pick a class" into "how do classes even work?" · +20***Why:* Offered a class to build, you instead demanded the system first — *"how can we make other classes? what will be their identities?"* — forcing the class MODEL to be articulated (a kit + attack-type + affinity layer ON TOP of universal stats/passives, not a stat-lock) before any code. Designing the framework rather than rubber-stamping a one-off. *(difficulty ×1.5 — a redirect that set the spine for every future class.)*

> **2026-06-18 ·** `process` **Drove the launch-roadmap curation + checkpoint discipline · +10***Why:* Turned a vague "how far from launch" into shippable structure — directed the two-tier roadmap be documented with *"no duplicate docs… remove outdated docs if necessary,"* asked for the M1–M5 work to be broken into real issues, and bookended the session with a clean checkpoint before clearing. Keeps the project a single source of truth. *(difficulty ×1)*

> **2026-06-22 ·** `spotting` **Falsified the "it's just the editor grid" diagnosis with a one-action test · +20***Why:* The new vertex-alpha blended ground rendered with an ugly speckled cross; the AI confidently called it harmless Scene-grid z-fighting (*"press Play, it'll vanish"*). You ran the decisive check and reported *"the grid did not disappear"* — a single negative result that killed the wrong hypothesis outright and forced the real hunt, which found the cause (sprite-Clamp wrap sampled with world-space UVs, fixed via a forced-repeat sampler). Hands-on QA where the falsifying test WAS the contribution. *(difficulty ×1.5 — didn't accept the AI's confident wrong read; the disproof redirected the whole debug.)*

> **2026-06-23 ·** `design` **Invented the World Creator pipeline — a browser graph-editor → JSON → Unity orchestrator · +75***Why:* Designed an entire content-authoring pipeline of your own, unprompted: a **grid where each cell is a map** (hover → `+` → type menu → colour-coded square), **blue-dot warps** dropped on shared borders (*up to 3 dots between two map cells… 3 portals on different link locations. it's fun :D*), **Export/Import JSON**, and a single **Unity World Creator** that digests the JSON and orchestrates the Map Generator per cell — feeding it portal locations so paths terminate at the doorways. That's the world's topology turned into one visual source of truth (the long-standing fix for links scattered across scenes). The AI only tightened the data model; the architecture — two tools, one JSON contract, Unity as the orchestrator — was entirely yours. *(difficulty ×2 — a complete, well-formed pipeline design spanning two tools and a contract, conceived before any code.)*

> **2026-06-23 ·** `spotting` **Corrected the RO connection model — doesn't RO use warp portal pads, not edge seams? · +20***Why:* The AI mislabeled edge seams as the *Ragnarok* style and offered it as an option; you caught the factual error cold — Ragnarok connects maps with **warp portals**, not walk-off-the-edge seams — which killed the edge-seam branch and kept the design on the authentic (and already-built `Portal3D`) model. A clean domain-knowledge correction of the AI's wrong premise. *(difficulty ×1.5 — corrected a confidently-stated mislabel that would have steered the whole travel layer wrong.)*

> **2026-06-23 ·** `process` **Diagnosed why your contribution log keeps going missing — the** `/clear` **habit · +15***Why:* Connected two facts the AI hadn't — *maybe the reason I don't have dev contribution logs for the past days is because I often /clear at moments like this… that causes claude to forget what I have contributed* — correctly identifying that context doesn't survive `/clear`, so any unlogged contribution is lost. A real root-cause on a *process* flaw, and it changed the workflow: log contributions at the moment they happen, not at session end. *(difficulty ×1.5 — meta-level self-observation that fixed a recurring data-loss bug in the project's own tooling.)*

> **2026-06-23 ·** `design` **Conceived the world-map HUD — simulate the grid, screenshot top-view of the whole world · +30***Why:* Saw that the connected-maps pipeline had unlocked a new feature and specified it precisely yourself — *we can just simulate the grid, and place the maps there according to their grid locations. and just do a screenshot top view of the whole world?* That IS the implementation (per-map top-down render → composite by grid cell → atlas), described before any code. Spotting an emergent feature the architecture made cheap, and naming its exact approach. *(difficulty ×1.5 — product instinct + the right technical approach in one breath.)*

> **2026-06-23 ·** `process` **Pushed back on the needless 2-step split — why? let's go direct · +10***Why:* When the AI proposed building a stylized world map first and the screenshots second, you challenged the ceremony — *why do we need to split into 2 steps? we can just go directly to the goal. any reason?* — and were right: the split was risk-aversion, not a real dependency, so we went straight to the screenshot atlas. Questioning a process the AI defaulted to instead of accepting it. *(difficulty ×1 — a fair, correct challenge that cut a needless detour.)*

> **2026-07-01 ·** `architecture` **"Create a centralized function where all the hotkey bindings live" — one input choke point · +50***Why:* Mid-fix, stopped the AI from bolting a typing-guard onto \~20 scattered `Input.GetKeyDown` sites and named the real solution instead — *"create a centralized function where all the hotkey bindings lives. so you wont be having problems like this. guard to central function, apply to all. ez?"* That's the DRY/choke-point principle by instinct: one `Hotkeys` gate means the rule can't be forgotten at a call site and the input backend swaps in a single edit. Turned a symptom-patch into an architecture fix. *(difficulty ×2 — named the pattern the AI missed and converted a 20-site hack into a single source of truth.)*

> **2026-07-01 ·** `process` **Elevated it to a global protocol — and caught the wrong memory scope (twice) · +20***Why:* Recognized the centralization lesson as a **general engineering philosophy, not a project quirk**, and drove it into the global CLAUDE.md rules + machine-root memory — then caught the AI writing it to *project-scoped* memory: *"why didnt you add it to the machine root memory? it is a general philosophy not only project?"* Forced the correct scope so the principle loads in every project, and made the AI articulate the fix (match the memory store to the reach of the lesson). Meta-level correction of *where knowledge lives*. *(difficulty ×1.5 — spotted a scoping error the AI repeated and fixed how the lesson persists.)*

---

## For the AI agent — how to maintain this

**Append to this doc whenever the dev does something worth logging during a session.** Watch for:

- **Diagnosing** a bug / naming the root cause or a sharp symptom (even if phrased as a question).
- **Steering design** — deciding architecture, choosing between options, redirecting an approach.
- **Improving the process** — tooling, automation, memory, workflow.
- **Showing a correct mental model** — a right prediction, a precise question, "wait, shouldn't X…?"
- **Spotting** something wrong the AI hadn't flagged.

**To append:** edit this file directly and add an entry to the Log above, then update **Current standing** (recompute Total XP, Level, any newly-earned Badge). *(This used to name the Tempo Docs tools. Those are retired for this project and must not be used — the docs are plain markdown in this repo now.)*

**Scoring:** use the rubric table + difficulty multiplier. **Every entry MUST carry a one-line *Why:*** explaining what earned the points (the insight / redirect / catch) — this is the whole point of the log; never log a bare title + XP. Keep it specific and **quote the dev** when the phrasing is punchy. **Obey the honesty rule** — don't score things the AI did; if the dev just rubber-stamped an AI proposal, skip it. Round XP to the nearest 5. Be generous about *noticing* contributions but honest about *scoring* them.
---

## Related

- [The Three-Brain Design Process](../0-process/design-process.md) — the workflow most of these contributions are steering.
- [Roadmap to Launch](roadmap-to-launch.md) — the plan several of these entries redirected.
- [Ring System — Current Facts (Trio-Ring)](../2-systems/ring-system-trio-ring.md) — the Trio-Ring entry's outcome, shipped.
- [World Creator — Spec](../3-world/world-creator-spec.md) — the pipeline the 2026-06-23 entry designed.
