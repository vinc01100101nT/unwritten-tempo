---
name: creation-stat-hexagon
description: Starting stats are chosen on a hexagon at character creation (user decision 2026-09-29) — pairs sum to 10, spends exactly the 48 starting points; rule lives once in UnitStatMath, applied by the map server on first entry.
metadata:
  type: project
---

**Decided by the user 2026-09-29:** "the hexagon must consume the points … starts with hexagon stats and zero remaining points."

- **Rule, one owner:** `UnitStatMath.IsCreationSpread` — six stats by `PrimaryStat` index, each 1–9, opposite pairs STR/INT, AGI/LUK, VIT/DEX (`CreationOpposite`) summing to 10. Below 10 a point costs 2, so every legal spread costs 2 × 24 = 48 = `StartingStatPoints`. Pinned by `CreationSpreads()` in `tools/game-math-tests/Checks.cs` (all 729 spreads).
- **Path:** client `StatHexagon` → `AccountClient.CreateCharacter(..., primary, ...)` → charserver `POST /characters` checks only SHAPE (six ints 1–9) and stores `primary` in the never-played progress doc (`Progress.CreationKeys` admits it) → map server `PlayerFighter` never-played branch calls `Character.StartWithCreationStats`, which re-checks the rule and reconciles points (→ 0). An illegal spread is logged and the character starts on all-1s + 48.
- **Why the charserver doesn't check pairs:** separate C# project with no access to game code; the rule is not written twice ([[single-source-of-truth]]).
- `UnitStatMath.EvenCreationSpread()` (all 5) is the hexagon's start and what `AgentLink`'s `create` sends.
- **Judgement call to revisit if challenged:** the six numbers travel player → server. They are the player's CHOICE (like a colour), fully validated, not an amount the server trusts — see RULE NOCLIENTMATH in the project CLAUDE.md.
