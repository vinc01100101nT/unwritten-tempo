# Combat / skills / progression / stats design

- [Weapon roster — 12 locked identities](../weapon-roster-12-identities.md) — the 12 weapons + one-sentence identity each (litmus test for every skill); doc q17d4q80p9rj4exmezrxhnkc8h8bad3x; Scythe/Shield need new WeaponTypes; no rosters authored yet — all 12 pools start fresh.
- Skill Numbers & Balance Baseline LOCKED 2026-07-28 (doc q17b9j3cjpvwf271kvdz0qf1s98bdrbz, under V3 parent). Measured anchors: L1 player HP20/MP10/Atk4/MAtk3/Def1; damage ≈ Attack×damageMult×affinity; normal damageMult ~1, ULT (meteor) =8 + multi-hit, chargeCost 5000 vs 100, tier 1. **Scaling defaults LOCKED** (closes V3 "Still open"): damage→Atk/MAtk; heal/regen/shield→MP; Guard dmg_reduction→Def; dot_damage→its own damage type; offense debuffs (vuln/slow/blind)→caster offense stat; buff_mult FLAT; Haste move_mult FLAT; ALL durations→MP; shove/leech/reflect raw. Composer starter-weights not yet updated to match.

- [Affinity channel must scale in applier](../affinity-channel-must-scale-applier.md) — a composer "boosts X" channel is a DEAD KNOB unless the applier passes ScaledMagnitude; Guard/Vulnerability were raw (fixed 2026-07-24). Catalogs window flags it (⚠).

- [Dash skill + aftercast root gotcha](../dash-skill-and-aftercast-root.md) — `SkillEffectKind.Dash`→`BeginDashToPoint` (visible gap-close to aimed point); skill movement must survive its OWN aftercast (`!CannotAct`) but die to hard-CC.
- [Dash ease-out killed by duplicate tickId](../dash-easeout-tick-reset.md) — 3 trail-puff steps all had `tickId: dash` → Dash effect fired 3× → `BeginGlideTo` restart reset `dashTotalDist` → ease baseline wiped. Real fix = CONFIG (unique tickId; trail puffs pure-visual); guardrail = composer warns on duplicate tickId. tickIds must be unique.

- [Weapon affixes ≠ ring voices — never duplicate](../weapon-affix-vs-ring-voice-separation.md) — weapon=control/targeting/util (Slow live), ring=stat voices; library in V2 doc; don't put Execute/Repeat/Expand on a weapon.

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
- [Passives must be COMPOSABLE (not hardcoded)](../sword-momentum-foundation.md) — passives = Skill Composer `SkillDef`s with a trigger, NOT C# classes (hardcoded Momentum reverted 2026-08-04); kept `CombatEvents.cs` trigger bus; composable plan (SkillTrigger + SkillRunner passive mode + grant routing + stacking Buff) in `handoffs/sword-skill-implementation.md`; aspd cap + ult-charge already existed (docs corrected).
- [Combat network-readiness seams](../combat-network-readiness-seams.md) — ALL combat damage routes `CombatAuthority.Apply`/`ApplyTrue`, all rolls `CombatRng`; caster threaded in `SkillEffectApplier`; committed `9f393fac`. Networking DEFERRED until vertical slice proves fun (don't re-fork direct `TakeDamage`/`Random`).
- [Player is TWO GameObjects → ONE store](../player-two-gameobjects-one-store.md) — persistent Character data GO + spawned body GO; player StatusController must share Character.Instance.States or states split (Haste sped player but showed no icon); mob mover reads Health.States.MoveMult not a private slowFactor.
- [Caster-side vs target-side effect passes](../skill-effects-caster-vs-target-passes.md) — Dash silently skipped on a Single-Target tick; `ApplyBurst` now runs a caster pass + a target pass, partitioned by `SkillEffect.IsSelfVerb` (don't add a 2nd classifier).
- [Unified unit stats: player === mob](../unified-unit-stats-implemented.md) — ONE stat model, shared formulas + point budget; tier / perLevelGrowth / global multiplier all DELETED; mobs wear gear and drop what they wear.
- [Pre-action trigger = ONE attempt, not a timer](../pre-action-trigger-one-attempt.md) — `OnBasicAttackStart` chain-fired because its parallel intent timer had always expired before it was read; announce now OPENS an attempt closed by `TryAttack`/`Cancel`. Charges spend on FIRE, never refunded (`StateGate`).
