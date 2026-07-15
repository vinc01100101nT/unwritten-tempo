---
name: character-art-quaternius-charcoal
description: "Characters/NPCs are now the Quaternius \"Ultimate Animated Character Pack\" (50+ low-poly, CHARCOAL-BLACK bodies w/ colored hair/clothes + white ink-eyes) — REPLACES Ninja Adventure for CHARACTERS; the charcoal=ink/\"writes the unwritten story\" brand through-line"
metadata: 
  node_type: memory
  type: project
  originSessionId: 2261fe6f-f31a-4adf-8150-c0284aeb419a
---

As of 2026-06-25 the **PLAYER** uses a baked **Quaternius "Ultimate Animated Character Pack"** character (50+ free animated low-poly models, baked to 8-dir billboards via [[bake-8dir-sprites-pipeline]] → `CharacterRoster.asset`). Signature look: **charcoal-black bodies** with colored hair/clothing and small **white ink-like eye/tear marks**. **Ninja Adventure is MOBS-ONLY now** (user directive 2026-06-25) — do NOT make the player ninja. Ninja FX (slash/fireball/shield) + UI/skill icons are still in use; only the player *character avatar* is Quaternius.

⚠️ **Player wiring (2026-06-25):** `Build25DSpike.BuildPlayer` auto-wires the player's avatar via `ApplyDefaultBakedCharacter` → instantiates roster entry **`BaseCharacter`** (fallback: first usable) + `BakedPlayerVisual` riding the `Player3DController` (hides its own SR). Same mechanism as `Tools ▸ unwritten ▸ Player ▸ Use Baked Character` — that's how to swap to a different roster character. **DELETED:** the `Sscary` player path (see removal) AND `Editor/NinjaGreenPlayer.cs` (the "Use NinjaGreen Player" tool) — both put ninja/old art on the player. The controller's `animSheet`/`AnimateRich` 2D path is now DORMANT (nothing wires it; baked visual rides on top). To change the default player character, edit `Build25DSpike.DefaultPlayerCharacter`.

**Why it matters (brand through-line, user's insight 2026-06-25):** the black charcoal body reads like **pencil/charcoal/ink — the medium that writes on paper**, which ties straight into the game name *Unwritten*. This **resolves the brand-vs-game tone gap** I'd flagged: GPT's logo brand is sleek **black + gold minimalist** (see the logo saga — quill/ink → broken skill-ring), and the charcoal characters MATCH it rather than clash. The unifying metaphor across name → logo → [[skill-system-mechanical-model]] (you "write" your build by walking the ring) → character art: **the colored world is the page, the charcoal characters are the ink, the player writes their story across it.**

Leanable hooks (not built, ideas): white ink-eyes echo the logo's glowing path-dots → make the player's skill-path glow the same white/gold; charcoal base = "unwritten," colored hair/clothes = the authored part → could tie cosmetic color unlocks to ring progress. Good splash-art / GPT brainstorm seed.

Pipeline/perf facts (Pxltgr 3D world, billboard baking, etc.) are unchanged — this note is only about the character ASSET swap + its brand significance.
