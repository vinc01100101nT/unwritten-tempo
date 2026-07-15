---
name: affix-not-passive-terminology
description: "Official term for the trio-ring's stat-bonus entries is \"AFFIX\", not \"passive\". Reserve \"passive\" for genuine triggered/conditional abilities."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 268be7fc-b023-4190-8064-f39e0fff5b88
---

Decided 2026-06-29: the trio-ring entries (the % stat multipliers in [[skill-system-mechanical-model]]) are called **AFFIXES**, NOT "passives". An **affix = a stat modifier entry** (ARPG vocab, à la Diablo/PoE); in unwritten they're % multipliers the player **drafts** (not randomly rolled — "rolled/random" describes acquisition, not the word). Reserve **"passive"** strictly for real triggered/conditional abilities (e.g. "on crit, heal") added later.

**Why:** "passive" implies a conditional/triggered skill; the trio ring just grants flat % stat scaling — that's an affix. Affix already fits the game's ARPG/RO vocabulary (elements, leech, rarity) and the multiply nuance is fully covered by gaming usage despite the literal "append" etymology.

**How to apply:** say/write "affix" for trio-ring stat-bonus entries everywhere going forward (docs, issues, code identifiers, UI); don't call them passives. Old `PassiveRanks.cs`/`percent-passive` naming predates this — rename toward "affix" when those files are next touched.
