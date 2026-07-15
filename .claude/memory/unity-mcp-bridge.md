---
name: unity-mcp-bridge
description: "Drive the Unity Editor directly via the CoplayDev MCP bridge (run menu items, read Console, edit scenes) — no window focus needed, but Unity must be open"
metadata: 
  node_type: memory
  type: reference
  originSessionId: d73f3fc5-ee96-47da-a443-058d9e14cd4f
---

Set up 2026-06-25: I can control the live Unity Editor through the **CoplayDev MCP for Unity** bridge.

- **Unity package:** `com.coplaydev.unity-mcp` pinned to `#v9.7.3` in `unity/Packages/manifest.json` (UPM git URL `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v9.7.3`).
- **Claude Code registration:** project-scoped `.mcp.json` at workspace root → server `UnityMCP`, **HTTP transport** `http://localhost:8080/mcp`. The in-editor `HttpAutoStartHandler` auto-starts the server when Unity opens.
- **How it works:** TCP/HTTP socket *inside the Editor process* calling Unity APIs (`AssetDatabase`, `EditorApplication.ExecuteMenuItem`, scene/GameObject APIs). It does NOT simulate mouse/keyboard and does NOT need the Unity window focused/foregrounded — I can work while the user is in other windows.
- **Hard requirement:** Unity Editor must be **open** with the project loaded; bridge can't drive a closed/headless editor.
- **Gotchas:** Unity defers recompile/asset refresh until focus returns (bridge forces `AssetDatabase.Refresh()`); Play Mode can throttle/behave oddly while unfocused. MCP tools only register at Claude Code **session start** — reload the session after first setup to see the `UnityMCP` tools.
- This is the live-control complement to the one-click [[editor-tools-menu]] (`Tools ▸ unwritten`) — I can now invoke those menu items myself instead of asking the user to click.
