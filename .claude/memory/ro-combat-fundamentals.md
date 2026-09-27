---
name: ro-combat-fundamentals
description: "Elements + status/DoT + leech/reflect combat layer (built 2026-06-19); test via P-panel \"Combat Test (RO)\""
metadata: 
  node_type: memory
  type: project
  originSessionId: 3e18f582-dcd3-4308-b38c-0cd6a5151e56
---

RO combat fundamentals built 2026-06-19 on branch `experiment/3d-world`. Three systems, all **additive** (new serialized fields default to `Neutral`/`0`, so normal play is unchanged and **no scene rebake** is needed):

1. **Elements** — `Element.cs`: enum of 10 properties (Neutral/Water/Earth/Fire/Wind/Poison/Holy/Dark/Ghost/Undead) + `Elements.Modifier(atk,def)` table. Mob armor element = `Health.element`; player armor = `Character.armorElement`; every attack carries an attack element. Spells wired (Fireball/Meteor=Fire, Frost=Water). Damage scaled by the matchup with Weak!/Resist/Immune cues.
2. **Status / DoT** — `StatusController.cs`: ONE unified host for mobs **and** the player. DoT: Poison/Bleed/Burn (0.8s tick). CC: Stun/Freeze/Slow/Silence/Blind. Mob hard-CC delegates to the existing `MonsterAI3D.Stun/Slow`; player CC is new (`Player3DController` respects stun/freeze/slow, spells respect silence, attacker respects blind). Front door: `Status.Apply(go, Ailment, seconds, magnitude)`. Auto-attached in the mob's / player's Awake.
3. **Leech / Reflect** — `Character.lifeStealPct` + `reflectPct` (the mechanical core of cards). Lifesteal heals on damage dealt (silent), reflect bounces a % of damage taken back at the attacker.

**Behavior change:** Fireball now applies Burn by default (`burnSeconds`/`burnDamage` on the component).

**Test surface:** press **P** → "Combat Test (RO)" group — cycle nearest-mob / player armor element, apply each ailment to nearest mob or self, slider Lifesteal/Reflect %.

**DEFERRED (4th gap):** threat/aggro table for party/ally play — needs the MMO/party layer first (mobs only target the player today).

Could not local-compile (Unity is the compiler); built by careful review. Links [[combat-multihit-status]] [[class-system-design]] [[gm-commands-are-the-test-tool]] [[stat-distribution-system]].
