# Handoff — Unified per-unit state store (`UnitStates`)

**Task** — Build the V3 State-System doc's unified per-unit state store so buffs + debuffs share ONE store per unit, driving both gameplay AND display (player buff tray + mob/other-player nameplate icons). Docs: `docs/active-skill-system-v3/implementation-notes.md` ("Unified state store" section) + `state-system-buffs-debuffs-combo-marks.md`.

**Status** — **ALL CODE WRITTEN, NOTHING PLAY-TESTED.** Treat every "it works" here with suspicion: this is balance-critical and must be combat-verified.

Four layers, each code-complete and each unverified:
1. Gameplay migration onto the store (2026-08-04/06)
2. Display — buff tray + mob nameplate icons (2026-08-06)
3. Stacking / charges / combo gate / no-self-feeding brake (2026-08-07)
4. Character window full stat readout + live state deltas (2026-08-07)

**Test BOTTOM-UP in that order**, or a failure won't tell you which layer caused it.

Verification actually done: the C# compiler bundled inside the Unity install (the same one Unity uses; no Unity refresh needed) was run over all runtime + editor scripts — **zero errors in every touched file**. That proves the code is valid, **not** that it behaves; a wrong formula compiles perfectly. Behaviour is parity-reasoned only. Also grep-clean: no `PlayerBuffs` / `.Buffs` / `RegisterBuff` / `ai.Slow` / `slowFactor` references left.

Full code-side record: `docs/active-skill-system-v3/implementation-notes.md`.

## Display phase — what was built this session (code-complete)

- **Store carries display metadata via a latch, not signature threading.** `UnitStates` got `SetDisplaySource(icon,name,description)` / `ClearDisplaySource()` — a transient source the NEXT `Apply` reads when the caller passed none (consumed by the applier's set-before / clear-after around each durational gameplay call). Keeps display data OUT of gameplay method signatures (BeginGuard/BuffDamage/StatusController.Apply stay display-free).
- **Player tray** (`BuffBarPanel`) now reads `Character.States.Active` (was `Character.Buffs`). Cells are `UnitStates.State`. **Behavior change to verify:** the tray now surfaces the WHOLE store — buffs AND any debuffs on the player — not just self-cast buffs. No-icon states fall back to the `StateId` name label.
- **Mob nameplate** (`MobNameplate`) renders a **pooled** debuff-icon row above the HP bar from `Health.States.Active` — rebuilt only on `OnChanged` / expiry (per-frame `states.Prune()` while shown; no per-frame alloc). **Known limitation:** icons only show while the plate is visible, i.e. once the mob's been damaged (`hideAtFullHealth`) — a slowed-but-full-HP mob shows no icon yet. Left as-is (changing hide-at-full-HP is a separate design call).
- **Applier** (`SkillEffectApplier`): player path stamps via the latch (removed `RegisterBuff` + the `PlayerBuffs` mirror). Mob path threads `srcIcon`/`srcName` (mirrors the existing `caster` threading) through `DispatchMobHits`→`ApplyEffectsToMob`/`ApplyHitToMob`→`ApplyOneToMob`, stamping Status/Haste/Vulnerability debuffs on the mob's store (description null — nameplate has no hover card, avoids per-mob string alloc).
- **`PlayerBuffs.cs` (+ `.meta`) DELETED.** `Character.Buffs` property + `Buffs.Clear()` in `Revive()` removed (`States.Clear()` already wipes the tray now).

## What's done

- `UnitStates.cs` (new) — the store: pooled `State` entries (zero-alloc add/remove), cached derived scalars recomputed only on change (O(1) readers), **lazy self-expiry** via `EnsureFresh` (no per-frame `Update`), **newest-wins** overwrite. Getters: `MoveMult`, `CannotAct`, `Silenced`, `HitPenalty`, `GuardCut`, `VulnMult`, `DamageBuffMult`/`AttackSpeedBuffMult`/`CastSpeedBuffMult`, `ManaLeechPct`, `ReflectBonus`, `ShieldPool`, `Remaining`, `Get`, `DrainShield`, `Active` (display list), `OnChanged`.
- **Migrated onto it (gameplay reads/writes):**
  - `Health.cs` — Vulnerability (`Health.States`, lazy per mob).
  - `Character.cs` — Guard, AttackSpeed/CastSpeed buffs, DamageBuff **incl. War-Banner ramp**, Regen (ticks off entry `nextTick`), ManaLeech, Reflect, Shield (drains via `DrainShield`), `Revive()` → `States.Clear()`. All old buff fields deleted.
  - `StatusController.cs` — all ailments (Poison/Bleed/Burn DoT + auras/tick-cues, Stun/Freeze, Slow, Silence, Blind) + Haste. Kept: VFX auras, mob-AI delegation (`ai.Stun`/`Slow`/`Stagger`), DoT tick, Cleanse, action-lock (`recoverUntil` — NOT a state). `Ailment i ↔ StateId i`.
  - **Every unit shares ONE** `UnitStates` (Health/Character/StatusController all `GetComponent<UnitStates>() ?? AddComponent`). **⚠ CORRECTED 2026-08-06:** this was FALSE for the PLAYER — the player is TWO GameObjects (persistent `Character` data GO `763514983` + spawned body GO `236044104` with `Player3DController`+`StatusController`), so it had TWO stores. Fixed: the player's `StatusController` now shares `Character.Instance.States` (lazy resolve). Mobs are one GO, already fine.
- **Adjacent (also this session):** passive trigger system (`PassiveEvents.cs` + `SkillRunner` `FireAsPassive`, `SkillDef.trigger`/`triggerSkillId`); natural-event fire-sites (OnBasicAttackLanded/OnHit/OnKill/OnLowHP); composer **State ▸ Buffs/Debuffs** picker; tray auto-register = **duration-based** (`seconds > 0`, instant effects excluded because `seconds` now defaults **0** + composer keeps non-durational kinds at 0); **movement time ≠ state** — Dash/Knockback/Pull are top-level actions, Knockback/Pull shove-time split into `SkillEffect.shoveSeconds`.

## Uncommitted working-tree changes (all revertible; NOTHING committed)

- New: `UnitStates.cs`, `PassiveEvents.cs` (+ `.meta`).
- Deleted: `PlayerBuffs.cs` (+ `.meta`) — retired by the display migration.
- Modified: `Character.cs`, `Health.cs`, `StatusController.cs`, `SkillEffect.cs`, `SkillEffectApplier.cs`, `SkillRunner.cs`, `SkillDef.cs`, `Player3DAttacker.cs`, `Editor/SkillComposerWindow.cs`, `BuffBarPanel.cs`, `MobNameplate.cs`.
- Assets: 9 skill `.asset`s had instant-effect `seconds` zeroed (Damage/Heal/etc. leaking the old default 4). `skill.new 3.asset` untracked.
- Docs: `implementation-notes.md`, `README.md`.

## What's left

1. **Combat-test the gameplay migration** (can't be verified without Play): Guard % cut, Vulnerability, Slow/Haste move-speed (`MoveMult`), Stun/Freeze gating, Silence, Blind, DoT ticks, Shield absorb+break, Regen, DamageBuff ramp, and death→`States.Clear()`.
2. **Play-test the display phase** (now code-complete): cast a buff → tray cell shows the hotbar icon + countdown + fills as it expires; cast a debuff on a mob → its nameplate icon row appears above the bar and clears on expiry; verify no debuffs incorrectly flood the player tray; death clears the tray. Watch `PerfHud` (F3) `GC/s` ≈ 0 during an AoE debuff on many mobs.

## Dead ends / gotchas (proven this session)

- **Tray validates by DURATION, not by kind.** A kind-based gate was tried and reverted — do NOT reintroduce it.
- Root cause of the "Heal shows an icon" bug: `SkillEffect.seconds` **defaulted to 4** and the composer never reset it for instant kinds → measured, fixed (default 0 + composer guard + asset migration). `seconds` now means **ongoing-condition duration only**.
- **Movement time is not a state.** Dash/Knockback/Pull are actions; their timers are movement, not conditions → `shoveSeconds`, `seconds = 0`, no icon.
- Ailment refresh changed from longer-wins (`Max`) to **newest-wins** — intended per the doc, but it IS a behavior change; watch for it in playtest.

## Next single step

**Play-test, bottom-up.** Everything is written and compile-verified; nothing is behaviour-verified. Four layers now sit on each other, so test in this order or a failure won't localise:

1. **Gameplay migration** — guard %, vulnerability, slow/haste move speed, stun/freeze gating, silence, blind, DoT ticks, shield absorb+break, regen, damage-buff ramp, death clears states.
2. **Display** — buff tray + mob nameplate icons appear/expire with the right icon.
3. **Stacking** — author a stacking effect; watch stacks climb, show a count badge, and FIFO-expire. Then a charge-granting skill + a consumer.
4. **Character sheet (C)** — every stat listed, green/red parentheses appear while buffed, nothing clips at the window's bottom edge.

Then collapse the durable lessons into memory and delete this handoff.

**Content still to author (user's job):** Lock Blades + Momentum — blocked on the roster's open auto-pursue question (one engine-wide mechanism: attack-lunge / pathfind / forced facing).

## How to resume

Read `handoffs/unified-state-store.md` + the "Unified state store" section of `docs/active-skill-system-v3/implementation-notes.md`, then combat-test the gameplay migration before writing the display phase.