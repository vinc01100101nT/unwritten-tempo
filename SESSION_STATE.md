# SESSION_STATE — resume here (keep this SHORT; git holds history)

_2026-06-20 · branch `experiment/3d-world` · last commit `e9f2932`_

## Protocols (full text in `~/.claude/.../memory/`)
- **Effort + model**: defaults **Sonnet 4.6 + Medium**. Assess each task first for BOTH effort + model; auto-GO on Low/Medium fitting the model; PAUSE for >Medium OR higher tier until user sets dials + GO. Tag each task reply with bold first line `**EFFORT: X · MODEL: Y — …**`. Opus 4.8 for hard/ambiguous; model switches bust the cache (don't micro-toggle).
- **Checkpoint**: OVERWRITE current state (never append history — git holds it); on milestones + before `/clear` only; keep short.

## ✅ Done (verified "it works"), UNCOMMITTED
Single-target aim-first skill:
- NEW `UnitTargeter.cs` — single-target reticle (analog of `GroundTargeter`): hover ring green/red, left-click locks+fires, Esc/right-click cancels, over ground auto-targets nearest mob to cursor.
- NEW `Player3DSoulStrike.cs` — Mage skill slot 3 / key 4; aim → confirm → `Projectile3D`, 2.6× magic dmg + 35% stun.
- EDIT `Player3DController.cs` (suppress move while `UnitTargeter.Active`), `GameCursor3D.cs` (64×64 `targetCursor`, ForceSoftware), `Editor/Build25DSpike.cs` (wire skill + cursor).
- In-game: Setup Systems Scene → Save → Play → Mage (P panel) → key 4.

## ⏳ TODO
1. **Commit** the above. Msg: "Aim-first single-target skill (Soul Strike) + UnitTargeter + 64px reticle cursor" + `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
2. **Write `unity/Assets/Scripts/PERF_LOG.md` entry** (newest on top): gameplay CLEAN — Hierarchy `UpdateScene` ≈1.4 KB/frame flat (34 mobs); PerfHud "4.5 MB/s" was editor/Profiler inflation. New systems alloc-free. Recovery note: "No cameras rendering" → re-run Setup Systems Scene.

Resume: re-read this + `git status`. "commit" → #1. "finish perf checkup" → #2.
