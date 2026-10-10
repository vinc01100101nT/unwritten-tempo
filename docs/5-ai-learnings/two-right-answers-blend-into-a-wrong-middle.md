# Two Right Answers Blend Into a Wrong Middle

**Reach for this when:** two end states each check out exactly, and whatever is drawn or computed BETWEEN them is
wildly wrong — a blend that swings through somewhere neither end is near, a path that flips half way.

## The rule

A solver that can reach the same result in more than one way hands back whichever way it found. Two results that
look alike from outside can sit at opposite ends of what is inside, and anything interpolated between them travels
through the inside, not the outside. Before interpolating between two solved states, print their INTERNAL values side
by side and compare those. Where they disagree, re-solve one end with its free choices turned until the insides
match — and if no choice matches, the two states are not joined by any real motion: change the plan, not the blend.

## Why

2026-10-10, weapon put-away animations. Each still pose pointed its weapon exactly where planned (0–3 degrees
off), so the poses were treated as good and blended joint by joint. The sword then swung through the head (27% of
it inside) and 20% of body height under the ground; the rifle went 56% under the ground. About ten rebuilds were
spent adding middle poses, re-timing, and re-solving the path as one keyed move (which left its line by 49 degrees)
before anyone printed the arm joints: the two sword poses had the forearm twisted to opposite ends of its range.
One scan of the blade's roll (12 steps, a choice the eye never sees) found poses whose joints agreed, and the next
blend was clean first time. The same printout showed in one look that the rifle's planned resting place could not be
joined to its starting pose by any roll — a fact ten more rebuilds would never have produced.

## How to apply

1. After solving the end states, print what the solver chose inside (joint values, branch, sign, winding) — not
   only the result it was asked for.
2. Compare neighbours. A value near one limit in one state and near the other limit in the next is the flip.
3. Turn the free choices (anything the result does not depend on visibly) and re-print; keep the one that agrees.
4. Still check the in-between itself, step by step, with a measure that would show a flip (biggest change in one
   step, lowest point, overlap) — agreement inside is necessary, not proof.
5. In this project: `arm-joints.cs` and `check-put-away.cs` in the `clone-ro-sprite-animation` skill.
