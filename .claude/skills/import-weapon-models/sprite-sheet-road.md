# A weapon from a 2D sprite sheet

A road of the `import-weapon-models` skill (`SKILL.md` beside this file), kept apart because only sprite-sheet work needs it. "Above" and "below" mean that file's steps and scripts; the scripts are the `.cs` files beside it.

First done on 2026-10-09 for the four Kashira weapons. Steps 1, 5b and 7–9 above still apply; S1–S5 take the place of
the rest of steps 2–6.

**S1. The sheet — two GPT messages, never one.** Asking GPT to design a weapon AND draw strict upright views in one
message gives stiff, symmetric, cup-shaped results (the Fist failed four times that way). So:
- **Design first:** a 3/4 hero view, four clearly different options side by side, the theme, "NOT symmetric", and a
  reference picture attached when the user names another game's style. The user picks one.
- **Then the sheet**, in the same chat: "Redraw option N exactly — same shapes, details, colours — as TWO
  orthographic views side by side, left to right: FRONT view (the broad face) and SIDE view (turned a quarter turn,
  showing its thickness). Same scale, upright, tip at the TOP and handle end at the BOTTOM on the same pixel rows in
  both views. Flat solid magenta #FF00FF background edge to edge, no shadow, no text, crisp pixel art, one solid
  connected object, nothing wire-thin (no strings, chains, ribbons)." A rifle is drawn barrel-up, a bow upright and
  stringless.
- **Never ask for a third, top-down view.** GPT draws it wrong (in one sheet it was a small copy of the hilt), and
  the converter carves the WHOLE weapon to that third picture's outline.
- The user saves the image file itself — a screenshot of the chat carries its corner buttons in as extra pieces.

**S2. Convert** — `python3 tools/sprite-sheet-to-weapon.py <sheet.png> --name <name>` writes
`2_Generated/Weapons/<name>/<name>.obj` and `.png` (length 1 along +Y, origin at the centre of the bottom end). The
import rule already makes that texture point-filtered and uncompressed.
- The output must say `views: 2`. The FIRST view is read as the front: when GPT swaps them (the sniper's profile
  came out on the right), swap the two halves of the picture in a scratch copy and convert that.
- Two views are lofted with rounded edges (2026-10-10): every row of the front view is an eight-cornered
  cross-section, as thick as the side view at that row only deep inside the front view's outline and rounding down
  to one block at it, so wide parts (blades, discs) end in a thin rounded edge and narrow ones (grips) come out
  round; the shading is smoothed across the curve. `--square` rounds only the outer two blocks, for parts that must
  stay blocky (the hammer head, the bow, the sniper). A surface may stray two blocks from the drawing so that many
  rows share one face.
- **The texture has 4×4 colours per block** (`--texels`), taken only from pixels 3 inside the drawing's outline and
  filled outward from there. One colour per block smeared a 40-block wand head into mush; the outline pixels put a
  dark and magenta rim on any surface that strays past the drawing. 13 weapons ≈ 7.3 MB of textures, fixed by the
  number of weapon models, not by the number of players.
- **The side view is only a thickness per row**, so an ornate head whose side view is filled by ornaments (the
  wand's clouds) comes out as a rounded lens as thick as those ornaments, never with its parts separated in depth.
- **Triangle budget, measured:** the character body is 2312 triangles and the pack's weapons 58–146. Rounded
  lofts: sword 412, greatsword 700, katar 626, wand 984, axe 920 (a pair is twice that), scythe 1138, shuriken
  1382. Ten corners per slice and a one-block fit cost up to 2359; stacked blocks 1622 to 5118 — never go back.
- **Check the shape against the sheet before Unity:** render the model straight from the front and the side beside
  the sheet (a small numpy rasteriser over the `.obj` and its `.png` is enough) — that comparison is what shows a
  head turned to slabs or a dark rim.
- Unity's model import mirrors X (measured: the sniper's scope, drawn on the right, came out at mesh −X).

**S3. Find the grip point in the front view's rows.** Load the converter as a module and print its `resample` mask
of the front view row by row (row 0 = bottom, 160 rows), with the side view's mask beside it. The handle is the
narrow run between pommel and guard: grip `y = (first row + last row + 1) / 2 / 160`; `x` and `z` are the run's
centre minus half the front width and half the side depth, `/160`, and `x` then has its sign flipped (the mirror
above). Measured examples: sword handle rows 10–38 → y 24.5/160; greatsword rows 15–36 → 26/160; the katar at the
midpoint between its two crossbars; the sniper on the stock's narrow wrist just behind the trigger guard.

**S4. Build the prefab against the model the item uses today** (the seed for that weapon type, not always the
sword): rotation = that prefab's `WeaponGrip` rotation; its grip point in the hand = its `localPosition` (a pack
model's centre point is its grip); scale `k` = (its mesh length × its scale) ÷ the new mesh length; position =
`seed.localPosition − R · (k · grip)`. Material: URP Lit with the generated texture, smoothness 0.5, metallic 0;
set the model's material import to None. Name the prefab and material after the sheet's `--name`. Then step 5b:
a rebuilt sheet weapon keeps its put-away place, and a new one carries the place of the model the item uses today.
- **A rifle** follows the hold the Sniper attack was built for (the animation skill's rifle branch): right hand;
  barrel (+Y) along the hand from wrist to knuckles — in a T-pose, hand position minus lower-arm position, in the
  hand's own space; the gun's top (the scope side) toward the thumb — the seed sword's +Y in hand space, made square
  to the barrel; fist at the seed sword's grip point; length 0.62 of body height (the stand-in was stock 0.12,
  barrel 0.5).
- **The hand-scale trap:** `CharacterSpec.Active.bodyHeight` is in world units, but a `WeaponGrip` scale is in the
  HAND BONE's space, and that bone's scale was 0.1035 on a body scaled to 0.327. A length in body heights must be
  divided by the hand's scale; the first sniper skipped that and came out ten times too small.

**S5. Pictures — also from the game camera.** Besides the picture script, render the in-game body (for example
`Casual_Bald.fbx`, not only the default model) from the game camera's angle: `CameraFollow3D`'s defaults put it 10
up and 4 back, a steep look down; render it from four sides. From up there a weapon whose broad face points sideways
shows only its edge — the Kashira katar, held like the dagger, came out as a thin sliver the size of the old dagger
in those pictures (2026-10-09), and the user had reported it "missing". A pair weapon
(`WeaponTypes.IsDualWield`) gets its second copy exactly as `WeaponSocket` mounts it: position x negated, rotation
`(x, −y, −z, w)`, scale x negated.

Then step 7 (point the item, only on the user's go) and steps 8–9.
