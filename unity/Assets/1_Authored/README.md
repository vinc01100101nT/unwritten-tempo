# 1_Authored — the stuff WE curate & place

Our **place-ready** set — made OR promoted by us into something a scene points at.
"Authored" here does NOT mean hand-drawn: it means we curated it. Most of this is
vendor-sourced but processed by us — props baked from vendor kits, materials wrapping
vendor textures, a vendor texture copied out and wired into a material. If a scene
depends on it and we put it there deliberately, it belongs here (not `0_Raw/`).

Browsed when building a map:
- `Prefabs/Props/` — **THE drag-and-drop palette** (what you place in a scene)
- `Art/` — our textures/sprites/ground tiles (hand-made OR promoted from a pack)

The rule vs the neighbors:
- `0_Raw/` = untouched vendor import — never place directly in a scene.
- `1_Authored/` = curated & place-ready — scenes reference THIS.
- `2_Generated/` = regenerable tool output — safe to delete & re-bake.
