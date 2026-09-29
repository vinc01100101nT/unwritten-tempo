---
name: creation-preview-ink-lines
description: "Character-creation preview ink outline was too heavy, then missing — three separate causes, fixed 2026-09-29"
metadata:
  node_type: memory
  type: project
  originSessionId: 2c09345b-c771-4ed3-8c35-2e06604f4599
  modified: 2026-09-29T02:35:39.038Z
---

The character-creation preview (CharacterPreview.cs) uses the game's own ink filter (HandDrawnFX); it has no copy of it. Getting it to look like the world took three fixes on 2026-09-29, all confirmed by the user on screen:

1. **Picture size.** The render picture was sized once at open, from a canvas scale that had not settled yet (120x171 while the box was 186x265), and was then stretched up. Now FitPicture resizes it to the box's real pixel size, re-checked every frame and remade only when the size changes.
2. **Ink switched off on the front screen.** GameRoot.Quiet() deactivates every Systems-scene root behind sign-in/character select, HandDrawnFX included, so the preview had no ink at all. Whether it showed depended on how the screen was reached. Quiet now skips the HandDrawnFX root; the front camera renders no layers (cullingMask 0), so it inks only the preview.
3. **View per pixel.** The ink compares depth a fixed number of PIXELS apart (thickness 2.19), so its line count depends on tan(fov/2)/pixel height. World camera: 35° over 972 px. Preview: 30° over 265 px, 3.1× more view per pixel, which drew a grid of crease lines. MatchWorldView copies the world camera's view per pixel (found once by the MainCamera tag, including inactive, because Camera.main is null while quieted) and backs the camera off along its line of sight to keep the framing.

**How to apply:** any second camera meant to show the world's ink look needs all three: picture size equal to its on-screen box, HandDrawnFX awake, and the world camera's view per pixel. See [[window-headings-and-status-box]] for the same screen's window work.
