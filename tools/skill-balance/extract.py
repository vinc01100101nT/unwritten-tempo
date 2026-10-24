#!/usr/bin/env python3
"""Skill balance report — STEP 1, the simple version.

Reads every weapon skill straight from its saved file, writes the numbers the formula needs to
skills-data.js (which the calculator page also loads), then prints the report through formula.js — the one
copy of the formula, shared with the calculator.

    python tools/skill-balance/extract.py

Only prices that come from the game's own rules are counted. Anything that would need a guessed weight
(a taunt, a dash, a slow, a passive) is listed as not priced, to be judged by hand and by play-testing.

The full balancing system is planned and waiting on play-test calibration:
docs/2-systems/active-skill-system-v3/skill-balancing-system-plan.md
"""
import importlib.util
import json
import math
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(HERE)

# The asset and enum reader is the skill-data checker's — imported, never copied.
_spec = importlib.util.spec_from_file_location("check_skill_data", os.path.join(TOOLS, "check-skill-data.py"))
csd = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(csd)

SKILLS_DIR, SCRIPTS, ASSETS = csd.SKILLS_DIR, csd.SCRIPTS, csd.ASSETS
ITEMS_DIR = os.path.join(ASSETS, "3_Data", "Resources", "Items")
SEQ_DIR = os.path.join(ASSETS, "3_Data", "Resources", "VfxSequences")
WEAPONS = ("sword", "bow", "staff")

# Marks a cash-in is priced at (decided 2026-10-24: 2, what one applying skill usually leaves).
MARKS = 2
# A looping step with no Stay Duration on its binding runs this long (VfxDirector.DefaultStay).
DEFAULT_STAY_NAME = "DefaultStay"

# Bow skill animations, measured 2026-10-24 in the editor from the clips the stance library names
# (SkillAnimation1Bow / 2 / 3): seconds the caster is locked when the skill is instant, and after a wind-up.
BOW_SKILL_ANIMATION = {1: (0.70, 0.35), 2: (0.70, 0.667), 3: (0.70, 0.70)}


def src(name):
    return csd.read(os.path.join(SCRIPTS, name))


def const(source, name):
    m = re.search(r"\b%s\s*=\s*([0-9.]+)f?" % re.escape(name), source)
    if not m:
        csd.die("Could not find %s in the game's C#; the report reads it instead of keeping a copy." % name)
    return float(m.group(1))


def swing_seconds(weapon_types):
    """Each weapon's base swing, from its one item and the game's own swing rule (UnitOffense.SwingSeconds)."""
    off = src("UnitOffense.cs")
    bare, per = const(off, "BareSwingSeconds"), const(off, "SwingSecondsPerWeight")
    dual_src = re.search(r"static bool IsDualWield\(WeaponType w\)\s*=>(.*?);", src("WeaponType.cs"), re.S)
    dual = set(re.findall(r"WeaponType\.(\w+)", dual_src.group(1))) if dual_src else set()
    out = {}
    for f in os.listdir(ITEMS_DIR):
        if not f.endswith(".asset"):
            continue
        a = csd.parse_asset(os.path.join(ITEMS_DIR, f))
        wt = csd.as_int(a.get("weaponType"))
        if wt == 0 or "weight" not in a:
            continue
        name = next((k for k, v in weapon_types.items() if v == wt), None)
        w = csd.as_float(a.get("weight"))
        out.setdefault(wt, bare + per * (w * 0.5 if name in dual else w))
    return out


def sequences():
    """{guid: parsed sequence} for every effect sequence a skill can point at."""
    out = {}
    for f in os.listdir(SEQ_DIR):
        if f.endswith(".asset.meta"):
            m = re.search(r"guid:\s*([0-9a-f]+)", csd.read(os.path.join(SEQ_DIR, f)))
            if m:
                out[m.group(1)] = csd.parse_asset(os.path.join(SEQ_DIR, f[:-5]))
    return out


def flag(v):
    return csd.as_int(v) == 1


def main():
    effect_src, def_src = src("SkillEffect.cs"), src("SkillDef.cs")
    kinds = {v: k for k, v in csd.parse_enum(effect_src, "SkillEffectKind").items()}
    subs = {v: k for k, v in csd.parse_enum(effect_src, "SubStat").items()}
    triggers = {v: k for k, v in csd.parse_enum(def_src, "SkillTrigger").items()}
    tmode = csd.parse_enum(def_src, "TargetMode")
    delivery = csd.parse_enum(def_src, "GroundDelivery")
    dmg_type = csd.parse_enum(effect_src, "DamageType")
    states = csd.parse_enum(src("UnitStates.cs"), "StateId")
    state_for_kind = csd.parse_state_for_kind(effect_src)
    pile_for_stat = csd.parse_pile_for_stat(effect_src)
    ailments = {v: k for k, v in csd.parse_enum(src("StatusController.cs"), "Ailment").items()}
    step_kind = csd.parse_enum(src("VfxSequence.cs"), "VfxStepKind")
    tick_when = csd.parse_enum(src("VfxSequence.cs"), "TickTiming")
    mark_names = {v: k for k, v in csd.parse_enum(effect_src, "MarkKind").items()}
    default_stay = const(src("VfxDirector.cs"), DEFAULT_STAY_NAME)
    base_crit = const(src("UnitStatMath.cs"), "BaseCritMultiplier")
    wtypes = csd.parse_enum(src("WeaponType.cs"), "WeaponType")
    wname = {v: k for k, v in wtypes.items()}
    swings = swing_seconds(wtypes)
    seqs = sequences()

    def pile_of(e):
        """Which pile an effect leaves — the game's own effect→pile switch (SkillEffect.TryStateFor)."""
        k = kinds.get(csd.as_int(e.get("kind")), "")
        st = state_for_kind.get(k)
        if st == "*" and k.startswith("SubStat"):
            st = pile_for_stat.get(subs.get(csd.as_int(e.get("subStat")), ""))
        return states.get(st) if st and st != "*" else None

    def stacks_given(e, swing):
        """Stacks one apply gives — SkillEffectApplier.ResolveStacks."""
        authored = max(1, int(csd.num_of(e.get("stacksPerApply"), 1.0)))
        if not flag(e.get("stacksFromAttackSpeed")) or not swing:
            return authored
        return max(1, math.ceil(csd.num_of(e.get("seconds")) / swing * max(0.0, csd.num_of(e.get("stackShare"), 1.0))))

    skills = []
    files = sorted(f for f in os.listdir(SKILLS_DIR) if f.endswith(".asset") and f.split(".")[1] in WEAPONS)
    # Every skill's cooldown by id, so a passive fired by casting a named skill knows how often that happens.
    assets = {}
    for f in files:
        a = csd.parse_asset(os.path.join(SKILLS_DIR, f))
        assets[a.get("id")] = a
    cooldowns = {i: csd.as_float(a.get("cooldown")) for i, a in assets.items()}

    def run_of(a, swing):
        """How many beats one press plays, the pile that pays for them, and how long the run's window lasts
        (SkillRunner: the authored count, a charge run, or a pile-held run)."""
        trig_entries = a.get("triggers") or []
        effects_all = [e for b in (a.get("bindings") or []) for e in (b.get("effects") or [])]
        bps = max(1, csd.as_int(a.get("repeatBeatsPerSwing"), 1))
        interval = csd.as_float(a.get("repeatInterval"))
        beat = swing / bps if flag(a.get("repeatScaleWithAttackSpeed")) and swing else (interval if interval > 0 else 0.35)
        out = dict(pile=None, repeats=max(1, csd.as_int(a.get("repeatCount"), 1)), beat=beat, window=0.0,
                   reasons=[], extras=[])
        if flag(a.get("repeatCostsCharge")):
            gate = next((csd.as_int(t.get("requireStateId")) for t in trig_entries
                         if csd.as_int(t.get("trigger")) == 0 and flag(t.get("requiresState"))), None)
            payer = next((e for e in effects_all if csd.as_int(e.get("side")) == 0 and pile_of(e) == gate), None)
            if payer is None:
                out["reasons"].append("a charge run whose charges this skill does not give")
            else:
                out.update(pile=gate, repeats=stacks_given(payer, swing), window=csd.num_of(payer.get("seconds")))
        elif flag(a.get("repeatWhilePileHeld")):
            pile = csd.as_int(a.get("repeatPileStateId"))
            payer = next((e for e in effects_all if csd.as_int(e.get("side")) == 0 and pile_of(e) == pile), None)
            if payer is None:
                out["reasons"].append("a run held by a pile this skill does not give")
            else:
                secs = csd.num_of(payer.get("seconds"))
                cap = csd.as_float(a.get("repeatMaxSeconds"))
                if cap > 0:
                    secs = min(secs, cap)
                out.update(pile=pile, repeats=int(secs // beat) + 1, window=secs)
            if a.get("repeatBonusSkillId"):
                out["extras"].append("extra beats from %s's stacks" % a.get("repeatBonusSkillId"))
        if flag(a.get("repeatScaleWithCastSpeed")):
            out["extras"].append("more beats with cast speed")
        if not out["window"]:
            out["window"] = max([csd.num_of(e.get("seconds")) for e in effects_all] + [0.0])
        return out

    # Skills that fire FREE while a pile is held (a non-press moment gated on that pile): the pile's giver prices them.
    free_firers = {}
    for i, a in assets.items():
        for t in a.get("triggers") or []:
            if csd.as_int(t.get("trigger")) != 0 and flag(t.get("requiresState")):
                # a pile is one per granting skill, so the key is the granter AND the pile
                free_firers.setdefault((t.get("requireSkillId") or i, csd.as_int(t.get("requireStateId"))), []).append(i)
    parry_cds = [cooldowns[i] for i, a in assets.items() if cooldowns[i] > 0 and any(
        kinds.get(csd.as_int(e.get("kind"))) == "Parry" for b in (a.get("bindings") or []) for e in (b.get("effects") or []))
        and any(csd.as_int(t.get("trigger")) == 0 for t in (a.get("triggers") or []))]
    companions = {a.get("companionOf"): i for i, a in assets.items() if a.get("companionOf")}

    for f in files:
        a = csd.parse_asset(os.path.join(SKILLS_DIR, f))
        wt = csd.as_int(a.get("weaponType"))
        weapon = wname.get(wt, str(wt))
        # The wind-up is the scaled cast time plus the fixed part (UnitStatMath.CastWindupSeconds), at a cut of 0.
        cd, mp = csd.as_float(a.get("cooldown")), csd.as_float(a.get("manaCost"))
        cast = csd.as_float(a.get("castTime")) + csd.as_float(a.get("fixedCastTime"))
        swing = swings.get(wt)
        reasons, unpriced = [], []
        trig_entries = a.get("triggers") or []
        trig = [csd.as_int(t.get("trigger")) for t in trig_entries]
        passive = 0 not in trig
        fire_rate = None
        if passive:
            # How often a passive's moment comes, from the game's own rhythms; any other moment is a guess.
            if len(trig_entries) != 1:
                reasons.append("passive with several moments — how often it fires is not read")
            else:
                t = trig_entries[0]
                name = triggers.get(csd.as_int(t.get("trigger")), "?")
                named = t.get("triggerSkillId") or ""
                gate_skill = t.get("requireSkillId") or ""
                gated_on_run = (flag(t.get("requiresState")) and gate_skill in assets and cooldowns.get(gate_skill, 0) > 0
                                and run_of(assets[gate_skill], swing)["pile"] == csd.as_int(t.get("requireStateId")))
                if name in ("OnBasicAttackDealt", "OnDamageDealt") and not named and gated_on_run:
                    fire_rate = dict(kind="duringRun", skillId=gate_skill, cd=cooldowns[gate_skill])
                elif name in ("OnBasicAttackDealt", "OnDamageDealt", "OnBasicAttackStart") and not named and swing:
                    fire_rate = dict(kind="cooldown", secs=swing)    # once a swing (skills landing on top not counted)
                elif name == "OnDamageReceived" and not named:
                    fire_rate = dict(kind="enemy")                   # once an enemy blow
                elif name == "OnSkillCastComplete" and named and cooldowns.get(named, 0) > 0:
                    fire_rate = dict(kind="cooldown", secs=cooldowns[named])
                elif name == "OnDodge" and not named:
                    fire_rate = dict(kind="dodge")
                elif name == "OnKill" and not named:
                    fire_rate = dict(kind="kill")
                elif name == "OnKill" and named and named in assets and cooldowns.get(named, 0) > 0:
                    # kills made BY that skill: only while its run lasts, once per its cooldown
                    fire_rate = dict(kind="killDuring", window=run_of(assets[named], swing)["window"], cd=cooldowns[named])
                elif name == "OnLowHP":
                    fire_rate = dict(kind="lowHp")
                elif name == "OnParry" and parry_cds:
                    fire_rate = dict(kind="parry", secs=min(parry_cds))
                else:
                    reasons.append("how often %s%s happens in a fight is a guess" % (name, " (" + named + ")" if named else ""))
        elif len(trig) > 1:
            gated = [t for t in trig_entries if csd.as_int(t.get("trigger")) != 0]
            if not all(flag(t.get("requiresState")) for t in gated):
                reasons.append("also fires for free on " + ", ".join(triggers.get(t, str(t)) for t in trig if t != 0))
            # a free firing gated on a pile is priced on the skill that gives that pile
        if a.get("companionOf"):
            reasons.append("priced inside " + str(a.get("companionOf")))
        if flag(a.get("isOrbRelease")):
            reasons.append("Release — casts a combination, has no value of its own")
        if swing is None:
            reasons.append("no weapon item found to read its swing time")

        # ---- how many beats one press plays; growth that needs an investment is an EXTRA on top, not priced
        ro = run_of(a, swing)
        reasons += ro["reasons"]
        extras = list(ro["extras"])
        run_pile, repeats, beat = ro["pile"], ro["repeats"], ro["beat"]
        run = repeats * beat if flag(a.get("holdCasterForRun")) and repeats > 1 else 0.0

        lock = 0.0
        if flag(a.get("hasReleaseStance")):
            anim = csd.as_int(a.get("weaponSkillAnimation"))
            if anim == 0:
                lock = swing or 0.0
            elif weapon == "Bow" and anim in BOW_SKILL_ANIMATION:
                lock = BOW_SKILL_ANIMATION[anim][1 if cast > 0 else 0]
            else:
                reasons.append("its weapon skill animation's length has not been measured")

        # ---- ticks per cast, from the skill's effect sequence (VfxDirector.ArmTicks)
        seq = seqs.get(csd.guid_of(a.get("vfx")) or "", {})
        stays = {b.get("tickId"): csd.as_float(b.get("stayDuration")) for b in (a.get("bindings") or [])}

        def ticks_of(tick_id, radius):
            n = 0
            for st in seq.get("steps") or []:
                if st.get("tickId") != tick_id:
                    continue
                if csd.as_int(st.get("tickWhen")) != tick_when["Interval"]:
                    n += 1
                    continue
                looping = flag(st.get("looping")) and csd.as_int(st.get("kind")) in (step_kind["PlayAt"], step_kind["Attach"])
                if not looping and csd.as_int(st.get("kind")) == step_kind["Carrier"] and csd.as_float(st.get("carrierSpeed")) > 0:
                    inside = 2 * max(0.1, radius) / csd.as_float(st.get("carrierSpeed"))
                    n += max(1, int(round(inside / max(0.02, csd.as_float(st.get("tickInterval"))))))
                    continue
                if not looping:
                    return None   # an interval over an effect's own length — not readable here
                length = stays.get(tick_id) or default_stay
                # the game keeps ticking while the next tick is within 1e-4 s of the end (VfxDirector), so round the same way
                n += int(length / max(0.02, csd.as_float(st.get("tickInterval"))) + 1e-4) + 1
            return n or 1

        cast_range = csd.as_float(a.get("castRange"))
        parts, effects = [], []
        for b in a.get("bindings") or []:
            tid = b.get("tickId")
            ticks = ticks_of(tid, csd.as_float(b.get("radius")))
            if ticks is None:
                reasons.append("tick '%s' fires at intervals that are not readable from the files" % tid)
                ticks = 1
            mode, dlv = csd.as_int(b.get("targetMode")), csd.as_int(b.get("groundDelivery"))
            radius = csd.as_float(b.get("radius"))
            if mode == tmode["Self"]:
                targets, ranged = 1.0, False
            elif mode == tmode["SingleTarget"]:
                jumps, kept = csd.as_int(b.get("bounceJumps")), csd.as_float(b.get("bounceDamageKept"), 0.75)
                targets, ranged = 1.0 + sum(kept ** i for i in range(1, jumps + 1)), cast_range >= 8
            elif dlv == delivery["Projectile"]:
                targets, ranged = 2.0, cast_range >= 8
            else:
                targets = 1.5 if radius < 2 else 2.0
                ranged = dlv != delivery["AroundCaster"] and cast_range >= 8
            # A tick that moves the caster in (a dash or a teleport) lands its hit up close, whatever the cast range.
            if any(kinds.get(csd.as_int(e.get("kind"))) in ("Dash", "Teleport") for e in b.get("effects") or []):
                ranged = False
            copies = max(1, csd.as_int(b.get("projectileCount"), 1))
            for e in b.get("effects") or []:
                k = kinds.get(csd.as_int(e.get("kind")), "?")
                side = "caster" if csd.as_int(e.get("side")) == 0 else "target"
                sub = subs.get(csd.as_int(e.get("subStat")), "?")
                mag, secs = csd.num_of(e.get("magnitude")), csd.num_of(e.get("seconds"))
                marked = MARKS if flag(e.get("stackPerMark")) else 1
                if run_pile is not None and side == "caster" and pile_of(e) == run_pile:
                    continue   # the run's own charges or window — already counted as its beats
                if k == "Damage":
                    dm = e.get("damageMult") or {}
                    base = dict(label="%s · damage" % tid, hits=max(1.0, csd.num_of(e.get("hitCount"), 1.0)) * ticks,
                                chance=csd.num_of(e.get("chance"), 1.0), copies=copies, targets=targets,
                                ranged=ranged, releaseEarly=flag(a.get("releaseEarly")))
                    dmg = csd.num_of(dm, 0.0)
                    dtype = csd.as_int(e.get("damageType"))
                    crit = base_crit if dtype == dmg_type["Crit"] else 1.0
                    parts.append(dict(base, dmg=dmg * crit))
                    per_mark = csd.num_of(e.get("damageMultPerMark"))
                    if per_mark:
                        parts.append(dict(base, label="%s · per mark (%d assumed)" % (tid, MARKS), dmg=per_mark * MARKS * crit))
                    bonus = csd.num_of(e.get("critChanceBonus"))
                    if bonus and dtype == dmg_type["Physical"]:
                        parts.append(dict(base, label="%s · bonus crit chance" % tid, dmg=dmg * bonus * (base_crit - 1)))
                    per_metre = csd.num_of(e.get("damageMultPerMetre"))
                    if per_metre:   # enemies stand anywhere in range: priced at half of it
                        parts.append(dict(base, label="%s · per metre (half the range)" % tid,
                                          dmg=per_metre * cast_range / 2 * crit, prov=True))
                    notes = []
                    if isinstance(dm, dict) and flag(dm.get("useStacks")):
                        notes.append("a built-up pile")
                    for n, label in (("damageMultPerAccuracyOver", "hit chance"), ("damageMultPerSpeedBonus", "speed"),
                                     ("damageMultPerMaxMana", "max MP"), ("damageMultPerBurnSecond", "burn time"),
                                     ("damageMultPerPointGivenUp", "points given up"),
                                     ("critChanceBonusVsSlowed", "crit against a slowed target")):
                        if csd.num_of(e.get(n)) != 0:
                            notes.append(label)
                    if notes:
                        extras.append("%s: extra from %s" % (tid, ", ".join(notes)))
                    if csd.as_int(e.get("cashMark")) != 0 and mark_names.get(csd.as_int(e.get("cashMark"))) == "Burn":
                        effects.append(dict(label="burn put out, payoff", type="burnPayoff", count=1,
                                            size=csd.num_of(e.get("burnPayoff"), 1.0), targets=targets, prov=True))
                elif k == "Parry" and side == "caster":
                    effects.append(dict(label="hits stopped", type="count", count=stacks_given(e, swing), size=1, targets=1))
                elif k == "ReduceOutgoingDamage" and side == "target":
                    effects.append(dict(label="enemy's next attacks cut", type="nextHit", size=mag,
                                        count=stacks_given(e, swing) * marked * repeats * ticks, targets=targets))
                elif k == "Static" and side == "target":
                    cap = csd.num_of(e.get("stackLimit"), 5.0)
                    stacks = min(cap, stacks_given(e, swing) * repeats * ticks)
                    effects.append(dict(label="Static: enemy attack speed cut", type="enemyTimed", size=mag * stacks,
                                        secs=secs, targets=targets))
                elif k == "SubStatDebuff" and side == "target" and sub in ("AttackSpeed", "Accuracy"):
                    effects.append(dict(label="enemy %s cut" % ("attack speed" if sub == "AttackSpeed" else "accuracy"),
                                        type="enemyTimed", size=mag, secs=secs, targets=targets))
                elif k == "SubStatDebuff" and side == "target" and sub in ("Defense", "MagicDefense"):
                    effects.append(dict(label="enemy %s cut" % sub, type="enemyDef", size=mag, secs=secs, targets=targets))
                elif k in ("SubStatBuff", "SubStatDebuff") and side == "caster" and sub in ("Defense", "MagicDefense"):
                    sign = 1 if k == "SubStatBuff" else -1
                    effects.append(dict(label="your %s %s" % (sub, "up" if sign > 0 else "down"), type="ownDef",
                                        size=mag * sign, secs=secs, targets=1))
                elif k == "SubStatBuff" and side == "caster" and sub in ("AttackSpeed", "Damage"):
                    # both are worth the extra share of a basic attack every swing for the window
                    effects.append(dict(label="your %s" % ("attack speed" if sub == "AttackSpeed" else "damage"),
                                        type="ownSpeed", size=mag * stacks_given(e, swing), secs=secs, targets=1))
                elif k == "BoostBasicAttackDamage" and side == "caster":
                    usable = min(stacks_given(e, swing), secs / swing) if swing and secs else stacks_given(e, swing)
                    effects.append(dict(label="next basic attacks boosted", type="count", count=usable,
                                        size=mag, targets=1))
                elif k == "DrainMana" and side == "target":
                    effects.append(dict(label="MP drained to you", type="mana", count=csd.num_of(e.get("amount")),
                                        size=1, targets=targets))
                elif k in ("SubStatBuff", "SubStatDebuff") and sub == "MovementSpeed":
                    effects.append(dict(label="%s movement speed" % ("your" if side == "caster" else "slow on them"),
                                        type="move", size=mag, secs=secs, targets=targets if side == "target" else 1, prov=True))
                elif k in ("Dash", "Teleport") and side == "caster":
                    dist = cast_range if mode == tmode["Ground"] else cast_range / 2
                    effects.append(dict(label="%s (%.1f m)" % (k.lower(), dist), type="dash", count=1, size=dist,
                                        targets=1, prov=True))
                elif k == "SubStatBuff" and side == "caster" and sub == "Accuracy":
                    effects.append(dict(label="your accuracy", type="accSelf", size=mag * stacks_given(e, swing), secs=secs,
                                        targets=1, prov=True))
                elif k == "SubStatBuff" and side == "caster" and sub == "Evasion":
                    effects.append(dict(label="your evasion", type="evaSelf", size=mag, secs=secs, targets=1, prov=True))
                elif k == "SubStatBuff" and side == "caster" and sub == "MPRegen":
                    effects.append(dict(label="your MP regen", type="mpRegen", size=mag, secs=secs, targets=1))
                elif k == "Heal" and side == "caster":
                    if flag(e.get("healShareOfMaxHP")):
                        effects.append(dict(label="heal (share of max HP)", type="healShare", count=1,
                                            size=csd.num_of(e.get("amount")), targets=1, prov=True))
                    else:
                        effects.append(dict(label="heal", type="flatHp", count=1, size=csd.num_of(e.get("amount")),
                                            targets=1, prov=True))
                elif k == "SubStatBuff" and side == "caster" and sub == "HPRegen":
                    effects.append(dict(label="your HP regen", type="flatHp", count=1, size=mag * secs,
                                        targets=1, prov=True))
                elif k == "SubStatDebuff" and side == "target" and sub == "HPRegen":
                    effects.append(dict(label="their HP regen cut", type="flatHp", count=1, size=mag * secs,
                                        targets=targets, prov=True))
                elif k == "Taunt" and side == "target":
                    effects.append(dict(label="taunt", type="taunt", size=1, secs=secs, targets=targets, prov=True))
                elif k == "Cleanse" and side == "caster":
                    effects.append(dict(label="ailments cleared", type="cleanse", count=1, size=1, targets=1, prov=True))
                elif k == "Status" and side == "target" and ailments.get(csd.as_int(e.get("ailment"))) in ("Burn", "Poison", "Bleed"):
                    # a damage-over-time: flat HP every DotInterval, against the reference attack
                    hp = mag * max(1, int(secs / 0.8)) * ticks
                    effects.append(dict(label=ailments.get(csd.as_int(e.get("ailment"))).lower() + " damage", type="flatHp",
                                        count=1, size=hp, targets=targets, prov=True))
                elif k in ("Chill", "CopyMark") and side == "target":
                    cap = csd.num_of(e.get("stackLimit"), 2.0) if k == "Chill" else MARKS
                    n = min(cap, stacks_given(e, swing) * repeats * ticks) if k == "Chill" else MARKS
                    spread = targets if k == "Chill" else max(1.0, targets - 1)   # a copy lands on the neighbours
                    effects.append(dict(label="%s placed" % ("Chill" if k == "Chill" else "Chill copied"), type="mark",
                                        count=n, size=1, targets=spread, prov=True))
                    effects.append(dict(label="Chill's slow", type="move", size=mag * n, secs=secs or 4.0,
                                        targets=spread, prov=True))
                elif k == "ExtendMarks" and side == "target":
                    effects.append(dict(label="marks extended", type="extendMarks", count=MARKS, size=1,
                                        targets=targets, prov=True))
                elif k == "ImmuneToDisables" and side == "caster":
                    effects.append(dict(label="disables turned away", type="immune", size=1, secs=secs, targets=1, prov=True))
                elif k == "SureHit" and side == "caster":
                    # charges are spent one a swing, so only as many as the window has swings can be used
                    usable = min(stacks_given(e, swing), secs / swing) if swing and secs else stacks_given(e, swing)
                    effects.append(dict(label="a miss prevented", type="sureHit", count=usable, size=1,
                                        targets=1, prov=True))
                elif k == "BoostOutgoingDamage" and side == "caster":
                    effects.append(dict(label="next attack boosted", type="count", count=stacks_given(e, swing),
                                        size=mag * csd.num_of(e.get("chance"), 1.0), targets=1))
                elif k == "ResetCooldown" and side == "caster" and flag(e.get("resetsLastCombination")):
                    effects.append(dict(label="last combination reset", type="ultAvg", weapon=weapon, size=1, targets=1,
                                        prov=True))
                elif k == "ResetCooldown" and side == "caster" and e.get("modifierSkillId"):
                    effects.append(dict(label="%s reset" % e.get("modifierSkillId"), type="refBudget",
                                        skillId=e.get("modifierSkillId"), size=1, targets=1, prov=True))
                elif k == "ExtendDuration" and e.get("modifierSkillId") in assets:
                    tgt = e.get("modifierSkillId")
                    window = run_of(assets[tgt], swing)["window"] or 1.0
                    effects.append(dict(label="%s lengthened" % tgt, type="refValue", skillId=tgt,
                                        size=csd.num_of(e.get("modifierSeconds"), 1.0) / window, targets=1, prov=True))
                elif k == "AddStacks" and e.get("modifierSkillId") in assets:
                    tgt = e.get("modifierSkillId")
                    tro = run_of(assets[tgt], swing)
                    n = csd.num_of(e.get("modifierStacks"), 1.0)
                    pile = csd.as_int(e.get("modifierStateId"))
                    giver = next((g for b2 in (assets[tgt].get("bindings") or []) for g in (b2.get("effects") or [])
                                  if csd.as_int(g.get("side")) == 0 and pile_of(g) == pile), None)
                    gk = kinds.get(csd.as_int(giver.get("kind"))) if giver else None
                    if tro["pile"] == pile:        # charges of a run: each is one more beat of it
                        effects.append(dict(label="%s charges added" % tgt, type="refValue", skillId=tgt,
                                            size=n / max(1, tro["repeats"]), targets=1, prov=True))
                    elif gk == "SubStatBuff" and swing:   # stacks of a timed buff: each is worth its own window
                        effects.append(dict(label="%s stacks added" % tgt, type="ownSpeed",
                                            size=csd.num_of(giver.get("magnitude")) * n, secs=csd.num_of(giver.get("seconds")),
                                            targets=1))
                    else:
                        unpriced.append("%s: stacks of %s (%s)" % (tid, tgt, gk or "a pile nothing prices"))
                elif k == "Charges" and side == "caster" and free_firers.get((a.get("id"), pile_of(e))):
                    fire = free_firers[(a.get("id"), pile_of(e))][0]
                    count = stacks_given(e, swing)
                    if swing:
                        count = min(count, secs / swing)   # one free firing a swing, inside the window
                    effects.append(dict(label="free %s" % fire, type="refValue", skillId=fire, size=count, targets=1, prov=True))
                else:
                    if k == "Status":   # an ailment says which one — Burn, Poison — not the word "Status"
                        k = ailments.get(csd.as_int(e.get("ailment")), k)
                    what = k + (" " + sub if k.startswith("SubStat") else "") + (" on you" if side == "caster" else " on them")
                    unpriced.append("%s: %s" % (tid, what))

        comp = companions.get(a.get("id"))
        if comp:
            eva = sum(f["size"] for f in effects if f.get("type") == "evaSelf")
            window = max([f.get("secs") or 0 for f in effects] + [0.0])
            effects.append(dict(label="counter-arrows (%s)" % comp, type="companionDodge", skillId=comp, size=eva,
                                secs=window, targets=1, prov=True))
        skills.append({"id": a.get("id"), "name": a.get("displayName"), "weapon": weapon, "extras": extras,
                       "passive": passive, "fireRate": fire_rate,
                       "ult": csd.as_int(a.get("tier")) == 1, "cd": cd, "cast": cast, "mp": mp,
                       "swing": swing, "lock": lock, "run": run, "repeats": repeats,
                       "parts": parts, "effects": effects, "unpriced": unpriced, "reasons": reasons})

    if not skills:
        csd.die("Read 0 weapon skills from %s — the folder moved or the reader went blind." % SKILLS_DIR)
    data = os.path.join(HERE, "skills-data.js")
    with open(data, "w", encoding="utf-8") as fh:
        fh.write("// GENERATED by tools/skill-balance/extract.py from the saved skill files. Do not edit; re-run it.\n")
        fh.write("var SKILL_DATA = " + json.dumps(skills, indent=1) + ";\n")
        fh.write("if (typeof module !== 'undefined') module.exports = SKILL_DATA;\n")
    sys.exit(subprocess.call(["node", os.path.join(HERE, "report.js")]))


if __name__ == "__main__":
    main()
