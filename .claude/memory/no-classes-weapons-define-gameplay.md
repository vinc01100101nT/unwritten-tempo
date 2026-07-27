---
name: no-classes-weapons-define-gameplay
description: "There is NO class system — the equipped weapon's skill kit + ring build define gameplay/role."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1c05d24a-e740-47fc-a4f8-b555d4f437fd
---

**unwritten has no character classes.** The "Class System — Design" doc (Swordsman/Mage/Acolyte/6-Tier-1 classes, advancement to Knight/Crusader, etc.) is **STALE and DELETED** — do not treat it or any "class" framing as current.

Current model: a player's gameplay identity comes from **(1) the equipped weapon, which grants its own active skill kit** (`Item.grantedSkillIds`; swap weapon → swap kit) and **(2) the ring build** (stat investment, which role-locks power per V3 — Heal→MP, Damage→Atk/MAtk). `WeaponType` is advisory (feel/animation), NOT a class gate — its own code comment says it "does not hard-gate which class may hold what."

Consequences:
- Skill **rosters are per-WEAPON, not per-class** (earlier "6 Tier-1 class kits" planning framing was wrong — it leaned on the deleted class doc).
- Any doc still using class language (Ring of Progression, Story canon sibling list, older handoffs) is stale on this point.

Scar: built a "Classes" page in the [[unwritten wiki]] from the deleted class doc; user corrected it. Ties to [[weapons-are-3d-no-2d]] and the V3 skill system (weapon-granted skills).
