# scripts/

## link-memory — Claude Code project memory

The project's Claude Code memory lives in the repo at `.claude/memory/` (versioned,
one copy). Claude Code, however, reads memory from a per-machine path under your home
folder (`~/.claude/projects/<encoded-repo-path>/memory`). These scripts create a
directory link from that per-machine path to the committed `.claude/memory/` folder,
so auto-recall works while the files stay in the repo.

**Run once per machine, after cloning:**

- Windows: `pwsh ./scripts/link-memory-windows.ps1`  (creates a directory junction)
- macOS / Linux: `./scripts/link-memory-mac.sh`  (creates a symlink)

The link is machine-local and is NOT stored in git — that's why every new machine
runs the script once. The scripts derive the target path from wherever you cloned the
repo, so no manual editing is needed. They back up any real memory folder already at
the target before linking, and are safe to re-run.
