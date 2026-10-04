---
status:
owner: vince
last-updated: 2026-10-04
tags:
  - ai-learnings
---

# 🚧 A Reviewer Called the Design a Bug, and I Changed the Design to Match

**Reach for this when:** a reviewer, red team or checker reports something as "traps the player", "blocks the user"
or "a bug", and the thing it describes is the system doing exactly what it was built to do.

## The rule

Before acting on a finding, check it against what the feature is FOR, not only against the code. A finding can
describe the code perfectly and still describe the design working as meant. Then it is not a problem, and nothing in
the design changes because of it. "Trapped" means there is no way forward: the one spot cannot be pressed, or pressing
it cannot finish the step. Being made to do the required thing is not being trapped.

## Why

On 2026-10-04 a red team reviewed the quest plan and ranked first: "a guide running without the path traps the
player — every bag opening goes dark except the one row to wear". That row was the tutorial step itself, and the
player could press it. I accepted the finding, tied the guide to the "Show path" button so turning off the path turned
off the guide, and called that "the fix". That made a skip button in disguise for a tutorial that is meant to be
mandatory. It cost a round of building, a round of explaining, and the user had to correct both the design and the
wording ("no players are trapped when they actually have something to do on the spotted target").

## How to apply

- For every "blocks / traps / stuck" finding, ask one question: **is there a click or key that moves the player
  forward from here?** If yes, it is not a trap. Say so, and drop the finding.
- A finding that would make a REQUIRED step optional is a design change. It goes to the user as a question, never
  into the build as a fix.
- Relay a reviewer's severity only after re-deriving it yourself. Its confident wording is not evidence.
