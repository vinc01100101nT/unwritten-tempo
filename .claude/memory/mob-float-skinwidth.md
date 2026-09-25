---
name: mob-float-skinwidth
description: "Mobs/NPCs \"obviously elevated\" (float ~0.08 above ground) while player looks planted — cause was CharacterController skinWidth"
metadata: 
  node_type: memory
  type: project
  originSessionId: 58c8bb40-de7d-480e-b31a-58ebfa159737
---

2026-07-01: mobs & NPCs rendered "obviously elevated" (floating ~0.08 above the grass) in field maps while the **player** and **trees** sat correctly on the ground. User flagged via screenshots.

**Root cause:** a Unity `CharacterController` rests with its capsule bottom `skinWidth` ABOVE the floor (skin is a buffer, not penetration). For the small-scaled billboard units the capsule geometry (`cc.center.y=0.1`, `height=1`, set in `Build25DSpike.BuildMonster`) makes the algebra cancel so the **sprite's feet float by exactly `skinWidth`, independent of mob scale** — which is why every mob floated the same visible amount. Unity's default skinWidth is **0.08**. The player looks planted because it uses its own tuned controller (`Player3DController`) whose seating sinks the feet slightly into the ground.

**Verified by MCP measurement** (play, field_11): player spriteBottom = −0.131 (feet in ground); mob spriteBottom = +0.059, feet ≈ +0.08 = skinWidth. Setting a live mob's `cc.skinWidth = 0.02` dropped spriteBottom to ≈0 (grounded).

**Fix:** `PathAgent3D.Awake()` sets `cc.skinWidth = Player3DController.DefaultCapsuleSkinWidth` (= 0.02; was a duplicate `0.02f` literal, changed 2026-07-03 to read the single owner constant so player + mobs share one value). PathAgent3D is on every mob/NPC/ambient billboard (not the player), so this grounds ALL of them at runtime — including already-baked scenes, no map regeneration needed. `BuildMonster` never set skinWidth (relied on the 0.08 default), so the runtime override is the single source of truth.

⚠️ Mobs are baked into scenes at generate time by the editor-only `Build25DSpike.BuildMonster`; runtime code can't call it. So runtime-side fixes (like this) in `PathAgent3D`/`MonsterAI3D` are the way to patch all existing maps without regenerating.

Related: houses/buildings were also reported "elevated" but measured bases sit at y=0 in both lastline and field_11 (not a physical float — likely flat foundation on top vs tall grass decor). The ~0.37 "elevated grass" is just decorative grass-tuft/flower prop HEIGHT on a flat Ground_Visual at y=0, NOT raised terrain. See [[world-ground-standard]].
