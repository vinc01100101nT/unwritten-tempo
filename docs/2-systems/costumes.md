# Costumes

**Status: BUILT 2026-10-16, not play-tested.** One test costume, the Adventurer Outfit, fitted to all 51 bodies.

## What the player gets

- A **Costume Body** box in the equipment window, bottom right, across from Shoes. A costume worn there replaces how
  the body looks from the neck to the ankles, whatever Body armour is worn. The test costume gives no stats.
- Sleeves and trousers bend at the elbows and knees with the body.
- Other players see the costume.
- A costume keeps its own colours: the clothes colours chosen at creation are not laid on it.
- The test costume: **Adventurer Outfit** (`/item "item.adventurer-outfit"`), a blue tunic with a belt and strap,
  olive trousers, fitted to every body in the cast. A body added to the cast later has no fit until the step below
  is run for it: until then the costume is not drawn on it, and the game's log says so.

## How a costume is made

1. **A picture.** GPT draws the clothes alone — no head, hands or feet — front view, arms straight out to the
   sides, using a picture of one of our bodies as the shape reference. The test costume's prompt asked for chibi
   proportions; GPT drew longer ones, and the fit below absorbed it.
2. **A model.** `tools/image-to-3d.py` (Trellis) at 2,000 triangles; the `.obj` is renamed `.obj.txt` and kept
   with its picture in `unity/Assets/2_Generated/CostumeModels/<name>/`.
3. **The fit.** `CostumeBaker.Bake(name, bodies, wide, tall, topHeight, margin)` through the live editor. The
   test costume: `("adventurer_outfit", { "Soldier_Male", "BlueSoldier_Male", "Knight_Male", "Witch",
   "Casual_Female" }, 0.02505, 0.02989, 0.02209, 0.00015)`. `wide` puts the cuffs at the wrists, `tall` puts the
   sleeves on the arms and the hem above the ground, `topHeight` puts the collar at the neck. It took 4.6 s.
4. **Every other body.** `CostumeBaker.FitBodies(name, bodies)` works out what each body hides, without changing the
   costume's shape; `null` = every body. About 0.4 s per body, so run it ten bodies at a time through the live
   editor (one call may take at most 30 s). The test costume: 51 bodies in five calls of 3.3–4.7 s.
5. **The item.** An item in the Costume Body slot whose model is the costume's prefab.

## How it is built

### The bake

- The costume is stored **once**, in the space every body shares, and bends with the bones of the nearest spot of
  the FIRST listed body's skin. Its bones are stored by name, because each body lists its bones in its own order.
- Every costume point inside any body listed to `Bake`, or nearer its skin than `margin`, is pushed out to
  `margin`. Bodies fitted later with `FitBodies` do not change the costume's shape.
- For each body, one bit per triangle marks what the costume covers: a body point is covered when looking straight
  out of the skin there meets the cloth within 0.0015 (a twentieth of a body's height), and a triangle with any
  covered corner is hidden. Test costume, for example: 1,370 of the soldiers' 2,594, 1,548 of the Ninja Female's
  2,792, 2,484 of the zombies' 3,886 and 4,046.
- The costume's picture is held at 512, the size of the bodies' own.

### On the body

`CostumeSocket` sits on every player body — your own (`Player3DBody.Configure`) and everyone else's
(`NetPlayer.EnsureBody`, from the worn set the server already sends). When the worn costume changes, it asks
`CostumeModels` for the body's model joined with the costume — the body's parts minus the hidden triangles, the
costume as one more part — and swaps it in with the costume's material after the body's own. Taking it off puts the
plain model back. A fade on the body at that moment wears the new look (`CharacterFade.SetMaterials`).

A joined model is made the first time anybody on this machine wears that body-and-costume pair, shared by everyone
wearing it, and let go when the last of them takes it off or leaves.

### What it costs

- **Per frame:** nothing beyond the costume's triangles. Each person stays one bending model; a costume as a
  second bending model would have cost what hair worn that way did, 22.6 ms a frame with 200 people.
- **Joining a pair:** 1.9–2.6 ms once, measured in the editor (8.0 ms for the first ever, which includes the
  code's own first run).
- **Memory, held with the item list for the whole session:** the costume model 157,752 bytes, its picture
  224,832 bytes, and one bit per triangle for each fitted body (263–506 bytes; all 51 bodies, 16,250 bytes). A joined pair, held only while worn: about 0.5 MB (489,166–526,912
  bytes measured).
- **Drawing:** the character shader checks one per-material value per pixel to skip the clothes colours; no extra
  shader versions.

## Known limits

- **The same costume shape on every body.** A body whose own clothes stand further out than the costume shows them
  where the costume does not cover: the Witch's skirt bands show at the waist and thighs.
- **The test costume's hem** carries a ragged cream edge, already on the Trellis model before fitting.
- **Mobs and townsfolk** do not wear costumes, as they do not wear capes.
- **License:** Trellis models may not be sold — the install's own note: "Trellis uses some nvidia code that disallows
  commercial use".

## Not built

- Costume Head and Costume Back: their slots are reserved and not shown.
