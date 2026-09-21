#!/usr/bin/env python3
"""Reading C# by method — the one parser every source-reading checker shares.

WHY THIS EXISTS: two checkers (editor writes, client math) both need "which method is this line in, what does
it call, and what is its body". Two copies of a brace-matcher drift apart the first time one is fixed; this is
the one copy. It is deliberately a regex-and-braces reader, not a compiler: good enough to find a method's body
and its call names, honest about being approximate (a `{` inside a string can mislead it).
"""
import re

METHOD_HEAD = re.compile(
    r"^[ \t]*(?:\[[^\n]*\][ \t]*\n[ \t]*)*"                      # attributes on their own lines
    r"(?:(?:public|private|protected|internal|static|override|virtual|async|unsafe|new)\s+)*"
    r"(?:[\w<>\[\],.?]+\s+)?(\w+)\s*\([^)]*\)\s*(?:where[^{;]*)?(=>|\{)", re.M)
NOT_METHODS = ("if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return", "else")
CALL = re.compile(r"\b(\w+)\s*\(")


def strip_comments(line):
    """The code half of a line — a write mentioned in a comment is not a write."""
    return line.split("//", 1)[0]


def strip_strings(line):
    """The code with every string literal blanked, so an operator inside quotes is not an operator."""
    return re.sub(r'"(?:\\.|[^"\\])*"', '""', line)


def code_only(text):
    """The whole file with comments removed line by line, line numbers preserved."""
    return "\n".join(strip_comments(l) for l in text.splitlines())


def methods_of(code):
    """name -> list of (head line, body-start line, body text, head text). Bodies are found by brace matching
    from the head; an expression-bodied member runs to its semicolon. Line numbers are 1-based."""
    found = {}
    for m in METHOD_HEAD.finditer(code):
        name, kind = m.group(1), m.group(2)
        if name in NOT_METHODS:
            continue
        i = m.end()
        if kind == "=>":
            j = code.find(";", i)
            body = code[i:j + 1]
        else:
            depth, j = 1, i
            while j < len(code) and depth:
                depth += {"{": 1, "}": -1}.get(code[j], 0)
                j += 1
            body = code[i:j]
        head_line = code[:m.start()].count("\n") + 1
        found.setdefault(name, []).append((head_line, code[:i].count("\n") + 1, body, m.group(0)))
    return found


def reachable(methods, roots):
    """Every method in the file that a root can reach by calling, to a fixpoint."""
    seen, todo = set(), list(roots)
    while todo:
        n = todo.pop()
        if n in seen or n not in methods:
            continue
        seen.add(n)
        for _h, _s, body, _head in methods[n]:
            for c in CALL.findall(body):
                if c in methods and c not in seen:
                    todo.append(c)
    return seen
