---
name: rain-scatter-phase-precision
description: Rain drops stop scattering after minutes — unbounded scroll phase overflows float32 precision in the shader hash
metadata: 
  node_type: memory
  type: reference
  originSessionId: 6f730a4a-a90d-4809-9419-d805e6d71092
---

**Symptom:** Screen-space rain (`RainScreen.shader` + `RainOverlay.cs`) starts with drops at random x positions, but after some minutes (no fixed duration) the scatter "turns off" — drops collapse into fixed, evenly-spaced vertical columns.

**Root cause (measured, not guessed):** `RainOverlay.LateUpdate` advanced the scroll clock forever (`phase += Time.deltaTime * fallSpeed`). The shader builds each drop-cell from `cell.y = floor(uv.y*rows + _Phase*speed)`, so cell.y climbs into the tens of thousands. Inside `hash21`, `frac(p.y * 456.21)` loses ALL fractional precision once the product passes float32's ~8M integer-exact ceiling → the per-row random freezes → dropX stops changing as the grid scrolls. Offline float32 replay: distinct dropX over 40 consecutive rows decayed **40 → 15 → 4 → 2 → 1** across ~0→900s (gradual, hence no exact duration).

**Fix (two parts):**
1. `RainScreen.shader` `sheet()`: wrap the per-sheet scroll AND `cell.y` into an integer `PERIOD = 1024` before hashing, so hash inputs stay small/precise. Grid is exactly periodic → bit-seamless, no pop. 1024 gives worst-case 24 distinct-x/40-rows with a 34–85s per-sheet loop (incommensurate across 4 sheets → no visible global repeat).
2. `RainOverlay.cs`: `phase = Mathf.Repeat(phase + Time.deltaTime * fallSpeed, 10240f)` — caps the C# accumulator so it never hits float's 2^23 ceiling (~1000h continuous rain) where per-frame increments round to 0 and rain freezes mid-fall. 10240 = 10×1024 and 10240×speed is a multiple of 1024 for all four sheet speeds (1.5/1.1/0.8/0.6), so the wrap is seamless.

**Gotchas:** the clock is NOT the randomness source — it's the falling *motion*; you can't remove it, only bound it. Perf unchanged: fixed-cost full-screen pass, zero raindrop objects, zero GC, off entirely when dry. `density` (160) is the GPU lever if ever needed.
