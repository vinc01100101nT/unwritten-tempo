---
name: skill-tooltip-voice
description: "How to write skill descriptions in the wiki/docs — clean in-game tooltip voice, not article prose"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9e25e105-8fe5-4d69-8675-77e7d79101cd
---

Skill descriptions (wiki `wiki/content/*.json` block docs, roster docs) must read like an **in-game tooltip**, not a website article or a person casually explaining mechanics.

**Why:** Vince rejected article-voice descriptions three times — prose paragraphs, rationale clauses ("so the more you build, the harder it hits"), casual narration ("you still take the damage, but…"), and designer meta-commentary all break the in-game feel.

**How to apply:**
- In the wiki, skills render as **cards** via a `skillTable` block (`skills:[{name,icon,cost,flavor,effect}]`, subtitle optional). One evocative *flavor* line + a terse mechanical `effect` string; `cost` compressed like the game card.
- Effect = **declarative rules, third person** ("Reduces damage taken by 50% for 5s", "Each basic attack grants +1 Momentum"). NOT "you"-narration, NOT explaining interactions casually.
- **Bold the numbers** (mirrors the game card's gold-highlighted values in `SkillTooltip.cs`).
- Cost cell compressed like the real card: `MP · CD · Cast · Range`. Untuned skills → `*in tuning*`, never invented numbers.
- **Cut designer/stress-tester meta-commentary** — e.g. "no cap" (a player never assumed a cap existed), "deliberate", rationale. If a mechanic is covered by another skill's tooltip (e.g. Momentum's standing bonus), don't re-explain it here.
- **No raw HTML** (`<br>` prints literally in the wiki renderer) — use separate columns instead of forced line breaks.

Ground the format against the real in-game renderer `unity/Assets/Scripts/SkillTooltip.cs` (flavor italic + bulleted effect lines + `MP/CD/Cast/Range` cost line). Reference the finished Sword pool in `wiki/content/sword.md`. See [[peer-to-peer-gpt-replies]] for the related "match the target medium's voice" pattern.
