# AI guardrails & automation — current state + roadmap

How this project steers the AI. Two kinds of layers: **soft** (advice loaded into
context — the model *can* ignore or forget it) and **hard** (harness machinery that runs
no matter what the model decides). Goal: move the important rules from soft → hard.

---

## What we have today

### Soft layers (advisory)
- **Root `CLAUDE.md`** (`~/.claude/CLAUDE.md`) — global personal rules (effort protocol, terse, confirm destructive).
- **Project `CLAUDE.md`** (`./CLAUDE.md`) — project layout, standing rules, domain doc index.
- **Nested `CLAUDE.md`** (`unity/Assets/Scripts/CLAUDE.md`) — folder-scoped rules; auto-loaded only when editing that folder.
- **Root + project `MEMORY.md`** (auto-memory) — persistent notes across sessions.

### Hard layers (enforced)
- **PreToolUse hook — the perf-gate** (`.claude/settings.json` → `tools/hook-analytics/perf_gate.py`).
  Blocks edits to runtime C# (`unity/Assets/Scripts/**/*.cs`, excluding `Editor/`) until
  `PERFORMANCE.md` has been read this session. This is the ONLY hard enforcement we have.

---

## What's missing (roadmap, highest leverage first)

1. **Permission deny-rules** (`.claude/settings.json` → `permissions.deny`).
   The bluntest guardrail — absolute "never touch X", no model judgment.
   Candidates: lock generated/baked scenes, `_Recovery/`, screenshots, etc.

2. **PostToolUse hook** — run *after* an edit. Candidates:
   - auto-format / auto-compile-check on `.cs` edits
   - nudge `/perf-checkup` after adding a per-frame/per-hit system
   - remind to update `Editor/TOOLS.md` when a `Tools ▸ unwritten` menu item changes

3. **Other hook events** — `UserPromptSubmit` / `SessionStart` (inject standing context
   each turn / at startup), `Stop` (end-of-turn checklist).

4. **Audit existing automation** — map `.claude/commands/` + skills (`perf-checkup`,
   editor tools) so we know what we already have before adding more.

5. **Subagents** (`.claude/agents/`) — none defined yet; specialized personas w/ own tools/model.

---

## Hard-won gotchas (don't relearn these)

- **Hooks load once, at session start.** Restarting the Tempo *app* ≠ restarting the
  *conversation*. After changing hook config, start a **new conversation** (or run `/hooks`
  in the CLI) to load it.
- **This Claude build only honors `exit 2` to block** a tool call — JSON-on-exit-0
  (`permissionDecision: deny`) is silently ignored. `deny()` must `sys.exit(2)` + write to stderr.
- **The transcript is JSONL** — file content read by the agent lands with `"`→`\"` escaping
  and mixed UTF-8/cp1252 bytes. Any "did the agent read X?" check must compare a
  **letters-and-numbers fingerprint**, not a raw substring.
- **Keep hooks in checked-in `.claude/settings.json`** (portable `$CLAUDE_PROJECT_DIR` path),
  not gitignored `settings.local.json`, so every session + every teammate gets them.

---

## Next session: start here
Pick from the roadmap above. Likely first move: add a small `permissions.deny` list and
one `PostToolUse` formatter/compile-check hook, then audit existing commands/skills.
