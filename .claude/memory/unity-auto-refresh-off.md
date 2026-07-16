---
name: unity-auto-refresh-off
description: Unity Auto Refresh is always OFF in this project — raw FBX/asset moves on disk are safe with Unity open.
metadata: 
  node_type: memory
  type: project
  originSessionId: db4a6bcd-1754-4809-8c2d-7d3c3f116c2b
---

Unity's **Auto Refresh is always OFF** in this project (Edit ▸ Preferences ▸ Asset Pipeline ▸ Auto Refresh unchecked). The user keeps it off permanently.

**Why it matters:** The documented "raw file move can purge an FBX with external deps" hazard only happens when Unity re-imports *live* mid-move. With Auto Refresh off, Unity is NOT watching the folder, so raw filesystem moves/deletes (moving file + its `.meta` together to preserve GUID) are safe **even with Unity open** — no need to make the user close Unity for asset reorg/promotion.

**How to apply:** Don't demand "close Unity" before moving/deleting FBX or other assets. Just do the GUID-preserving raw move (file + `.meta`). Afterward the user brings Unity into focus and presses **Ctrl+R** (Assets ▸ Refresh) to re-import at the new paths. Still never call `refresh_unity` via MCP (separate ban). Relates to [[changes-must-persist-in-repo]].
