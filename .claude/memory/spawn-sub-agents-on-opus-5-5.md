---
name: spawn-sub-agents-on-opus-5-5
description: Every sub-agent must run on Opus 5.5 (claude-opus-5-5) — user standing instruction 2026-09-23
metadata:
  type: feedback
---

Every sub-agent I spawn runs on Claude Opus 5.5 (`claude-opus-5-5`), never an older model.

**Why:** the user switched the main session to Opus 5.5 on 2026-09-23 and said "from now on, always spawn opus 5.5".

**How to apply:** all eight agent definitions under `.claude/agents/` pin `model: opus`, and on 2026-09-23 `/model opus` resolved to `claude-opus-5-5` — so a spawn that uses the definition's model gets 5.5. When spawning, pass `model: "opus"` (or leave the definition's pin), never "sonnet"/"haiku"/"fable". If a future Claude Code maps the `opus` alias to something else, that breaks this silently — re-check what `/model opus` resolves to before trusting it. Relates to [[the-five-anti-invention]].
