# Single source of truth — one owner per value, everyone else READS it (HARD-PINNED principle)

**User directive 2026-07-03** (emphatic): every meaningful value must have exactly ONE authoritative owner; every other place that needs it READS from that owner and NEVER keeps its own parallel hand-synced copy. Pin this everywhere and apply it proactively across the whole codebase, not only where asked.

## The rule
- A gameplay number / config / state / derived fact lives in ONE place (its owner).
- Everyone else references or computes from it — no second editable copy.
- Two editable fields holding "the same" number in two windows/files is the bug **even if they never collide at runtime**: the moment they drift, the reader lies.
- Data-shaped sibling of the centralization rule: centralization = one code PATH per concern; single-source = one STORAGE location per value.

## How to fix a duplicate when caught
1. Pick the real owner (the authoritative side).
2. Make the other side a **read-only mirror** or a computed lookup.
3. **Label** it as a mirror so the next reader can't re-fork it.

## Signs you're violating it
- "keep X and Y in sync"
- a preview/editor field that copies a runtime/authoritative field
- the same constant typed in two places

## Canonical example (what triggered this) — radius
The **AoE radius was authored in TWO windows**: `SkillTickBinding.radius` (Skill Composer, the real gameplay hit radius) AND a hand-typed `radiusRingR` float in the **Vfx Composer** preview ring. Not a runtime collision (the Vfx one is preview-only), but a hand-synced copy = the confusion.

**Fix:** `VfxComposerWindow` no longer holds its own gameplay radius. `RefreshSkillRadii()` scans all `SkillDef`s whose `vfx == sel` (the previewed sequence) and reads every Area-scope `binding.radius`; the preview draws one ring per distinct authoritative value (pooled `LineRenderer`s). Toolbar shows a read-only "⟵ skill 1.5, 3.0" mirror. A hand-typed `manualRingR` remains ONLY as a plain measuring ruler when no skill references the sequence yet. Re-reads on `OnFocus` + `Select`.

Related canonical: **Pivot = base anchor** (also called "single source of truth" in project CLAUDE.md) — seat props by snapping the PIVOT, never mesh-bounds bottom.

## Where it's pinned
- Global `~/.claude/CLAUDE.md` → "Single source of truth" section
- Project `CLAUDE.md` → standing rule bullet (with the radius example)
- This memory file
