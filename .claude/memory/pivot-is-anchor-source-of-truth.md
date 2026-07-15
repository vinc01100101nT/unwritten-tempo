# Pivot is the ONLY source of truth for an object's base anchor

Decided 2026-07-01. From now on, an object's ground/base anchor = its **PIVOT** (`transform.position`), NEVER the mesh bounds bottom / lowest vertex.

- **Rule:** to seat a prop on the ground, snap the PIVOT to the ground hit — assume "pivot = base". Do not compute mesh-min / bounds-bottom and offset by it.
- This is what `Drop Selected to Ground` (`RegionBuilder.cs:188–193`) already does: raycast down from the pivot, set `transform.position.y = hit.point.y`. It ignores mesh bounds entirely.
- The bounds-based path (`Drop to Ground` → `PropPlacement.DropToGround`, seats mesh-bounds-bottom on ground) produces the "perched / lip exposed" look and is NOT the standard anymore.
- The map generator was fixed to seat by pivot (`seatByPivot` for solid props) so freshly generated maps match the hand-placed "planted" look.
- Why it works artistically: props are authored with the pivot slightly above the lowest vertex (foundation lip hangs below pivot), so snapping pivot to y=0 sinks the lip → buried → looks planted. That's the intended look.

⚠️ When adding any new "seat on ground" / placement logic, base it on the PIVOT, not bounds.

**Extended to billboards 2026-07-03:** the old "mobs are centre-pivoted" carve-out is GONE — player+mobs are now FEET-pivoted too (sprite renders up, collider stacks up, vertical offsets via `UnitAnchor`). See [[billboard-feet-pivot-unitanchor]].
