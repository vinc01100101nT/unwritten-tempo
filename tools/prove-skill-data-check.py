#!/usr/bin/env python3
"""Prove the skill-data checker CATCHES faults — it isn't enough that it prints "clean".

WHY THIS EXISTS: a checker that silently stops seeing anything reports exactly what a clean
project reports. That "0 problems" then reads as proof the data is fine, when it is really
proof of nothing at all — the instrument went deaf and said so in the same words it uses for
good news. So the checker itself gets checked: this makes a throwaway copy of the real skill
data, breaks ONE thing in it per case, and fails if the checker doesn't notice.

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


# Each case: a plain-language name, the breakage, and a phrase the report MUST contain.
CASES = [
    ("a gate that names no pile",
     patch("skill.sword.deathblow.asset", ("requireStateIdSet: 1", "requireStateIdSet: 0")),
     "nobody picked WHICH pile"),
    ("a gate naming a skill that does not exist",
     patch("skill.sword.deathblow.asset",
           ("requireSkillId: skill.sword.momentum\n    requireStateId",
            "requireSkillId: skill.sword.ghost\n    requireStateId")),
     "does not exist"),
    ("a gate pointing at a pile the granter never leaves",
     patch("skill.sword.deathblow.asset", ("requireStateId: 14", "requireStateId: 13")),
     "never leaves that pile behind"),
    ("gameplay wired to a beat the visual does not have",
     patch("skill.sword.deathblow.asset", ("tickId: follow_hit", "tickId: nonesuch")),
     "does not have"),
    # A NUMBER is what pulls now, so the break has to be made on a number's own record, not on the effect.
    ("a number sized by stacks on a skill with no gate",
     patch("skill.sword.committed_strike.asset",
           ("damageMult:\n        value: 2\n        useStacks: 0",
            "damageMult:\n        value: 2\n        useStacks: 1")),
     "always comes out at nothing"),
    # Flip the asset's OWN answer rather than inserting a second one: the field now gets written into every saved
    # skill, and a duplicate key would simply be overruled by the real one further down — the break would silently
    # not happen, and this proof would report the checker as blind when nothing was ever broken.
    ("a repeat that spends charges it never gated",
     patch("skill.meteor.asset", ("repeatCostsCharge: 0", "repeatCostsCharge: 1")),
     "plays once and stops"),
    ("two skills sharing one id",
     patch("skill.meteor.asset", ("id: skill.meteor", "id: skill.bulwark")),
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
     patch("skill.sword.encore.asset", ("modifierStateId: 14", "modifierStateId: 13")),
     "never leaves that pile behind"),
    # Flips the asset's own answer, like the repeat case above, so the break is real rather than a second key the
    # true one further down would overrule.
    ("an effect that can never happen",
     patch("skill.sword.guard_stance.asset",
           ("chance:\n        value: 1", "chance:\n        value: 0")),
     "can never happen"),
]


def run_against(checker, break_it):
    """Run the checker over a private copy of the library, with one thing broken."""
    tmp = tempfile.mkdtemp()
    try:
        folder = os.path.join(tmp, "Skills")
        shutil.copytree(REAL_SKILLS, folder)
        why_not = break_it(folder) if break_it else None
        if why_not:
            return None, why_not
        original, checker.SKILLS_DIR = checker.SKILLS_DIR, folder
        out = io.StringIO()
        try:
            with contextlib.redirect_stdout(out):
                code = checker.main()
        finally:
            checker.SKILLS_DIR = original
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
