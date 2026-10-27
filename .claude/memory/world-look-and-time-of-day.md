---
name: world-look-and-time-of-day
description: How the world's colours are made (post-processing, the light look, the fixed fill, the time-of-day screen tint) and what the pack's flower cards and plant shader can and cannot do — settled with the user 2026-10-28
metadata:
  type: project
---

Settled with the user 2026-10-28, each measured in Play:
- **Post-processing was never on until 2026-10-28**: the renderer (`Spike3DRenderer.asset`) had no post-processing data, so every colour profile did nothing. It is on now.
- **The look** (`IdyllicLook_PostProcessing.asset`): no warm white balance, no channel mixer, no split toning, neutral tone setting, contrast 5, saturation +15, white bloom, vignette 0.15. The copied demo warmth turned pink into red-orange.
- **Players' sliders** (Settings ▸ Graphics: Brightness, Contrast, Saturation, Fog thickness) ADD to the look; 0 = the look as made. `LookAdjustments` only touches a profile that sets those values itself.
- **Time of day = ONE screen tint** (a runtime global volume with only a colour filter, built by `DayNightLight`), the user's design: "keep their colors, and just use a masking layer". White whenever daylight ≥ 0.5, warm only in a narrow band at sunrise/sunset (band 0.15, strength 0.35), night tint (0.34, 0.42, 0.60). The sun, the three-direction fill, the sky and the billboards keep their midday values at every hour. Never tint lights per hour again.
- **The pack's plant shader lights itself** through gradients whose dark end is 25–70% grey: dimming lights cannot darken plants; only the screen tint can.
- **Meadow flowers are flat cards** of side-view bunches (8 triangles); only 26% of a card is petals, so from the top-down camera a pink card averages peach (166, 129, 69) vs petals (222, 100, 160). The pack's single flowers face up but are 60 triangles; at equal cost they are 13% as many (sparse). The user kept the cards (2026-10-28). Grass no longer grows inside meadow patches.
- Each meadow patch is ONE colour — a yellow meadow is yellow, not pink gone wrong.

Related: [[world-builder]], [[seamless-world-ground-on-server]].
