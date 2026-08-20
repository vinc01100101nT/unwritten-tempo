---
name: unity-mcp-bridge
description: "The live connection to the Unity Editor is the Unity CLI (unity status / unity cmd / the unity-editor-mcp tools) — the old CoplayDev HTTP bridge was retired 2026-08-20"
metadata:
  node_type: memory
  type: reference
  originSessionId: d73f3fc5-ee96-47da-a443-058d9e14cd4f
---

**Superseded 2026-08-20.** The CoplayDev HTTP bridge (`com.coplaydev.unity-mcp`, `http://localhost:8080/mcp`) is
RETIRED — removed from `.mcp.json` and its server stopped. It had been dead for five days with nobody noticing,
and I worked a whole session believing I had a connection I did not have. **Never assume the link is up; check.**

The live connection is now the **Unity CLI**, set up and proven working 2026-08-20:

- **Binary:** `~/.unity/bin/unity` (v1.0.0-beta.5, ships inside the Unity Hub app bundle; `~/.zshrc` sources
  `~/.unity/env` to add it to the PATH). A NON-interactive shell may not load that — `which unity` came back empty
  twice and I twice told the user it wasn't installed. **Use the full path when a bare `unity` is not found.**
- **In-editor half:** the `com.unity.pipeline` package (0.5.0-exp.1) in `unity/Packages/manifest.json`. Without it
  the CLI SEES the editor (project, PID, version) but cannot talk to it — `unity status` prints an empty table.
- **As tools:** `.mcp.json` registers `unity-editor-mcp` (stdio, `unity mcp --project-path unity/`), so the editor's
  ~143 commands arrive as ordinary tools.
- **HOW to use it:** Unity's own skill at `~/.claude/skills/unity-cli/SKILL.md` loads every session. Read it; don't
  re-derive the command surface.

**Is it connected?** `unity status` → a row with state `ready`. Empty table = the editor isn't attached, usually
because the package hasn't been imported yet (Unity imports it when the user focuses the window).

**Proven by measurement, 2026-08-20:** read the live scene tree, read the console clean, read compile state, and
captured a scene-view screenshot I actually looked at. **Reading is proven. MUTATING is NOT** — the first person to
change something through it should verify and say so.

**Measured gotcha:** `capture_scene_view`'s `save_path` is IGNORED — it always returns the PNG inline as base64.
Decode it yourself; don't report a path it never wrote.

Still true: Unity must be OPEN with the project loaded, and the forced-recompile ban stands (see
[[act-editor-actions-via-mcp]] and the project `CLAUDE.md`) — its speed on the CLI is UNMEASURED.
