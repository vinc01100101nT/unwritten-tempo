#!/usr/bin/env python3
"""Which files a tool call WRITES — the one answer both guards ask, given once.

WHY THIS EXISTS: the read-before-you-edit gate and the end-of-turn checker each worked this
out for themselves, and both worked it out the same wrong way — by trusting the "which file"
field that ONLY the dedicated editing tools carry. A change made with a shell command
therefore looked like no change at all. On 2026-08-31 a whole session's edits to the game's
C# went through the shell: the gate never asked for PERFORMANCE.md and the end-of-turn
checker decided the session had written nothing and ran neither checker. One blind spot, in
two copies, is exactly what a single shared answer prevents.

A shell command has to be READ to learn what it writes, and reading one is guesswork with a
DIRECTION: over-reporting a write costs a re-read of a document, while under-reporting one
costs a guard that silently guards nothing. Every rule below therefore leans towards saying
"written", and the guards are built to survive being told so.
"""
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))

# The tools that say outright which file they are writing.
EDIT_TOOLS = {"Edit", "Write", "MultiEdit", "NotebookEdit"}

# Anything shaped like a file name, wherever it sits — bare, in a path, or inside the quotes of
# a script fed to an interpreter on the command line.
_FILEISH = re.compile(r"[\w./\\+-]*\w\.[A-Za-z]\w{0,7}")

# A destination written by the shell itself. `2>&1` yields nothing: the character class stops
# at the ampersand, so there is no target to take.
_REDIRECT = re.compile(r">>?\s*([^\s;|&()<>\"']+)")
_TEE = re.compile(r"\btee\b\s+(?:-a\s+)?([^\s;|&()<>\"']+)")

# A command that copies or moves: everything lands on the LAST word of that segment.
_COPY = re.compile(r"\b(?:cp|mv|install|rsync)\b([^;|&\n]*)")

# Shapes that rewrite a file named ANYWHERE in the command — an editor working in place, an
# interpreter handed a script inline, a patch. None of these announce their target as a word
# of their own, so every file the command names has to count as written.
_REWRITES_IN_PLACE = re.compile(
    r"""\b(?:sed|perl|ruby)\b[^;|&\n]*?\s-i          # sed -i / perl -i
      | \bopen\s*\([^)]*['"][wax]                     # open(path, 'w')
      | \.write(?:lines|_text)?\s*\(                  # f.write( / Path.write_text(
      | \b(?:os\.replace|os\.rename|shutil\.(?:copy|move))\s*\(
      | \bpatch\b | \btruncate\b | \bdd\s+if=         # patch / truncate / dd
      | \bapplypatch\b
    """,
    re.VERBOSE,
)

# Where a bare file name is looked for. Deliberately NOT the whole repo: the engine's own
# library folders hold hundreds of thousands of files and are never edited by hand.
_SEARCH_ROOTS = [
    os.path.join(REPO, "unity", "Assets"),
    os.path.join(REPO, "tools"),
    os.path.join(REPO, "accounts", "src"),
    os.path.join(REPO, "docs"),
]
# open(p, "w") / Path(p).write_text(...) — the name being written is held in `p`.
_WRITE_VAR = re.compile(r"(?:open|write_text|writelines|replace|rename|copy|move)\s*\(\s*(\w+)")

_SKIP_DIRS = {".git", "node_modules", "Library", "Temp", "obj", "bin", "__pycache__"}

# Only these count when a name is swept out of the middle of a command. A file being written
# by name is taken as written whatever it is called, but `io.open` and `self.write` are shaped
# exactly like file names too, and an allow-list is what tells them apart.
_CODE_EXTENSIONS = {
    "cs", "asset", "py", "sh", "js", "ts", "tsx", "json", "jsonl", "md", "uxml", "uss",
    "yaml", "yml", "txt", "html", "css", "shader", "cginc", "prefab", "unity", "meta",
}


def files_written(tool_name, tool_input, cwd=""):
    """Every file this call is taken to write, absolute where that can be worked out. An
    editing tool names its own; a shell command is read for the shapes above."""
    tool_input = tool_input or {}
    if tool_name in EDIT_TOOLS:
        target = tool_input.get("file_path")
        return [_resolve(target, cwd)] if target else []
    if tool_name == "Bash":
        return _from_command(tool_input.get("command") or "", cwd)
    return []


def _from_command(command, cwd=""):
    out = []
    for match in _REDIRECT.finditer(command):
        out.append(match.group(1))
    for match in _TEE.finditer(command):
        out.append(match.group(1))
    for match in _COPY.finditer(command):
        words = [w for w in match.group(1).split() if not w.startswith("-")]
        if len(words) >= 2:
            out.append(words[-1])                     # the destination, never the source
    out.extend(_rewritten_in_place(command))

    seen, resolved = set(), []
    for raw in out:
        raw = raw.strip("\"'")
        if not raw or "." not in os.path.basename(raw):
            continue                                  # a directory, or a stream like /dev/null
        path = _resolve(raw, cwd)
        if path not in seen:
            seen.add(path)
            resolved.append(path)
    return resolved


def _rewritten_in_place(command):
    """Files rewritten by an in-place editor or an inline script — taken from the WRITING LINE
    only, never from the whole command.

    Why the line matters: one shell call routinely writes one file while quoting the names of a
    dozen others in a test payload, a comment or a search. Reading the whole command as "wrote all
    of these" attributed seven files to a session that never opened them, and the end-of-turn check
    then demanded fixes to somebody else's code. The name is usually right there on the line that
    writes; when the line writes through a variable, the variable's own assignment is followed."""
    out = []
    for line in command.splitlines():
        if not _REWRITES_IN_PLACE.search(line):
            continue
        out.extend(t for t in _FILEISH.findall(line)
                   if t.rsplit(".", 1)[-1].lower() in _CODE_EXTENSIONS)
        for var in _WRITE_VAR.findall(line):
            for name in re.findall(r"\b%s\s*=\s*['\"]([^'\"]+)['\"]" % re.escape(var), command):
                if name.rsplit(".", 1)[-1].lower() in _CODE_EXTENSIONS:
                    out.append(name)
    return out


def _resolve(path, cwd=""):
    """Absolute where it can be: as given, then against the shell's directory, then the repo,
    then by hunting the name through the folders people actually edit. A name that matches
    none of those is returned as it was written — a file being CREATED does not exist yet, and
    a guard has to be able to speak about it anyway."""
    if os.path.isabs(path):
        return os.path.normpath(path)
    for base in ([cwd] if cwd else []) + [REPO]:
        candidate = os.path.normpath(os.path.join(base, path))
        if os.path.exists(candidate):
            return candidate
    if os.sep not in path and "/" not in path:
        found = _find_by_name(path)
        if found:
            return found
    return path


def _find_by_name(name):
    for root in _SEARCH_ROOTS:
        for dirpath, dirs, files in os.walk(root):
            dirs[:] = [d for d in dirs if d not in _SKIP_DIRS]
            if name in files:
                return os.path.join(dirpath, name)
    return None
