---
name: ticks-on-a-person-lost-their-colour
description: "Every damage-over-time tick on a PLAYER (poison, a brace's bill, reflect) was drawn in plain hit red — the server passed the tick's colour to its own sink but dropped it from the message to the player's machine. Fixed 2026-09-15."
metadata: 
  node_type: memory
  type: project
  originSessionId: 99f08ae2-01f3-49ab-a776-212cb043a3a2
  modified: 2026-09-14T16:01:07.166Z
---

**Symptom (play-test 2026-09-14):** a Brace bill of 89 on Alpha drew in the same red as a creature's bite, so it looked
like an extra hit. Eight numbers for five bites — really 5 bites, 2 pale-blue held-back blows (Brace's own 99CCFF)
and the 89 bill 4 s later. Nothing was drawn twice.

**Root cause:** `CombatAuthority.ApplyTrue(target, amount, color)` announces a tick on a person through
`GameNetwork.AnnounceBlowOnPlayer(..., tick: true)` WITHOUT the colour, and `Character.ShowBlowFromServer` drew every
blow magic-violet or hit-red. (A creature's tick was then drawn inside `Health.TakeTrueDamage` — on the server, so in fact on no screen at all.)

**Fix:** the tick now carries WHAT it is, not a colour — `TrueDamageCause` (poison, bleed, burn, brace debt, reflected
by a person / by a creature, GM kill) on `PlayerHitResult.tickCause` and `HitResult.tickCause` — and every screen
colours it from one table, `TrueDamageCauses.Colour`. `Character.ColourOfBlow(info, tick, tickCause)` is the one colour
choice for a blow on a person (own machine and watchers, `NetPlayer.ShowBlow`); a creature's tick is drawn by
`Health.DrawShownBlow` from the `HitResult` (2026-10-07; before that, creature ticks were drawn only on the server,
so nobody saw them).
Changes the message shape — server and game must be rebuilt together. Visible change: poison ticks on a player now
show the ailment's colour instead of red, matching creatures.

**Related:** [[crit-on-player-drawn-ordinary]] (the crit flag is still ignored by the same colour choice).
