---
name: dual-wield-offhand-decision
description: "User decisions 2026-10-09 for dual-wield (Katar and Axe): one item fills both hands, a second half-power hit per swing at 90%, counted per hit — all BUILT, not play-tested."
metadata:
  node_type: memory
  type: project
  originSessionId: b8ce1cd0-619e-456d-9766-3499e785620e
  modified: 2026-10-08T17:45:19.131Z
---

Decided by the user on 2026-10-09; BUILT 2026-10-09 (not play-tested):
- **Dual-wield weapons: Katar and Axe.** Every basic swing lands two hits, decided by the server.
- **Off-hand hit = 50%** of the main-hand hit.
- **The off-hand is a full hit of its own:** its own hit/miss roll, its own crit roll, its own on-hit effects and
  passive procs (its own proc chance).
- Both hits are fractions of the swing time, so attack speed scales them (user: "still scalable with attack speed").
- **Charges and the run of hits count PER HIT** (user, 2026-10-09): each hand's hit uses up its own damage-boost
  charge and adds to (or, on a miss, ends) the hits-in-a-row count — the same as every other second hit in the game.
- Proposed and not objected to: main hand lands at the shared contact moment (70%), off-hand later in the same swing;
  creatures holding these weapons get the same.
- **One item fills both hands** (user, 2026-10-09: "we do not equip separate weapon each hand"): equipping a pair
  weapon shows the same model mirrored in the other hand automatically. BUILT 2026-10-09: `WeaponTypes.IsDualWield` is the
  one list (Katar, Axe) and `WeaponSocket` mounts the mirror copy from it; the off-hand HIT must read the same list.

Plan + red-team (2026-10-09) at `D:\kimodo\out\ro_refs\offhand-plan.md`; red team found: weapon swap mid-swing
re-weapons both blows (store the weapon on the pending blow), landing path doesn't refuse a dead victim, charges and
the run of hits count per landed hit (the user chose per hit, so nothing to special-case), mob half needs its own pending slot
cleared on disable, one shared "contacts per weapon" function instead of two copies, new attack stance ids must join
StanceDefaults + composer rows + IsBasicAttack, the left-hand copy must be handled everywhere the socket handles one.

Three attack animations are locked and never re-made — [[locked-attack-animations]]. Every other weapon's attack gets cloned from an RO sprite ([[attack-animations-from-ro-sprites]]); the
per-weapon reference picks are in `D:\kimodo\out\ro_refs\attack_references.png`, awaiting the user's yes.
