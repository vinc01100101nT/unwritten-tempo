"""A throwaway copy of the repo in which ONE file differs — and the real project is never written to.

WHY THIS EXISTS: a prover's whole job is to plant a fault, confirm the check screams, and put the file
back. The old way planted the fault in the REAL game file and relied on a `finally` to undo it. A
`finally` does not run when the process is killed, and worse, the backup lived next to the source as
`<file>.prove-backup`: a run that died left BOTH a faulted file and a stale backup, and the next run
copied the already-faulted file over that good backup and then "restored" from it — baking the fault
into the game permanently while reporting that the check had gone deaf. That is not a risk worth
running to test a test. (2026-09-22: it put MaxHitChance = 1.05 into Combat.cs and it reached a commit
attempt; only the pre-commit check stopped it.)

WHAT THIS DOES INSTEAD: builds a directory of SYMLINKS pointing at the real repo, then replaces just
the one file on the path being faulted with a real, modified copy — punching real directories only
along that one path. Nothing outside the temporary directory is ever opened for writing, so no crash,
kill, timeout or power cut can leave the project changed. It costs no disk: everything else is a link.
"""

import os
import shutil
import tempfile


def mirror_with_change(repo, rel_path, old, new, count=1):
    """A temp directory that mirrors `repo`, in which `rel_path` has `old` swapped for `new`.

    Returns (root, how_many_swapped). The caller runs its checker inside `root` and then calls
    `discard(root)`. A swap count of 0 means the anchor was not found and the run proves nothing.
    """
    root = tempfile.mkdtemp(prefix="prove-mirror-")
    parts = rel_path.split(os.sep)

    # Every level along the faulted file's path is a REAL directory holding links to its real
    # siblings; every level off that path is a single link. So only this one file is a real file.
    here_real, here_temp = repo, root
    for depth, part in enumerate(parts):
        last = depth == len(parts) - 1
        for name in os.listdir(here_real):
            if name == part:
                continue
            os.symlink(os.path.join(here_real, name), os.path.join(here_temp, name))
        if last:
            with open(os.path.join(here_real, part), encoding="utf-8") as f:
                text = f.read()
            hits = text.count(old)
            if hits < count:
                discard(root)
                return None, hits
            with open(os.path.join(here_temp, part), "w", encoding="utf-8") as f:
                f.write(text.replace(old, new, count))
            return root, hits
        here_real = os.path.join(here_real, part)
        here_temp = os.path.join(here_temp, part)
        os.mkdir(here_temp)

    discard(root)
    return None, 0


def discard(root):
    """Throw the mirror away. Links are removed, never followed, so the real files are untouched."""
    if root:
        shutil.rmtree(root, ignore_errors=True)
