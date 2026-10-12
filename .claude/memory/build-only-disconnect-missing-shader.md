---
name: build-only-disconnect-missing-shader
description: "A built game (not the editor) dropped the connection on every Thousand Petals dash: Shader.Find of a shader no saved material uses returns null in a build, and a throw inside a Mirror message disconnects."
metadata:
  node_type: memory
  type: project
---

**Symptom (2026-10-12):** in the patcher-installed build only, Thousand Petals disconnected the player for a few seconds,
then the client's retry brought them back. Editor Play never did it. The server log showed only a bare "player left".

**Measured in the client's Player.log:** `Disconnecting connection ... handling a message of type Mirror.RpcMessage caused
an Exception ... ArgumentNullException: shader` at `Afterimage.EnsureMaterial` ← `Player3DController.ShowLook` ←
`BeginGlideTo` ← `NetPlayer.TargetMove`. `Shader.Find("Universal Render Pipeline/Unlit")` is null in a build because
no saved material uses that shader; the editor holds every shader, so it worked there.

**Fix:** `Afterimage` uses `Sprites/Default` (built-in, on Always Included Shaders), tint on `_Color`, and Capture
returns early when the material cannot be made.

**Lessons:**
- Any runtime `Shader.Find` must name a shader on Project Settings ▸ Graphics ▸ Always Included Shaders (ASSETS.md
  rule 12), now enforced by `tools/check-runtime-shaders.py` (run by the end-of-turn gate, proven by
  `tools/prove-runtime-shaders-check.py`).
- Mirror drops the connection when a message handler throws. A "disconnect" with nothing on the server is an
  exception on the CLIENT — read `tools/platform.sh player-log` first.
- Build-only bugs: the editor is not evidence. Ask for Player.log.
