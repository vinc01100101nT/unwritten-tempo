---
status:
owner: vince
last-updated: 2026-09-25
tags:
  - ai-learnings
---

# Image-Generator Tiles: Sizes, Counts and Seams

**Reach for this when:** you are writing prompts for an image generator to make repeating ground textures (or any
tile with a real-world scale), or a generated tile came back too big, too busy, or with a visible seam.

## The rule

The generator ignores real-world units and vague amounts, and "fix the seam" makes it draw a new picture. So give
every size as a **fraction of the image width** (with the centimetres beside it) stated as a **maximum**, give every
scattered thing an **exact total count** and ban everything else, and test every image yourself for seams before
accepting it. To repair a seam, a **short plain reply on the image** works better than a long instruction.

## Why

On 2026-09-24/25, 26 ground tiles were made in ChatGPT for a game that repeats each tile every 2.5 m.

- **Sizes in metres were ignored.** "Pebbles 2–8 cm" came back as stones about 34 cm across — 3 to 5 times too big —
  and the user saw it only after walking on it in the game. Rewritten as "the largest pebble no wider than 1/30 of
  the image width", the next one came back at about 10 cm.
- **Vague amounts became hundreds of things.** "A light scatter of tiny broken shells" came back as hundreds of
  shells and stones over the whole tile. "The whole image contains ONLY about 10 to 15 shell pieces, NO stones" fixed
  it in one go.
- **Every one of the first 9 tiles had a seam** even though the prompt said "seamlessly tileable". Spelling out what
  a perfect repeat means (edges continue into the opposite edge; keep big shapes 1/10 of the width from the edges;
  mentally shift by half and check) brought the next batches down to about the level of an approved tile.
- **A long "fix only the seams" prompt failed three times** (pebbles, dune sand, cracked earth): each time the
  generator redrew a different picture with the same seam. The user then replied "still not repeating properly" on
  the image, and cracked earth went from a 4.0× join to 1.8× in one try.
- **A literal word became a literal picture.** "Ropey flow wrinkles" (the real geology term for that lava) came back
  as twisted rope. Describe the shape you want to SEE, not the name a specialist gives it.
- Screenshots pasted into chat were cropped (1426 × 1424, not square), so seam numbers measured on them were
  inflated. Test the downloaded file, never the chat copy.

The whole session cost roughly a dozen regenerations that a better first prompt would have avoided, and one image
account ran out of credits.

## How to apply

- **Sizes:** "at most N cm (1/K of the image width)", or "about N to M across the image". Say sizes are maximums.
- **Scattered things:** "the whole image contains ONLY about N to M <things> in total … NO <every other kind>".
- **Repeat:** keep large shapes 1/10 of the width from every edge; the band near the edges holds only fine, even
  texture.
- **Put the rules in one message first, and end every tile request with a one-line reminder** — the generator
  drifts from the rules as the chat grows.
- **Seam test (stdlib only, no image library needed):** convert to BMP with `sips -s format bmp`, then compare the
  average colour step across the wrap (left column vs right column, top row vs bottom row) with the average step
  between neighbouring columns inside the image. An approved tile measured about 1.9×; anything above about 2.5× showed
  a visible seam. Also build the 2×2 corner view (the last N pixels of the right/bottom edges joined to the first N
  of the left/top) and LOOK at it — the number alone missed faint tone steps on very smooth textures.
- **Tiles only.** For a whole map or scene these exact rules backfire and draw a diagram:
  [A Generated Scene Came Back Looking Like a Diagram](image-generator-scenes-come-back-as-diagrams.md).
- **Seam repair:** first reply "still not repeating properly" on the image. If that fails on a smooth texture, cross-
  fade a 40-pixel strip of each edge into the opposite edge and crop — invisible on sand, snow and ice, but it leaves
  see-through ghost shapes on pebbles, so redo those instead.
