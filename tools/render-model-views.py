#!/usr/bin/env python3
"""Render a model (OBJ + its texture) from five angles beside the picture it was made from, to judge it BEFORE it goes
into Unity: straight front, straight side, three-quarter, high three-quarter and from behind and above (the angles
least kind to a model made from one or two drawings). Needs numpy + PIL only.

    python3 tools/render-model-views.py <model.obj> <texture.png> <reference.png> <out.png> [--top 0.3]

--top keeps only the upper share of the model (and of the reference) for a close-up of a head.
"""
import argparse
import numpy as np
from PIL import Image


def load(obj):
    V, T, N, F = [], [], [], []
    for line in open(obj):
        p = line.split()
        if not p:
            continue
        if p[0] == "v":
            V.append([float(x) for x in p[1:4]])
        elif p[0] == "vt":
            T.append([float(x) for x in p[1:3]])
        elif p[0] == "vn":
            N.append([float(x) for x in p[1:4]])
        elif p[0] == "f":
            F.append([[int(i) - 1 for i in c.split("/")] for c in p[1:4]])
    return np.array(V), np.array(T), np.array(N), np.array(F)


def turn(yaw, pitch):
    a, b = np.radians(yaw), np.radians(pitch)
    ry = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]])
    rx = np.array([[1, 0, 0], [0, np.cos(b), -np.sin(b)], [0, np.sin(b), np.cos(b)]])
    return rx @ ry


def render(V, T, N, F, tex, R, size):
    Vr, Nr = V @ R.T, N @ R.T
    used = np.unique(F[:, :, 0])
    lo, hi = Vr[used, :2].min(0), Vr[used, :2].max(0)
    P = (Vr[:, :2] - lo) * ((size - 20) / max(hi - lo)) + 10
    P[:, 1] = size - P[:, 1]
    img = np.full((size, size, 3), 70.0)
    zb = np.full((size, size), -1e9)
    th, tw = tex.shape[:2]
    light = np.array([-0.5, 0.6, 0.6]) / np.linalg.norm([-0.5, 0.6, 0.6])
    for f in F:
        vi, ti, ni = f[:, 0], f[:, 1], f[:, 2]
        p, z = P[vi], Vr[vi, 2]
        x0, y0 = np.maximum(np.floor(p.min(0)).astype(int), 0)
        x1, y1 = np.minimum(np.ceil(p.max(0)).astype(int), size - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0]) + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1])
        if abs(d) < 1e-12:
            continue
        a = ((p[1, 1] - p[2, 1]) * (xs - p[2, 0]) + (p[2, 0] - p[1, 0]) * (ys - p[2, 1])) / d
        b = ((p[2, 1] - p[0, 1]) * (xs - p[2, 0]) + (p[0, 0] - p[2, 0]) * (ys - p[2, 1])) / d
        c = 1 - a - b
        inside = (a >= -1e-6) & (b >= -1e-6) & (c >= -1e-6)
        if not inside.any():
            continue
        zz = a * z[0] + b * z[1] + c * z[2]
        uv = a[..., None] * T[ti[0]] + b[..., None] * T[ti[1]] + c[..., None] * T[ti[2]]
        nn = a[..., None] * Nr[ni[0]] + b[..., None] * Nr[ni[1]] + c[..., None] * Nr[ni[2]]
        nn = nn / np.maximum(np.linalg.norm(nn, axis=-1, keepdims=True), 1e-9)
        sub = zb[y0:y1 + 1, x0:x1 + 1]
        draw = inside & (zz > sub)
        u = np.clip((uv[..., 0] * tw).astype(int), 0, tw - 1)
        v = np.clip(((1 - uv[..., 1]) * th).astype(int), 0, th - 1)
        shade = 0.35 + 0.75 * np.abs(nn @ light)
        im = img[y0:y1 + 1, x0:x1 + 1]
        im[draw] = (tex[v, u].astype(float) * shade[..., None])[draw]
        sub[draw] = zz[draw]
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("obj"); ap.add_argument("texture"); ap.add_argument("reference"); ap.add_argument("out")
    ap.add_argument("--top", type=float, default=0.0, help="keep only this upper share of the model (0 = all)")
    ap.add_argument("--size", type=int, default=420)
    a = ap.parse_args()
    V, T, N, F = load(a.obj)
    if a.top:
        keep = V[:, 1] >= V[:, 1].max() - (V[:, 1].max() - V[:, 1].min()) * a.top
        F = F[keep[F[:, :, 0]].all(1)]
    tex = np.asarray(Image.open(a.texture).convert("RGB"))
    ref = Image.open(a.reference).convert("RGB")
    if a.top:
        w, h = ref.size
        ref = ref.crop((0, 0, w, int(h * (a.top + 0.05))))
    ref.thumbnail((a.size, a.size))
    S = a.size
    canvas = Image.new("RGB", (S * 6, S), (70, 70, 70))
    canvas.paste(ref, (0, 0))
    for i, (yaw, pitch) in enumerate(((0, 0), (90, 0), (40, 0), (40, 35), (200, 60))):
        canvas.paste(render(V, T, N, F, tex, turn(yaw, pitch), S), (S * (i + 1), 0))
    canvas.save(a.out)
    print("saved %s (%d triangles drawn)" % (a.out, len(F)))


if __name__ == "__main__":
    main()
