# CLAUDE.md — roguelike-mmo (unwritten)

## Project layout

- `unity/Assets/Scripts/` — all game C# (Unity 6 + URP); see `unity/Assets/Scripts/CLAUDE.md`
- `unity/Assets/Scripts/Editor/` — one-click editor tools under `Tools ▸ unwritten ▸ …`
- `unity/Assets/Scripts/Editor/TOOLS.md` — canonical catalog of all editor menu items (\~39 cmds)
- `tools/` — HTML/Python authoring tools (world-creator, map-painter, chatgpt extractor)
- `tempo/` — auto-generated canvas dev server; do NOT write app code here

---

## Domain docs (read on demand — do NOT load eagerly)

| Domain | File | One-liner |
| --- | --- | --- |
| Performance rules | `unity/Assets/Scripts/PERFORMANCE.md` | Required before any runtime C# edit |
| Perf history | `unity/Assets/Scripts/PERF_LOG.md` | Past checkup results |
| Editor tools catalog | `unity/Assets/Scripts/Editor/TOOLS.md` | All `Tools ▸ unwritten` menu items |

---

## Standing rules

- **User is new to Unity, strong in JS/TS** — explain C#/Unity by analogy; give click-by-click UI steps
- **No Play mode without being asked** — hand off play-testing to the user
- **Prefer editor tools** over manual editor steps; extend `Tools ▸ unwritten ▸ …` for new one-click ops
- **Enemy term is "mobs"** — never "bestiary"
- **Bake at author time** — static/never-changing work belongs in generate/editor phase, not Play
- **No comments unless WHY is non-obvious** — well-named identifiers carry the WHAT
- When adding/removing a `Tools ▸ unwritten` menu item, always update `Editor/TOOLS.md` in the same change