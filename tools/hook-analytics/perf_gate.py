#!/usr/bin/env python3
"""PreToolUse gate: block an edit until the rulebook for that KIND of file has been read this session, and log
every gate decision to hook-analytics.jsonl for the dashboard.

Three rulebooks, one gate (the table is GATES below):
  · runtime C# under Assets/Scripts             -> unity/Assets/Scripts/PERFORMANCE.md
  · a texture/model/audio import setting, a prefab, a project setting, the package manifest
                                                 -> unity/Assets/ASSETS.md
  · the sign-in service, the character server, a migration
                                                 -> SERVER.md (repo root)
The second and third were added on 2026-09-20 after an audit found that half its findings broke a rule that was
already written down and never injected: a written rule is a wish, an injected one is read.

Invoked by Claude Code with the hook payload (session_id, tool_name, tool_input,
transcript_path, ...) on stdin. See tools/hook-analytics/README.md.

How "has it been read?" works WITHOUT a hardcoded phrase
-------------------------------------------------------
We look for a sentinel — a phrase that enters the transcript ONLY when PERFORMANCE.md
is actually Read. Earlier this sentinel was a string literal in THIS file, which meant
reading the gate's own source (to debug it) poisoned the transcript and defeated the
gate for the rest of the session. So we now DERIVE the sentinel at runtime from the
live PERFORMANCE.md: its longest prose line that is NOT also present in the sibling
CLAUDE.md. CLAUDE.md is auto-injected into the transcript whenever you edit a script,
so any line it shares with PERFORMANCE.md would be a false "already read" — we exclude
those. The literal phrase never lives in this file, so reading this file can't trip it.
"""
import sys, os, re, json, datetime

import guard_check   # sibling file — the heartbeat that proves this gate really ran
import tool_writes   # sibling file — the one answer to 'which files does this call write?'

HERE = os.path.dirname(os.path.abspath(__file__))
LOG = os.path.join(HERE, "hook-analytics.jsonl")

_WS = re.compile(r"\s+")
_NONALNUM = re.compile(r"[^a-z0-9]+")


def normalize(text: str) -> str:
    return _WS.sub(" ", text).strip()


def fingerprint(text: str) -> str:
    """Lowercase, then collapse every run of non-alphanumeric chars to a single
    space. The transcript is JSONL, so the Read'd file content lands with its quotes
    backslash-escaped (" -> \\") and is decoded from mixed UTF-8/cp1252 bytes; an exact
    substring of the raw sentinel therefore never matches. Reducing both sides to bare
    [a-z0-9] words is immune to quote-escaping, encoding drift, line-number prefixes,
    and soft-wrapping — it matches on the words alone."""
    return _NONALNUM.sub(" ", text.lower()).strip()


def is_runtime_cs(path: str) -> bool:
    p = path.replace("\\", "/").lower()
    if not p.endswith(".cs"):
        return False
    # Editor-only code isn't a runtime hot path — exclude it.
    if "/assets/scripts/editor/" in p or p.startswith("editor/"):
        return False
    if "/assets/scripts/" in p:
        return True
    # A bare name the shell was given for a file that does not exist YET — a new script being
    # created. It cannot be placed, and the safe way to be wrong is to ask for the document:
    # a needless read costs seconds, while a miss here is the hole this whole gate exists to
    # close. (A .cs anywhere else in this repo would be the first of its kind.)
    return "/" not in p


TEXTURE_OR_MODEL = (".png", ".tga", ".jpg", ".jpeg", ".psd", ".tif", ".tiff", ".exr", ".fbx", ".glb", ".gltf",
                    ".obj", ".wav", ".mp3", ".ogg")


def is_asset_file(path: str) -> bool:
    """A saved setting the ASSETS rulebook governs: an importer's .meta for art/models/audio, a prefab, a
    project or pipeline setting, the package manifest."""
    p = path.replace("\\", "/").lower()
    if p.endswith(".meta"):
        base = p[:-5]
        return "/assets/" in p and base.endswith(TEXTURE_OR_MODEL)
    if p.endswith(".prefab"):
        return "/assets/" in p
    if p.endswith("/packages/manifest.json"):
        return True
    if p.endswith(".asset"):
        return "/projectsettings/" in p or "/assets/settings/" in p
    return False


def is_server_file(path: str) -> bool:
    p = path.replace("\\", "/").lower()
    if "/node_modules/" in p or "/bin/" in p or "/obj/" in p:
        return False
    if "/accounts/src/" in p and p.endswith(".ts"):
        return True
    if "/accounts/sql/" in p and p.endswith(".sql"):
        return True
    if "/charserver/" in p and (p.endswith(".cs") or p.endswith(".sh")):
        return True
    return False


def repo_root() -> str:
    return os.path.dirname(os.path.dirname(HERE))


def scripts_root(file_path: str) -> str:
    """The '.../Assets/Scripts/' prefix of the edited file, in its real casing —
    so PERFORMANCE.md and CLAUDE.md can be located as siblings of any gated file,
    regardless of where the repo lives."""
    fwd = file_path.replace("\\", "/")
    idx = fwd.lower().find("/assets/scripts/")
    if idx >= 0:
        return fwd[: idx + len("/assets/scripts/")]
    # A name with no folder on it (a script being created through the shell). The documents
    # still have to be found, so fall back to this repo's own scripts folder.
    repo = os.path.dirname(os.path.dirname(HERE))
    return os.path.join(repo, "unity", "Assets", "Scripts") + "/"


def read_text(path: str) -> str:
    try:
        with open(path, "r", encoding="utf-8", errors="ignore") as f:
            return f.read()
    except OSError:
        return ""


def is_prose(line: str) -> bool:
    """A wrapped body line — not a heading, list item, table row, blockquote, rule,
    or checkbox. These are the lines that read as plain sentences in the transcript."""
    s = line.strip()
    if len(s) < 40:
        return False
    if s[0] in "#>|*-[":
        return False
    if s.startswith("---"):
        return False
    if re.match(r"^\d+[.)]", s):
        return False
    if "|" in s:  # table row / cell
        return False
    return True


def derive_sentinel(perf_md_path: str, claude_md_path: str) -> str:
    """Longest normalized prose line in PERFORMANCE.md that does NOT also appear in
    the sibling CLAUDE.md (which the harness auto-injects into the transcript). Empty
    string if PERFORMANCE.md is missing/has no usable line."""
    perf = read_text(perf_md_path)
    if not perf:
        return ""
    exclusion = normalize(read_text(claude_md_path))
    best = ""
    fenced = False
    for raw in perf.splitlines():
        if raw.strip().startswith("```"):
            fenced = not fenced          # a command inside a code block is not a sentence anyone reads
            continue
        if fenced or not is_prose(raw):
            continue
        n = normalize(raw)
        if n in exclusion:
            continue
        if len(n) > len(best):
            best = n
    return best


def transcript_has(sentinel: str, transcript_path: str) -> bool:
    if not sentinel or not transcript_path or not os.path.isfile(transcript_path):
        return False
    return fingerprint(sentinel) in fingerprint(read_text(transcript_path))


def log(session_id: str, file_path: str, outcome: str, doc: str = "PERFORMANCE.md") -> None:
    rec = {
        "ts": datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
        "session_id": session_id,
        "file": file_path,
        "outcome": outcome,  # "blocked" | "passed"
        "doc": doc,
    }
    try:
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec) + "\n")
    except OSError:
        pass  # analytics must never break the gate


def allow():
    sys.exit(0)


def deny(reason: str):
    # Satisfy BOTH block protocols so this works on any Claude Code build:
    #  - JSON on stdout (modern): permissionDecision "deny"
    #  - exit code 2 (classic): stderr is surfaced to the model and the call is blocked
    # Some builds ONLY honor exit 2, so we must exit 2 (not 0) and put the reason on stderr.
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }))
    sys.stderr.write(reason + "\n")
    sys.exit(2)


def main():
    try:
        data = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        data = {}
    # Stamp BEFORE deciding anything: a gate that runs and waves the edit through has still
    # run, and that is the case a heartbeat exists to tell apart from a gate that never fired.
    guard_check.beat(os.path.basename(__file__), data.get("session_id", ""))
    if not data:
        allow()  # malformed payload → never block real work
        return

    # Which files does this call write? Asked of the one shared answer, because an edit made
    # with a shell command is still an edit — trusting the editing tools' "which file" field
    # alone is precisely how this gate slept through a session of them.
    written = tool_writes.files_written(data.get("tool_name") or "",
                                        data.get("tool_input") or {},
                                        data.get("cwd") or "")
    repo = repo_root()
    # (matcher, rulebook path, the auto-injected file whose lines must not count as "read", the refusal, log name)
    GATES = [
        (is_runtime_cs, None, None,
         "Performance is a hard constraint here. Read unity/Assets/Scripts/PERFORMANCE.md before editing "
         "runtime C#, then retry this edit.", "PERFORMANCE.md"),
        (is_asset_file, os.path.join(repo, "unity", "Assets", "ASSETS.md"), os.path.join(repo, "CLAUDE.md"),
         "An import setting, prefab, project setting or package is a number that fails silently. Read "
         "unity/Assets/ASSETS.md before editing it, then retry this edit.", "ASSETS.md"),
        (is_server_file, os.path.join(repo, "SERVER.md"), os.path.join(repo, "CLAUDE.md"),
         "The two services have rules that failed silently once already. Read SERVER.md (repo root) before "
         "editing accounts/, charserver/ or a migration, then retry this edit.", "SERVER.md"),
    ]
    session_id = data.get("session_id") or ""
    transcript = data.get("transcript_path") or ""
    for matcher, doc, exclusion, refusal, doc_name in GATES:
        gated = [f for f in written if matcher(f)]
        if not gated:
            continue
        file_path = gated[0]
        if doc is None:                                    # the runtime-C# rulebook sits beside the scripts
            root = scripts_root(file_path)
            doc, exclusion = root + "PERFORMANCE.md", root + "CLAUDE.md"
        sentinel = derive_sentinel(doc, exclusion)
        if transcript_has(sentinel, transcript):
            log(session_id, file_path, "passed", doc_name)
            continue
        log(session_id, file_path, "blocked", doc_name)
        deny(refusal)
        return
    allow()


if __name__ == "__main__":
    main()
