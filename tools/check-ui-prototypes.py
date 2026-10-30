#!/usr/bin/env python3
"""Check what a mock page actually PAINTS against the colours and fonts the game really has.

A mock page is only worth anything if what it shows is what the game will show. So this opens each
page in a real browser, finds the parts marked as being the screen, and reads the colour and font
of every element inside them — what is painted, not what is written in the file.

Marking a screen, on the element that IS the window:

    <section data-mocks="Inventory"> … </section>

Everything OUTSIDE a marked region is ignored completely: a page may carry a title, an explanation
or a control for the reader, and none of it is mistaken for the design. A page may mark several
regions, each naming its own screen — a window, its tooltip and its right-click menu are three
different screens in the game, with three different sets of colours, and they are checked as three.

Each colour lands in one of four piles:

  shared        from the game's shared palette or its rarity table                   — fine
  own screen    declared by the screen this region names                             — fine, noted
  borrowed      real, but belonging to a DIFFERENT screen                            — flagged
  nowhere       exists nowhere in the game                                           — flagged

The third pile is the one a whole-project check can never find, and this project has already made
that mistake once: a colour that is real, so it passes any "does the game have this" test, and is
still wrong because it belongs to another window.

A marked region that contains nothing is FLAGGED, never called clean — a right-click menu that only
appears when you right-click paints nothing at rest, and an empty report from a tool that looked at
nothing reads exactly like a clean one. A mock must show every state it claims to mock.

Run:  tools/check-ui-prototypes.py                     every page in prototypes/ and mockups/
      tools/check-ui-prototypes.py path/to/page.html   just these
Exit code 1 if anything is flagged.

Reads prototypes/ui-tokens.json, so run tools/ui-tokens.py first if the game's colours changed.
"""

import base64
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile

from platform_ask import ask

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOKENS = os.path.join(ROOT, "prototypes", "ui-tokens.json")
GEOMETRY = os.path.join(ROOT, "prototypes", "ui-geometry.json")
FOLDERS = ["prototypes", "mockups"]

# A stack may fall back to whatever the machine has; only the FIRST family is what the page asks for.
GENERIC = {"system-ui", "-apple-system", "blinkmacsystemfont", "sans-serif", "serif", "monospace",
           "cursive", "fantasy", "ui-sans-serif", "ui-serif", "ui-monospace", "inherit", "initial",
           "unset", "-webkit-standard", "-webkit-body"}

RED = "\033[31m"
YELLOW = "\033[33m"
GREEN = "\033[32m"
DIM = "\033[2m"
BOLD = "\033[1m"
OFF = "\033[0m"

# Runs inside the page. Reports what every marked region PAINTS, which is the only thing that can
# be compared with the game — the file's stylesheet says nothing about which rules land where.
PROBE = r"""
(function () {
  var out = [];
  var regions = document.querySelectorAll('[data-mocks]');
  for (var i = 0; i < regions.length; i++) {
    var root = regions[i];
    var kids = Array.prototype.slice.call(root.querySelectorAll('*'));
    var els = [root].concat(kids);
    var cols = {}, fonts = {};
    els.forEach(function (e) {
      var cs = getComputedStyle(e);
      ['color', 'backgroundColor', 'borderTopColor', 'borderRightColor', 'borderBottomColor',
       'borderLeftColor', 'outlineColor', 'textDecorationColor'].forEach(function (p) {
        var v = cs[p];
        if (v) cols[v] = 1;
      });
      var f = (cs.fontFamily || '').split(',')[0].trim().replace(/^["']|["']$/g, '');
      if (f) fonts[f] = 1;
    });
    // Sizes, for the parts the page has labelled. Column NAMES come from the heading a person
    // reads, so a page never has to repeat them in an attribute — and a heading the game does not
    // have shows up as a column that exists only in the mock.
    // clientWidth, not the outer box: the game's number is the room INSIDE its window frame,
    // so comparing it with a border-box width would be off by the mock's own border.
    var geo = { width: root.clientWidth, columns: {}, cell_sizes: {}, parts: {} };
    var hdr = root.querySelector('[data-part="header"]');
    var names = [];
    if (hdr) {
      geo.parts.content = hdr.getBoundingClientRect().width;
      var hkids = Array.prototype.slice.call(hdr.children);
      hkids.forEach(function (b) {
        var label = (b.textContent || '').replace(/[▲▼]/g, '').trim();
        if (!label) return;
        names.push(label);
        geo.columns[label] = b.getBoundingClientRect().width;
      });
      if (hkids.length) geo.parts.header_size = parseFloat(getComputedStyle(hkids[0]).fontSize);
    }
    var row = root.querySelector('[data-part="row"]');
    if (row) {
      geo.parts.row = row.getBoundingClientRect().height;
      Array.prototype.slice.call(row.children).forEach(function (c, i) {
        geo.cell_sizes[names[i] || ('column ' + (i + 1))] = parseFloat(getComputedStyle(c).fontSize);
      });
    }
    ['search', 'list'].forEach(function (p) {
      var e = root.querySelector('[data-part="' + p + '"]');
      if (e) geo.parts[p] = e.getBoundingClientRect().height;
    });

    out.push({ name: root.getAttribute('data-mocks'), elements: kids.length,
               colours: Object.keys(cols), fonts: Object.keys(fonts), geometry: geo });
  }
  document.title = 'UICHECK' + btoa(unescape(encodeURIComponent(JSON.stringify(out))));
})();
"""


def find_browser():
    # Where the browser lives depends on the computer, and tools/platform.sh is the one place that knows.
    return ask("browser")


def render(browser, path):
    """Open the page and read back what each marked region paints. None if the browser fails."""
    html = open(path, encoding="utf-8", errors="replace").read()
    injected = "<script>setTimeout(function(){%s}, 400)</script>" % PROBE
    if "</body>" in html:
        html = html.replace("</body>", injected + "</body>", 1)
    else:
        html += injected

    # The copy sits beside the original so every relative path in it — the generated stylesheet, the
    # game's own font files — still resolves exactly as it does for the real page.
    folder = os.path.dirname(os.path.abspath(path))
    fh = tempfile.NamedTemporaryFile("w", suffix=".html", prefix=".uicheck-", dir=folder,
                                     delete=False, encoding="utf-8")
    fh.write(html)
    fh.close()
    try:
        p = subprocess.run(
            [browser, "--headless=new", "--disable-gpu", "--no-sandbox", "--virtual-time-budget=6000",
             "--dump-dom", pathlib.Path(fh.name).as_uri()],
            # Read as UTF-8 whatever the computer's default is — Windows' default could not decode the page.
            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=90)
        m = re.search(r"<title>UICHECK([A-Za-z0-9+/=]*)</title>", p.stdout)
        if not m:
            return None
        return json.loads(base64.b64decode(m.group(1)).decode("utf-8"))
    except (subprocess.SubprocessError, OSError, ValueError):
        return None
    finally:
        try:
            os.unlink(fh.name)
        except OSError:
            pass


def expand(h):
    """#abc and #aabbcc both come back as #AABBCC."""
    h = h.upper()
    if len(h) == 4:
        return "#" + h[1] * 2 + h[2] * 2 + h[3] * 2
    return h


def to_hex(css):
    """One painted colour as hex, or None when it paints nothing at all."""
    css = css.strip()
    m = re.match(r"rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)\s*(?:[,/]\s*([\d.%]+)\s*)?\)", css)
    if m:
        a = m.group(4)
        if a is not None:
            a = float(a.rstrip("%")) / 100 if a.endswith("%") else float(a)
            if a == 0:
                return None
        return "#%02X%02X%02X" % tuple(int(float(m.group(i))) for i in (1, 2, 3))
    if re.match(r"^#[0-9a-fA-F]{3}$|^#[0-9a-fA-F]{6}$", css):
        return expand(css)
    return None


PART_WORDS = {
    "content": "the row's own width",
    "row": "row height",
    "header_size": "heading text size",
    "search": "search bar height",
    "list": "list height",
}


def compare_geometry(geo, real, out):
    """Every size the mock and the game disagree about, in plain words, both numbers shown.

    The game's own sizes are measured at its window's real size and at the resolution its interface
    is built against, so a browser pixel and an engine unit mean the same thing here — which is why
    a mock must be read at that size to be compared at all.
    """
    if not geo:
        return
    # A pixel of slack on lengths, because a browser lays out on fractions and the engine does not.
    # Type sizes get almost none: eleven against twelve is a whole point, and it shows.
    def line(what, mine, theirs, tight=False):
        if mine is None or theirs is None:
            return
        if abs(mine - theirs) > (0.4 if tight else 1.0):
            out.append("%s: the mock is %gpx, the game is %gpx"
                       % (what, round(mine, 1), round(theirs, 1)))

    rp, gp = geo.get("parts", {}), real.get("parts", {})
    line("the window's width", geo.get("width"), gp.get("window"))
    if "content" in rp and gp.get("window") and rp["content"] - gp["window"] > 1.0:
        out.append("the rows need %gpx but the game's window is only %gpx wide — this layout does "
                   "not fit the window it mocks" % (round(rp["content"], 1), round(gp["window"], 1)))
    for key in ("row", "header_size", "search", "list"):
        line(PART_WORDS.get(key, key), rp.get(key), gp.get(key), key.endswith("_size"))

    rc, gc = geo.get("columns", {}), real.get("columns", {})
    for name in sorted(set(rc) | set(gc)):
        if name not in gc:
            out.append('column "%s" is in the mock and not in the game' % name)
        elif name not in rc:
            out.append('column "%s" is in the game and not in the mock' % name)
        else:
            line('column "%s"' % name, rc[name], gc[name])

    rs, gs = geo.get("cell_sizes", {}), real.get("cell_sizes", {})
    for name in sorted(set(rs) & set(gs)):
        line('the "%s" column\'s text' % name, rs[name], gs[name], True)


def pretty(css, hexv):
    """A painted colour in the form the page writes it, with its transparency when it has any."""
    m = re.match(r"rgba\([^,]+,[^,]+,[^,]+[,/]\s*([\d.]+%?)\s*\)", css.strip())
    if m:
        a = m.group(1)
        a = float(a.rstrip("%")) / 100 if a.endswith("%") else float(a)
        if a < 1:
            return "%s at %d%% opacity" % (hexv.lower(), round(a * 100))
    return hexv.lower()


def text_colours(html):
    """Fallback only: every colour the FILE writes, furniture included. Announced when used."""
    out = {}
    for m in re.finditer(r"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b", html):
        out.setdefault(expand(m.group(0)), m.group(0))
    for m in re.finditer(r"rgba?\([^)]*\)", html):
        h = to_hex(m.group(0))
        if h:
            out.setdefault(h, m.group(0))
    return out


def text_fonts(html):
    asked, remote = set(), set()
    for m in re.finditer(r"font-family:\s*([^;}\n]+)", html):
        stack = m.group(1).strip()
        if stack.startswith("var("):
            continue
        first = stack.split(",")[0].strip().strip("\"'")
        if first and first.lower() not in GENERIC:
            asked.add(first)
    for m in re.finditer(r"https?://[^\"')\s]*(?:fonts\.googleapis|fonts\.gstatic|typekit|fontawesome)[^\"')\s]*", html):
        remote.add(m.group(0))
    return asked, remote


def main():
    if not os.path.exists(TOKENS):
        print("%sno generated token file — run tools/ui-tokens.py first%s" % (RED, OFF))
        return 2
    data = json.load(open(TOKENS, encoding="utf-8"))

    # A colour written fully see-through paints nothing, so it is not part of the palette a page can
    # be right or wrong about — the rarity table's empty Common glow is exactly that.
    shared = {c["hex"] for c in list(data["palette"].values()) + list(data.get("palette_dark", {}).values()) if c["alpha"] > 0}
    for tier in data["rarity"].values():
        for chan in tier.values():
            if chan["alpha"] > 0:
                shared.add(chan["hex"])
    screens = data["screens"]
    families = {f["family"] for f in data["fonts"]}

    # What the real screens actually measure, when someone has measured them.
    geometry = {}
    if os.path.exists(GEOMETRY):
        geometry = {k: v for k, v in json.load(open(GEOMETRY, encoding="utf-8")).items()
                    if k != "generated_by"}

    # Which screen owns a colour, for the "borrowed" line. A colour used by several screens names
    # them all, so the report never claims a single owner it cannot prove.
    owner = {}
    for name, cols in screens.items():
        for hexv in cols:
            owner.setdefault(hexv, set()).add(name)

    def judge(hexv, written, screen, bad, notes):
        if hexv in shared:
            return
        own = screens.get(screen, {}) if screen else {}
        if hexv in own:
            notes.append("%s belongs to %s alone, not to the shared palette" % (written, screen))
            return
        if hexv in owner:
            who = sorted(owner[hexv])
            # A colour a few screens use is a BORROW worth flagging. One that turns up all over the
            # game (black shadows, white highlights) is shared in everything but name, so it is a
            # note — flagging it would bury the borrows in noise.
            if len(who) >= 3:
                notes.append("%s is used by %d screens but is not in the shared palette"
                             % (written, len(who)))
            else:
                bad.append("%s is real but belongs to %s" % (written, ", ".join(who)))
            return
        bad.append("%s exists nowhere in the game" % written)

    targets = []
    if len(sys.argv) > 1:
        targets = [os.path.abspath(p) for p in sys.argv[1:]]
    else:
        for folder in FOLDERS:
            d = os.path.join(ROOT, folder)
            if not os.path.isdir(d):
                continue
            targets += [os.path.join(d, fn) for fn in sorted(os.listdir(d)) if fn.endswith(".html")]

    browser = find_browser()
    if not browser:
        print("%sno browser found — falling back to reading the page's text, which also reads the%s"
              % (YELLOW, OFF))
        print("%sparts that are NOT the screen. Install Chrome for the real check.%s" % (YELLOW, OFF))

    pages, flagged = 0, 0
    for path in targets:
        pages += 1
        folder = os.path.basename(os.path.dirname(path))
        fn = os.path.basename(path)
        html = open(path, encoding="utf-8", errors="replace").read()
        print("\n%s%s/%s%s" % (BOLD, folder, fn, OFF))

        regions = render(browser, path) if browser else None
        if regions is None and browser and "data-mocks" in html:
            print("  %sthe browser could not open this page — nothing was checked%s" % (RED, OFF))
            flagged += 1
            continue

        if regions:
            page_bad = []
            for reg in regions:
                screen = (reg.get("name") or "").strip()
                bad, notes = [], []
                known = screen in screens
                head = '  %sregion "%s"%s' % (BOLD, screen, OFF)
                if not known:
                    built = screen in data["screen_names"]
                    head += " %s(the game has no screen by that name%s)%s" % (
                        DIM, "" if built else ", and none is built", OFF)
                print("%s — %d elements inside" % (head, reg["elements"]))

                if reg["elements"] == 0:
                    bad.append("nothing inside it — this region was never brought to life, so it "
                               "was NOT checked. Show it in the page (open the menu, raise the "
                               "tooltip) or drop the mark.")
                for css in sorted(set(reg["colours"])):
                    hexv = to_hex(css)
                    if hexv:
                        # Reported as hex, because that is how it is written in the page a
                        # designer would go and fix; the browser hands it over as rgb().
                        judge(hexv, pretty(css, hexv), screen if known else None, bad, notes)
                for fam in sorted(set(reg["fonts"])):
                    if fam.lower() in GENERIC:
                        continue
                    if fam not in families:
                        bad.append('font "%s" — the game only has %s'
                                   % (fam, ", ".join(sorted(families))))
                # Sizes: only when this screen has been measured from the running game.
                sizes = []
                real = geometry.get(screen) if known else None
                geo = reg.get("geometry") or {}
                if real:
                    if geo.get("columns") or geo.get("parts"):
                        compare_geometry(geo, real, sizes)
                    else:
                        notes.append("no sizes compared — label the parts with data-part=\"header\", "
                                     "\"row\", \"search\", \"list\" to check them")
                elif known:
                    notes.append("no sizes compared — this screen has not been measured; run "
                                 "tools/ui-geometry.py")

                for n in notes:
                    print("    %snote%s  %s" % (DIM, OFF, n))
                for b in bad:
                    print("    %sflag%s  %s" % (RED, OFF, b))
                for s in sizes:
                    print("    %ssize%s  %s" % (YELLOW, OFF, s))
                if not bad and not sizes:
                    print("    %sclean%s" % (GREEN, OFF))
                page_bad += bad + sizes
            if page_bad:
                flagged += 1
            continue

        # No marked region: read the file's text and say plainly that furniture is included.
        print("  %snothing marked as a screen — read the page's text instead, furniture and all%s"
              % (YELLOW, OFF))
        print('  %smark the window with  <section data-mocks="Inventory">  for the real check%s'
              % (DIM, OFF))
        bad, notes = [], []
        for hexv, written in sorted(text_colours(html).items()):
            judge(hexv, written, None, bad, notes)
        asked, remote = text_fonts(html)
        for fam in sorted(asked):
            if fam not in families:
                bad.append('font "%s" — the game only has %s' % (fam, ", ".join(sorted(families))))
        for url in sorted(remote):
            bad.append("loads a font from the internet (%s) — the game's own font files are in "
                       "prototypes/ui-tokens.css" % url.split("?")[0])
        for n in notes:
            print("    %snote%s  %s" % (DIM, OFF, n))
        for b in bad:
            print("    %sflag%s  %s" % (RED, OFF, b))
        if not bad:
            print("    %sclean%s" % (GREEN, OFF))
        else:
            flagged += 1

    print("\n%d page(s) read, %d flagged." % (pages, flagged))
    return 1 if flagged else 0


if __name__ == "__main__":
    sys.exit(main())
