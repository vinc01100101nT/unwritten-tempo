---
status:
owner: vince
last-updated: 2026-10-10
tags:
  - ai-learnings
---

# A Fix Passed Your Measurement and the User Says Nothing Changed

**Reach for this when:** you are about to fix something the user SAW (a thing hidden, overlapping, floating,
clipping), or your fix passed your own numbers and the user answers "still the same" — or your number says one
thing ("the arm is straight") and the user's eyes say another.

## The rule

Measure the thing the user sees, from the camera they see it with, and nothing else. A number about the 3D scene
("the arm is no longer inside the head") is not the complaint ("the arm disappears") unless it is the same fact.
And before building, check the fix is even POSSIBLE with a two-minute measurement of the hard limit. If your own
pictures still show the problem, do not keep the change and explain it — stop, and say the fix cannot work.

## Why

2026-10-08: on a slam animation fitted to the game's chibi characters, the user reported that one arm disappears
behind the big head when the weapon is lifted overhead. I pushed the arms out of the head's volume — 19 of 90
frames inside, deepest 15% of body height, became 0 frames — and reported the number. My own pictures from the
same camera still showed the arm hidden: pushed out of the head, it went BEHIND the head. Only afterwards did I
measure the limit that settled it from the start: the shoulders sit 10.2% from the body's centre while the head is
15.7% wide on each side, and a fully raised wrist reaches 87.5% of body height against a head top at 99%. No
overhead lift can ever show the arms from the front on that body. The user had to ask "can't you see it yourself in
your fix?" — a full round spent on a change that could not work.

2026-10-10, the same mistake on a different object: asked for a straight running arm, I measured the elbow on the
animation clip (175°) and called it straight — twice. The video the user watched was drawn by a separate script
that lays the arm pose over the run, and its filter skipped the forearm, so every frame kept the run's own 90°
elbow. My number was true of a file the user never saw. Two rounds, the second with "dont fool me".

## How to apply

- Write the user's complaint as a test on the picture: "from the game camera, is the arm's silhouette clear of the
  head's silhouette?" — not a test in the 3D scene.
- Before building, measure the hard limit (reach, size, angle) that decides whether ANY fix of that kind can work.
  If it cannot, propose a different kind of fix (here: a different motion, over the shoulder) instead of building.
- Look at your own before/after pictures before reporting. If the after looks like the before, the fix failed —
  report that and do not keep the change.
- Measure on the artifact the user is shown — the video's own frames, the render's own pose — not on its source.
  Anything that turns the source into the picture (an overlay, a filter, a camera) can change the very thing you
  are measuring. When the user's eyes and your number disagree, your number is about the wrong object.
