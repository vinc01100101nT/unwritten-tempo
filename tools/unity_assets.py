#!/usr/bin/env python3
"""Reading Unity's saved files — the one answer every checker asks, given once.

WHY THIS EXISTS: more than one checker needs "which file is this guid?", and each one that works
it out for itself gets to be subtly wrong on its own. The specific wrongness this module exists to
prevent, measured on 2026-09-12: a sweep that built its map from `Assets/` alone reported 34 broken
references in hand-authored material files. Every one of them was fine — they pointed at sample
files inside an installed PACKAGE, which simply were not in the map. A checker that cries wolf on
its first run teaches everyone to ignore it, so the map has to cover everywhere Unity actually
looks, not just the folder we happen to care about.
"""
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
UNITY = os.path.join(REPO, "unity")
ASSETS = os.path.join(UNITY, "Assets")

# Where a guid can legitimately live. Our own files are only the first of them: a scene or a
# material may reference something inside an installed package, and that is not a fault.
GUID_ROOTS = [
    ASSETS,
    os.path.join(UNITY, "Packages"),
    os.path.join(UNITY, "Library", "PackageCache"),
]

# Folders with nothing addressable in them, skipped so the walk stays quick.
SKIP_DIRS = {".git", "node_modules", "Temp", "obj", "bin", "__pycache__", "Logs"}

_GUID_LINE = re.compile(r"^guid:\s*([0-9a-f]{32})", re.M)
_GUID_REF = re.compile(r"guid:\s*([0-9a-f]{32})")


def is_builtin(guid):
    """Unity's own built-in resources — its default shaders, fonts, meshes and materials. They carry
    hand-made guids that follow a fixed shape and belong to no file on disk, so they can never be
    found by a walk and must never be reported as broken."""
    return guid.startswith("0000000000000000") or set(guid) <= set("0123456789abcdef") and guid.endswith("0" * 16)


def guid_map(roots=None):
    """Every guid Unity can resolve on this machine → the file it names, absolute.

    Built from the `.meta` files, which is where Unity records a guid. Covers the project AND the
    installed packages, because a reference into a package is ordinary and resolving only the
    project's own would report it as missing."""
    out = {}
    for root in (roots or GUID_ROOTS):
        if not os.path.isdir(root):
            continue
        for base, dirs, files in os.walk(root):
            dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
            for f in files:
                if not f.endswith(".meta"):
                    continue
                path = os.path.join(base, f)
                try:
                    with open(path, encoding="utf-8", errors="ignore") as fh:
                        head = fh.read(400)
                except OSError:
                    continue
                m = _GUID_LINE.search(head)
                if m:
                    out.setdefault(m.group(1), path[:-len(".meta")])
    return out


def references(path):
    """Every guid this saved file points at, once each."""
    try:
        with open(path, encoding="utf-8", errors="ignore") as fh:
            return set(_GUID_REF.findall(fh.read()))
    except OSError:
        return set()


def rel(path):
    """A path said the short way, for a report."""
    path = os.path.abspath(path)
    return os.path.relpath(path, ASSETS) if path.startswith(ASSETS) else os.path.relpath(path, REPO)
