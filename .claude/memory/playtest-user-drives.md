---
name: playtest-user-drives
description: Default = the USER runs in-game play tests; only enter Unity Play mode / drive the game via MCP when explicitly asked
metadata: 
  node_type: memory
  type: preference
  originSessionId: d73f3fc5-ee96-47da-a443-058d9e14cd4f
---

Set 2026-06-25. **The user runs play tests by default.** Do NOT enter Unity Play mode, puppeteer the game, or capture in-game frames via the [[unity-mcp-bridge]] on my own initiative — even to verify my own change.

- After a code change, hand off to the user to play-test rather than auto-entering Play mode. State what to look for.
- Only `manage_editor play` / drive combat / screenshot the running game when the user **explicitly asks** ("you test it", "play it", "drive it", etc.).
- Still fine WITHOUT asking: non-Play editor work via MCP — read Console, recompile/`refresh_unity`, inspect scene/assets, run menu items that don't require Play. The restriction is specifically about *running/observing the live game*.
- Extra reason beyond preference: unfocused Play mode throttles (the focus caveat), so my live tests stall unless the user has Unity foregrounded anyway — another reason the user is the better play-tester.
