---
name: weapon-affix-vs-ring-voice-separation
description: Active-skill design rule — weapon affixes and ring voices must NEVER cover the same behavior.
metadata: 
  node_type: memory
  type: project
  originSessionId: 65dcd9a3-277c-48e7-b7d0-e69c60228288
---

**Weapon affixes ≠ ring voices — they own DIFFERENT behavior spaces, never duplicate.** (Active Skill System V2.)

- **Ring voices** (from stat investment, build-wide): ATK=Intensify(cleave/explode/knockback-finisher), MATK=Expand(bigger/split/spread), HP=Leech, MP=Extend, DEF=Fortify(shield), Accuracy=Pierce(ignore-evasion), Crit=Execute(low-HP finisher), AttackSpeed=Repeat(extra hit/echo), CastSpeed=Accelerate(cdr), Evasion=Flow(dash/reposition).
- **Weapon affixes** (baked at loot, weapon-bound): CONTROL (slow/root/silence/push/pull), TARGETING (penetrate/bounce/homing), DAMAGE FLAVOR (element-convert/raw), RESOURCE/UTIL (mana-leech/refund/reflect/combo), PERSISTENCE (lingering/trail).

**Why:** a weapon affix that duplicates a voice creates the "why does my sword do the same thing as my Crit stat?" confusion (real scar: I seeded Iron Sword with an *Execute* affix = the Crit voice; user caught it; swapped to *Frostbite* = slow/control).

**How to apply:** when authoring a weapon affix, pull from the library section of the V2 design doc (`q179qjaem3cpwc1rqbgv1kd6pd8awxpe`, "Weapon affix library"). NEVER add Execute/Repeat/Expand/Extend/Lifesteal/Barrier/Guard/Cleave/Explode/Finisher/Multi-Target/Blink as a weapon affix — those are ring-only. Runtime: weapon affixes fold into the SAME `CastMods` as ring voices but via different dials (weapon=slowFactor/control; ring=executePower/extraHits/radiusBonus). See [[active-skill-ring-voices-live-not-skillmod]].
