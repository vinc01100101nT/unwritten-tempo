#!/usr/bin/env python3
"""Prove that check-ui-prototypes.py can actually SEE what it claims to check.

A checker nobody has watched fail is not a checker — an empty report can mean "clean" or "deaf",
and those look identical. So this feeds it pages whose answer is known in advance and fails if any
verdict comes back wrong. Re-run it after ANY edit to the checker.

  tools/prove-ui-prototype-check.py
"""

import json
import os
import re
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CHECKER = os.path.join(ROOT, "tools", "check-ui-prototypes.py")
TOKENS = os.path.join(ROOT, "prototypes", "ui-tokens.json")
GEOMETRY = os.path.join(ROOT, "prototypes", "ui-geometry.json")
# The pages must sit where a real mock sits, or the generated stylesheet beside them will not load.
PAGE_DIR = os.path.join(ROOT, "prototypes")

PAGE = """<!DOCTYPE html><html><head><meta charset="utf-8">
<link rel="stylesheet" href="ui-tokens.css">
<style>
  body { background: %(outside)s; color: %(text)s; font-family: "%(font)s", sans-serif; }
  .furniture { background: %(outside)s; color: %(outside)s; }
  .screen { background: %(panel)s; color: %(text)s; font-family: "%(font)s", sans-serif; }
  .screen b { color: %(inside)s; }
</style></head><body>
<h1 class="furniture">a title that is not part of the screen</h1>
%(region)s
</body></html>"""

REGION = '<section class="screen" data-mocks="%s"><p>Grilled Fish <b>+7</b></p></section>'
EMPTY_REGION = '<section class="screen" data-mocks="%s"></section>'
# A region with one labelled row, whose height is set by the caller — for the size cases.
# A whole mini-screen built to whatever measurements are handed in — used to prove that a mock
# matching the game reports NO size differences, and that changing one number brings it back.
def screen_region(screen, real, row_extra=0.0):
    parts = real.get("parts", {})
    cols = real.get("columns", {})
    sizes = real.get("cell_sizes", {})
    width = parts.get("window", 488)
    row_h = parts.get("row", 22) + row_extra
    head_pt = parts.get("header_size", 12)
    cells = "".join(
        '<span style="width:%gpx;flex:0 0 %gpx;font-size:%gpx">%s</span>' % (w, w, head_pt, n)
        for n, w in cols.items())
    body = "".join(
        '<span style="width:%gpx;flex:0 0 %gpx;font-size:%gpx">x</span>' % (w, w, sizes.get(n, 14))
        for n, w in cols.items())
    return ('<section class="screen" data-mocks="%s" style="width:%gpx;box-sizing:border-box">'
            '<div data-part="header" style="display:flex;width:%gpx">%s</div>'
            '<div data-part="row" style="display:flex;height:%gpx">%s</div>'
            '</section>') % (screen, width, width, cells, row_h, body)


def run(html):
    fh = tempfile.NamedTemporaryFile("w", suffix=".html", prefix=".prove-", dir=PAGE_DIR,
                                     delete=False, encoding="utf-8")
    fh.write(html)
    fh.close()
    try:
        p = subprocess.run([sys.executable, CHECKER, fh.name], capture_output=True, text=True)
        return p.returncode, re.sub(r"\033\[[0-9;]*m", "", p.stdout)
    finally:
        os.unlink(fh.name)


def main():
    if not os.path.exists(TOKENS):
        print("run tools/ui-tokens.py first")
        return 2
    data = json.load(open(TOKENS, encoding="utf-8"))

    panel = data["palette"]["PanelFill"]["hex"]
    text = data["palette"]["TextMain"]["hex"]
    gold = data["palette"]["Gold"]["hex"]
    font = data["fonts"][1]["family"]
    INVENTED = "#ff00ff"

    # A colour that belongs to ONE other screen, taken from the generated data rather than typed
    # here, so this test cannot rot when the game's colours move.
    borrowed, lender = None, None
    counts = {}
    for name, cols in data["screens"].items():
        for hexv in cols:
            counts.setdefault(hexv, []).append(name)
    shared = {c["hex"] for c in data["palette"].values()}
    for tier in data["rarity"].values():
        for chan in tier.values():
            shared.add(chan["hex"])
    for hexv, who in sorted(counts.items()):
        if len(who) == 1 and hexv not in shared and who[0] != "Inventory":
            borrowed, lender = hexv, who[0]
            break

    def page(inside=None, outside=None, fnt=None, region=None, screen="Inventory"):
        return PAGE % {
            "panel": panel, "text": text, "font": fnt or font,
            "inside": inside or gold,
            "outside": outside or panel,
            "region": region if region and region.startswith("<section class=\"screen\" data-mocks=\"" + screen)
                      else (region or REGION) % screen,
        }

    cases = [
        ("a region using only the game's own colours and font passes",
         page(), 0, ["clean"], ["flag  "]),

        ("an invented colour INSIDE the screen is caught",
         page(inside=INVENTED), 1, ["%s exists nowhere in the game" % INVENTED.lower()], []),

        ("the same invented colour OUTSIDE the screen is ignored",
         page(outside=INVENTED), 0, ["clean"], [INVENTED, "flag  "]),

        ("a font the game does not own is caught",
         page(fnt="Comic Sans MS"), 1, ['font "Comic Sans MS"'], []),

        ("a marked region with nothing in it is flagged, never called clean",
         page(region=EMPTY_REGION), 1, ["never brought to life"], ["clean"]),

        ("a region naming a screen the game has not built is checked and noted, not failed",
         page(screen="Level Up"), 0, ["the game has no screen by that name"], ["flag  "]),

        ("a page with nothing marked says so and reads the text instead",
         PAGE % {"panel": panel, "text": text, "font": font, "inside": gold, "outside": panel,
                 "region": '<section class="screen"><p>x</p></section>'},
         0, ["nothing marked as a screen"], ["flag  "]),
    ]

    # Sizes: only once the real screen has been measured from the running game.
    if os.path.exists(GEOMETRY):
        geo = json.load(open(GEOMETRY, encoding="utf-8")).get("Inventory", {})
        real_row = (geo.get("parts") or {}).get("row")
        if real_row:
            cases.append(
                ("a mock built to the game's own measurements reports NO size differences",
                 page(region=screen_region("Inventory", geo)), 0, ["clean"], ["size  "]))
            cases.append(
                ("one size changed IS reported, with both numbers",
                 page(region=screen_region("Inventory", geo, 12)), 1,
                 ["row height: the mock is %gpx, the game is %gpx" % (real_row + 12, real_row)], []))
        else:
            print("  (no measured row height for the Inventory — size cases skipped)")
    else:
        print("  (nothing measured from the game yet — run tools/ui-geometry.py; size cases skipped)")

    if borrowed:
        cases.append(
            ("a real colour borrowed from ANOTHER screen is caught inside the screen",
             page(inside=borrowed), 1, ["is real but belongs to %s" % lender], []))
    else:
        print("  (no colour is owned by exactly one other screen right now — borrow case skipped)")

    fails = 0
    for title, html, want_code, want_in, want_not_in in cases:
        code, out = run(html)
        why = []
        if code != want_code:
            why.append("exit %d, expected %d" % (code, want_code))
        for s in want_in:
            if s not in out:
                why.append("missing from the report: %r" % s)
        for s in want_not_in:
            if s in out:
                why.append("should not have appeared: %r" % s)
        if why:
            fails += 1
            print("FAIL  %s" % title)
            for w in why:
                print("      %s" % w)
            print("      ---- what it printed ----")
            for line in out.strip().split("\n"):
                print("      %s" % line)
        else:
            print("ok    %s" % title)

    print("\n%d case(s), %d failed." % (len(cases), fails))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
