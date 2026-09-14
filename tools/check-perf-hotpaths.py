#!/usr/bin/env python3
"""Find code in the game's per-frame paths that PERFORMANCE.md forbids.

WHY THIS EXISTS: the read-before-you-edit gate makes sure the rules are READ. Nothing made
sure they were FOLLOWED — and on 2026-08-31 the gate turned out to have been attached to
nothing for five weeks, so neither half was happening. Reading a document is a promise;
this is the receipt.

WHAT IT LOOKS AT: the body of every Update / LateUpdate / FixedUpdate / OnGUI in the game's
runtime scripts — the methods the engine calls once per frame, per object. Editor-only code
is not a frame path and is skipped, exactly as the read-first gate skips it.

WHAT IT CANNOT SEE, and nobody should read its silence as approval:
  · a helper called FROM Update — the cost is real, the call is one hop away, and following
    it needs a compiler, not a text scan;
  · per-hit combat paths, which are hot for the same reason but carry no shared name;
  · anything decided at runtime (how often a branch is actually taken).
It reports what it CAN prove from the text, and a clean run means only that.

Output is one line per finding: path:line: what is wrong. Exit 1 when anything is found.
"""
import os
import re
import sys

REPO = os.path.dirname(os.path.abspath(os.path.dirname(__file__)))
SCRIPTS = os.path.join(REPO, "unity", "Assets", "Scripts")

# The methods the engine itself calls every frame.
MODS = r"(?:public|private|protected|internal|virtual|override|sealed|static|\s)*"

# Called by the engine every frame, per object.
HOT = re.compile(r"^\s*" + MODS +
                 r"void\s+(Update|LateUpdate|FixedUpdate|OnGUI|OnAnimatorMove|OnWillRenderObject|"
                 r"OnPreRender|OnPreCull|OnPostRender|OnRenderObject|OnAnimatorIK)\s*\(\s*\)\s*\{?")

# Called by the physics engine, once per contact per frame while things touch.
CONTACT = re.compile(r"^\s*" + MODS +
                     r"void\s+(OnTriggerStay|OnTriggerEnter|OnTriggerExit|OnCollisionStay|"
                     r"OnCollisionEnter|OnCollisionExit|OnControllerColliderHit)\s*\(")

# A coroutine is resumed every frame for as long as it is running, so its loop body is a frame path.
COROUTINE = re.compile(r"^\s*" + MODS + r"IEnumerator\s+(\w+)\s*\(")

# Where a hit actually lands. Named because a per-hit path has no engine callback to look for, and
# the guide calls it out as hot for the same reason a frame path is: it runs N times a second in a fight.
COMBAT_ENTRY = {"Health.cs": {"ApplyDamage", "TakeDamage", "TakeTrueDamage", "TakeDamageOverkill"},
                "SkillEffectApplier.cs": {"Apply", "ApplyAll", "ApplyOne"}}

# name -> (pattern, what to say). Order is the order they are reported in.
RULES = [
    ("find", re.compile(r"\b(?:FindObjectOfType|FindObjectsOfType|FindObjectsByType|"
                        r"FindFirstObjectByType|FindAnyObjectByType|GameObject\.Find)\s*[<(]"),
     "searches the whole scene every frame"),
    ("getcomponent", re.compile(r"\.?\bGetComponents?(?:InChildren|InParent)?\s*(?:<|\(\s*typeof)"),
     "looks up a component every frame"),
    ("addcomponent", re.compile(r"\bAddComponent\s*<"),
     "adds a component every frame"),
    ("cameramain", re.compile(r"\bCamera\.main\b"),
     "asks the engine for the camera every frame"),
    ("alloc", re.compile(r"\bnew\s+(?:List|Dictionary|HashSet|Queue|Stack|StringBuilder|"
                         r"WaitForSeconds|WaitForSecondsRealtime|GameObject|Texture2D|Material|"
                         r"Mesh|Color\[|string\[|int\[|float\[|Vector[23]\[|byte\[)"),
     "allocates every frame (garbage collector pressure)"),
    ("newarray", re.compile(r"\bnew\s+\w+\s*\[\s*\w"),
     "allocates an array every frame"),
    ("linq", re.compile(r"\.(?:Where|Select|OrderBy|OrderByDescending|Any|All|"
                        r"FirstOrDefault|LastOrDefault|Sum|Average|Concat|Distinct)\s*\(\s*[^)\s]"
                        r"|\.(?:ToList|ToArray)\s*\(\s*\)"),
     "uses LINQ every frame (allocates)"),
    ("interp", re.compile(r"\$\"|\.ToString\s*\(|\bstring\.Format\s*\("),
     "builds a string every frame"),
    ("distance", re.compile(r"(?:Vector[23]\.Distance\s*\(|\.magnitude\b)[^\n]*[<>]|"
                            r"[<>][^\n]*(?:Vector[23]\.Distance\s*\(|\.magnitude\b)"),
     "compares a real distance where a squared one would do"),
    ("physicsalloc", re.compile(r"\bPhysics\.(?:OverlapSphere|OverlapBox|OverlapCapsule|"
                                r"RaycastAll|SphereCastAll|BoxCastAll|CapsuleCastAll)\s*\("),
     "runs a physics query that allocates an array every frame"),
    ("resources", re.compile(r"\bResources\.(?:Load|LoadAll|FindObjectsOfTypeAll)\s*(?:<[^>]+>)?\s*\("),
     "loads from disk every frame"),
    ("sendmessage", re.compile(r"\.SendMessage(?:Upwards)?\s*\("),
     "calls by name every frame (slow reflection)"),
    ("instantiate", re.compile(r"\b(?:Object\.)?Instantiate\s*\("),
     "creates an object every frame instead of reusing a pooled one"),
]

# A lookup written as "cache it once, then never again" is the pattern the guide asks for, so
# the line that does it is not a finding — unless the thing looked for can legitimately be
# absent, which the guide warns turns "runs once" into "runs forever". Only a human can tell
# those apart, so a guarded line is reported quietly, under its own heading.
# The shapes that mean "only the first time": a null test, a dictionary memo, or a pool being
# grown to reach an index. Each one is the pattern the guide ASKS for, so what sits inside it is
# reported quietly rather than as a per-frame fault.
GUARD = re.compile(r"\bnull\b|\?\?=|\bif\s*\(\s*!"
                   r"|\bTryGetValue\s*\(|\bContainsKey\s*\(|\bCount\s*<=?|\bLength\s*<=?"
                   r"|[!=]="            # "only when the value CHANGED" — the guide asks for this one
                   r"|\bTime\.")          # a throttle: "not again until the clock says so"


# The one way to say "I checked this, it is fine, here is why" — written on the line itself so the
# reason sits with the code and can be argued with, not buried in a list somewhere else.
PERF_OK = re.compile(r"//\s*perf-ok\b")


def strip_noise(line):
    """The code on this line, without a trailing comment or a string's contents. Keeps the
    scan from reading a rule's own name out of a comment about it."""
    line = re.sub(r"//.*$", "", line)
    return re.sub(r'"(?:[^"\\]|\\.)*"', '""', line)


# Any method definition, so a per-frame body can be followed into what it calls.
ANY_METHOD = re.compile(r"^\s*(?:public|private|protected|internal|static|virtual|override|"
                        r"sealed|async|\s)*[\w<>,\[\]\.]+\s+(\w+)\s*\([^;]*\)\s*\{?\s*$")
CALL = re.compile(r"(?<![.\w])(\w+)\s*\(")          # a bare call: this file's own helper
QUALIFIED = re.compile(r"\b(\w+)\.(\w+)\s*\(")       # Other.Thing() — resolved across files below
# Words that look like a call but are not one of ours.
NOT_A_CALL = {"if", "for", "foreach", "while", "switch", "return", "lock", "using", "catch",
              "new", "typeof", "sizeof", "nameof", "yield"}


def _body(lines, i):
    """(first, last) of the block that starts at or just after line i, by counting braces."""
    depth, started, j = 0, False, i
    while j < len(lines):
        code = strip_noise(lines[j])
        depth += code.count("{") - code.count("}")
        if "{" in code:
            started = True
        if started and depth <= 0:
            break
        j += 1
    return i, min(j, len(lines) - 1)


def _yield_loops(lines, body):
    """The loop bodies inside a coroutine that actually yield — the only part of it that repeats."""
    start, end = body
    out = []
    for i in range(start, end + 1):
        code = strip_noise(lines[i])
        if not re.match(r"\s*(?:while|for|foreach)\s*\(", code):
            continue
        span = _body(lines, i)
        if any("yield" in strip_noise(lines[j]) for j in range(span[0], span[1] + 1)):
            out.append(span)
    return out


def hot_spans(lines, follow=True, entries=None):
    """Every per-frame method body — and, up to two hops deeper, the bodies of the methods those
    call in the same file. A cost does not stop being per-frame because it was moved into a
    helper, and the helper is where most of it actually lives."""
    defs = {}
    for i, raw in enumerate(lines):
        m = ANY_METHOD.match(strip_noise(raw))
        if m and m.group(1) not in NOT_A_CALL:
            defs.setdefault(m.group(1), i)

    spans, reached = [], {}
    for i, raw in enumerate(lines):
        code = strip_noise(raw)
        if HOT.match(code) or CONTACT.match(code):
            spans.append(_body(lines, i))
        elif COROUTINE.match(code):
            # A coroutine's SETUP runs once per start; only the loop it yields inside runs frame after
            # frame. Treating the whole body as per-frame reported a window being built once as 40
            # per-frame faults. (The one thing the guide bans in the setup — a `new WaitForSeconds`
            # per start — is checked over the whole body, separately, in scan().)
            spans.extend(_yield_loops(lines, _body(lines, i)))
    for name in entries or ():
        if name in defs:
            span = _body(lines, defs[name])
            if span not in spans:
                spans.append(span)

    if follow:
        frontier = list(spans)
        for _hop in range(2):
            nxt = []
            for start, end in frontier:
                for n in range(start, end + 1):
                    for name in CALL.findall(strip_noise(lines[n])):
                        if name in NOT_A_CALL or name not in defs:
                            continue
                        span = _body(lines, defs[name])
                        if span in spans:
                            continue
                        spans.append(span)
                        nxt.append(span)
                        # A helper called behind a "have we built this yet?" test runs once, not
                        # every frame — so everything inside it inherits that, or a window's
                        # one-time construction reads as 49 per-frame faults. (Measured: it did.)
                        # The gate can sit at the CALL, not inside the helper: a window's refresh
                        # is routinely written as "nothing changed -> return" in the caller, with
                        # the string-building left plain in the method below it.
                        quiet = (guarded(lines, n)
                                 or after_memo_return(lines, start, n)
                                 or reached.get((start, end), ("", False))[1])
                        reached[span] = (name, quiet)
            frontier = nxt
    return spans, reached


# "Test it and reset it in the same breath" — `if (dirty) { dirty = false; … }` and
# `if (timer >= 0.25f) { timer = 0f; … }` are the same gate wearing two faces: the work runs, the
# thing that allowed it is immediately cleared, so it cannot run again until something sets it back.
RESET_GATE = re.compile(r"\b(?:if|while)\s*\(\s*!?\s*(\w+)")


def resets_its_own_test(line):
    m = RESET_GATE.search(line)
    return bool(m) and re.search(r"\b%s\s*=[^=]" % re.escape(m.group(1)), line) is not None


def throttled_return(lines, start, n):
    """An early `if (clock/flag …) return;` whose tested field is pushed forward just below it —
    the "not again until later" gate the guide asks for (see FootstepTracker's nextFind)."""
    for i in range(start, n):
        line = strip_noise(lines[i])
        m = re.match(r"\s*if\s*\(\s*([\w.]+)\s*[<>=]", line)
        if not m or "return" not in line:
            continue
        field = m.group(1).split(".")[-1]
        for j in range(i, min(i + 4, n)):
            if re.search(r"\b\w*%s\w*\s*=[^=]" % re.escape(field), strip_noise(lines[j])):
                return True
        # a clock read on the left means the field being pushed is on the right of the comparison
        for j in range(i, min(i + 4, n)):
            if re.search(r"\b(next\w*|\w*Timer|\w*At)\s*=[^=]", strip_noise(lines[j])):
                return True
    return False


def guarded(lines, n):
    """Is this line only reached when something was missing? A lookup written as "find it once,
    then never again" is the pattern the guide ASKS for, so it is reported separately.

    Two shapes count, and nothing else: the test sits on this very line, or an `if` that tests for
    absence OPENS the block this line is inside — which is why the indent has to be compared. A
    plain `x != null` sitting on a neighbouring line means nothing about whether THIS line runs,
    and taking it as a guard is what hid a per-frame whole-scene search two lines below one."""
    here = strip_noise(lines[n])
    if GUARD.search(here) or resets_its_own_test(here):
        return True
    indent = len(here) - len(here.lstrip())
    for back in range(1, 8):
        if n - back < 0:
            break
        above = strip_noise(lines[n - back])
        if not above.strip() or above.strip() in ("{", "}"):
            continue                              # a brace of its own is not the test, keep looking
        col = len(above) - len(above.lstrip())
        if col >= indent:
            continue                              # a sibling statement, not the test that opened us
        if above.lstrip().startswith(("if", "while")) and (GUARD.search(above)
                                                            or resets_its_own_test(above)):
            return True
        if col < indent:
            break                                 # we have left the block; stop looking
    return False


# "We already have one — stop here." Everything after such a line in the same method runs only on
# the first pass, so it is not a per-frame cost.
# "!= null" is a memo ("we already have one, stop"); "== null" is the opposite — it means the work
# could not be done this frame, and says nothing about the frames after. Only the first counts.
MEMO_RETURN = re.compile(r"^\s*if\s*\(.*(?:TryGetValue|ContainsKey|[!=]=).*\)[^;]*\breturn\b")
# ...but "== null → return" is the opposite of a memo: it means the work could not be done at all
# this frame, and says nothing about the next one.
NOT_A_MEMO = re.compile(r"==\s*null|null\s*==")


def after_memo_return(lines, start, n):
    """Does a 'we already built this' early return sit above this line, in the same method?"""
    for i in range(start, n):
        line = strip_noise(lines[i])
        if MEMO_RETURN.search(line) and not NOT_A_MEMO.search(line):
            return True
    return throttled_return(lines, start, n)


def scan(path, expensive=None):
    expensive = expensive or {}
    with open(path, "r", encoding="utf-8", errors="ignore") as f:
        lines = f.read().splitlines()

    out = []
    in_block_comment = False
    editor_only = 0
    for i, raw in enumerate(lines):
        if not COROUTINE.match(strip_noise(raw)):
            continue
        cstart, cend = _body(lines, i)
        for n in range(cstart, cend + 1):
            if PERF_OK.search(lines[n]):
                continue
            if re.search(r"\bnew\s+WaitForSeconds", strip_noise(lines[n])):
                out.append((n + 1, "wait",
                            "allocates a new wait every time this coroutine starts (cache it in a field)",
                            guarded(lines, n), strip_noise(lines[n]).strip(), None))

    spans, reached = hot_spans(lines, entries=COMBAT_ENTRY.get(os.path.basename(path)))
    for start, end in spans:
        where, once = reached.get((start, end), (None, False))
        for n in range(start + 1, end + 1):
            raw = lines[n]
            bare = raw.strip()
            if bare.startswith("#if UNITY_EDITOR") or bare.startswith("#if DEVELOPMENT_BUILD"):
                editor_only += 1
            elif bare.startswith("#endif") and editor_only:
                editor_only -= 1
            if editor_only:
                continue                          # not in a shipped build; the guide allows it
            if "/*" in raw:
                in_block_comment = True
            if in_block_comment:
                if "*/" in raw:
                    in_block_comment = False
                continue
            if PERF_OK.search(raw):
                continue                          # marked as checked, with its reason on the line
            code = strip_noise(raw)
            if not code.strip() or code.strip().startswith("*"):
                continue
            hit = False
            for name, pattern, why in RULES:
                if pattern.search(code):
                    quiet = once or guarded(lines, n) or after_memo_return(lines, start, n)
                    out.append((n + 1, name, why, quiet, code.strip(), where))
                    hit = True
                    break
            if hit:
                continue
            for recv, method in QUALIFIED.findall(code):
                owner = recv + ".cs"
                if owner == os.path.basename(path):
                    continue                      # its own class; the direct rules already saw it
                if method in expensive.get(owner, ()):
                    out.append((n + 1, "callsexpensive",
                                "calls %s.%s(), which searches the whole scene every time"
                                % (recv, method),
                                once or guarded(lines, n) or after_memo_return(lines, start, n),
                                code.strip(), where))
                    break
    return out


# A method whose own body searches the scene or spawns objects. Built once across every runtime
# script, so a per-frame call to somebody ELSE's expensive helper is caught too — a cost does not
# become cheap by living in another file. (Measured: this is the only way the sky's per-frame
# whole-scene search for the cloud layer was ever going to show up.)
EXPENSIVE_BODY = re.compile(r"\b(?:FindObjectOfType|FindObjectsOfType|FindObjectsByType|"
                            r"FindFirstObjectByType|FindAnyObjectByType|GameObject\.Find)\s*[<(]"
                            r"|\bResources\.Load")


def expensive_methods(files):
    """Which methods search the scene, per FILE — {"CloudShadows.cs": {"Ensure"}}. Keyed by file
    because a Unity script is named after its type, so `CloudShadows.Ensure()` can be resolved to
    the real body instead of to whichever other class happens to own a method of the same name.
    (Measured: keyed by name alone, three of the four hits were the wrong class entirely.)"""
    out = {}
    for path in files:
        with open(path, "r", encoding="utf-8", errors="ignore") as f:
            lines = f.read().splitlines()
        for i, raw in enumerate(lines):
            m = ANY_METHOD.match(strip_noise(raw))
            if not m or m.group(1) in NOT_A_CALL:
                continue
            start, end = _body(lines, i)
            for n in range(start, end + 1):
                # A search that is itself behind "only if we haven't got one yet" costs nothing after
                # the first call, so calling that method every frame is not a per-frame search.
                if EXPENSIVE_BODY.search(strip_noise(lines[n])) and not guarded(lines, n):
                    out.setdefault(os.path.basename(path), set()).add(m.group(1))
                    break
    return out


def main():
    argv = sys.argv[1:]
    show_guarded = "--all" in argv
    findings, guarded = [], []

    every = []
    for dirpath, dirs, files in os.walk(SCRIPTS):
        dirs[:] = [d for d in dirs if d != "Editor"]
        every.extend(os.path.join(dirpath, f) for f in files if f.endswith(".cs"))
    expensive = expensive_methods(every)

    for dirpath, dirs, files in os.walk(SCRIPTS):
        dirs[:] = [d for d in dirs if d != "Editor"]
        for name in sorted(files):
            if not name.endswith(".cs"):
                continue
            path = os.path.join(dirpath, name)
            rel = os.path.relpath(path, REPO)
            for line, _rule, why, is_guarded, code, where in scan(path, expensive):
                via = " (reached via %s)" % where if where else ""
                row = "%s:%d: %s%s  ->  %s" % (rel, line, why, via, code[:110])
                (guarded if is_guarded else findings).append(row)

    for row in findings:
        print(row)
    if guarded:
        print("\n%d more sit behind a null-check — cached once, or a search that repeats forever "
              "when the thing is absent. Run with --all to see them." % len(guarded))
        if show_guarded:
            for row in guarded:
                print(row)
    if not findings and not guarded:
        print("no per-frame violations found")
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
