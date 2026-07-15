---
name: project-memory-in-repo-junction
description: This project's Claude memory lives IN the repo at .claude/memory/, linked from the harness path via a junction/symlink.
metadata:
  type: project
---

This project's Claude Code memory is version-controlled **inside the repo** at `.claude/memory/`
(committed, pushed to GitHub) — it does NOT live only in the home folder like the default.

**How it works:** Claude Code always reads/writes memory at its per-machine harness path
`~/.claude/projects/<encoded-repo-path>/memory/` (the path is the repo's absolute path with every
`:` `\` `/` turned into `-`, e.g. `D--tempo-projects-roguelike-mmo`). On this machine that path is a
**directory junction** (symlink on mac/linux) pointing at the repo's `.claude/memory/`. So a new
session that writes a memory file writes to "its" harness path as usual, but the bytes land in the
repo folder and show up as an uncommitted change ready to commit — one folder, two names, no copy,
no drift.

**Per-machine setup (the link is NOT stored in git):** after cloning on any machine, run once:
- Windows: `powershell -ExecutionPolicy Bypass -File ./scripts/link-memory-windows.ps1`
- mac/linux: `./scripts/link-memory-mac.sh`

The scripts derive the harness path from wherever the repo was cloned, back up any real folder
already there, and are safe to re-run. If the script has NOT been run on a machine, that harness
path is a plain empty folder again and memory writes will NOT reach the repo — so re-linking per
machine is required. Global CLAUDE.md + global `~/.claude/memory/` are a separate machine-wide
concern and were intentionally left out of this repo. See [[learn-protocol-persist-to-memory]].
