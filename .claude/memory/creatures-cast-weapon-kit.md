---
name: creatures-cast-weapon-kit
description: "Creatures cast their held weapon's rolled kit plus one extra skill slot — actives/ult in place of a swing, passives on triggers, paying MP; their skills land on PEOPLE. Built 2026-09-16, extra slot 2026-09-17, unplayed."
metadata:
  type: project
---

**Built 2026-09-16 at the user's request (unplayed — `docs/1-planning/play-test-checklist/` section 9).**

- **One skill list for every caster:** `UnitSkills` (extracted from `Character`) holds defs, lazily-built runners and the
  trigger index. `ICombatUnit.Skills` replaced `SkillRunners`; `ICombatUnit.PassiveIsOn` is the ownership gate
  (`Character` → `PassiveSkills.IsActive`; `Health` → always true, its list is only the weapon kit + extra skills). `SkillRunner.Caster`
  is an `ICombatUnit`; the press-only walk-into-range stays on `Character`.
- **A creature's list:** `Health.Skills` rebuilds from the held weapon's `skillIds` plus `Health.ExtraSkillIds` (catalog order) whenever
  `UnitEquipment.Version` or the extra skills change, server-side only, and calls `SkillRunner.ForgetCooldown` so a new life starts ready.
- **Extra skill slot (2026-09-17, user decisions):** `ExtraSkillRoll.Slots = 1` for every creature (subject to change). Pool = every PUBLISHED skill of any weapon type, ultimates and passives included, minus the weapon's kit. Re-rolled per life in `MobRespawn.Reactivate` after the gear reseat; first life rolls lazily in `Health.Skills`. `/summon … <skill id>` → `Health.PinExtraSkill`: any catalog id (unpublished allowed), kept through every respawn. Future: per-biome hand-written extras.
- **When it casts:** `MonsterAI3D.TryCastAtPrey` at the swing moment — ultimate first, else a random ready one
  (`SkillRunner.CouldCastNow` + cast range); the swing cadence paces it. `StatusController.SkillRooted` halts the brain
  during its own wind-up/aftercast.
- **Sides are relative:** in `SkillEffectApplier`, `SkillTargets.Mobs` in a creature's hands reaches PEOPLE
  (`CastByCreature`, `GatherPeople`, `DispatchPeopleHits`, `MultiHitBurst.RunOnUnits`, `RandomOpponentNear`,
  `OpponentAt`). A creature's skill never hits other creatures. Decision made by the session, not the user.
- **Watchers:** cast announcements take `ICombatUnit` (`IdentityOfCaster` → `NetMob` shell). The effect, cast bar and ring
  are drawn by the shared `WatchedCast` (used by `NetPlayer` and `NetMob`); a creature's skill call is only a bubble
  over its head (`ChatController.CreatureCall`), never a chat-log line. A person aimed at is drawn on
  `NetPlayer.DrawnBody`.
- **Bubble gotcha (2026-09-16 play-test):** a flat sprite creature has no 3D pose, so a pose-only bubble was silently skipped on it; `ChatController.CreatureCall` now falls back to the view body at 2×`UnitAnchor.HalfHeight` + `SpeechBubble.HeadClearance`. Most field creatures are flat.
- **Cheap skills starve the rest (2026-09-16 play-test, ~1 hour: only Lunge + Parry seen):** field creatures are level 3–5 with 17–23 max MP and 1.5 MP/s regen; random-among-affordable lets the two 8-MP actives take ~5 casts per 30s fight vs ~1 for each 12-MP one; 40-MP ultimates are never affordable below level 11 (INT 4, no gear MP). Replay: scratchpad mobcast_sim.py (session-only). Selection-rule change awaiting the user's decision.
- **Cast log (2026-09-16):** with `/log cast on`, a creature writes `weighed its skills: <id> <ready|reason>; … — picked <id>` or `— none ready, swinging` (`SkillRunner.BlockedBy`/`DescribeBlock`, `MonsterAI3D.TryCastAtPrey`). Deduped: a pick always writes, a wait only when the reason KINDS change (`lastNoneReadyReasons`). Replayed: ~24 lines per fighting creature per minute vs 60 undeduped. The AI pre-filters, so it never sends a cast that would be refused for mana.
- **Reach, weapon in hand, skill poses (2026-09-16):** `MonsterAI3D.HoldDistance` no longer caps a creature at the prey's reach when its weapon sets `attackRange` > 0 (bow = 5; the cap remains for authored/unarmed reach). Shadow creatures get a `WeaponSocket` bound to their `UnitEquipment` (new `OnChanged` event). A creature caster's casting pose (held, window = wind-up) and release stance (action, beat = SwingTime, or the repeat interval) go out through `GameNetwork.AnnounceHeldOnCaster` / `AnnounceActionOnCaster` → `NetMob` SyncVars → the shared `Agent3DVisual.TellHeld` (NetPlayer now uses it too). No creature has a `projectileSheet` (0 of 86 prefabs), so a bow creature's blow lands with no arrow.
- **Open defects found 2026-09-16, not fixed (need a decision or a session):** (1) Retribution's description says its damage is "split across 5 strikes" and it "does nothing with an empty bank", but each of its 5 strikes lands the full Attack + 250% of the banked total, and its gate needs 0 stacks, so with nothing banked it still fires for 40 MP and 5 × Attack. (2) Only `Character` fires `OnLowHP`, `OnParry` and `OnDamageReceived` — a creature holding Last Stand, Composure or anything answering those never fires them (mobs-get-everything violation). (3) Other players' copies on your screen show no weapon (only the local avatar gets a `WeaponSocket`). (4) 0 of 86 creature prefabs have a `projectileSheet`, so a bow creature's blow lands with no arrow. (5) Creature skill choice: user decision pending (save up vs most expensive affordable; ultimates unaffordable below level 11).
- **Not a bug:** a player "getting Brace without having it" — their weapon rolled Last Stand, which arms 5 Brace charges on low HP.
- **Gaps:** skill damage a creature deals earns it no creature XP (only swings call `GainCombatXp`); no cast pose on the
  creature; only Sword skills are published.

Related: [[kill-loot-on-ground-random-creature-gear]], [[mobs-get-everything-players-get]], [[whole-skill-runs-on-the-server]].
