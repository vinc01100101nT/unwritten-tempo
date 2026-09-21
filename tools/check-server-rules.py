#!/usr/bin/env python3
"""Check the two services — accounts/ (sign-in, Node) and charserver/ (characters, C#) — against the rules in
SERVER.md, the ones the 2026-09-20 audit found broken, turned into tests.

WHY THIS EXISTS: the servers' shared secret defaulted to the word "change-me" and the real server ran with it;
a player could overwrite their own save through a route nobody remembered; the account pool had no timeout at
all; the leaderboard read every character row every five seconds with no index that could serve it. None of it
shows until somebody reads the code, so the code is read every turn.

THE RULES, as tests:
  S1  No secret has a literal default. In a shell script, `${...SECRET...:=value}` / `TOKEN` / `PASSWORD`;
      in TypeScript or C#, an environment read of such a name followed by `??` and a string. Missing = refuse
      to start, never "change-me".
  S2  The account service's pool is bounded: `max`, `statement_timeout`, `connectionTimeoutMillis` all set.
  S3  The character server's connection sets `CommandTimeout`.
  S4  The account service registers the rate limiter (`register(rateLimit`).
  S5  The account service serves NO character route: no app.<verb>("/characters...") in accounts/src.
  S6  Every `order by` in a C# query string names the index that serves it — `/* index: <name> */` — and that
      index is created by a file in accounts/sql. A read that grows with the table is silent until it is not.
  S7  (warn) Every migration file in accounts/sql has been applied to the live database, when it can be asked.

USAGE:   tools/check-server-rules.py [--root DIR] [--all]
EXIT:    0 = clean, 1 = a FAIL printed, 2 = could not run
Fault lines:  FAIL <path>(<line>): <what>     or     FAIL <path>: <what>
"""
import argparse
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REPO = os.path.dirname(HERE)
SECRETISH = r"\w*(?:SECRET|TOKEN|PASSWORD|PASSWD|API_KEY|PRIVATE_KEY)\w*"


def rel(repo, path):
    return os.path.relpath(path, repo).replace(os.sep, "/")


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def code_of(path):
    """The file with line comments removed, line count preserved — a rule quoted in a comment is not the rule."""
    return "\n".join(l.split("//", 1)[0] if not l.lstrip().startswith("--") else "" for l in read(path).splitlines())


def walk(repo, sub, exts):
    root = os.path.join(repo, sub)
    if not os.path.isdir(root):
        return
    for dirpath, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in ("node_modules", "bin", "obj", ".git")]
        for f in sorted(files):
            if f.endswith(exts):
                yield os.path.join(dirpath, f)


# ---- S1  secrets never default --------------------------------------------------------------------------------
def check_secret_defaults(repo, out):
    shell_default = re.compile(r"\$\{(" + SECRETISH + r"):=([^}]*)\}")
    env_fallback = re.compile(r"(?:GetEnvironmentVariable|process\.env)\s*[\(\[.]\s*\"?(" + SECRETISH + r")\"?\s*[\)\]]?\s*\?\?\s*\"[^\"]+\"")
    for sub in ("charserver", "accounts", "tools", "deploy", "scripts"):
        for path in walk(repo, sub, (".sh", ".ts", ".cs")):
            for i, line in enumerate(code_of(path).splitlines(), 1):
                m = shell_default.search(line) or env_fallback.search(line)
                if m:
                    out("FAIL", "%s(%d)" % (rel(repo, path), i),
                        "%s has a literal default — a secret that can be guessed is a door; refuse to start "
                        "without it (the server ran with \"change-me\" for weeks)" % m.group(1))


# ---- S2 / S3  bounded pools -----------------------------------------------------------------------------------
def check_pools(repo, out):
    db_ts = os.path.join(repo, "accounts", "src", "db.ts")
    if os.path.isfile(db_ts):
        t = code_of(db_ts)
        m = re.search(r"new Pool\(\{(.*?)\}\)", t, re.S)
        block = m.group(1) if m else ""
        for key in ("max", "statement_timeout", "connectionTimeoutMillis"):
            if not re.search(r"\b%s\s*:" % key, block):
                out("FAIL", rel(repo, db_ts), "the pool does not set `%s` — unbounded, one hung query holds its "
                    "connection forever" % key)
    db_cs = os.path.join(repo, "charserver", "Db.cs")
    if os.path.isfile(db_cs):
        if "CommandTimeout" not in code_of(db_cs):
            out("FAIL", rel(repo, db_cs), "the connection sets no CommandTimeout — a hung query holds a pooled "
                "connection until the socket dies")


# ---- S4 / S5  the account service: rate-limited, and no character routes -------------------------------------
def check_account_routes(repo, out):
    server_ts = os.path.join(repo, "accounts", "src", "server.ts")
    if os.path.isfile(server_ts) and "register(rateLimit" not in code_of(server_ts):
        out("FAIL", rel(repo, server_ts), "the rate limiter is not registered — a burst of sign-ups from one address "
            "pins every worker thread (each hash is 32 MB and one of four threads)")
    for path in walk(repo, os.path.join("accounts", "src"), (".ts",)):
        for i, line in enumerate(code_of(path).splitlines(), 1):
            if re.search(r"app\.(?:get|post|put|patch|delete)\(\s*\"/character", line):
                out("FAIL", "%s(%d)" % (rel(repo, path), i),
                    "the account service answers a character route — characters live on the character server "
                    "and nowhere else; this was the door through which a player could rewrite their own save")


# ---- S6  every ordered read names its index -------------------------------------------------------------------
def check_ordered_reads(repo, out):
    created = set()
    for path in walk(repo, os.path.join("accounts", "sql"), (".sql",)):
        for m in re.finditer(r"create\s+(?:unique\s+)?index\s+(?:if\s+not\s+exists\s+)?(\w+)", read(path), re.I):
            created.add(m.group(1).lower())
        for m in re.finditer(r"constraint\s+(\w+)\s+(?:primary\s+key|unique)", read(path), re.I):
            created.add(m.group(1).lower())
    for path in walk(repo, "charserver", (".cs",)):
        for i, line in enumerate(read(path).splitlines(), 1):
            if not re.search(r"\border\s+by\b", line, re.I):
                continue
            m = re.search(r"/\*\s*index:\s*(\w+)\s*\*/", line)
            if not m:
                out("FAIL", "%s(%d)" % (rel(repo, path), i),
                    "an `order by` with no `/* index: <name> */` — a read with no index behind its ordering "
                    "sorts every row and grows with the table (the leaderboard did, every five seconds)")
            elif m.group(1).lower() not in created:
                out("FAIL", "%s(%d)" % (rel(repo, path), i),
                    "names index `%s`, which no file in accounts/sql creates" % m.group(1))


# ---- S7  migrations applied (warn) ---------------------------------------------------------------------------
def check_migrations_applied(repo, out):
    env = os.path.join(repo, "accounts", ".env")
    sql_dir = os.path.join(repo, "accounts", "sql")
    if not os.path.isfile(env) or not os.path.isdir(sql_dir):
        return
    url = None
    for line in read(env).splitlines():
        if line.startswith("DATABASE_URL="):
            url = line.split("=", 1)[1].strip()
    if not url:
        return
    try:
        done = subprocess.run(["psql", url, "-A", "-t", "-c", "select filename from schema_migration"],
                              capture_output=True, text=True, timeout=5)
    except (OSError, subprocess.TimeoutExpired):
        return
    if done.returncode != 0:
        return
    applied = {l.strip() for l in done.stdout.splitlines() if l.strip()}
    for f in sorted(os.listdir(sql_dir)):
        if f.endswith(".sql") and f not in applied:
            out("WARN", rel(repo, os.path.join(sql_dir, f)),
                "not yet applied to the live database — `npm run migrate` in accounts/")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=DEFAULT_REPO)
    ap.add_argument("--all", action="store_true")
    a = ap.parse_args()
    repo = os.path.abspath(a.root)
    if not os.path.isdir(os.path.join(repo, "accounts")) and not os.path.isdir(os.path.join(repo, "charserver")):
        print("cannot run: %s has neither accounts/ nor charserver/" % repo)
        return 2
    fails, warns = [], []

    def out(level, where, what):
        (fails if level == "FAIL" else warns).append("%s %s: %s" % (level, where, what))

    check_secret_defaults(repo, out)
    check_pools(repo, out)
    check_account_routes(repo, out)
    check_ordered_reads(repo, out)
    check_migrations_applied(repo, out)
    for line in fails:
        print(line)
    if a.all:
        for line in warns:
            print(line)
    if fails:
        print("\n%d server rule(s) broken — the rules are SERVER.md at the repo root." % len(fails))
        return 1
    print("server rules clean%s." % (" (%d warning(s) — run with --all)" % len(warns) if warns and not a.all else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
