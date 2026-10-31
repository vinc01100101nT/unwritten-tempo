#!/usr/bin/env python3
"""Write the wiki's World Map — wiki/content/world.generated.json and wiki/assets/world-map.png — from the seamless
world as it is BUILT. The one writer of both files.

Where each value comes from:
  - the picture is the game's own whole-world minimap (Resources/Minimaps/<map>.png, drawn by Bake Minimaps, placed by
    its MinimapCatalog entry), cut down to the built ground (the world scene's worldMin..worldMax) and scaled to WIDTH;
  - every town on it is a town area of the world scene (the areas of kind 1 the town builder writes), outlined where
    it stands; its id is the world layout's (data/world-layout.json), matched by name;
  - there are no doors: one seamless map has none.
Which creatures live where is NOT written here: the page reads it from the creature export, so there is one copy.

The JSON keeps the exact shape website/index.html's worldMap block reads (picture, width, height, maps[], doors[]), and
each file is written only when its bytes moved.

USAGE:   python3 tools/export-wiki-world-map.py [--check]
         Run it after Bake Minimaps or after a town is built. --check writes nothing and exits 1 when either file is
         out of date.
EXIT:    0 = written (or already current); 1 = out of date under --check, or a source could not be read.
"""
import io
import json
import os
import re
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
MAP = "world"
SCENE = os.path.join(REPO, "unity", "Assets", "Scenes", MAP + ".unity")
RES = os.path.join(REPO, "unity", "Assets", "3_Data", "Resources")
CATALOG = os.path.join(RES, "MinimapCatalog.asset")
MINIMAP = os.path.join(RES, "Minimaps", MAP + ".png")
LAYOUT = os.path.join(REPO, "data", "world-layout.json")
WIKI = os.path.join(REPO, "wiki")
PICTURE_REL = "assets/world-map.png"
DATA = os.path.join(WIKI, "content", "world.generated.json")
WIDTH = 1536          # the picture's width on the wiki, in pixels; its height follows the built ground's shape
TOWN_AREA = "1"       # SeamlessWorld area kind: a town
NOTE = ("GENERATED from the built seamless world (the world scene's built ground and town areas, the game's own "
        "whole-world minimap picture, and the world layout's town ids) by tools/export-wiki-world-map.py. Do NOT "
        "hand-edit: run the tool again after Bake Minimaps or after a town is built.")
NUM = r"(-?[\d.]+(?:e-?\d+)?)"


def fail(why):
    print("export-wiki-world-map: " + why)
    sys.exit(1)


def read(path):
    try:
        with open(path, encoding="utf-8") as fh:
            return fh.read()
    except OSError:
        fail("cannot read " + os.path.relpath(path, REPO))


def built_ground(scene):
    lo = re.search(r"^  worldMin: \{x: %s, y: %s\}" % (NUM, NUM), scene, re.M)
    hi = re.search(r"^  worldMax: \{x: %s, y: %s\}" % (NUM, NUM), scene, re.M)
    if not (lo and hi):
        fail("no worldMin/worldMax in the world scene — is it still a seamless world?")
    return float(lo.group(1)), float(lo.group(2)), float(hi.group(1)), float(hi.group(2))


def town_areas(scene):
    return [(m.group(1).strip(), float(m.group(2)), float(m.group(3)), float(m.group(4)), float(m.group(5)))
            for m in re.finditer(r"^  - name: (.+)\n    kind: %s\n    min: \{x: %s, y: %s\}\n    max: \{x: %s, y: %s\}"
                                 % (TOWN_AREA, NUM, NUM, NUM, NUM), scene, re.M)]


def minimap_square(catalog):
    m = re.search(r"^  - map: %s\n    worldMin: \{x: %s, y: %s\}\n    worldSize: %s" % (re.escape(MAP), NUM, NUM, NUM),
                  catalog, re.M)
    if not m:
        fail("MinimapCatalog.asset has no entry for '%s' — run Bake Minimaps" % MAP)
    return float(m.group(1)), float(m.group(2)), float(m.group(3))


def picture(x0, z0, x1, z1, mm):
    with open(MINIMAP, "rb") as fh:
        head = fh.read(8)
    if head != b"\x89PNG\r\n\x1a\n":
        fail("%s is not a picture (a Git LFS pointer? run: git lfs pull --include=\"%s\")"
             % (os.path.relpath(MINIMAP, REPO), os.path.relpath(MINIMAP, REPO)))
    im = Image.open(MINIMAP).convert("RGB")
    mx, mz, size = mm
    px = im.width / size
    # The picture's top row is its north edge: map z grows northward, picture rows grow southward.
    box = (round((x0 - mx) * px), round((mz + size - z1) * px), round((x1 - mx) * px), round((mz + size - z0) * px))
    cut = im.crop(box)
    out = cut.resize((WIDTH, max(1, round(WIDTH * cut.height / cut.width))), Image.LANCZOS)
    buf = io.BytesIO()
    out.save(buf, "PNG", optimize=True)
    return buf.getvalue(), out.width, out.height


def main():
    check = "--check" in sys.argv
    scene = read(SCENE)
    x0, z0, x1, z1 = built_ground(scene)
    w, h = x1 - x0, z1 - z0
    png, pw, ph = picture(x0, z0, x1, z1, minimap_square(read(CATALOG)))

    try:
        ids = {t["name"]: t["id"] for t in json.loads(read(LAYOUT)).get("towns", [])}
    except ValueError:
        fail("data/world-layout.json is not JSON")
    maps = []
    for name, ax0, az0, ax1, az1 in town_areas(scene):
        maps.append({
            "id": ids.get(name, re.sub(r"[^a-z0-9]+", "_", name.lower()).strip("_")),
            "name": name,
            "type": "town",
            "preview": "",
            "left": (ax0 - x0) / w,
            "top": (z1 - az1) / h,
            "width": (ax1 - ax0) / w,
            "height": (az1 - az0) / h,
        })
    data = json.dumps({"note": NOTE, "picture": PICTURE_REL, "width": pw, "height": ph, "maps": maps, "doors": []},
                      indent=4, ensure_ascii=False) + "\n"

    stale = []
    for path, body in ((os.path.join(WIKI, PICTURE_REL), png), (DATA, data.encode("utf-8"))):
        try:
            with open(path, "rb") as fh:
                same = fh.read() == body
        except OSError:
            same = False
        if same:
            continue
        stale.append(os.path.relpath(path, REPO))
        if not check:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "wb") as fh:
                fh.write(body)
    if check and stale:
        print("wiki world map out of date: " + ", ".join(stale) + " — run python3 tools/export-wiki-world-map.py")
        return 1
    print("wiki world map: %d town(s) on a %dx%d picture of the built world%s"
          % (len(maps), pw, ph, " — wrote " + ", ".join(stale) if stale and not check else " (already current)"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
