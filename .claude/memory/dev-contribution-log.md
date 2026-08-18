---
name: dev-contribution-log
description: "Maintain the gamified \"Dev Contribution Log\" Tempo doc — append + score the dev's real contributions each session"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 7e46c3f0-a8b6-4991-952c-5c03f2890bdc
---

The dev (vince) wants a **gamified record of HIS own contributions** (not the AI's), to prove he's genuinely directing the work and learning the *systems*, not "pure vibe coding." Lives as a Tempo doc: **"🏆 Dev Contribution Log — unwritten"**, docId **`q17a89053cf2xqjadkyk7agjb988nvgp`** (Docs tab). The doc holds the scoring rubric, level ladder, badges, current standing, and a log table — read it for the canonical rubric.

**Why:** he's new to C# and wants accountability that he understands the system. This is his motivation loop — treat it as a real feature, keep it accurate and a little fun.

**How to apply:**
- **During every session, watch for real dev contributions** and append a Log row: 🐛 bug diagnosis / root-cause (even phrased as a question), 🧭 design direction or choosing between options, ⚙️ process/tooling/automation/memory improvements, 🧠 correct mental model / sharp question, 🔍 spotting something the AI missed.
- To append: `docs_get_markdown_body(docId)` → grab `expectedSeq` → `docs_edit_markdown_body` to add the entry AND update the **Current standing** block (recompute Total XP, Level, new badges). Use the rubric + difficulty multiplier (×1/×1.5/×2) in the doc; round XP to nearest 5.
- **Entry format (required):** each log entry MUST carry a one-line **`*Why:*`** explaining what earned the points (the insight / redirect / catch) — never log a bare title + XP. Shape: `**YYYY-MM-DD · <category> Short title · +XP**` then a `*Why:* …(difficulty ×N)*` line. Quote the dev when the phrasing is punchy.
- **Honesty rule (important):** score the DEV's input only. If the AI proposed something and he just said "ok," don't score it. Diagnosing, redirecting, choosing between genuine options, pushing back = scored. Be generous about *noticing*, honest about *scoring*.
- Mention notable XP/level/badge gains to him in the reply when they happen (it's the reward).
- As of 2026-06-14 he's at **400 XP, Level 3 (Builder)**.
