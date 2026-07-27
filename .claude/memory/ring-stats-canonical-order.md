---
name: ring-stats-canonical-order
description: "Always list the 10 ring stats in ring LOOP order, never enum order — in docs, wiki, and replies."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1c05d24a-e740-47fc-a4f8-b555d4f437fd
---

When mentioning the Ring's ten stats anywhere (docs, the wiki, chat replies, code comments), ALWAYS use the ring's **loop order**, never the `StatId` enum order.

Canonical loop order (from `SkillRing.Ring` in `unity/Assets/Scripts/SkillRing.cs`):
**Defense → HP → Attack → Accuracy → Cast Speed → Magic Attack → MP → Evasion → Critical → Attack Speed** → (back to Defense)

**Why:** it's a *ring* — order is meaning. The loop order makes the poles correct (Defense idx 0 ↔ Magic Attack idx 5 sit dead-opposite) and defines the draft's neighbors (sliding window of 3). The `StatId` enum order (Atk, MAtk, HP, MP, Def, Accuracy, CastSpeed, Evasion, AttackSpeed, Critical) is deliberately NOT the ring order — the code comment says so — and listing it that way is wrong/misleading.

**How to apply:** verify against `SkillRing.Ring` before writing the list (order can be re-tuned). Two stats are dual: Defense scales phys + magic defense (no MDef node); Critical scales crit rate + crit damage. See [[no-classes-weapons-define-gameplay]] and the "Ring System — Current Facts (Trio-Ring)" doc.
