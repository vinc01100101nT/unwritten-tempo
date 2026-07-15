---
name: stat-distribution-system
description: RO-style stat-point distribution (STR/AGI/VIT/INT/DEX/LUK) — the FLAT layer under the skill % layer; built 2026-06-18
metadata: 
  node_type: memory
  type: project
  originSessionId: 8576e31b-0aae-4065-b86a-1663b87add7c
---

The **flat stat layer** the skill doc reserved ("flat lives on level growth, stat-point distribution, equipment"). Built 2026-06-18 in the same session as the skill MVP — RO-flavored, simplified for MVP.

**Model:** `enum PrimaryStat { STR, AGI, VIT, INT, DEX, LUK }` on `Character` (`int[] primary`, each starts 1). `StatPoints` = `startingStatPoints` (9) + `statPointsPerLevel` (3) per level. `AddStat(s)` spends 1 point (permanent — **no respec yet**; **1 pt per stat, no RO escalating cost** = MVP simplification).

**Mapping (tunable consts at top of Character.cs):** STR→ATK · VIT→HP(+8 each)+DEF · INT→MP(+5)+M.ATK+M.DEF · AGI→ASPD+Evasion · LUK→Crit(+a little ATK/M.ATK) · DEX→`CastSpeedMult`(+a little ATK). Contributions fold INTO the flat layer (inside `Scale`), so the [[skill-system-mechanical-model]] passive % multiplies on top: `Effective = (base + levelGrowth + statContribution + gear) × (1 + passive%)`.

**UI:** `StatPanel.cs` (runtime, on GameUICanvas, added by `UIShellBuilder`) — 6 rows + `[+]` buttons + "Stat Points" counter + live derived-stats readout, built into CharacterPanel/Content. It disables the old static "Stats" text and subscribes to OnStatsChanged **+ OnHealthChanged + OnManaChanged** (the last fixed a bug where the C-panel text went stale during MP regen). `Player3DCaster` cooldown ÷ `CastSpeedMult` (DEX = faster casting).

⚠️ **Re-bake = Build UI Shell** (StatPanel into the prefab). Character/caster = code-only. Runtime-only (resets each session — no save). NEXT: persistence, respec item, balance/tuning.
