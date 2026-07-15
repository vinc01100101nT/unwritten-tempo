# Workflow / tooling / meta (Tempo, MCP, extractors, logs)

- [In-game chat + GM slash-commands](../chat-and-gm-command-system.md) — `ChatController`+`GmCommands`; add via `Register` in `EnsureBootstrap`.
- [Canvas validate false-positives on @/ aliases](../canvas-alias-validator-false-positive.md) — "missing_package @/..." harmless; trust render.
- [Tempo docs drop markdown tables on edit](../tempo-docs-no-markdown-tables.md) — GFM tables vanish on docs_edit; use bullet lists.
- [Reading ChatGPT share links](../chatgpt-share-extractor.md) — run tools/extract_chatgpt_share.py, NOT WebFetch.
- [Unity MCP bridge](../unity-mcp-bridge.md) — `com.coplaydev.unity-mcp` → HTTP localhost:8080/mcp; Unity must be OPEN.
- [Run editor actions YOURSELF via MCP](../act-editor-actions-via-mcp.md) — NEVER tell user to click Tools ▸ unwritten / add a component; `execute_code` the dialog-free `*Core()`; still no refresh_unity.
- [Maintain gamified Dev Contribution Log](../dev-contribution-log.md) — score dev's OWN contributions; doc q17a89053cf2xqjadkyk7agjb988nvgp.
- [YouTube update format template](../youtube-update-format-template.md) — all lowercase, no icons; new assets / weather / next.
- [YouTube devlog publishing spec](../devlog-youtube-spec.md) — start 2026-06-12, day-N calc, title `…day N | game-dev-NN`, unwritten logo card, export/tags/hashtags.
- [Protocol: "what did you learn" = persist to memory](../learn-protocol-persist-to-memory.md) — WRITE it (topic file + index), not just chat.
- [Feature knobs live on their OWN object/asset](../feature-knobs-own-object.md) — dedicated named GameObject (e.g. `WaterFX`), never piled onto the crowded `Sun`.
- [Editor→runtime delegate bridge](../editor-runtime-delegate-bridge.md) — runtime can't name an Editor-folder type; editor fills a `static Func` hook on `[InitializeOnLoadMethod]`, runtime invokes it.
- [Dead-code proof = 2-step scan](../dead-code-guid-scan.md) — code-ref grep AND .meta-guid grep of scenes/prefabs; scan by declared TYPE names (partial classes / multi-type files lie).
- [Dormant-but-KEPT scripts](../dormant-kept-systems.md) — MultiHitBurst (Meteor revival), DialogueUI/InteractPrompt (future 3D press-to-talk); zero refs on purpose, don't re-flag.
- [Editor tool + prefab-instance undo/redo](../editor-tool-prefab-instance-undo-redo.md) — Ctrl+Y silently no-ops without `RecordPrefabInstancePropertyModifications`; RecordObject alone only does undo.
