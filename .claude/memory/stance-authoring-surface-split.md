---
name: stance-authoring-surface-split
description: "3-layer cue→StanceId→action→clip chain; Stance Composer owns action+timing/speed/priority, but which ACTUAL clip file is code (Character3DClipMap + MixamoOverride), not author-facing"
metadata: 
  node_type: memory
  type: project
  originSessionId: 02e43484-1632-4cc4-9ae8-2d93ddc14227
---

Answering "does the Stance Composer decide which animation plays for a cue?" — **partly**. The chain has 3 links, and Composer owns only the middle one:

1. **cue → `StanceId`** — gameplay decides (e.g. `PlayerStanceBrain` → `StanceId.Attack`). Code.
2. **`StanceId` → `CharacterAction` + playback** — **Stance Composer** (`StanceLibrary`/`StanceCatalog`): action, play mode, speed source, priority, FitWindow. This is where you tune *how* a stance plays.
3. **`CharacterAction` → the real clip FILE** — **code, NOT the composer**:
   - Quaternius clips: `Character3DClipMap.cs` (the SSOT suffix map, e.g. `Attack → "SwordSlash"`, `Cast → "Shoot_OneHanded"`).
   - Caster's Mixamo swaps: the `MixamoOverride` dict in `Use3DCaster.cs` (e.g. `Attack → "Standing 2H Magic Attack 03"`).

So: **"Attack should use *this* clip"** = edit `MixamoOverride`/`Character3DClipMap` (code). **"Attack should play faster / higher priority / fit the swing window"** = Stance Composer.

**Friction (open):** layer 3 (action→clip) isn't author-facing — clip choice lives in code. Candidate to surface caster's Mixamo/Quaternius clip picks into the Stance Composer (or a small caster asset) so it's one authoring surface.

Same 3-layer map is also recorded in the "Authoring map" section of [[mixamo-clip-rebind-to-model-paths]]. See also [[character-3d-visual-system]], [[stance-system]].
