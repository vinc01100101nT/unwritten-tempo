#!/usr/bin/env python3
"""Turn a 2D weapon sprite sheet into a low-poly 3D weapon model Unity imports as-is.

The sheet holds one to three views of ONE weapon, side by side, left to right, on a transparent or solid
background:
  1. face view  - the weapon standing tip-up, looking at its broad side       (required)
  2. edge view  - the same weapon turned a quarter turn, showing its thickness (optional)
  3. top view   - looking straight down from the tip                           (optional)

The weapon is carved from the views: a block is kept only where every view shows weapon. With the face view
alone it gets a flat thickness instead. With face and edge views only, each part is as thick as the edge view
only deep inside the face view's outline and rounds down to one block at it, so edges are rounded, wide parts
(blades, discs) end in a thin rounded edge and the shading runs smoothly over the curve (--square rounds only
the outer two blocks, for blocky parts). Each
surface is painted from the view that faces it; surfaces facing up or down take the face view's colour at their
height, because a top view only shows the topmost part.

The views need not share a scale: the face and edge views are each fitted to the weapon's length, and the top
view is stretched to the face view's width and the edge view's thickness.

Output, into <out>/<name>/:  <name>.obj  (length 1 along +Y, origin at the centre of the bottom end)
                             <name>.png  (one texel per block face, read with point filtering, no mipmaps)

    python3 tools/sprite-sheet-to-weapon.py <sheet.png> [--name sword_a] [--cells 160] [--square] [--out <folder>]
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_OUT = os.path.join(REPO, "unity", "Assets", "2_Generated", "Weapons")


def foreground(rgba):
    """Weapon pixels: opaque ones if the sheet has transparency, otherwise those far from the border colour."""
    alpha = rgba[:, :, 3]
    if (alpha < 128).mean() > 0.2:
        return alpha >= 128
    rgb = rgba[:, :, :3].astype(int)
    border = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]])
    bg = np.median(border, axis=0)
    spread = np.percentile(np.abs(border - bg).sum(1), 99)
    return np.abs(rgb - bg).sum(2) > max(60, spread * 2)


def split_views(fg):
    """Column runs of weapon pixels separated by a gap of at least 2% of the sheet width; tiny runs are noise."""
    cols = fg.sum(0)
    min_gap = max(2, int(fg.shape[1] * 0.02))
    runs, start, empty = [], None, 0
    for x, c in enumerate(cols):
        if c:
            if start is None:
                start = x
            empty = 0
            end = x
        elif start is not None:
            empty += 1
            if empty >= min_gap:
                runs.append((start, end))
                start = None
    if start is not None:
        runs.append((start, end))
    total = fg.sum()
    views = []
    for a, b in runs:
        sub = fg[:, a:b + 1]
        if sub.sum() < total * 0.002:
            continue
        ys = np.where(sub.any(1))[0]
        views.append((a, b + 1, ys[0], ys[-1] + 1))
    return views


def shrink(fg, px):
    """The weapon pixels at least `px` pixels inside the outline: the soft rim between weapon and background is
    part background colour, and averaging it in tints every edge block with the background."""
    core = fg.copy()
    for _ in range(px):
        core[1:, :] &= core[:-1, :].copy()
        core[:-1, :] &= core[1:, :].copy()
        core[:, 1:] &= core[:, :-1].copy()
        core[:, :-1] &= core[:, 1:].copy()
    return core


def resample(rgb, fg, box, cells_w, cells_h):
    """Cut a view into cells_w x cells_h cells: a cell is weapon when over half its pixels are, coloured by the
    average of its pixels away from the rim (all its weapon pixels when it has none of those)."""
    x0, x1, y0, y1 = box
    core = shrink(fg, 2)
    mask = np.zeros((cells_h, cells_w), bool)
    col = np.zeros((cells_h, cells_w, 3), np.uint8)
    xs = np.linspace(x0, x1, cells_w + 1)
    ys = np.linspace(y0, y1, cells_h + 1)
    for r in range(cells_h):
        ya, yb = int(round(ys[r])), max(int(round(ys[r + 1])), int(round(ys[r])) + 1)
        for c in range(cells_w):
            xa, xb = int(round(xs[c])), max(int(round(xs[c + 1])), int(round(xs[c])) + 1)
            m = fg[ya:yb, xa:xb]
            if m.mean() > 0.5:
                mask[cells_h - 1 - r, c] = True          # row 0 = bottom of the weapon
                k = core[ya:yb, xa:xb]
                col[cells_h - 1 - r, c] = rgb[ya:yb, xa:xb][k if k.any() else m].mean(0)
    return mask, col


def inside(fg, box, cells_w, cells_h, px=3):
    """Which cells hold weapon pixels at least `px` pixels inside the outline. Only those give the texture its
    colours; the rest is filled from them, so the drawing's dark outline and the background blended into its soft
    rim never reach the model — a surface that strays a block past the drawing would show them as a dark edge."""
    x0, x1, y0, y1 = box
    core = shrink(fg, px)
    out = np.zeros((cells_h, cells_w), bool)
    xs = np.linspace(x0, x1, cells_w + 1)
    ys = np.linspace(y0, y1, cells_h + 1)
    for r in range(cells_h):
        ya, yb = int(round(ys[r])), max(int(round(ys[r + 1])), int(round(ys[r])) + 1)
        for c in range(cells_w):
            xa, xb = int(round(xs[c])), max(int(round(xs[c + 1])), int(round(xs[c])) + 1)
            out[cells_h - 1 - r, c] = core[ya:yb, xa:xb].any()
    return out


def greedy_rects(mask):
    """Cover the True cells of a 2D mask with as few rectangles as possible (row-major greedy)."""
    m = mask.copy()
    out = []
    h, w = m.shape
    for i in range(h):
        j = 0
        while j < w:
            if not m[i, j]:
                j += 1
                continue
            k = j
            while k < w and m[i, k]:
                k += 1
            n = i + 1
            while n < h and m[n, j:k].all():
                n += 1
            m[i:n, j:k] = False
            out.append((i, n, j, k))
            j = k
    return out


TOLERANCE = 2.0      # cells a two-view weapon's surface may stray from the views, to cover many rows with one face
SLIGHT_ROUNDING = 2  # cells: with --square, how far in from the outline an edge rounds over before it is flat
ROUND_REACH = 1.5    # otherwise an edge rounds over this many times the part's half-thickness, so wide parts
                     # (a blade, a disc) end in a thin rounded edge and narrow ones (a grip) come out round
CREASE = 50.0        # degrees: faces meeting more sharply keep a hard edge; gentler ones are shaded as one curve


def distance_in(mask):
    """How far each cell of the face view lies inside its outline, in cells, centre to the nearest empty cell's edge
    (a two-pass chamfer distance: 1 across, 1.414 diagonally)."""
    pad = np.pad(np.where(mask, 1e9, 0.0), 1)
    h, w = mask.shape
    for y in range(1, h + 1):
        for x in range(1, w + 1):
            if pad[y, x]:
                pad[y, x] = min(pad[y, x], pad[y - 1, x] + 1, pad[y, x - 1] + 1,
                                pad[y - 1, x - 1] + 1.414, pad[y - 1, x + 1] + 1.414)
    for y in range(h, 0, -1):
        for x in range(w, 0, -1):
            if pad[y, x]:
                pad[y, x] = min(pad[y, x], pad[y + 1, x] + 1, pad[y, x + 1] + 1,
                                pad[y + 1, x + 1] + 1.414, pad[y + 1, x - 1] + 1.414)
    return pad[1:-1, 1:-1] - 0.5


def sections(F, E, square):
    """Each row's cross-sections, one per run of the face view, as [left, right, depth centre, half-thickness at the
    two ends, how far in the rounding reaches, half-thickness there on the left, in the middle, there on the right]
    in cells. A point's thickness grows with how far it lies inside the face view's outline (in every direction, so
    tips and the tops of round parts thin out too) along a quarter circle, up to the edge view's thickness at that
    row: one block at the outline, full over SLIGHT_ROUNDING cells (--square) or ROUND_REACH half-thicknesses."""
    dist = distance_in(F)
    out = []
    for y in range(F.shape[0]):
        row = []
        ez = np.where(E[y])[0]
        xs = np.where(F[y])[0]
        if len(ez) and len(xs):
            zc, half = (ez[0] + ez[-1] + 1) / 2.0, (ez[-1] - ez[0] + 1) / 2.0
            e = min(0.5, half)
            reach = SLIGHT_ROUNDING if square else max(SLIGHT_ROUNDING, ROUND_REACH * half)
            for run in np.split(xs, np.where(np.diff(xs) > 1)[0] + 1):
                a, b = float(run[0]), float(run[-1] + 1)
                r = min(reach, 0.45 * (b - a))

                def thick(x):
                    c = int(min(max(x, a), b - 1))
                    t = min(dist[y, c] / reach, 1.0)
                    return e + (half - e) * np.sqrt(1.0 - (1.0 - t) ** 2)
                row.append(np.array([a, b, zc, e, r, thick(a + r), thick((a + b) / 2.0), thick(b - r)]))
        out.append(row)
    return out


def pieces(secs):
    """Rows' cross-sections joined into pieces: a run continues into the next row where each is the only run of its
    row overlapping the other. Each piece is a list of (row, index) from bottom to top."""
    def over(p, q):
        return p[0] < q[1] and q[0] < p[1]
    nxt = {}
    for y in range(len(secs) - 1):
        for i, p in enumerate(secs[y]):
            up = [j for j, q in enumerate(secs[y + 1]) if over(p, q)]
            if len(up) == 1 and sum(over(r, secs[y + 1][up[0]]) for r in secs[y]) == 1:
                nxt[(y, i)] = (y + 1, up[0])
    joined = set(nxt.values())
    out = []
    for y in range(len(secs)):
        for i in range(len(secs[y])):
            if (y, i) not in joined:
                c = [(y, i)]
                while c[-1] in nxt:
                    c.append(nxt[c[-1]])
                out.append(c)
    return out


def keyframes(P):
    """The fewest rows of a piece that need their own cross-section: every row between two kept ones lies within
    TOLERANCE of the straight blend between them, so one sloped face covers them all."""
    keys, i, n = [0], 0, len(P)
    while i < n - 1:
        j = i + 1
        while j + 1 < n:
            c = j + 1
            t = (np.arange(i + 1, c) - i)[:, None] / float(c - i)
            if np.abs(P[i + 1:c] - (P[i] + (P[c] - P[i]) * t)).max() > TOLERANCE:
                break
            j = c
        keys.append(j)
        i = j
    return keys


SECTION_WALLS = (0, 4)   # the corners starting the two thin end walls, painted from the edge view


def section(p):
    """A cross-section's corners in (x, z), going round: the left end wall, over the top (where the rounding ends
    on each side), the right end wall, and back under the bottom. The middle's own thickness is not a corner: the
    rounding has reached full thickness by then on all but the narrowest parts, and a corner there would cost a
    fifth more triangles for nothing visible."""
    a, b, zc, e, r, hl, hm, hr = p
    return [(a, zc - e), (a, zc + e), (a + r, zc + hl), (b - r, zc + hr),
            (b, zc + e), (b, zc - e), (b - r, zc - hr), (a + r, zc - hl)]


def build(sheet, name, cells, out_dir, thickness, square=False, texels=4):
    rgba = np.asarray(Image.open(sheet).convert("RGBA"))
    fg = foreground(rgba)
    views = split_views(fg)
    if not views:
        sys.exit("no weapon found on the sheet")
    if len(views) > 3:
        sys.exit("found %d views; a sheet holds at most three (face, edge, top)" % len(views))
    rgb = rgba[:, :, :3]

    face = views[0]
    H = cells
    px = (face[3] - face[2]) / H                        # sheet pixels per cell, from the face view's length
    W = max(1, int(round((face[1] - face[0]) / px)))
    F, Fcol = resample(rgb, fg, face, W, H)

    if len(views) >= 2:
        edge = views[1]
        epx = (edge[3] - edge[2]) / H
        D = max(1, int(round((edge[1] - edge[0]) / epx)))
        E, Ecol = resample(rgb, fg, edge, D, H)
    else:
        D = thickness
        E = F.any(1, keepdims=True).repeat(D, 1)        # flat slab as deep as `thickness` cells
        Ecol = None

    if len(views) == 3:
        T, _ = resample(rgb, fg, views[2], W, D)         # rows = depth (z), cols = x
    else:
        T = np.ones((D, W), bool)

    lofted = len(views) == 2
    if lofted:
        secs = sections(F, E, square)
        found = any(secs)
    else:
        # vox[y, x, z]: kept only where every view shows weapon
        vox = F[:, :, None] & E[:, None, :] & T.T[None, :, :]
        found = vox.any()
    if not found:
        sys.exit("the views do not overlap anywhere - check their order and alignment")

    # Texture: the face view, then the edge view, each `texels` x `texels` colours per block with a one-block border
    # round each, so the sheet's detail survives inside a block. Empty texels are filled from their neighbours so
    # point sampling at a face's rim never picks background.
    ts = texels
    _, Ftc = resample(rgb, fg, face, W * ts, H * ts)
    Ft = np.repeat(np.repeat(F, ts, 0), ts, 1) & inside(fg, face, W * ts, H * ts)
    TW, TH = (W + 2 + D + 2) * ts, (H + 2) * ts
    tex = np.zeros((TH, TW, 3), np.uint8)
    have = np.zeros((TH, TW), bool)
    FX, EX = ts, (W + 3) * ts

    def put(region_x, m, c):
        for y in range(m.shape[0]):
            row = TH - ts - 1 - y
            tex[row, region_x:region_x + m.shape[1]][m[y]] = c[y][m[y]]
            have[row, region_x:region_x + m.shape[1]] |= m[y]

    put(FX, Ft, Ftc)
    if Ecol is not None:
        _, Etc = resample(rgb, fg, views[1], D * ts, H * ts)
        put(EX, np.repeat(np.repeat(E, ts, 0), ts, 1) & inside(fg, views[1], D * ts, H * ts), Etc)
    else:   # one view: the sides take the face view's outline colour of that row
        for y in range(H * ts):
            xs = np.where(Ft[y])[0]
            if len(xs):
                tex[TH - ts - 1 - y, EX:EX + D * ts] = Ftc[y, xs[0]]
                have[TH - ts - 1 - y, EX:EX + D * ts] = True
    for _ in range(max(TW, TH)):
        if have.all():
            break
        grown = have.copy()
        acc = np.zeros((TH, TW, 3), np.int32)
        cnt = np.zeros((TH, TW), np.int32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sh = np.roll(have, (dy, dx), (0, 1))
            st = np.roll(tex, (dy, dx), (0, 1)).astype(np.int32)
            take = sh & ~have
            acc[take] += st[take]
            cnt[take] += 1
            grown |= take
        new = grown & ~have
        tex[new] = (acc[new] / cnt[new][:, None]).astype(np.uint8)
        have = grown

    def u_face(x):
        return (FX + x * ts) / TW

    def u_edge(z):
        return (EX + z * ts) / TW

    def v_row(y):
        return (y + 1) * ts / TH

    s = 1.0 / H
    ox, oz = W / 2.0, D / 2.0
    verts, uvs, norms, tris = [], [], [], []

    def quad(corners, uv, n):
        base = len(verts)
        for p, t in zip(corners, uv):
            verts.append(((p[0] - ox) * s, p[1] * s, (p[2] - oz) * s))
            uvs.append(t)
        norms.append(n)
        ni = len(norms)
        tris.append(((base, ni), (base + 1, ni), (base + 2, ni)))
        tris.append(((base, ni), (base + 2, ni), (base + 3, ni)))

    # Lofted triangles collect here first, as cell-space corners with one face normal each, so the shading can be
    # smoothed across gentle angles before they are written.
    lv, lt, ln_ = [], [], []

    def tri(p, t, outward):
        p = [np.asarray(q, float) for q in p]
        n = np.cross(p[1] - p[0], p[2] - p[0])
        length = np.linalg.norm(n)
        if length < 1e-9:
            return
        if np.dot(n, outward) < 0:
            p, t, n = [p[0], p[2], p[1]], [t[0], t[2], t[1]], -n
        lv.extend(p)
        lt.extend(t)
        ln_.append(n / length)

    if lofted:
        # Two views: each piece is lofted between the cross-sections of its key rows. Faces across the top and bottom
        # take the face view's colours (u from x), the thin end walls the edge view's (u from z); both run up by v.
        sides = len(section(secs[next(y for y in range(H) if secs[y])][0]))
        for piece in pieces(secs):
            P = np.array([secs[y][i] for y, i in piece])
            y0, y1 = piece[0][0], piece[-1][0]
            keys = keyframes(P)
            levels = [(float(y0), P[0])] + [(y0 + k + 0.5, P[k]) for k in keys[1:-1]] + [(y1 + 1.0, P[-1])]
            for (ya, pa), (yb, pb) in zip(levels, levels[1:]):
                ha, hb = section(pa), section(pb)
                axis = np.array([(pa[0] + pa[1] + pb[0] + pb[1]) / 4.0, (ya + yb) / 2.0, (pa[2] + pb[2]) / 2.0])
                for k in range(sides):
                    k2 = (k + 1) % sides
                    c = [(ha[k][0], ya, ha[k][1]), (ha[k2][0], ya, ha[k2][1]), (hb[k2][0], yb, hb[k2][1]), (hb[k][0], yb, hb[k][1])]
                    wall = k in SECTION_WALLS
                    t = [(u_edge(q[2]) if wall else u_face(q[0]), v_row(q[1])) for q in c]
                    out = np.mean(c, axis=0) - axis
                    tri(c[:3], t[:3], out)
                    tri([c[0], c[2], c[3]], [t[0], t[2], t[3]], out)
            for yl, p, up, row in ((levels[0][0], P[0], -1.0, y0), (levels[-1][0], P[-1], 1.0, y1)):
                h = section(p)
                v = (row + 1.5) * ts / TH
                for k in range(1, sides - 1):
                    c = [(h[0][0], yl, h[0][1]), (h[k][0], yl, h[k][1]), (h[k + 1][0], yl, h[k + 1][1])]
                    tri(c, [(u_face(q[0]), v) for q in c], (0.0, up, 0.0))
        # Smooth shading: a corner's normal is the average of the faces meeting at that point that bend less than
        # CREASE from its own face, so rounded edges shade as one curve and the flat ends keep a hard rim.
        cos_crease = np.cos(np.radians(CREASE))
        at = {}
        for i, q in enumerate(lv):
            at.setdefault((round(q[0], 4), round(q[1], 4), round(q[2], 4)), []).append(i)
        for i, q in enumerate(lv):
            own = ln_[i // 3]
            n = np.zeros(3)
            for j in at[(round(q[0], 4), round(q[1], 4), round(q[2], 4))]:
                if np.dot(ln_[j // 3], own) >= cos_crease:
                    n += ln_[j // 3]
            n /= max(np.linalg.norm(n), 1e-9)
            verts.append(((q[0] - ox) * s, q[1] * s, (q[2] - oz) * s))
            uvs.append(lt[i])
            norms.append(tuple(n))
        for f in range(len(lv) // 3):
            tris.append(tuple((3 * f + c, 3 * f + c + 1) for c in range(3)))
        info = {"pieces": len(pieces(secs))}
    else:
        info = {"blocks": int(vox.sum())}
        pad = np.pad(vox, 1)
        filled = pad[1:-1, 1:-1, 1:-1]
        # +Z / -Z: painted from the face view
        for sign in (1, -1):
            nb = pad[1:-1, 1:-1, 2:] if sign > 0 else pad[1:-1, 1:-1, :-2]
            exposed = filled & ~nb
            for z in range(D):
                zz = z + 1 if sign > 0 else z
                for y0, y1, x0, x1 in greedy_rects(exposed[:, :, z]):
                    c = [(x0, y0, zz), (x1, y0, zz), (x1, y1, zz), (x0, y1, zz)]
                    t = [(u_face(x0), v_row(y0)), (u_face(x1), v_row(y0)), (u_face(x1), v_row(y1)), (u_face(x0), v_row(y1))]
                    if sign < 0:
                        c, t = c[::-1], t[::-1]
                    quad(c, t, (0, 0, sign))
        # +X / -X: painted from the edge view
        for sign in (1, -1):
            nb = pad[1:-1, 2:, 1:-1] if sign > 0 else pad[1:-1, :-2, 1:-1]
            exposed = filled & ~nb
            for x in range(W):
                xx = x + 1 if sign > 0 else x
                for y0, y1, z0, z1 in greedy_rects(exposed[:, x, :]):
                    c = [(xx, y0, z1), (xx, y0, z0), (xx, y1, z0), (xx, y1, z1)]
                    t = [(u_edge(z1), v_row(y0)), (u_edge(z0), v_row(y0)), (u_edge(z0), v_row(y1)), (u_edge(z1), v_row(y1))]
                    if sign < 0:
                        c, t = c[::-1], t[::-1]
                    quad(c, t, (sign, 0, 0))
        # +Y / -Y: the face view's colour at that height (u along x, v fixed at the row's centre)
        for sign in (1, -1):
            nb = pad[2:, 1:-1, 1:-1] if sign > 0 else pad[:-2, 1:-1, 1:-1]
            exposed = filled & ~nb
            for y in range(H):
                yy = y + 1 if sign > 0 else y
                v = (y + 1.5) * ts / TH
                for z0, z1, x0, x1 in greedy_rects(exposed[y].T):
                    c = [(x0, yy, z1), (x1, yy, z1), (x1, yy, z0), (x0, yy, z0)]
                    t = [(u_face(x0), v), (u_face(x1), v), (u_face(x1), v), (u_face(x0), v)]
                    if sign < 0:
                        c, t = c[::-1], t[::-1]
                    quad(c, t, (0, sign, 0))

    folder = os.path.join(out_dir, name)
    os.makedirs(folder, exist_ok=True)
    Image.fromarray(tex).save(os.path.join(folder, name + ".png"))
    with open(os.path.join(folder, name + ".obj"), "w", newline="\n") as f:
        f.write("# made by tools/sprite-sheet-to-weapon.py from %s - re-run it, never hand-edit\n" % os.path.basename(sheet))
        f.write("o %s\ng %s\n" % (name, name))   # Unity names the mesh after the group
        for v in verts:
            f.write("v %.5f %.5f %.5f\n" % v)
        for t in uvs:
            f.write("vt %.6f %.6f\n" % t)
        for n in norms:
            f.write("vn %.4f %.4f %.4f\n" % n)
        for face in tris:
            f.write("f " + " ".join("%d/%d/%d" % (vi + 1, vi + 1, ni) for vi, ni in face) + "\n")

    return dict({"views": len(views), "cells": (W, H, D)}, **info,
                triangles=len(tris), vertices=len(verts), texture=(TW, TH), folder=folder)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sheet")
    ap.add_argument("--name", help="output name (default: the sheet's file name)")
    ap.add_argument("--cells", type=int, default=160, help="blocks along the weapon's length (default 160)")
    ap.add_argument("--thickness", type=int, default=3, help="depth in blocks when the sheet has only a face view")
    ap.add_argument("--square", action="store_true",
                    help="two-view sheet: round only the outer two blocks of every edge, so blocky parts stay blocky "
                         "(a hammer head, a rifle), instead of thinning wide parts to a rounded edge")
    ap.add_argument("--texels", type=int, default=4, help="texture colours per block, across and down (default 4)")
    ap.add_argument("--out", default=DEFAULT_OUT)
    a = ap.parse_args()
    name = a.name or os.path.splitext(os.path.basename(a.sheet))[0]
    info = build(a.sheet, name, a.cells, a.out, a.thickness, square=a.square, texels=a.texels)
    for k, v in info.items():
        print("%s: %s" % (k, v))


if __name__ == "__main__":
    main()
