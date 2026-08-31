#!/usr/bin/env python3
"""Report which player-wiki pages have fallen behind the design docs they were written from.

Each page in wiki/content/manifest.json may declare:
    "sourceDocs": ["docs/foo.md", ...]   which design docs feed this page
    "syncedAt":   {"docs/foo.md": "<sha>"}  the doc commit this page was last written against

A page is STALE when a source doc's current commit differs from the stamped one.

    check                  list stale pages (exit 1 if any)
    stamp <page-id> ...    mark pages as synced to the docs' current commits
    bootstrap              stamp every page with the doc commit that was current
                           as of that page's own last commit (one-time backfill)
"""
import json
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
MANIFEST = REPO / "wiki" / "content" / "manifest.json"


def git(*args):
    return subprocess.run(
        ["git", *args], cwd=REPO, capture_output=True, text=True, check=True
    ).stdout.strip()


# --follow makes every lookup survive a doc being MOVED. Without it, reorganising docs/ into
# folders silently resets each page's history to empty, and every page reports STALE with
# "0 commits" — stale-looking for a reason that has nothing to do with its content.
def head_sha(doc):
    """Current commit of a doc, or None if git has never seen it."""
    return git("log", "--follow", "-1", "--format=%H", "--", doc) or None


def sha_as_of(doc, when):
    """The doc's commit as it stood at a point in time."""
    return git("log", "--follow", "-1", "--format=%H", f"--before={when}", "--", doc) or None


def page_last_commit_date(page_file):
    return git("log", "-1", "--format=%cI", "--", f"wiki/content/{page_file}") or None


def commits_between(doc, old_sha):
    """Human-readable list of what changed in the doc since old_sha."""
    if not old_sha:
        return []
    try:
        out = git("log", "--follow", "--oneline", f"{old_sha}..HEAD", "--", doc)
    except subprocess.CalledProcessError:
        return ["(stamped commit not found — history rewritten?)"]
    return [line for line in out.splitlines() if line]


def load():
    return json.loads(MANIFEST.read_text())


def save(manifest):
    MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")


def pages(manifest):
    for group in manifest.get("groups", []):
        for page in group.get("pages", []):
            yield page


def missing_docs(page):
    return [d for d in page.get("sourceDocs", []) if not (REPO / d).exists()]


def check(manifest):
    stale, unmapped, broken = [], [], []
    for page in pages(manifest):
        gone = missing_docs(page)
        if gone:
            broken.append((page, gone))
        docs = [d for d in page.get("sourceDocs", []) if d not in gone]
        if not page.get("sourceDocs"):
            unmapped.append(page)
            continue
        stamps = page.get("syncedAt", {})
        drifted = [
            (d, stamps.get(d), commits_between(d, stamps.get(d)))
            for d in docs
            if head_sha(d) != stamps.get(d)
        ]
        if drifted:
            stale.append((page, drifted))

    for page, gone in broken:
        print(f"!  {page['id']}: sourceDocs point at missing files: {', '.join(gone)}")
    if broken:
        print()

    if not stale:
        print("All mapped wiki pages are in sync with their design docs.")
    for page, drifted in stale:
        print(f"STALE  {page['title']}  ({page['file']})")
        for doc, stamped, commits in drifted:
            if not stamped:
                print(f"       {doc} — never stamped")
                continue
            print(f"       {doc} — {len(commits)} commit(s) since last sync:")
            for line in commits:
                print(f"           {line}")
        print()

    if unmapped:
        print("Pages with no sourceDocs (never checked): " + ", ".join(p["id"] for p in unmapped))
    return 1 if stale else 0


def stamp(manifest, ids):
    touched = []
    for page in pages(manifest):
        if ids and page["id"] not in ids:
            continue
        docs = page.get("sourceDocs", [])
        if not docs:
            continue
        page["syncedAt"] = {d: sha for d in docs if (sha := head_sha(d))}
        touched.append(page["id"])
    save(manifest)
    print("Stamped: " + (", ".join(touched) if touched else "nothing"))
    unknown = set(ids) - set(touched)
    if unknown:
        print("Unknown or source-less page ids: " + ", ".join(sorted(unknown)))
    return 0


def bootstrap(manifest):
    for page in pages(manifest):
        docs = page.get("sourceDocs", [])
        if not docs:
            continue
        when = page_last_commit_date(page["file"])
        if not when:
            print(f"skip {page['id']}: page not committed yet")
            continue
        page["syncedAt"] = {d: sha for d in docs if (sha := sha_as_of(d, when))}
        print(f"{page['id']}: stamped against docs as of {when[:10]}")
    save(manifest)
    return 0


def main():
    args = sys.argv[1:]
    cmd = args[0] if args else "check"
    manifest = load()
    if cmd == "check":
        return check(manifest)
    if cmd == "stamp":
        return stamp(manifest, set(args[1:]))
    if cmd == "bootstrap":
        return bootstrap(manifest)
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main())
