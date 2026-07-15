---
name: tempo-docs-no-markdown-tables
description: Tempo docs DROP markdown tables on the next edit round-trip — use bullet lists instead
metadata: 
  node_type: memory
  type: reference
  originSessionId: 41f6a2ce-05c4-4cb8-aed0-15c3ba6f6c7e
---

When writing Tempo docs via `docs_set_markdown_body` / `docs_edit_markdown_body`, a GFM **table** renders on the first write but gets **silently dropped on the NEXT edit** of that doc (the TipTap/YJS round-trip doesn't preserve it). Hit 2026-06-18 on the [[class-system-design]] doc — the roster table vanished after a later `docs_edit_markdown_body` touched a different section.

**How to apply:** don't use `| col | col |` tables in docs you'll edit again. Use bullet lists with `·`-separated fields instead (e.g. `- **Name** — a · b · c → x`). If a table is already there, expect it gone after any edit and restore it as a list.
