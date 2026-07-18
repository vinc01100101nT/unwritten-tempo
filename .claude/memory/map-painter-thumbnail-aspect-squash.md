---
name: map-painter-thumbnail-aspect-squash
description: Painter prop thumbnails were rendered SQUARE (long axis + 1.06 margin) but stretched into the non-square footprint rect — elongated props squashed/shrunk.
metadata: 
  node_type: memory
  type: project
  originSessionId: a757d62d-6795-442b-9ea0-dcd770218a18
---

**Symptom:** In the map painter, elongated props drew squashed and undersized (House_D 2.00×1.08u appeared as a ~1.9×0.56u flat bar); near-square props looked fine.

**Root cause (measured by decoding the PNGs):** `BrushPaletteWindow.RenderThumbnail` rendered every thumbnail as a 128×128 SQUARE covering `max(fx,fz)*1.06` world units per side — the prop centred with empty margin along its short axis (House_D content bbox 120×66 px). The painter (`drawObject`) stretches the whole image into the prop's exact `fx×fz` footprint rect, applying the footprint aspect a second time → double-squash + shrink by `fx/M, fz/M`.

**Fix:** render the thumbnail viewport at the footprint's aspect with ZERO margin so image edge == bounds edge: non-square pixel rect (`ThumbPx` on the long axis), `orthographicSize = fz/2`, `RenderOnce(pru,bg,w,h)`. Painter unchanged — it now maps 1:1. Thumbnails are baked: fix shows only after re-running Brush Palette ▸ Export Manifest with thumbnails ON.

**Gotcha:** exporting the manifest with "Render prop thumbnails" UNTICKED writes `thumb:""` for every prefab AND ground brush — the painter silently falls back to flat colour blocks. Always export with thumbnails on after art changes. See [[map-painter-ground-density-mismatch]].
