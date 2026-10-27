#!/usr/bin/env python3
"""Prove the skill-data checker CATCHES faults — it isn't enough that it prints "clean".

WHY THIS EXISTS: a checker that silently stops seeing anything reports exactly what a clean
project reports. That "0 problems" then reads as proof the data is fine, when it is really
proof of nothing at all — the instrument went deaf and said so in the same words it uses for
good news. So the checker itself gets checked: this makes a throwaway copy of the real skill
data and the visuals it plays, breaks ONE thing in it per case, and fails if the checker doesn't notice.

The fixtures are deliberately anchored to REAL text in the real assets. If someone re-saves a
skill and a fixture's anchor disappears, that case reports NOT PROVEN rather than passing
quietly — an anchor that no longer matches proves nothing, and pretending otherwise would be
the very failure this script exists to catch.

USAGE:   tools/prove-skill-data-check.py
EXIT:    0 = every fault caught, 1 = the checker missed one (or a fixture no longer applies).
"""
import contextlib
import importlib.util
import io
import os
import shutil
import sys
import tempfile

REPO = os.path.dirname(os.path.abspath(os.path.dirname(__file__)))
CHECKER = os.path.join(REPO, "tools", "check-skill-data.py")
REAL_SKILLS = os.path.join(REPO, "unity", "Assets", "3_Data", "Resources", "Skills")
REAL_VISUALS = os.path.join(REPO, "unity", "Assets", "3_Data", "Resources", "VfxSequences")

RED, YEL, GRN, OFF = "\033[31m", "\033[33m", "\033[32m", "\033[0m"


def load_checker():
    spec = importlib.util.spec_from_file_location("skill_data_check", CHECKER)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def patch(filename, *replacements):
    """Break one thing, by swapping real text for broken text in a COPY of one asset."""
    def apply(folder):
        path = os.path.join(folder, filename)
        if not os.path.isfile(path):
            return "%s is no longer in the library" % filename
        text = open(path, encoding="utf-8").read()
        for old, new in replacements:
            if old not in text:
                return "%s no longer contains %r" % (filename, old.strip())
            text = text.replace(old, new, 1)
        open(path, "w", encoding="utf-8").write(text)
        return None
    return apply


def patch_visual(filename, *replacements):
    """The same swap as patch(), made in the copy of the visuals that sits beside the copied skills."""
    inner = patch(filename, *replacements)
    return lambda folder: inner(os.path.join(os.path.dirname(folder), "VfxSequences"))


# Each case: a plain-language name, the breakage, and a phrase the report MUST contain.
CASES = [
    ("a number typed into a description",
     patch("skill.sword.deathblow.asset", ("hitting harder for each.", "hitting 40% harder for each.")),
     "has a number in its description"),
    ("a gate that names no pile",
     patch("skill.sword.deathblow.asset", ("requireStateIdSet: 1", "requireStateIdSet: 0")),
     "nobody picked WHICH pile"),
    ("a gate naming a skill that does not exist",
     patch("skill.sword.deathblow.asset",
           ("requireSkillId: skill.sword.momentum\n    requireStateId",
            "requireSkillId: skill.sword.ghost\n    requireStateId")),
     "does not exist"),
    ("a gate pointing at a pile the granter never leaves",
     patch("skill.sword.deathblow.asset", ("requireStateId: 10", "requireStateId: 13")),
     "never leaves that pile behind"),
    ("gameplay wired to a beat the visual does not have",
     patch("skill.sword.deathblow.asset", ("tickId: follow_hit", "tickId: nonesuch")),
     "does not have"),
    # A second step carrying the beat Lunge's gameplay is wired to — the shape that made Dash restart its glide
    # three times a cast.
    ("a beat carried by two steps of a visual",
     patch_visual("skill_dash.asset", ("\n  loopEnd: 3", "\n  - note: VFX\n    tickId: dash_tick\n  loopEnd: 3")),
     "steps of its visual carry that beat"),
    # A NUMBER is what pulls now, so the break has to be made on a number's own record, not on the effect.
    ("a number sized by stacks on a skill with no gate",
     patch("skill.sword.committed_strike.asset",
           ("damageMult:\n        value: 2.75\n        useStacks: 0",
            "damageMult:\n        value: 2.75\n        useStacks: 1")),
     "always comes out at nothing"),
    # Flip the asset's OWN answer rather than inserting a second one: the field now gets written into every saved
    # skill, and a duplicate key would simply be overruled by the real one further down — the break would silently
    # not happen, and this proof would report the checker as blind when nothing was ever broken.
    ("a repeat that spends charges it never gated",
     patch("skill.staff.meteor.asset", ("repeatCostsCharge: 0", "repeatCostsCharge: 1")),
     "plays once and stops"),
    # Release early on a wind-up DEX can remove: Hailstone's is variable only, so 150 DEX would hand out 1.5x free.
    ("release early with no fixed cast time",
     patch("skill.bow.hailstone.asset", ("releaseEarly: 0", "releaseEarly: 1")),
     "no fixed cast time"),
    # Release early on a skill nobody presses: Jet Stream's own release flag, with its press taken away.
    ("release early on a skill never pressed",
     patch("skill.bow.jet_stream.asset", ("  - trigger: 0\n", "  - trigger: 1\n")),
     "is never pressed"),
    ("two skills sharing one id",
     patch("skill.staff.meteor.asset", ("id: skill.staff.meteor", "id: skill.bulwark")),
     "shares its id"),
    ("waiting on a finished cast but naming no skill",
     patch("skill.sword.lock_blades.asset",
           ("triggerSkillId: skill.sword.lunge", "triggerSkillId: ")),
     "never says which skill"),
    ("waiting on a skill that does not exist",
     patch("skill.sword.lock_blades.asset",
           ("triggerSkillId: skill.sword.lunge", "triggerSkillId: skill.sword.nope")),
     "does not exist"),
    ("editing another skill's pile without naming which pile",
     patch("skill.sword.encore.asset", ("modifierStateIdSet: 1", "modifierStateIdSet: 0")),
     "nobody picked WHICH pile"),
    ("editing a pile that skill never leaves behind",
     patch("skill.sword.encore.asset", ("modifierStateId: 10", "modifierStateId: 13")),
     "never leaves that pile behind"),
    ("an all-in cost put on the target side, where the game never pays it",
     patch("skill.bulwark.asset", ("    - kind: 3\n", "    - kind: 23\n")),
     "a cost the caster pays"),
    ("a strike that grows with what was given up, in a beat that gives nothing up",
     patch("skill.staff.meteor.asset", ("      damageMultPerPointGivenUp:\n        value: 0\n",
                                       "      damageMultPerPointGivenUp:\n        value: 0.1\n")),
     "never grows"),
    ("a bounce on a beat that is not Single Target",
     patch("skill.staff.meteor.asset", ("    bounceJumps: 0\n", "    bounceJumps: 2\n")),
     "not Single Target"),
    ("a bounce past the most jumps a chain can make",
     patch("skill.staff.meteor.asset", ("    bounceJumps: 0\n", "    bounceJumps: 40\n")),
     "bounces 40 times"),
    ("a Staff combination someone made learnable",
     patch("skill.staff.scald.asset", ("  maxLevel: 0\n", "  maxLevel: 5\n")),
     "never learned with points"),
    ("a Staff orb held to an ordinary skill's levels",
     patch("skill.staff.orb_fire.asset", ("  maxLevel: 35\n", "  maxLevel: 10\n")),
     "max level is not 35"),
    # Flips the asset's own answer, like the repeat case above, so the break is real rather than a second key the
    # true one further down would overrule.
    ("an effect that can never happen",
     patch("skill.sword.guard_stance.asset",
           ("chance:\n        value: 1", "chance:\n        value: 0")),
     "can never happen"),
    # THE SHAPE RULE. A file left in an older format is the one fault that used to be invisible: the game converted
    # it on load and said nothing, so it looked identical to a healthy file from every angle. Putting a retired field
    # back is exactly the state seven skills were really in, and the checker must refuse it.
    ("a skill left in an older saved format",
     patch("skill.sword.guard_stance.asset",
           ("\n  manaCost:", "\n  triggersMigrated: 1\n  manaCost:")),
     "OLD FORMAT"),
    # A trademark weapon is called by its skill's own name now; a separate weapon name left in a file is the retired
    # shape, and the checker must refuse it rather than let a second copy of the name drift.
    ("a separate trademark name left in a skill",
     patch("skill.sword.lunge.asset", ("\n  manaCost:", "\n  trademarkName: Tsuki\n  manaCost:")),
     "OLD FORMAT"),
    ("a skill raised past level 1 where nothing grows",
     patch("skill.sword.parry.asset", ("  maxLevel: 1", "  maxLevel: 5")),
     "nothing on it grows"),
    ("a weapon skill with no max level",
     patch("skill.sword.lunge.asset", ("  maxLevel: 10", "  maxLevel: 0")),
     "has no max level"),
    ("a requirement the skill never uses",
     patch("skill.sword.unwavering.asset",
           ("needsSkillId: skill.sword.guard_stance", "needsSkillId: skill.sword.lunge")),
     "nothing in it uses"),
    ("two skills that each need the other",
     patch("skill.sword.lunge.asset", ("  needsSkillId: \n", "  needsSkillId: skill.sword.lock_blades\n")),
     "lead back round"),
    ("a requirement on another weapon's skill",
     patch("skill.sword.lock_blades.asset", ("  weaponType: 1", "  weaponType: 8")),
     "different weapon"),
    ("a skill that opens before the skill it needs",
     patch("skill.sword.lunge.asset", ("  masteryLevel: 1\n", "  masteryLevel: 9\n")),
     "sits before the skill it needs"),
    ("a tick left in an older saved format",
     patch("skill.sword.guard_stance.asset",
           ("\n  - tickId:", "\n  - scope: 0\n    tickId:")),
     "OLD FORMAT"),
]


def run_against(checker, break_it):
    """Run the checker over a private copy of the library, with one thing broken."""
    tmp = tempfile.mkdtemp()
    try:
        folder = os.path.join(tmp, "Skills")
        visuals = os.path.join(tmp, "VfxSequences")
        shutil.copytree(REAL_SKILLS, folder)
        shutil.copytree(REAL_VISUALS, visuals)
        why_not = break_it(folder) if break_it else None
        if why_not:
            return None, why_not
        original, checker.SKILLS_DIR = checker.SKILLS_DIR, folder
        original_visuals, checker.VISUALS_DIR = checker.VISUALS_DIR, visuals
        out = io.StringIO()
        try:
            with contextlib.redirect_stdout(out):
                code = checker.main()
        finally:
            checker.SKILLS_DIR = original
            checker.VISUALS_DIR = original_visuals
        return (code, out.getvalue()), None
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    if not os.path.isfile(CHECKER):
        sys.stderr.write("The skill-data checker is missing, so there is nothing to prove.\n")
        return 1
    checker = load_checker()

    missed, unproven = 0, 0
    for name, break_it, expected in CASES:
        result, why_not = run_against(checker, break_it)
        if why_not:
            print("%s NOT PROVEN%s  %-52s %s" % (YEL, OFF, name, why_not))
            unproven += 1
            continue
        code, report = result
        if expected in report and code == 1:
            print("%scaught%s      %s" % (GRN, OFF, name))
        else:
            print("%sMISSED%s      %s" % (RED, OFF, name))
            print("\n".join("            " + line for line in report.splitlines()))
            missed += 1

    # The control: with nothing broken, the checker must be quiet. A checker that complains
    # about healthy data is as useless as one that stays quiet about broken data.
    result, why_not = run_against(checker, None)
    code, report = result
    if code == 0:
        print("%squiet%s       on untouched data" % (GRN, OFF))
    else:
        print("%sNOISY%s       on untouched data — it reports a fault that is really there, "
              "or a false alarm:" % (RED, OFF))
        print("\n".join("            " + line for line in report.splitlines()))
        missed += 1

    print()
    if missed:
        print("%s%d fault(s) the checker did not catch. Do not trust a clean run until this "
              "passes.%s" % (RED, missed, OFF))
    elif unproven:
        print("%sEvery fault it could still be tested on was caught, but %d case(s) no longer "
              "apply — their fixtures need re-anchoring to the current data.%s" % (YEL, unproven, OFF))
    else:
        print("%sAll %d faults caught, and quiet on healthy data.%s" % (GRN, len(CASES), OFF))
    return 1 if (missed or unproven) else 0


if __name__ == "__main__":
    sys.exit(main())
