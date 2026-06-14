# SESSION_STATE — unwritten (Unity 2D)

**Last updated:** 2026-06-14 · **Status:** 🟢 foundation A–F built; Phase G is next.

> Cheap-resume file. Canonical status lives in the **"unwritten — Development Roadmap"** doc (Tempo Docs); this is the quick pointer. On "resume": read this, then the roadmap doc, then the on-disk files in the **real Unity project** at `D:\Unity3d\unwritten\Assets\Scripts\`. ⚠️ There is no longer a `unity/` copy in this repo — edit the real project directly.

---

## Where we are

The playable foundation is essentially complete (Phases A–F). Most recent work (2026-06-14) **reworked movement & combat into Dota-style controls**:
- `PlayerCommander` (new) — order brain: **right-click** move / attack (enemy) / follow (`FriendlyUnit`); **A** then click = attack-click (enemy = attack, ground = attack-move); **S** = stop & cancel everything (incl. un-landed damage) + `onStop` hook for future skill-casts.
- `PlayerController2D` is now a click-to-move **service** (`MoveTo`/`MoveToward`/`Halt`); **WASD removed**.
- `PlayerAttacker` is now a swing **executor** with a `windupTime` — damage lands at the end of the swing, so **S** cancels it.
- `GameCursor` (new) drives a **hardware cursor** from the **Kenney Cursor Pack** (`Assets/Art/KenneyCursorPack`, Outline) — pointer / sword / crosshair. Replaces the old UI `CombatCursor` (deleted).
- Also new: `ClickMarker` (click ripple), `FriendlyUnit` (follow marker), and a **Tools ▸ unwritten ▸ Add Test Ally** menu. All wired by `Setup Mouse Combat`.

(Earlier predecessor work had reworked combat into mouse-target click-to-attack with `SlashEffect` / `CharacterAnimator2D.PlayAttack` / `TargetIndicator` — those FX still apply.)

| Phase | State |
|---|---|
| A Engine/hello-world | ✅ done |
| B Character & tiled world | ✅ done |
| C Towns/portals/dialogue | ✅ systems done (town content hand-painted) |
| D Monsters & combat (mouse-target) | ✅ done |
| E UI shell (HUD + panels + drag-equip) | ✅ done |
| F RPG numbers (`Character`) | 🟢 core done — **only the death/respawn screen is missing** |
| G Skill-draft (signature) | ⬜ **next** |
| H Multiplayer | ⬜ deferred |

## Immediate next

**Phase G — the graph-based level-up skill draft.** Port the web prototype in `src/skill-draft/` (`graph.ts`, `SkillCard`, `SkillTree`, `SkillDraftPage`) to Unity: skill graph as ScriptableObjects → `DraftService` (2 guaranteed + 1 wildcard, no-repeat, class reveal) → 3-card draft UI → tree view. Hang it off `Character.OnLeveledUp` and the **Skills** panel (K). Tracked by issues **VIN-1** (epic) + **VIN-3/4/5/6**.

Small loose end if preferred first: Phase F **death/respawn screen** (at 0 HP you currently instant-revive at full HP; `Character.OnDied` already fires).

## Issue board (synced 2026-06-14)

- Done: **VIN-2** (setup), **VIN-7** (player+XP loop), **VIN-8** (Phase B), **VIN-10** (Phase D), **VIN-11** (Phase E).
- In progress: **VIN-9** (Phase C), **VIN-12** (Phase F).
- Planned (Phase G): **VIN-1** epic + **VIN-3/4/5**; **VIN-6** (tree view) in backlog.
- ⚠️ **Cross-artifact links are NOT wired yet** — `link_items` was down (server 500s) during the 2026-06-14 sync. Retry: link VIN-3/4/5/6 → VIN-1, and link VIN-1 + the new milestone issues → the roadmap doc.

## Gotchas / decisions

- Scripts are authored **directly** in the real Unity project at `D:\Unity3d\unwritten\Assets\Scripts\`. The old `unity/` staging copy in this repo was **deleted 2026-06-14** — there is no copy step any more; never recreate it.
- PPU 16 for all art; world content is hand-painted by the user (don't auto-generate).
- Pathfinding deferred — monster chase is line-of-sight only.
