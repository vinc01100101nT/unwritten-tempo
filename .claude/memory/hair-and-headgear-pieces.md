---
name: hair-and-headgear-pieces
description: Every body is bare-headed; hair (13 styles) and hats (15 look-only Head items) are pieces put on by HeadSocket — built 2026-10-10; the gotchas that cost rounds
metadata:
  type: project
---

Built 2026-10-10, not play-tested. The design and every decision are in `docs/2-systems/hair-and-headgear.md`.

**The facts:**
- Every body is bare-headed. Hair and hats are pieces that `HeadSocket` puts on, for players, townsfolk and Shadows
  alike.
- `HeadPieceBaker.BakeAll(true)` (live editor) cuts them out of the Quaternius models. Bare bodies go to
  `2_Generated/Characters/Bodies`, and the `Characters/<Model>` addresses point there; pieces go to
  `2_Generated/Characters/HeadPieces`.
- The table is `Resources/HeadPieces.asset`. Hats are Head items whose `equipModel` is the piece.
- After a re-bake, run `CharacterDyePartsBaker.BakeAll()`: it reads the bare bodies.

**Gotchas that cost real time:**
- **Hair is a separate set of triangles inside the one body mesh**, not a separate object. The plain head (256
  triangles, colour 31,31,31, plus four 80-triangle eyes) is identical in the shared mesh space of every model, but
  bone ORDER and bind rotations differ per model. So a piece is fixed under the target's head bone at the TARGET'S
  `bindposes[head]`, and bones are always matched by name.
- **Hair that bends with the body cost 22.6 ms a frame for 200 people;** fixed to the head bone it cost nothing
  measurable. Only the Ninja Hood bends.
- **`CharacterDye3D` must read the body renderer only.** A piece's texture in the signature would be saved as the
  character's `modelName`.
- **Height measures exclude pieces** (`HeadPiece.IsPiece`). Hats used to shrink whole bodies: the Witch stood at 73%.
- **Re-baking a mesh** must go through `HeadPieceBaker.Rewrite`, and re-baking a prefab through `LoadPrefabContents`.
  See [[rebuilding-an-asset-blanks-open-scene-references]] in docs/5-ai-learnings.
- **A covering hat cuts the hair where it sits on the head** (`HairCut`, measured per hat in 24 directions as
  `HeadPiece.cutRim`). It counts only the hat's points over the head — a level plane at the hat's lowest point
  cut too low under wide brims and gutted the hair under the jaw-length helmet. Headbands leave hair alone (user,
  2026-10-10).
- **A worn piece made of nothing (`madeOf` None) rolls no word and is never a random drop.** That is what "no effect"
  means for the 15 hats.

Related: [[character-art-quaternius-charcoal]], [[other-players-default-colours]].
