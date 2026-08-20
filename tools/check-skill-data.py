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
    field ("Status" follows the ailment, "Buff" follows the stat) as the marker "*"."""
    body = _body_of(source, r"public static bool TryStateFor\s*\([^)]*\)\s*\{",
                    "which pile an effect leaves behind")
    mapping = {}
    for kind, state in re.findall(r"case\s+SkillEffectKind\.(\w+)\s*:\s*id\s*=\s*StateId\.(\w+)\s*;", body):
        mapping[kind] = state
    # The two that resolve from a sibling field rather than a constant.
    for kind in re.findall(r"case\s+SkillEffectKind\.(\w+)\s*:\s*\n?\s*id\s*=\s*e\.", body):
        mapping[kind] = "*"
    if "Status" in body and "Status" not in mapping:
        mapping["Status"] = "*"
    if not mapping:
        die("Read the effect-to-pile function but understood none of its cases.")
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


def as_float(v, default=0.0):
    try:
        return float(str(v).strip())
    except (TypeError, ValueError):
        return default


def guid_of(ref):
    m = re.search(r"guid:\s*([0-9a-f]{32})", str(ref))
    return m.group(1) if m else ""


# --------------------------------------------------------------------------------------

def main():
    if not os.path.isdir(SKILLS_DIR):
        die("No skills folder at %s" % SKILLS_DIR)

    global RUN_OF_HITS_ID
    RUN_OF_HITS_ID = parse_run_of_hits_id(read(os.path.join(SCRIPTS, "StateGate.cs")))

    effect_src = read(os.path.join(SCRIPTS, "SkillEffect.cs"))
    states = parse_enum(read(os.path.join(SCRIPTS, "UnitStates.cs")), "StateId")
    ailments = parse_enum(read(os.path.join(SCRIPTS, "StatusController.cs")), "Ailment")
    kinds = parse_enum(effect_src, "SkillEffectKind")
    buff_stats = parse_enum(effect_src, "BuffStat")
    triggers = parse_enum(read(os.path.join(SCRIPTS, "SkillDef.cs")), "SkillTrigger")
    weapons = parse_enum(read(os.path.join(SCRIPTS, "WeaponType.cs")), "WeaponType")
    state_for_kind = parse_state_for_kind(effect_src)
    later_piles = parse_later_piles(effect_src)

    kind_name = {v: k for k, v in kinds.items()}
    state_name = {v: k for k, v in states.items()}
    ail_name = {v: k for k, v in ailments.items()}
    stat_name = {v: k for k, v in buff_stats.items()}
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
        if kind == "Buff":
            stat = stat_name.get(as_int(effect.get("buffStat")), "Damage")
            return {"AttackSpeed": "AttackSpeedBuff", "CastSpeed": "CastSpeedBuff"}.get(stat, "DamageBuff")
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

    tick_cache = {}

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
        found = {str(s.get("tickId", "")).strip() for s in steps if isinstance(s, dict)}
        found.discard("")
        tick_cache[g] = found
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

    # A skill saved before the trigger LIST existed still carries its one old trigger in
    # the flat fields beside it, and the game rebuilds an entry from them as it loads. We
    # read it back the same way, or half the library would be checked as if it had no
    # trigger at all — an answer that would always look clean because we never looked.
    def entries_of(skill):
        listed = skill.get("triggers") or []
        if listed or as_int(skill.get("triggersMigrated")) == 1:
            return listed
        return [{
            "trigger": skill.get("trigger", 0),
            "triggerSkillId": skill.get("triggerSkillId", ""),
            "requiresState": skill.get("requiresState", 0),
            "requireSkillId": skill.get("requireSkillId", ""),
            "requireMinStacks": skill.get("requireMinStacks", 1),
            "consumeAllStacks": skill.get("consumeAllStacks", 0),
            "consumeStacks": skill.get("consumeStacks", 0),
            # The old pile field is deliberately NOT carried over by that rebuild, so an
            # old gate arrives with no pile named — which is exactly what we must report.
            "requireStateIdSet": 0,
        }]

    for s in skills:
        sid = str(s.get("id", ""))
        entries = entries_of(s)
        gated = [e for e in entries if as_int(e.get("requiresState")) == 1]

        # ---- identity -----------------------------------------------------------------
        if not sid:
            problem(s, "has no id, so nothing can load it.")
        elif len(by_id.get(sid, [])) > 1:
            problem(s, "shares its id with another skill — only one of them can ever load.")
        elif sid != s["_file"]:
            warn(s, "is saved under a different name than its id, so it is easy to lose track of.")
        if as_int(s.get("published")) == 1 and as_int(s.get("weaponType")) == weapons.get("None", 0):
            warn(s, "is published for loot but belongs to no weapon, so no weapon can ever roll it.")

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
            if tick in bound_ticks:
                problem(s, "wires two pieces of gameplay to the same beat \"%s\"; only the first is read." % tick)
            bound_ticks.add(tick)

            for e in b.get("effects") or []:
                kind = kind_name.get(as_int(e.get("kind")), "?")

                # A chance of 0 is a real saved value meaning "never happens", and it looks exactly like a field
                # nobody filled in. Every effect carries 1 (certain) unless its author chose otherwise, so a 0
                # is either a dead effect or an asset that missed the upgrade — both worth stopping for.
                if "chance" in e and as_float(e.get("chance"), 1.0) <= 0.0:
                    problem(s, "has an effect set to a 0% chance, so it can never happen.")

                if as_int(e.get("useStackValue")) == 1 and not gated:
                    problem(s, "has an effect sized by a pile of stacks, but the skill never reads a "
                               "pile — so that effect always comes out at nothing.")

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
