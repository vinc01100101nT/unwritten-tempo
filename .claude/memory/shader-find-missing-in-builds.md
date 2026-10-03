---
name: shader-find-missing-in-builds
description: A shader the game looks up by NAME (Shader.Find) is in a build only if it is in Always Included Shaders or a shipped material uses it — the editor always finds it, so Play mode hides the gap
metadata:
  type: project
---

`Shader.Find("Unwritten/…")` returns null in a built game unless that shader is in Project Settings ▸ Graphics ▸
Always Included Shaders or a material the build ships references it. The editor always finds every shader, so the
feature works in Play mode and silently does nothing in every build.

**Why:** on 2026-10-02 the first builds anyone played (Windows and Mac, over the Wi-Fi) had no ink outline, no paper
grade, and character colours did nothing. `InkScreen`, `HandDrawnGrade` and `AtlasRecolor` (used by `CharacterDye3D`)
had never been on the list nor used by any material — measured across every commit of GraphicsSettings. Each build's
Player.log said "Shader 'Unwritten/InkScreen' not found". Added all three; both rebuilt builds contain them.

**How to apply:** when adding a `Shader.Find`, add the shader to Always Included in the same change. To audit: list
every `Shader.Find("Unwritten/…")`, map each name to its `.shader` guid, and check the guid is in
`m_AlwaysIncludedShaders` or in a shipped `.mat`. A built game's `Player.log` "not found" lines are the ground truth.
Related: [[lan-play-plain-http-blocked]].
