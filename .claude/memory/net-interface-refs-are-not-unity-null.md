---
name: net-interface-refs-are-not-unity-null
description: "An ICombatUnit reference to a DESTROYED MonoBehaviour passes `== null` and then throws — use PlayerUnits.Gone, never a plain null check."
metadata: 
  node_type: memory
  type: project
  originSessionId: 655b9438-53c3-49a7-b55a-b2d9183754d1
  modified: 2026-08-29T14:32:30.831Z
---

`unit == null` on a variable typed `ICombatUnit` (or any interface) is a **plain C# comparison**. It does not run
Unity's `==` overload, so a `Character`/`Health` that Unity has already destroyed sails through the check and
throws `MissingReferenceException` on the next member read.

This got much more likely on 2026-08-29 when players became server-held objects that are destroyed on disconnect:
a blow can outlive whoever threw it (a projectile in flight, a delayed tick, a creature's death fade), and the
attacker can be gone by the time it lands.

**Why:** it looks correct and passes review — the null check is right there. It only fails when an object is
destroyed mid-flight, which is exactly the case nobody tests.

**How to apply:** call `PlayerUnits.Gone(unit)` — it does `ReferenceEquals` first to tell a failed cast apart from
a destroyed object, then asks Unity. Every field holding an `ICombatUnit` across a frame must use it: the
creature's current prey, the recorded last attacker, and the damage door's own attacker reads. A concrete Unity
object typed as `Character` or `Health` is fine with plain `!= null` — the trap is only the interface.
Related: [[one-screens-answer-breaks-on-a-server]].
