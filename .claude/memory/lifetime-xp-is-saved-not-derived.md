---
name: lifetime-xp-is-saved-not-derived
description: "Lifetime experience is a SAVED figure (save v10+), not one worked out from level — and the level curve lives in UnitStatMath."
metadata:
  type: project
---

A character's total experience ever earned is stored in the save as `lifetimeXp` (`SaveData`, `CurrentVersion = 10`),
counted in `Character.AddXP` alongside `XP`. It is NOT derived from the level on demand, so a later change to the
level curve can never rewrite what somebody actually earned. `XP` alone can never answer it — that field is emptied
on every level-up and only holds progress inside the current level.

The curve is owned in one place: `UnitStatMath.XpToNextFrom(level)` (`5 + 5L`) and `UnitStatMath.XpEarnedByLevel(level)`
(the closed form, in `decimal`, saturating at `long.MaxValue`). `Character.XPToNext` reads the first — do not retype
the formula anywhere, including in the character server.

Two decisions worth knowing (mine, 2026-09-07, stated to the user):

- **A dialed-in level raises a floor, never lowers.** `Character.SetLevel` (the GM/dev knob) hands out no experience,
  so a character levelled straight to 80 would read 0 lifetime on a board that ranks by it. It is raised to the least
  a real climb would have cost and never reduced, so a dialed-DOWN level cannot erase real earning.
- **Old saves are told apart by VERSION, not by value**, because 0 is a real figure. Below v10 `RestoreSave` derives
  it from the curve; migration `accounts/sql/005_lifetime_xp.sql` wrote the same figure into every existing row and
  deliberately leaves `version` alone (the game did not write it, and re-deriving gives the identical number).

Adding the field bumped the shared character-build string both servers must agree on to `unwritten-character-v11`
(`MapServerLink` and `charserver/MapServers.cs`) — so a deploy must rebuild the game server AND restart both, or
they refuse each other. See [[whole-skill-runs-on-the-server]].
