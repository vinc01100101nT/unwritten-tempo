#!/usr/bin/env python3
"""Prove tools/check-server-rules.py still SEES each server fault it exists for — and is quiet on a sound copy.

WHY THIS EXISTS: a checker that has gone blind prints exactly what a clean project prints. This copies the
handful of real files the rules read into a scratch tree, confirms that copy is quiet, then plants one fault at
a time — each the exact shape of a 2026-09-20 finding — and demands the checker name the file.

USAGE:   tools/prove-server-rules-check.py
EXIT:    0 = every planted fault caught and the sound copy quiet, 1 = blind or crying wolf.
"""
import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CHECK = os.path.join(HERE, "check-server-rules.py")
RED, GRN, OFF = "\033[31m", "\033[32m", "\033[0m"

FIXTURE = [
    "charserver/run.sh",
    "charserver/Db.cs",
    "charserver/WorldStats.cs",
    "charserver/Characters.cs",
    "accounts/src/db.ts",
    "accounts/src/server.ts",
    "accounts/src/routes/accounts.ts",
    "accounts/sql/001_init.sql",
    "accounts/sql/006_leaderboard_index.sql",
]


def build_copy(root):
    for relp in FIXTURE:
        src = os.path.join(REPO, relp)
        if not os.path.isfile(src):
            print(RED + "fixture file missing from the real project: " + relp + OFF)
            sys.exit(1)
        dst = os.path.join(root, relp)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)


def run(root):
    done = subprocess.run([sys.executable, CHECK, "--root", root], capture_output=True, text=True)
    return done.returncode, done.stdout


def edit(root, relp, fn):
    p = os.path.join(root, relp)
    text = open(p, encoding="utf-8").read()
    new = fn(text)
    assert new != text, "the plant changed nothing in " + relp
    open(p, "w", encoding="utf-8").write(new)


def main():
    failures = 0
    base = tempfile.mkdtemp(prefix="server-rules-prove-")
    try:
        build_copy(base)
        code, out = run(base)
        if code != 0:
            print(RED + "the sound copy is NOT quiet — the checker cries wolf:\n" + out + OFF)
            return 1
        print(GRN + "sound copy: quiet" + OFF)
        cases = [
            ("a secret with a shell default (\"change-me\")", "charserver/run.sh",
             lambda t: t.replace("export SERVER_SECRET\n", ': "${SERVER_SECRET:=change-me}"\nexport SERVER_SECRET\n', 1)),
            ("a secret with a code fallback", "charserver/Db.cs",
             lambda t: t + '\nstatic class Leak { static string S = System.Environment.GetEnvironmentVariable("SERVER_SECRET") ?? "change-me"; }\n'),
            ("an unbounded account pool", "accounts/src/db.ts",
             lambda t: re.sub(r"new Pool\(\{.*?\}\)", "new Pool({ connectionString: config.databaseUrl })", t, count=1, flags=re.S)),
            ("a character-server connection with no command timeout", "charserver/Db.cs",
             lambda t: t.replace("CommandTimeout = 5,\n", "", 1)),
            ("the rate limiter not registered", "accounts/src/server.ts",
             lambda t: t.replace("await app.register(rateLimit", "// await app.register(rateLimit", 1)),
            ("a character route on the account service (the save-overwrite door)", "accounts/src/routes/accounts.ts",
             lambda t: t + '\n  app.put("/characters/:id/progress", async (request, reply) => reply.send({}));\n'),
            ("an ordered read that names no index", "charserver/WorldStats.cs",
             lambda t: t.replace(" /* index: character_leaderboard */", "", 1)),
            ("an ordered read naming an index no migration creates", "charserver/WorldStats.cs",
             lambda t: t.replace("/* index: character_leaderboard */", "/* index: character_nowhere */", 1)),
        ]
        for name, relp, fn in cases:
            root = tempfile.mkdtemp(prefix="server-rules-case-")
            try:
                shutil.copytree(base, root, dirs_exist_ok=True)
                edit(root, relp, fn)
                code, out = run(root)
                named = code == 1 and os.path.basename(relp) in out
                print((GRN + "caught  " if named else RED + "MISSED  ") + name + OFF)
                if not named:
                    failures += 1
                    print(out)
            finally:
                shutil.rmtree(root, ignore_errors=True)
    finally:
        shutil.rmtree(base, ignore_errors=True)
    total = 9
    if failures:
        print(RED + "%d of %d case(s) failed — the server-rules checker is blind somewhere." % (failures, total) + OFF)
        return 1
    print(GRN + "All %d cases right: the server-rules checker sees every planted fault and is quiet on a sound copy." % total + OFF)
    return 0


if __name__ == "__main__":
    sys.exit(main())
