# Combat / skills / progression / stats design

- [Multi-hit yellow total was never wired](../multihit-yellow-total-never-wired.md) — `MultiHitBurst` dead code; `hitCount>1` now cascades via `SkillEffectApplier.DispatchMobHits`.
- [Cast reticle "persisted" on failed release](../cast-reticle-soft-fade-persists-on-fail.md) — GroundTargeter soft-fade masked on success; fix = `EffectHandle.Stop(true)` hard-clear.
- [Single-target aim couldn't hit mobs in a crowd](../unittargeter-mob-shadowed-by-player.md) — UnitTargeter's own first-hit raycast let the player capsule shadow mobs behind it; fix = route mob pick through magnetised `MonsterUnderCursor`.

- [Cast time (spell wind-up)](../cast-time-feature.md) — `SkillDef.castTime`→`SkillRunner.CastWindup`; `EndCast()` choke; 0=instant.
- [Legacy skills retired → composed-only pool](../legacy-skills-retired-composed-pool.md) — hand `Player3D*` DELETED; pool=`SkillDef`s via `SkillCatalog`.
- [Class system = kit + attack + affinity](../class-system-design.md) — kit identity NOT stat locks; doc q171yxv1de9zce12zhzrssr59188ws6t.
- [RO-style multi-hit + status (stun)](../combat-multihit-status.md) — `MultiHitBurst.Run` + `MonsterAI3D.Stun`.
- [Multi-hit SFX cut off at death](../multihit-sfx-onhit-cue-gated.md) — audible per-hit sound is `OnHitBySkill` VfxCue (gated by `wasAlive`), NOT empty `CombatSfx.skillHit`; fix = ungate in `TakeMagicDamageOverkill`.
- [RO combat fundamentals](../ro-combat-fundamentals.md) — `Element.cs`, `StatusController.cs` (DoT+CC), lifeSteal/reflectPct.
- [Trio-ring entries are "affixes" not "passives"](../affix-not-passive-terminology.md) — affix = % stat-modifier; passive = triggered.
- [Phase G skill: TRIO-RING %-passive MVP](../skill-system-mechanical-model.md) — 3 Trio + 1 wildcard, soft-cap DR; doc q17as16xh2gt1g058kemazjar588tvc8.
- [RO-style stat distribution](../stat-distribution-system.md) — STR/AGI/VIT/INT/DEX/LUK; flat layer under skill %; StatPanel.
- [Ring of Progression — concept](../ring-of-progression-concept.md) — doc q17122jsbff6fpvswcg3ht7w6989ka3m; tradeable Ring holds progression.
- [Active Skill Design Charter](../active-skill-design-charter.md) — doc q17c22jpppnztax6q9ppndmfds89n0x5; anti-stale; §8 hooks §9 currencies §10 PvP.
- [Active-skill archetype↔ring backbone](../active-skill-archetype-ring-mapping.md) — LOCKED 2026-07-18; 6 verbs hard/soft-mapped to stat ring; Light=Defense pole, Dark=MAtk pole; doc q170cz40rsxav6kq0r6pe431bn89me24.
- [Active-skill MVP built + ring REVAMPED 2026-07-19](../active-skill-mvp-ring-revamp-2026-07.md) — StatId now 10-node doc ring (old StatId STALE); affinity on SkillEffect; new effect kinds; Ultimate tier/charge/slot; 6 skills w/ PLACEHOLDER meteor VFX.
- [Conservation as universal balance driver](../conservation-as-balance-driver.md) — every gain pays a cost; Charter §10.
- [Ring of Progression IMPLEMENTED](../ring-of-progression-implemented.md) — RingProgress on Ring (slot 12); pick per 10 pts; SaveData v4.
- [Loot & melee reach](../loot-and-melee-reach.md) — reach = `Player3DAttacker.EffectiveRange`; click→route→collect.
