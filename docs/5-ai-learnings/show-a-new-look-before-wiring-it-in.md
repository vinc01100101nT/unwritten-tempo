# Show a New Look Before Wiring It Into the Game

**Reach for this when:** you have made a new pose, animation or other look and are about to put it in the game —
or the user says something you added "looks awkward" and asks whether you checked it first — or the user plays the
game and still sees the OLD look you replaced — or the user says your picture shows the opposite of what you
reported.

## The rule

Your own check of a look measures what you thought to measure — clearance from the floor, overlap with the body —
and says nothing about whether it looks right. Only the person who judges the look can pass it. So a new look goes
to the user as a picture or video first, with the measurements beside it, and is wired into the game only after a
yes. And after the yes, wiring it in is a step of its own that needs its own proof: read what the game will actually
load and confirm the new look is there and the old one is gone.

## Why

2026-10-09: a katar "carry pose" was built, checked in private pictures for blade-floor clearance (29% of body
height above the floor) and body overlap (none), and wired straight into the game. The user's first look in Play:
"it looks awkward haha, did you checked first?" The numbers were right and the pose was wrong — the arms alone,
raised over a relaxed standing body, read as stiff rather than ready. One build round and one play-test were spent
on a pose a single picture would have stopped. The same day's attack animations had gone the other way — sample
videos first — and passed on the first look.

2026-10-10, the other half: the replacement — the katar's crossed guard for running — was built and checked on
2026-10-09, but the setting that picks the katar's running arms still named the old pose. Nothing failed and nothing
warned: the new clip simply sat unused. The user's play-test showed the old parallel blades and cost a round to find.

2026-10-10, the same mistake with a model: 13 weapons made from 2D drawings were re-made with rounded edges and
finer textures. I compared my own renders against the drawings — straight front, straight side and one 35° view,
lit evenly — judged them "much closer", re-made the files in place (so the game picked them up at once) and
reported the wand as fixed. The user opened the wand in Unity's own preview, at a three-quarter angle under the
editor's light, and saw a boxy, stepped head: "if you call this fixed, it is not". My views were the flattering
ones; theirs was the one that counts. One more round lost, on a look nobody had passed.

2026-10-16, the same mistake in my own report: a cape was given arm collision and rendered from eight sides in an
idle pose. I wrote "no arm passes through the cloth in any of the 8 views". Two of those very pictures showed the
cape's side panel drawn over the arm, with the lining showing between arm and body. The user cropped them and asked
"are you blind?". I had looked for what I expected the fix to do, not at what the picture showed.

## How to apply

- Render the look the way the game shows it (real body, real weapon, real camera angle), save it where the user
  can open it, and name the file in the reply.
- Say what you measured AND that the look itself is unjudged until they see it.
- Never describe your own picture as passing. A sentence like "nothing passes through in any view" is a claim the
  user can disprove from the file you just sent; describe only what you measured, and let them judge the picture.
- For a model, include the angles that are least kind to it (three-quarter, from above, the game camera), not only
  the straight views the source drawing was made from, and never call it fixed in the same reply.
- Re-making a file the game already loads IS wiring it in. Write the new version beside the old one until the yes.
- Wire it in only after the yes. A system change that needs no look (a fix, a data layer) can go ahead in parallel.
- When wiring it in, read back what the game loads — for an animation, the rebuilt controller's states — and confirm
  the new name is there and the old one is gone. Name both in the report.
