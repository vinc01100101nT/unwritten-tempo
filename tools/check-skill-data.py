#!/usr/bin/env python3
"""Check the SAVED skill assets for authoring mistakes that are SILENT at runtime.

WHY THIS EXISTS: the compile checker proves the code builds; nothing proved the DATA
is sane. Every field on a skill deserializes to a legal-looking value, so an unfinished
gate reads as a real choice — a pile nobody picked reads as the first state in the enum,
a repeat that spends charges it never gated simply plays once, an effect that pulls from
a pile with no gate silently reads zero. None of that throws, none of it compiles wrong,
and the composer draws a plausible value on screen either way. It only shows up as
"that skill feels like it isn't scaling", days later.

Reads the assets on disk (never the editor's in-memory copy), so what it checks is what
the game will actually load.

SINGLE SOURCE OF TRUTH: every enum and the effect->state mapping are PARSED OUT OF THE
C# at run time, never copied here. Add a state, reorder a kind, or extend the mapping's
switch and this script follows automatically. If a parse fails it says so and exits
rather than falling back to a stale table.

USAGE:   tools/check-skill-data.py
EXIT:    0 = clean (warnings still print), 1 = problems found, 2 = setup problem.
"""
import os
import re
import sys

REPO = os.path.dirname(os.path.abspath(os.path.dirname(__file__)))
ASSETS = os.path.join(REPO, "unity", "Assets")
SKILLS_DIR = os.path.join(ASSETS, "3_Data", "Resources", "Skills")
SCRIPTS = os.path.join(ASSETS, "Scripts")
# Where visuals are read from on top of ASSETS. None = only where they really sit; the self-test points it at a
# broken copy so it never has to edit the real ones.
VISUALS_DIR = None

RED, YEL, GRN, DIM, OFF = "\033[31m", "\033[33m", "\033[32m", "\033[2m", "\033[0m"

# Filled from the C# at run time (see load_run_of_hits_id) so it can never drift from the game, exactly like the enums.
RUN_OF_HITS_ID = None


def die(msg):
    sys.stderr.write("%s%s%s\n" % (RED, msg, OFF))
    sys.exit(2)


def read(path):
    with open(path, "r", encoding="utf-8", errors="ignore") as f:
        return f.read()


# --------------------------------------------------------------------------------------
# C# parsing — the enums and the effect->state switch are OWNED by the C#; we only read them.
# --------------------------------------------------------------------------------------

_LINE_COMMENT = re.compile(r"//.*$", re.M)


def parse_run_of_hits_id(source):
    """The granter id that means 'the fight counts this, no skill grants it' — read from the C# constant."""
    m = re.search(r'\bRunOfHitsId\s*=\s*"([^"]+)"', source)
    if not m:
        die("Could not find RunOfHitsId in the game's C#. This checker reads the real value instead of\n"
            "keeping its own copy, so it stops rather than guess.")
    return m.group(1)


def parse_enum(source, name):
    """{member_name: int} in declaration order, honouring explicit `= N` assignments."""
    m = re.search(r"\benum\s+%s\b\s*(?::\s*\w+\s*)?\{" % re.escape(name), source)
    if not m:
        die("Could not find the definition of %s in the game's C#. This checker reads the\n"
            "real enums instead of keeping its own copy, so it stops rather than guess." % name)
    depth, i = 1, m.end()
    while i < len(source) and depth:
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
        i += 1
    body = _LINE_COMMENT.sub("", source[m.end():i - 1])
    body = re.sub(r"/\*.*?\*/", "", body, flags=re.S)
    out, nxt = {}, 0
    for chunk in body.split(","):
        chunk = chunk.strip()
        if not chunk:
            continue
        if "=" in chunk:
            member, _, val = chunk.partition("=")
            member, val = member.strip(), val.strip()
            if not re.fullmatch(r"-?\d+", val):      # e.g. `1 << 0` — flags, resolved below
                shift = re.fullmatch(r"(\d+)\s*<<\s*(\d+)", val)
                val = int(shift.group(1)) << int(shift.group(2)) if shift else nxt
            else:
                val = int(val)
        else:
            member, val = chunk, nxt
        if not re.fullmatch(r"[A-Za-z_]\w*", member):
            continue
        out[member] = val
        nxt = val + 1
    if not out:
        die("Parsed %s but found no members in it." % name)
    return out


def _body_of(source, signature_pattern, what):
    m = re.search(signature_pattern, source)
    if not m:
        die("Could not find the function that says %s.\n"
            "It is the one owner of that rule; this checker reads it rather than copying it." % what)
    depth, i = 1, m.end()
    while i < len(source) and depth:
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
        i += 1
    return source[m.end():i - 1]


def parse_later_piles(source):
    """Piles an effect causes LATER rather than at apply time (a held hit becoming a debt).
    Read from the same C# function the composer's authoring list is built from, so this
    checker can never offer a stricter answer than the editor does."""
    body = _body_of(source, r"public static void CollectPiles\s*\([^)]*\)\s*\{",
                    "which piles an effect can lead to")
    out = {}
    for kind, state in re.findall(r"e\.kind\s*==\s*SkillEffectKind\.(\w+)[^;]*?into\.Add\(StateId\.(\w+)\)", body, re.S):
        out.setdefault(kind, set()).add(state)
    return out


def parse_state_for_kind(source):
    """The effect-kind -> state mapping, read out of the one C# function that owns it.

    Returns {kind_name: state_name}, plus the two kinds whose state depends on another
    field ("Status" follows the ailment, the sub-stat kinds follow the stat) as "*"."""
    body = _body_of(source, r"public static bool TryStateFor\s*\([^)]*\)\s*\{",
                    "which pile an effect leaves behind")
    mapping = {}
    for kind, state in re.findall(r"case\s+SkillEffectKind\.(\w+)\s*:\s*id\s*=\s*StateId\.(\w+)\s*;", body):
        mapping[kind] = state
    # The ones that resolve from a sibling field rather than a constant. Fall-through labels share the line that
    # actually assigns, so collect every label stacked above such an assignment, not just the nearest one.
    for labels, _ in re.findall(
            r"((?:case\s+SkillEffectKind\.\w+\s*:\s*)+)id\s*=\s*(e\.|SubStats\.|ConverterPile\()", body):
        for kind in re.findall(r"SkillEffectKind\.(\w+)", labels):
            mapping[kind] = "*"
    if "Status" in body and "Status" not in mapping:
        mapping["Status"] = "*"
    if not mapping:
        die("Read the effect-to-pile function but understood none of its cases.")
    return mapping


def parse_converter_piles(source):
    """(armor_pile, weapon_pile) — the two piles an Element Converter fills, read out of the one C# function that
    decides between them, so this checker never keeps its own copy of the names."""
    m = re.search(r"converts\s*==\s*EquipSlotType\.Armor\s*\?\s*StateId\.(\w+)\s*:\s*StateId\.(\w+)", source)
    if not m:
        die("Could not read which piles an Element Converter fills (SkillEffect.ConverterPile).")
    return m.group(1), m.group(2)


def parse_boostable(source):
    """The effect kinds the fight raises with a skill's level — the case labels of EffectScaling.Boostable that
    return true. Status is raised only for a damage-over-time ailment, which StatusController.IsDot names."""
    body = _body_of(source, r"public static bool Boostable\(SkillEffect e\)", "EffectScaling.Boostable")
    head = body.split("return true;")[0]
    kinds = set(re.findall(r"case SkillEffectKind\.(\w+):", head))
    if not kinds:
        die("Could not read which effect kinds EffectScaling.Boostable raises — this checker is blind to levels.")
    return kinds


def parse_dot_ailments(source):
    m = re.search(r"public static bool IsDot\(Ailment a\)\s*=>\s*([^;]+);", source)
    if not m:
        die("Could not read StatusController.IsDot — this checker cannot tell which ailments grow with a level.")
    return set(re.findall(r"Ailment\.(\w+)", m.group(1)))


def parse_pile_for_stat(source):
    """Which pile each sub-stat lives in, read out of the one C# function that owns it."""
    body = _body_of(source, r"public static StateId PileFor\s*\([^)]*\)\s*\{",
                    "which pile each sub-stat lives in")
    mapping = dict(re.findall(r"case\s+SubStat\.(\w+)\s*:\s*return\s+StateId\.(\w+)\s*;", body))
    fallback = re.search(r"default\s*:\s*return\s+StateId\.(\w+)\s*;", body)
    if fallback:
        # The default arm covers the last stat, which is deliberately not spelled out as its own case.
        for stat in parse_enum(source, "SubStat"):
            mapping.setdefault(stat, fallback.group(1))
    if not mapping:
        die("Read the sub-stat-to-pile function but understood none of its cases.")
    return mapping


# --------------------------------------------------------------------------------------
# Unity YAML — a small reader for the exact subset Unity writes (maps, lists of maps,
# scalars, inline references). Not a general YAML parser and doesn't pretend to be.
# --------------------------------------------------------------------------------------

def _tokens(text):
    out, lines, i = [], text.splitlines(), 0
    while i < len(lines):
        raw = lines[i]
        i += 1
        if not raw.strip() or raw.startswith("%") or raw.startswith("---"):
            continue
        indent = len(raw) - len(raw.lstrip(" "))
        body = raw.strip()
        # A quoted scalar may run over several lines (descriptions do). Swallow the rest
        # of it so its text can never be mistaken for structure.
        q = re.match(r"^(-\s*)?[\w.]+:\s*'", body)
        if q and not re.search(r"'\s*$", body[q.end():]):
            while i < len(lines):
                cont = lines[i]
                i += 1
                if re.search(r"'\s*$", cont):
                    break
            body = body[:q.end() - 1] + "''"
        out.append((indent, body))
    return out


def _split(entry):
    key, _, val = entry.partition(":")
    key, val = key.strip(), val.strip()
    # Unity quotes any string YAML would otherwise read as an indicator — an id beginning with '@' is the case that
    # found this. The quotes are YAML's, never part of the value, so a reader that keeps them compares a different
    # string than the game does and reports a fault that only exists in this file's punctuation.
    if len(val) >= 2 and val[0] == val[-1] and val[0] in "'\"":
        val = val[1:-1].replace("''", "'")
    return key, val


def _parse_map(toks, i, indent):
    out = {}
    while i < len(toks) and toks[i][0] >= indent:
        ind, body = toks[i]
        if ind > indent or body.startswith("- "):
            i += 1                      # stray deeper line — nothing here claims it
            continue
        key, val = _split(body)
        if val == "":
            if i + 1 < len(toks) and toks[i + 1][0] == indent and toks[i + 1][1].startswith("- "):
                lst, i = _parse_list(toks, i + 1, indent)
                out[key] = lst
                continue
            if i + 1 < len(toks) and toks[i + 1][0] > indent and not toks[i + 1][1].startswith("- "):
                sub, i = _parse_map(toks, i + 1, toks[i + 1][0])
                out[key] = sub
                continue
            out[key] = ""
        else:
            out[key] = [] if val == "[]" else val
        i += 1
    return out, i


def _parse_list(toks, i, indent):
    items = []
    while i < len(toks) and toks[i][0] == indent and toks[i][1].startswith("- "):
        first = toks[i][1][2:]
        inner = indent + 2
        sub = [(inner, first)]
        j = i + 1
        while j < len(toks) and (toks[j][0] > indent or
                                 (toks[j][0] == indent and not toks[j][1].startswith("- ") and toks[j][0] > indent)):
            sub.append(toks[j])
            j += 1
        item, _ = _parse_map(sub, 0, inner)
        items.append(item)
        i = j
    return items, i


def parse_asset(path):
    toks = _tokens(read(path))
    # Unity writes one document whose single top-level key is the type ("MonoBehaviour:").
    if not toks:
        return {}
    top = [t for t in toks if t[0] == 0]
    start = toks.index(top[0]) + 1 if top else 0
    body_indent = toks[start][0] if start < len(toks) else 2
    out, _ = _parse_map(toks, start, body_indent)
    return out


def as_int(v, default=0):
    try:
        return int(str(v).strip())
    except (TypeError, ValueError):
        return default


def num_of(v, default=0.0):
    """One authored number, whatever shape it is saved in. Every number on an effect is now a small record
    (value + how it meets the pile); a bare scalar is still accepted so this can never go blind on old data."""
    if isinstance(v, dict):
        return as_float(v.get("value"), default)
    return as_float(v, default)


def as_float(v, default=0.0):
    try:
        return float(str(v).strip())
    except (TypeError, ValueError):
        return default


def guid_of(ref):
    m = re.search(r"guid:\s*([0-9a-f]{32})", str(ref))
    return m.group(1) if m else ""


# --------------------------------------------------------------------------------------

def check_export(skills, problems):
    """The wiki and the design docs READ the export, so a stale export is a page telling the player a number the
    game no longer uses — the exact drift the pipeline exists to end. Two ways it can rot, both silent:

      1. The export is missing a skill that exists on disk (or lists one that no longer does).
      2. The wiki's served copy has drifted from the neutral owner.

    We deliberately do NOT re-derive the sentences here. Those come from the game's own describer, and a second
    copy of that logic in Python would be the very duplication this whole change removes. What we can check without
    guessing is COVERAGE and the copy — and both catch the realistic failure, which is "somebody changed a skill and
    never re-saved it in the Composer".
    """
    export = os.path.join(REPO, "data", "skills.json")
    wiki_copy = os.path.join(REPO, "wiki", "content", "skills.generated.json")

    if not os.path.exists(export):
        problems.append(("the skill export",
                         "data/skills.json does not exist. Run Tools > unwritten > Skills > Export Skill Data, "
                         "or press Save on any skill in the Skill Composer."))
        return

    import json as _json
    try:
        data = _json.loads(read(export))
    except Exception as e:
        problems.append(("the skill export", "data/skills.json is not readable JSON (%s)." % e))
        return

    exported = set(s.get("id", "") for s in data.get("skills", []))
    on_disk = set(s.get("id", "") for s in skills)

    for missing in sorted(on_disk - exported):
        problems.append(("the skill export",
                         "'%s' exists as an asset but is NOT in data/skills.json — the wiki and the docs cannot "
                         "see it. Re-save it in the Skill Composer." % missing))
    for extra in sorted(exported - on_disk):
        problems.append(("the skill export",
                         "data/skills.json still lists '%s', which no longer exists as an asset. Re-run the "
                         "export." % extra))

    docs_table = os.path.join(REPO, "data", "skills.md")
    if not os.path.exists(docs_table):
        problems.append(("the skill export",
                         "the design docs' table (data/skills.md) is missing. Re-run the export."))
    else:
        # Cheap coverage check: every skill must have a row. We match on the display name because the table is for
        # humans and carries no ids — and coverage is the failure that actually happens (somebody added a skill and
        # never re-saved), not a mangled cell.
        table = read(docs_table)
        for s in skills:
            name = s.get("displayName", "")
            if name and name not in table:
                problems.append(("the skill export",
                                 "'%s' has no row in data/skills.md — the design docs cannot see it. Re-run the "
                                 "export." % name))

    if not os.path.exists(wiki_copy):
        problems.append(("the skill export",
                         "the wiki's copy (wiki/content/skills.generated.json) is missing — the Sword page will "
                         "render empty. Re-run the export."))
    elif read(wiki_copy) != read(export):
        problems.append(("the skill export",
                         "the wiki's copy has drifted from data/skills.json. Re-run the export; never hand-edit "
                         "either file."))


def main():
    if not os.path.isdir(SKILLS_DIR):
        die("No skills folder at %s" % SKILLS_DIR)

    global RUN_OF_HITS_ID
    RUN_OF_HITS_ID = parse_run_of_hits_id(read(os.path.join(SCRIPTS, "StateGate.cs")))

    effect_src = read(os.path.join(SCRIPTS, "SkillEffect.cs"))
    states = parse_enum(read(os.path.join(SCRIPTS, "UnitStates.cs")), "StateId")
    ailments = parse_enum(read(os.path.join(SCRIPTS, "StatusController.cs")), "Ailment")
    kinds = parse_enum(effect_src, "SkillEffectKind")
    sub_stats = parse_enum(effect_src, "SubStat")
    pile_for_stat = parse_pile_for_stat(effect_src)
    triggers = parse_enum(read(os.path.join(SCRIPTS, "SkillDef.cs")), "SkillTrigger")
    weapons = parse_enum(read(os.path.join(SCRIPTS, "WeaponType.cs")), "WeaponType")
    state_for_kind = parse_state_for_kind(effect_src)
    armor_pile, weapon_pile = parse_converter_piles(effect_src)
    armor_slot = parse_enum(read(os.path.join(SCRIPTS, "Item.cs")), "EquipSlotType")["Armor"]
    later_piles = parse_later_piles(effect_src)
    boostable = parse_boostable(effect_src)
    dot_ailments = parse_dot_ailments(read(os.path.join(SCRIPTS, "StatusController.cs")))

    kind_name = {v: k for k, v in kinds.items()}
    state_name = {v: k for k, v in states.items()}
    ail_name = {v: k for k, v in ailments.items()}
    stat_name = {v: k for k, v in sub_stats.items()}
    weapon_name = {v: k for k, v in weapons.items()}

    # Which pile does one saved effect leave behind? Mirrors the C# switch we just read.
    def pile_of(effect):
        kind = kind_name.get(as_int(effect.get("kind")), "")
        mapped = state_for_kind.get(kind)
        if mapped is None:
            return None
        if mapped != "*":
            return mapped
        if kind == "Status":
            return ail_name.get(as_int(effect.get("ailment")))
        if kind in ("SubStatBuff", "SubStatDebuff"):
            stat = stat_name.get(as_int(effect.get("subStat")), "Damage")
            return pile_for_stat.get(stat)
        if kind == "ElementConverter":
            return armor_pile if as_int(effect.get("converts")) == armor_slot else weapon_pile
        return None

    # ---- load every skill ------------------------------------------------------------
    skills, by_id = [], {}
    for fname in sorted(os.listdir(SKILLS_DIR)):
        if not fname.endswith(".asset"):
            continue
        data = parse_asset(os.path.join(SKILLS_DIR, fname))
        if not data.get("id") and not data.get("displayName"):
            continue
        data["_file"] = fname[:-len(".asset")]
        skills.append(data)
        by_id.setdefault(str(data.get("id", "")), []).append(data)

    if not skills:
        die("Found no skill assets to check in %s" % SKILLS_DIR)

    # ---- the beats each skill's visual actually exposes -------------------------------
    guid_to_asset = {}
    for root, _dirs, files in os.walk(ASSETS):
        for f in files:
            if f.endswith(".asset.meta"):
                g = guid_of(read(os.path.join(root, f)))
                if g:
                    guid_to_asset[g] = os.path.join(root, f[:-len(".meta")])
    if VISUALS_DIR:
        for f in os.listdir(VISUALS_DIR):
            if f.endswith(".asset.meta"):
                g = guid_of(read(os.path.join(VISUALS_DIR, f)))
                if g:
                    guid_to_asset[g] = os.path.join(VISUALS_DIR, f[:-len(".meta")])

    tick_cache, repeat_cache = {}, {}

    def ticks_of(vfx_ref):
        g = guid_of(vfx_ref)
        if not g:
            return None
        if g in tick_cache:
            return tick_cache[g]
        path = guid_to_asset.get(g)
        if not path or not os.path.isfile(path):
            tick_cache[g] = None
            return None
        steps = parse_asset(path).get("steps") or []
        named = [str(s.get("tickId", "")).strip() for s in steps if isinstance(s, dict)]
        found = set(named)
        found.discard("")
        tick_cache[g] = found
        repeat_cache[g] = {t: named.count(t) for t in found if named.count(t) > 1}
        return found

    # ---- what each skill GRANTS: {state_name: stacking? (None = the engine's own, not
    #      the author's, so this checker holds no opinion on how it stacks)} -----------
    grants = {}
    for s in skills:
        sid, piles = str(s.get("id", "")), {}
        for b in s.get("bindings") or []:
            for e in b.get("effects") or []:
                pile = pile_of(e)
                if pile:
                    piles[pile] = piles.get(pile) or as_int(e.get("stacking")) == 1
                for later in later_piles.get(kind_name.get(as_int(e.get("kind")), ""), ()):
                    piles.setdefault(later, None)
        grants[sid] = piles

    problems, warnings = [], []

    def problem(skill, text):
        problems.append((str(skill.get("displayName") or skill.get("id")), text))

    def warn(skill, text):
        warnings.append((str(skill.get("displayName") or skill.get("id")), text))

    def name_of(skill_id):
        rows = by_id.get(skill_id) or []
        return str(rows[0].get("displayName")) if rows else skill_id

    # ⛔ A FORMAT CHANGE FINISHES ON DISK. The game used to convert an old skill into the
    # current shape as it loaded and leave the file untouched — so a skill nobody happened
    # to re-save stayed in the old shape indefinitely while the game silently papered over
    # it. Seven of twenty-five sat like that for months; the day the old fields were tidied
    # away they would have lost their values with NO error, because the zero each fell back
    # to is a legal value. That conversion is gone (2026-08-23) and this rule is what keeps
    # it gone: a saved skill must already be in the current shape.
    #
    # Any of these fields present means the file predates a format change and was never
    # rewritten. Fix by re-saving every skill through Unity as part of the change that moved
    # the format — never by reviving a converter.
    RETIRED_FIELDS = {
        "trigger":          "its trigger",
        "triggerSkillId":   "which skill its trigger names",
        "requiresState":    "its stack gate",
        "requireSkillId":   "whose pile its gate reads",
        "requireState":     "which pile its gate reads",
        "consumeAllStacks": "whether its gate spends the whole pile",
        "triggersMigrated": "the old already-converted marker",
        "targetingMigrated": "the old already-converted marker",
        "trademarkName":    "a separate weapon name (a trademark weapon is now called by its skill's own name)",
    }

    def entries_of(skill):
        return skill.get("triggers") or []

    for s in skills:
        sid = str(s.get("id", ""))

        # THE SHAPE RULE, checked before anything else: a saved skill must already be in the
        # current shape. A retired field still sitting in the file means that file predates a
        # format change and was never rewritten — the exact silent state this whole rule exists
        # to outlaw. Reported per field so the fix is obvious.
        for field, what in RETIRED_FIELDS.items():
            if field in s:
                problem(s, "was saved in an OLD FORMAT — it still stores %s the retired way (%s). "
                           "Re-save every skill through Unity; never revive a converter." % (what, field))
        for b_ in s.get("bindings") or []:
            for field in ("targets", "scope"):
                if field in b_:
                    problem(s, "has a tick saved in an OLD FORMAT — it still stores its targeting "
                               "the retired way (%s). Re-save every skill through Unity." % field)
                    break

        entries = entries_of(s)
        gated = [e for e in entries if as_int(e.get("requiresState")) == 1]

        if not entries:
            problem(s, "has no trigger at all, so nothing can ever run it.")

        # ---- identity -----------------------------------------------------------------
        if not sid:
            problem(s, "has no id, so nothing can load it.")
        elif len(by_id.get(sid, [])) > 1:
            problem(s, "shares its id with another skill — only one of them can ever load.")
        elif sid != s["_file"]:
            warn(s, "is saved under a different name than its id, so it is easy to lose track of.")
        if as_int(s.get("published")) == 1 and as_int(s.get("weaponType")) == weapons.get("None", 0):
            warn(s, "is published for loot but belongs to no weapon, so no weapon can ever roll it.")

        # ---- learning: how far it can be raised, and what it needs first ------------------------------
        # The skill-tree rules (decided 2026-10-03): a skill that belongs to a weapon is learned and raised with
        # mastery points up to its max level — 1 when nothing on it grows, so no point is ever spent on nothing —
        # and it may only need another skill that it literally USES.
        def grows(skill):
            for b in skill.get("bindings") or []:
                for e in b.get("effects") or []:
                    kind = kind_name.get(as_int(e.get("kind")), "")
                    if kind in boostable:
                        return True
                    if kind == "Status" and ail_name.get(as_int(e.get("ailment"))) in dot_ailments:
                        return True
            return False

        if as_int(s.get("weaponType")) != weapons.get("None", 0):
            top = as_int(s.get("maxLevel"))
            if top < 1:
                problem(s, "belongs to a weapon but has no max level, so no mastery point can ever be spent on it.")
            elif top > 10:
                problem(s, "can be raised to level %d — the highest any skill goes is 10." % top)
            elif top > 1 and not grows(s):
                problem(s, "can be raised to level %d, but nothing on it grows with its level, so every point past "
                           "the first would do nothing. Its max level must be 1." % top)

        need = str(s.get("needsSkillId", "")).strip()
        if need:
            def uses(skill, parent_id):
                for e in entries_of(skill):
                    if parent_id in (str(e.get("triggerSkillId", "")).strip(), str(e.get("requireSkillId", "")).strip()):
                        return True
                for b in skill.get("bindings") or []:
                    for e in b.get("effects") or []:
                        if str(e.get("modifierSkillId", "")).strip() == parent_id:
                            return True
                # A moment the parent is the source of: a skill that answers a parry needs a skill that parries.
                parent = (by_id.get(parent_id) or [None])[0]
                trig = {v: k for k, v in triggers.items()}
                answers = {trig.get(as_int(e.get("trigger")), "") for e in entries_of(skill)}
                parent_kinds = {kind_name.get(as_int(e.get("kind")), "")
                                for b in (parent or {}).get("bindings") or [] for e in b.get("effects") or []}
                return "OnParry" in answers and "Parry" in parent_kinds

            parent = (by_id.get(need) or [None])[0]
            # Walk up the chain of requirements: a skill that comes back round to itself can never be learned.
            seen_chain, at, loops = {sid}, need, False
            while at:
                if at in seen_chain:
                    loops = True
                    break
                seen_chain.add(at)
                row = (by_id.get(at) or [None])[0]
                at = str(row.get("needsSkillId", "")).strip() if row else ""
            if need not in by_id:
                problem(s, "needs a skill that does not exist (%s), so it can never be learned." % need)
            elif loops:
                problem(s, "needs %s, whose own requirements lead back round to this skill — neither can ever be "
                           "learned first." % name_of(need))
            elif as_int(parent.get("weaponType")) != as_int(s.get("weaponType")):
                problem(s, "needs %s, which belongs to a different weapon — the points that learn it can never be "
                           "spent on the same tree." % name_of(need))
            elif max(1, as_int(s.get("masteryLevel"), 1)) < max(1, as_int(parent.get("masteryLevel"), 1)):
                problem(s, "opens at mastery %d but needs %s, which opens at %d — it sits before the skill it needs "
                           "on the timeline." % (max(1, as_int(s.get("masteryLevel"), 1)), name_of(need),
                                                 max(1, as_int(parent.get("masteryLevel"), 1))))
            elif not uses(s, need):
                problem(s, "needs %s, but nothing in it uses %s. A requirement must be something the skill literally "
                           "uses — its stacks, its pile, its cast or a moment it causes (the skill-tree rule)."
                        % (name_of(need), name_of(need)))


        # ---- triggers and their gates -------------------------------------------------
        seen = set()
        for e in entries:
            tname = trigger_name = {v: k for k, v in triggers.items()}.get(as_int(e.get("trigger")), "?")
            key = (tname, str(e.get("triggerSkillId", "")))
            if key in seen:
                problem(s, "answers the same moment twice; the second copy can never run.")
            seen.add(key)

            named = str(e.get("triggerSkillId", "")).strip()
            if trigger_name == "OnSkillCastComplete" and not named:
                problem(s, "only runs when another skill finishes casting, but never says which skill — "
                           "so it never runs, and it takes no hotbar slot either.")
            elif named and named not in by_id:
                problem(s, "waits on a skill that does not exist (%s), so that moment never arrives." % named)

            if as_int(e.get("requiresState")) != 1:
                continue

            granter = str(e.get("requireSkillId", "")).strip()
            if not granter:
                problem(s, "reads someone's stacks but never says whose, so it counts nothing.")
                continue
            # The run of consecutive hits is counted by the fight, not granted by a skill: it names no pile because
            # there is none, and it is never spent. Every check below asks about a granting skill, so none applies.
            if granter == RUN_OF_HITS_ID:
                continue
            if as_int(e.get("requireStateIdSet")) != 1:
                problem(s, "reads %s's stacks but nobody picked WHICH pile — it counts nothing and "
                           "spends nothing, silently." % name_of(granter))
                continue
            pile = state_name.get(as_int(e.get("requireStateId")), "?")
            if granter not in by_id:
                problem(s, "reads a pile from a skill that does not exist (%s)." % granter)
                continue
            granted = grants.get(granter, {})
            if pile not in granted:
                problem(s, "waits on %s's %s pile, but %s never leaves that pile behind — "
                           "it will always read empty."
                        % (name_of(granter), pile, name_of(granter)))
            elif granted[pile] is False and (as_int(e.get("requireMinStacks")) > 1
                                             or as_int(e.get("consumeStacks")) > 1):
                warn(s, "wants several of %s's charges, but %s only ever hands out one at a time."
                     % (name_of(granter), name_of(granter)))

        # ---- things that read a pile the skill never gated -----------------------------
        if as_int(s.get("repeatCostsCharge")) == 1 and not gated:
            problem(s, "is set to repeat until its charges run out, but it holds no charges — "
                       "so it plays once and stops.")

        # ---- bindings, effects, modifiers ---------------------------------------------
        exposed = ticks_of(s.get("vfx"))
        repeated = repeat_cache.get(guid_of(s.get("vfx")), {})
        bindings = s.get("bindings") or []
        if bindings and not guid_of(s.get("vfx")):
            problem(s, "has gameplay wired up but no visual to hang it on, so none of it ever runs.")

        bound_ticks = set()
        for b in bindings:
            tick = str(b.get("tickId", "")).strip()
            if not tick:
                problem(s, "has a piece of gameplay that names no beat of its visual, so it never runs.")
            elif exposed is not None and tick not in exposed:
                problem(s, "wires gameplay to a beat called \"%s\" that its visual does not have, "
                           "so that gameplay never runs." % tick)
            # Every step carrying a beat sets off the gameplay wired to it, so a beat on two steps runs it twice —
            # the Dash that restarted its glide three times a cast and never eased out.
            elif tick in repeated:
                problem(s, "wires gameplay to the beat \"%s\", but %d steps of its visual carry that beat, so the "
                           "gameplay is set off again by every one of them." % (tick, repeated[tick]))
            if tick in bound_ticks:
                problem(s, "wires two pieces of gameplay to the same beat \"%s\"; only the first is read." % tick)
            bound_ticks.add(tick)

            for e in b.get("effects") or []:
                kind = kind_name.get(as_int(e.get("kind")), "?")

                # A chance of 0 is a real saved value meaning "never happens", and it looks exactly like a field
                # nobody filled in. Every effect carries 1 (certain) unless its author chose otherwise, so a 0
                # is either a dead effect or an asset that missed the upgrade — both worth stopping for.
                if "chance" in e and num_of(e.get("chance"), 1.0) <= 0.0:
                    problem(s, "has an effect set to a 0% chance, so it can never happen.")

                # Every number is now its own little record, and any of them may pull from the pile. So the question
                # is asked of the NUMBERS, not of the effect: does anything here pull with no pile to pull from?
                if not gated:
                    for fname, fval in e.items():
                        if isinstance(fval, dict) and as_int(fval.get("useStacks")) == 1:
                            problem(s, "has a number (%s) sized by a pile of stacks, but the skill never reads a "
                                       "pile — so it always comes out at nothing." % fname)
                            break

                if kind in ("ExtendDuration", "AddStacks"):
                    target = str(e.get("modifierSkillId", "")).strip()
                    if not target:
                        problem(s, "edits another skill's pile but never says whose.")
                        continue
                    if as_int(e.get("modifierStateIdSet")) != 1:
                        problem(s, "edits %s's pile but nobody picked WHICH pile, so it edits nothing."
                                % name_of(target))
                        continue
                    if target not in by_id:
                        problem(s, "edits a pile belonging to a skill that does not exist (%s)." % target)
                        continue
                    mpile = state_name.get(as_int(e.get("modifierStateId")), "?")
                    if kind == "AddStacks" and mpile in (armor_pile, weapon_pile):
                        problem(s, "adds stacks to %s's %s, but an element converter never stacks — one element "
                                   "on one slot has no 'twice'." % (name_of(target), mpile))
                        continue
                    if mpile not in grants.get(target, {}):
                        problem(s, "edits %s's %s pile, but %s never leaves that pile behind."
                                % (name_of(target), mpile, name_of(target)))
                    elif grants[target][mpile] is False:
                        warn(s, "adds to %s's %s, which is not a stacking pile — there is nothing to add to."
                             % (name_of(target), mpile))

    # ---- report ------------------------------------------------------------------------
    def show(title, rows, color):
        if not rows:
            return
        print("\n%s%s%s" % (color, title, OFF))
        last = None
        for who, text in rows:
            if who != last:
                print("  %s" % who)
                last = who
            print("    - %s" % text)

    check_export(skills, problems)

    print("Checked %d skills." % len(skills))
    show("PROBLEMS — these are silently wrong in the running game:", problems, RED)
    show("WORTH A LOOK:", warnings, YEL)
    if not problems and not warnings:
        print("%sEvery skill's wiring holds together.%s" % (GRN, OFF))
    elif not problems:
        print("\n%sNo silent breakage. %d thing(s) worth a look.%s" % (GRN, len(warnings), OFF))
    else:
        print("\n%s%d problem(s), %d warning(s).%s" % (RED, len(problems), len(warnings), OFF))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
