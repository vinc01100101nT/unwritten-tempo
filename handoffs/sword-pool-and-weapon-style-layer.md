# Handoff — Sword skill pool (near-done) + a new weapon "style layer" idea

**Task** — Finish the Sword weapon skill pool (V3 active-skill system) and decide a DS-inspired "style/typeface" flavor layer for all 12 weapons. Uses the **Three-Brain design process** (`docs/design-process.md`).

**Status** — Sword pool **LOCKED except 1 active** (still to design) and it hasn't had its **Stage-7 stress-test** yet. The style-layer idea is **exploratory, not locked**. Nothing committed.

## What's DONE this session

- **Sword pool authored & locked** in `docs/active-skill-system-v3/sword-skill-roster.md`:
  - **Ults (3):** Deathblow, Bladedance (full-invuln), Retribution (no cap).
  - **Actives (8 + 1 OPEN):** Lunge, Parry, Guard Stance, Overclock, Feint, Committed Strike, Disarm, Onslaught. **1 more active still to design** (was going to be a standalone single-target verb — candidates floated: Thrust / Guard Break; Sidestep/Pursuit/Riposte-Stance already rejected).
  - **Passives (9, complete):** Momentum, Perfect Form, Encore, Last Stand, Composure, Unwavering, Opening, Pressure, Lock Blades. Each ult has its special passive partner (Deathblow→Perfect Form, Bladedance→Encore, Retribution→Last Stand).
- **Momentum reframed** as a **rollable passive** (Deathblow's partner), NOT default weapon behavior. Option A locked: a Deathblow without Momentum is a flat ~600% nuke, no scaling.
- **Attack Speed cap (code)** — added `MaxAttackSpeedMult = 3f` clamp in `Character.cs` (`AttackSpeedMult` getter) to bound Momentum's equilibrium. **Not compile-verified** (user recompiles).
- **Wiki updated** — `wiki/content/sword.md` now lists the full pool, player-facing. (Wiki is the single source: `docs/wiki/` was collapsed into repo-root `wiki/` this session — see CLAUDE.md exception.)
- **Three-Brain design process** documented — `docs/design-process.md` (+ indexed). Reusable for all design work.
- Companion V3 docs updated: pool-size-is-arbitrary rule, universal-but-specialized heuristic, support-graph terms, state-doc first-consumer reframe, balance-baseline aspd-cap note.

## Design vocabulary locked this session
- **Anchor / Support (direct·indirect) / Standalone** — a skill either amplifies a specific *anchor* (support) or stands alone. Indirect support only counts if it chains through a *real* ult-support (never "leans toward"). Each ult must have ≥1 support (its "special"); some can be ult-exclusive (fine — passives are rolled per build). *(Player-facing names for these still TBD — "support/partner" felt off; "duo/trio" for clusters was floated.)*

## Uncommitted working-tree changes
- `unity/Assets/Scripts/Character.cs` — aspd cap.
- `docs/active-skill-system-v3/*` — sword-skill-roster (full pool), weapon-roster-12, skill-numbers-balance-baseline, state-system, README.
- `docs/README.md`, `docs/design-process.md` (new), `CLAUDE.md` (wiki exception).
- `docs/wiki/` **deleted** (collapsed into `wiki/`); `wiki/content/sword.md` (new), `skills.md`, `weapons.md`, `manifest.json` updated.
- `handoffs/sword-skill-roster-v3.md` deleted earlier; this file new.

## What's LEFT
1. **Design the Sword's 9th active** (standalone verb) → then run **Stage-7 stress-test** (GPT: STRESS-TESTER) on the full 9-active/9-passive/3-ult pool → Stage-9 review → final wiki refresh.
2. **The weapon "style layer" decision** (the big open idea — see below).
3. **Shield → off-hand?** Pending: replace Shield in the 12-main-weapon roster with a **Flower** weapon (proposed identity: perception/evasion glass duelist), moving Shield to off-hand gear so the tank role survives. NOT decided.
4. **Code TODOs for the Sword pool** (none blocking design): the **trigger system** for passives (event→condition→effect; the real build), **mob attack telegraph lead-time** (Feint/Opening), **mob HP regen + healer mobs** (Pressure), **Lock Blades↔Lunge combo**, parry-telegraph VFX. All logged in the roster doc's impl section.

## OPEN EXPLORATION — the weapon "style layer" (Demon Slayer–inspired)
Instinct: give each of the 12 weapons a named **combat "style"** with a personality, DS-Breathing-style, plus **numbered "Forms"** as a presentation skin over the existing rolled skills. **Legally:** fork the *structure* (elemental schools, numbered forms, signature ultimate) freely — ideas/mechanics aren't copyrightable — but **invent our own names/art**, never use "Breathing"/DS terms, don't mirror DS's exact 14-style set, never name-drop DS. Reskin to *unwritten's* lost-text lore.

**The standout idea: map each weapon to a FONT FAMILY** (perfect for a game about a lost *text* — weapons = the recovered typefaces of the broken book; each writes in a different hand). Proposed mapping (trait-matched, IP-clean):
- Sword→**Serif** (classic all-rounder) · Greatsword→**Slab Serif** (heavy blocks) · Axe→**Handwritten/Marker** (feral scrawl) · Warhammer→**Black/ultra-bold** (crushing weight) · Spear→**Condensed** (long reach) · Bow→**Italic** (in motion) · Dagger→**Hairline/Didone** (fine cut) · Firearm→**Monospace** (mechanical rhythm ✓✓) · Staff→**Display/Decorative** (loud, area) · Wand→**Uncial/Illuminated** (gilded, mends) · Scythe→**Blackletter/Fraktur** (gothic death-hand ✓✓) · Shield→**Rounded Grotesque** (weak fit; drops if Shield→off-hand).
Decisions still needed: is the style layer **cosmetic-only or mechanical**? adopt font-families vs generic-elements vs ink-motif? does it change the **elements + card-resist** system we already have in code (Element enum, cards give elemental resists)?

## Next single step
Design the Sword's **9th active** (Stage 2: Claude opens candidates → Vince filters), OR make the **style-layer call** (cosmetic vs mechanical; font-family yes/no) — Vince picks which thread first.

## How to resume
Fresh session: `Read handoffs/sword-pool-and-weapon-style-layer.md and docs/design-process.md, then the Sword roster at docs/active-skill-system-v3/sword-skill-roster.md. We're finishing the Sword pool (1 active left + stress-test) and deciding a DS-inspired weapon "style layer" (font-family mapping). Continue via the Three-Brain flow.`
