---
name: weapon-granted-passives-never-fired-on-server
description: No passive granted by a worn weapon ever fired on the server — the gate asked Character.Instance (null on a server) for the weapon; ticked passives were unaffected.
metadata: 
  node_type: memory
  type: project
  originSessionId: 99f08ae2-01f3-49ab-a776-212cb043a3a2
  modified: 2026-09-14T11:43:39.022Z
---

**Symptom (play-test 2026-09-14):** Composure, Last Stand and Encore — all rolled onto Alpha's sword — never fired;
every passive ticked by hand in the Grimoire (J) fired every time. Cleanest measurement: Momentum fired on every blow
while ticked, and 0 times in 60 blows while granted by the worn Rare sword and unticked. The server log had 45
`fired skill.sword.momentum` lines and not one line for any weapon-granted passive.

**Root cause:** `PassiveLoadout.GrantedByWeapon` read `Character.Instance` to find the weapon. That handle is set only
`if (!GameServer.IsServer)` (one assignment in `Character`), so on the server it is null → "no weapon" → `IsActive`
collapsed to `IsChecked` at the one gate in `PassiveEvents.Fire`. `UsableSkills()` had already been fixed for the same
trap (its comment names it); the gate had not.

**Fix (2026-09-14, uncommitted):** `PassiveLoadout` now takes its owning `Character` in its constructor and reads
that character's weapon; `Character.PassiveSkills` builds it lazily with `this`. Compiles clean; NOT yet play-tested
(needs a server rebuild + restart).

**Gotcha — the class, not the instance:** any server-run code reaching for `Character.Instance` to mean "this
character" is the same bug. See [[find-or-create-hides-duplicates]] and [[combat-network-readiness-seams]].
