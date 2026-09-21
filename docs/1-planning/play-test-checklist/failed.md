---
status: open
owner: vince
last-updated: 2026-09-21
tags:
  - planning-production
---

# Play-Test Checklist — Failed

Steps that were run and failed; each carries a *Status:* line with what was measured. Every step keeps the section and number it had in the single checklist, so a step referred to elsewhere as "step 2 of section 10" is still that step, and a step moves between these files as its verdict changes. The four files are [passed](passed.md), [failed](failed.md), [on-going](on-going.md) and [not done yet](not-done-yet.md).

## In Play mode

Before running any of these, do what [not-done-yet.md ▸ Before you start](not-done-yet.md#before-you-start--what-to-rebuild-and-restart) says must be rebuilt and restarted.

### 15. GMCP — an AI plays the built game as text (`MT-38`)

3. **Failed** — Type `/tp field_15` and try to walk.
   *Should:* you can walk away from where you land.
   *FAIL:* every step is refused.
   *Status:* 2026-09-17 — field_15's Start spawn at (0,0) is inside a tree's collider (Deciduous_tree2, also Rye and Oak_tree3 named), so anyone teleported or respawned there is stuck until they teleport away.

### 16. GMCP reworked for fewer round trips and tokens

8. **Failed** — Enter `field_11` from town through its door as a weak character, die there, `revive`, then `go d1`.
   *Should:* you walk away from field_11's Start spot.
   *FAIL:* every step is refused.
   *Status:* 2026-09-18 14:10 — tester: every step refused, the client naming `Tavern_LOD0` as what is in the way, at (-0.2,-2.0) while the screen said field_11; a creature stood half a metre away for 8 minutes and never hit him. The debugger settled where it comes from: **field_11 carries its own tavern** (every field has one; the town has none in that map file), and the server had him correctly in field_11 the whole time — so no map leaked into another. He was standing a metre above the floor, on the building. Whether the building is also what stops the creature's blows is unmeasured; the test is to park a player there with the hit log on and see whether the server writes a refusal or nothing at all.
11. **Failed** — Stand at a field's centre (`say /tp field_11`, or walk to (0,0) in `field_11`, `field_12` or `field_0`) and send `ground`, then `wait 3` and `ground` again.
   *Should:* `ground` names the field's own floor below you and says you are grounded, and your height does not change.
   *FAIL:* `ground` says IN THE AIR with nothing below you, and your height falls by tens of units between the two reads.
   *Status:* 2026-09-18 14:30 — **run, and it answered.** At `field_11`'s Start: `IN THE AIR`, nothing below for 500 units. At both of that map's door spawns: standing on `Ground_Floor`, the 42 × 1 × 42 floor box, at 0.02 — the floor is present and works. Walking in from a door you are stopped 1.5 m short of the centre, on the floor, touching two taverns, four trees and a sunflower. `field_0`'s Start: 30 units down within 3 seconds. `field_10`'s Start leaves you 0.56 up on its props. So the arrival spot is inside the scenery in every field; moving those spots clear of the props is an authoring decision and is left to you.
