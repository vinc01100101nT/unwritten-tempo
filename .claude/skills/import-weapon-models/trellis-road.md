# An ornate weapon made with Trellis

A road of the `import-weapon-models` skill (`SKILL.md` beside this file), kept apart because only Trellis work needs it. "Above" and "below" mean that file's steps and scripts; the scripts are the `.cs` files beside it.

When a weapon's head has parts at different depths (a wand's sun and clouds, a staff's crown), the converter above
cannot build it: make it with the `image-to-3d` skill instead, from the sheet's front view. First done 2026-10-10
for the wand, then for all 13 weapon types the same day (1,410–1,484 triangles each). Steps 1, 5b and 7–9 still
apply; this replaces the rest.

- **Make it:** `python3 tools/image-to-3d.py <front view.png> --name <name>` → `2_Generated/WeaponModels/<name>/`.
  Judge it with `tools/render-model-views.py` and show the user before anything else.
- **Import:** texture compressed, bilinear, mip-mapped, `maxTextureSize` 1024, not readable (painted, not pixel
  art); the model's material import set to None; a URP Lit material in `Materials/<name>.mat` with that texture.
- **Fit:** against the model the item uses today, like S4 — same rotation, same length (`k` = its length ÷ the new
  mesh's `bounds.size.y`), and the fist on the SAME FRACTION of the length as the old model's held point (the old
  held point is the sword's fist point carried into the old mesh: `R⁻¹ · (G − old.localPosition) / old.scale`).
  The new grip's x and z are the middle of the new mesh's vertices within 1.5% of the length of that height — the
  shaft there. Trellis's model is centred, not bottom-anchored, so measure from `bounds.min.y`.
- **Check which way Trellis turned it.** It does not keep the drawing's facing: on 2026-10-10 the axe came out a
  quarter turn round its length and the bow and scythe a half turn. Compare where the head (or the string) points in
  hand space against the old model, and add the turn about the length before saving.
- **A handle thicker than the fist** (the warhammer after it was tripled, 2026-10-10: 0.032 across against the
  sword grip's 0.008, in world units on the default body): never edit the model file. Copy the mesh into a new asset
  beside it (`<name>_thin_handle.asset`), move every point of the handle's run toward that slice's centre line until
  it is as wide as the sword's grip at its fist, with a short smooth band at each end, and point the prefab's mesh at
  the copy. First check the handle's centre line runs THROUGH the fist point — the hammer's ran along the hand's
  outer edge, hidden by its thickness, and the mesh had to be shifted 0.011. The bodies have no finger bones: the
  hand is a closed mitten, so "fits" means the handle runs inside it.
- **⛔ A shuriken is ALWAYS held at its middle, never by a blade** (user, 2026-10-10: "all handles should always be
  at the middle, not on the blades"). A clearance fix may turn, tilt or resize it — never slide the hold onto a blade.
- **A weapon not held by a handle** (katar, shuriken) goes on the fist's middle, as in step 5; the shuriken is held
  at the star's centre and turned so the star clears the leg at idle — measure that with the body's vertices.
- **Name it `<weapon type>_<Kashira>`** (`sword_veyra`, `wand_seraphine`), the Kashira from the story canon's table,
  and point the item at it only after the user's yes on pictures in the hand and from the game camera (S5).
  Renaming later: `AssetDatabase.MoveAsset` for the prefab, material, model folder and files (IDs stay), then the
  mesh name inside the OBJ (the `o` / `g` lines), re-import, and put the renamed mesh back on the prefab.
