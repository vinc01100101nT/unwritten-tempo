#!/usr/bin/env python3
"""Write the interface theme's shape images — plain white masks the game tints with its palette colours.

The colours live in one place, the palette in UiKit.cs. These images carry only SHAPE: a rounded rectangle for
each corner radius the theme uses, and the two layers of a window's title bar (its rounded-top body, and the
fade laid over it). The game colours them by tinting, so changing a colour never needs these re-made; changing
a corner radius does.

Run:  tools/ui-theme-sprites.py
Then let Unity import them; each needs its 9-slice border, which is set when they are imported (see the
BORDERS table printed at the end).
"""
import math
import os
import struct
import zlib

OUT = os.path.join(os.path.dirname(__file__), "..", "unity", "Assets", "3_Data", "Resources", "UI", "Theme")

# Every corner radius drawn anywhere: a window (5) and its body (4), a button (3) and its well (2), a speech bubble (14).
RADII = [2, 3, 4, 5, 14]
# The title bar's top corners sit inside a window body, so they use the body's radius.
TITLE_RADIUS = 4
TITLE_W, TITLE_H = 16, 32


def png(path, w, h, pixels):
    """pixels: rows top-to-bottom of (r, g, b, a) 0-255."""
    raw = b"".join(b"\x00" + bytes(v for px in row for v in px) for row in pixels)
    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


def rounded_alpha(fx, fy, w, h, r, top_only=False):
    """Coverage 0..1 of a pixel centre (fx, fy measured from the BOTTOM-left) inside a rounded rectangle."""
    cx = min(max(fx, r), w - r)
    cy = min(max(fy, r), h - r)
    if top_only and fy <= h - r:
        return 1.0
    d = math.hypot(fx - cx, fy - cy)
    return min(1.0, max(0.0, r - d + 0.5))


def main():
    os.makedirs(OUT, exist_ok=True)
    borders = {}
    for r in RADII:
        s = r * 2 + 4
        rows = []
        for y in range(s - 1, -1, -1):
            rows.append([(255, 255, 255, round(255 * rounded_alpha(x + 0.5, y + 0.5, s, s, r))) for x in range(s)])
        name = "Rounded%d" % r
        png(os.path.join(OUT, name + ".png"), s, s, rows)
        borders[name] = (r + 1, r + 1, r + 1, r + 1)

    # The title bar is two layers: a solid body tinted the bar's BOTTOM colour, and over it a fade tinted the TOP
    # colour whose opacity runs from full at the top edge to none at the bottom — together, an even blend.
    for name, fade in (("TitleBarBody", False), ("TitleBarFade", True)):
        rows = []
        for y in range(TITLE_H - 1, -1, -1):
            ramp = (y + 0.5) / TITLE_H if fade else 1.0
            row = []
            for x in range(TITLE_W):
                a = rounded_alpha(x + 0.5, y + 0.5, TITLE_W, TITLE_H, TITLE_RADIUS, top_only=True) * ramp
                row.append((255, 255, 255, round(255 * a)))
            rows.append(row)
        png(os.path.join(OUT, name + ".png"), TITLE_W, TITLE_H, rows)
        borders[name] = (TITLE_RADIUS + 1, 0, TITLE_RADIUS + 1, TITLE_RADIUS + 1)

    print("wrote", len(borders), "images to", os.path.normpath(OUT))
    print("BORDERS (left, bottom, right, top):")
    for n, b in borders.items():
        print("  %-14s %s" % (n, b))


if __name__ == "__main__":
    main()
