---
name: locked-attack-animations
description: "LOCKED by the user: the Greatsword attack, the Bow attack and the Shuriken throw, and the WHOLE animation sets of the Axe, the Katar and the Dagger — never remake, rework or offer to rework them"
metadata:
  node_type: memory
  type: project
  originSessionId: 605f945c-3281-4842-813b-03aa221e090f
  modified: 2026-10-10T07:03:43.840Z
---

The Greatsword's attack (the imported `AttackSword2H`, two-handed already), the Bow's attack (`AttackBow`) and the
Shuriken throw are LOCKED by the user: they stay exactly the clips they are. Never build a new clip or key table for
them, never "rework" one, and never put one in a list of offered fixes. Measuring them is fine. The only changes are
the two the user made on 2026-10-11: the shuriken throw's wind-up keeps the hand farther from the head ("try moving
the hand farther from the head during wind up", then "ship them in-game"), and the bow's attack plays MIRRORED because
the bow moved to the right hand ("the attack animation for bow should be flipped to become right handed"). Every other weapon's
attack is cloned from an RO sprite ([[attack-animations-from-ro-sprites]]).

**Also locked since 2026-10-10 (the user: "lock these and put into the game with complete animations"): the Axe,
the Katar and the Dagger, each as a whole set** — attack, combat idle, running arms, put-away, take-out, how the
model sits in the hand and where it rests on the body. All three are in the game. The one change the user ordered in
the same message is the dagger SPIN (the dagger turning in the hand during its put-away), which is an addition on
top of the locked put-away, not a rework of it.

**Why:** the user locked them on 2026-10-09. On 2026-10-10 the greatsword's blade measured inside the body, a rework
was offered among three other fixes, the user answered "go fix them all", and a new greatsword attack was built
(kept out of the game). The user: "i instructed you that we will keep the existing greatsword attack as itself, why
did you create a new one?" A lock is not lifted by a general "go" on a list that should never have contained it.

**How to apply:** when a measurement finds a defect in a locked clip or set, report the measurement and say the clip is
locked — no offer. Only the user saying in so many words that the lock is lifted changes that. Related:
[[feedback-check-standing-decisions-before-offering]], [[dual-wield-offhand-decision]].
