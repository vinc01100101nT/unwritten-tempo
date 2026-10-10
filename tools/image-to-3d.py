#!/usr/bin/env python3
"""Turn one picture into a textured 3D model with Trellis.2, at a game-sized triangle count, as an OBJ + PNG Unity
imports as-is. No server, no window: it drives the installed Trellis app's own worker (its low-memory patches
included) once and exits.

    python3 tools/image-to-3d.py <picture.png> --name wand_seraphine [--faces 1500] [--seed 42]
    python3 tools/image-to-3d.py a.png=name_a b.png=name_b ...   (a batch: Trellis loads once)
                                 [--texture 1024] [--out <folder>] [--trellis <install folder>]

Writes <out>/<name>/<name>.obj, <name>.png (the base colour) and <name>_input.png (the picture after Trellis's own
background removal - check it shows the whole object). The model stands along +Y, centred on the origin.

The picture: ONE object, whole, centred, on a plain or transparent background. A weapon's front view cut from its
two-view sheet works.

Trellis lives outside the repo (a one-click install, ~21 GB of model weights); this script re-runs itself under that
install's Python. Point --trellis or TRELLIS_HOME at it. License: the install's own note says Trellis contains
NVIDIA code that "disallows commercial use" - models made with it are not for sale until that changes.
"""
import argparse
import io
import json
import os
import struct
import subprocess
import sys
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_OUT = os.path.join(REPO, "unity", "Assets", "2_Generated", "WeaponModels")
DEFAULT_TRELLIS = os.environ.get("TRELLIS_HOME", r"C:\Users\Admin\Downloads\trellis2-stableprojectorz_v22")

# The switches the install's own launcher sets; without them the worker cannot open its light maps (.exr) or
# import its renderer (setuptools' distutils hack against the portable Python).
LAUNCHER_ENV = {"SETUPTOOLS_USE_DISTUTILS": "stdlib", "PYTHONNOUSERSITE": "1", "OPENCV_IO_ENABLE_OPENEXR": "1"}


def trellis_python(home):
    return os.path.join(home, "code", "venv", "Scripts", "python.exe")


def relaunch(args, home):
    py = trellis_python(home)
    if not os.path.isfile(py):
        sys.exit("Trellis is not installed at %s (no %s) - run its run-browser\\run-gradio.bat once, or pass "
                 "--trellis" % (home, py))
    env = dict(os.environ, **LAUNCHER_ENV, HF_HOME=os.path.join(home, "code", "models"), IMAGE_TO_3D_INNER="1")
    return subprocess.call([py, os.path.abspath(__file__)] + args, env=env)


def glb_to_obj(glb_path, name, folder):
    """The GLB Trellis wrote, as an OBJ (named mesh, v flipped to OBJ's bottom-up) and its base-colour PNG."""
    import numpy as np
    from PIL import Image
    data = open(glb_path, "rb").read()
    _, _, length = struct.unpack_from("<III", data, 0)
    off, js, binc = 12, None, None
    while off < length:
        clen, ctype = struct.unpack_from("<II", data, off)
        chunk = data[off + 8: off + 8 + clen]
        if ctype == 0x4E4F534A:
            js = json.loads(chunk)
        elif ctype == 0x004E4942:
            binc = chunk
        off += 8 + clen
    ctypes = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}
    comps = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}

    def acc(i):
        a = js["accessors"][i]
        bv = js["bufferViews"][a["bufferView"]]
        dt = np.dtype(ctypes[a["componentType"]])
        n = comps[a["type"]]
        start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = bv.get("byteStride", 0) or dt.itemsize * n
        raw = np.frombuffer(binc, np.uint8, stride * (a["count"] - 1) + dt.itemsize * n, start)
        rows = np.lib.stride_tricks.as_strided(raw, (a["count"], dt.itemsize * n), (stride, 1)).copy()
        return rows.view(dt).reshape(a["count"], n).astype(np.float64)

    V, N, T, F, base = [], [], [], [], 0
    for mesh in js["meshes"]:
        for prim in mesh["primitives"]:
            at = prim["attributes"]
            p = acc(at["POSITION"])
            V.append(p)
            N.append(acc(at["NORMAL"]) if "NORMAL" in at else np.zeros_like(p))
            T.append(acc(at["TEXCOORD_0"]) if "TEXCOORD_0" in at else np.zeros((len(p), 2)))
            F.append(acc(prim["indices"]).astype(int).reshape(-1, 3) + base)
            base += len(p)
    V, N, T, F = np.vstack(V), np.vstack(N), np.vstack(T), np.vstack(F)
    im = js["images"][0]
    bv = js["bufferViews"][im["bufferView"]]
    blob = binc[bv.get("byteOffset", 0): bv.get("byteOffset", 0) + bv["byteLength"]]
    Image.open(io.BytesIO(blob)).convert("RGB").save(os.path.join(folder, name + ".png"))
    with open(os.path.join(folder, name + ".obj"), "w", newline="\n") as f:
        f.write("# made by tools/image-to-3d.py (Trellis.2) - re-run it, never hand-edit\n")
        f.write("o %s\ng %s\n" % (name, name))   # Unity names the mesh after the group
        for v in V:
            f.write("v %.6f %.6f %.6f\n" % tuple(v))
        for t in T:
            f.write("vt %.6f %.6f\n" % (t[0], 1.0 - t[1]))
        for n in N:
            f.write("vn %.5f %.5f %.5f\n" % tuple(n))
        for a, b, c in F + 1:
            f.write("f %d/%d/%d %d/%d/%d %d/%d/%d\n" % (a, a, a, b, b, b, c, c, c))
    return len(F), (V.max(0) - V.min(0))


def generate(a, home):
    # one or more pictures; each "picture=name", or a lone picture named by --name
    jobs = []
    for item in a.pictures:
        path, _, name = item.rpartition("=") if "=" in os.path.basename(item) else (item, "", a.name)
        if not name:
            sys.exit("no name for %s - pass --name, or picture=name" % item)
        jobs.append((os.path.abspath(path), name, os.path.join(os.path.abspath(a.out), name)))
    for _, _, folder in jobs:
        os.makedirs(folder, exist_ok=True)
    code = os.path.join(home, "code")
    sys.path.insert(0, code)
    os.chdir(code)   # the worker loads its light maps and settings from paths relative to here
    from PIL import Image
    from pipeline_worker import PipelineWorker
    t0 = time.time()
    worker = PipelineWorker()   # loaded once for the whole batch: the load is two thirds of a single run
    done = []
    try:
        print("Trellis ready after %.0f s" % (time.time() - t0), flush=True)
        for picture, name, folder in jobs:
            image = worker.preprocess(Image.open(picture))
            image.save(os.path.join(folder, name + "_input.png"))
            t1 = time.time()
            # The settings of the town-prop run (memory trellis2-tier1-town-pipeline-proven); 4 preview views only.
            state, _ = worker.generate(
                image=image, seed=a.seed,
                ss_params={"steps": 14, "guidance_strength": 7.5, "guidance_rescale": 0.7, "rescale_t": 5.0},
                shape_params={"steps": 14, "guidance_strength": 7.5, "guidance_rescale": 0.5, "rescale_t": 3.0},
                tex_params={"steps": 14, "guidance_strength": 1.0, "guidance_rescale": 0.0, "rescale_t": 3.0},
                pipeline_type="1024_cascade", nviews=4,
                profiling={"enable_python": False, "enable_torch": False, "enable_sync_hunter": False,
                           "delay_sec": 0.0, "max_duration_sec": 0.0, "max_events": 0})
            # Trellis's own exporter reduces the shape BEFORE it lays out the texture. Reducing its 100k-triangle
            # model afterwards (tools/optimize-glb.sh) stalls at ~6,800 triangles on the texture seams.
            glb = os.path.join(folder, name + ".glb")
            worker.extract_glb(state, a.faces, a.texture, glb)
            tris, size = glb_to_obj(glb, name, folder)
            os.remove(glb)
            done.append(name)
            print("%s: %d triangles, size %s, %.0f s -> %s" % (name, tris, size.round(3), time.time() - t1, folder),
                  flush=True)
    finally:
        worker.shutdown()
    print("done %d of %d" % (len(done), len(jobs)))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("pictures", nargs="+", help="a picture, or several as picture=name (one Trellis load for all)")
    ap.add_argument("--name", help="output name for a single picture; the OBJ, its mesh and its texture use it")
    ap.add_argument("--faces", type=int, default=1500, help="triangles in the model (default 1500)")
    ap.add_argument("--seed", type=int, default=42, help="same picture + same seed = nearly the same model")
    ap.add_argument("--texture", type=int, default=1024, help="texture size in pixels (default 1024)")
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--trellis", default=DEFAULT_TRELLIS, help="the Trellis install folder (or TRELLIS_HOME)")
    a = ap.parse_args()
    if os.environ.get("IMAGE_TO_3D_INNER") != "1":
        sys.exit(relaunch(sys.argv[1:], a.trellis))
    generate(a, a.trellis)


if __name__ == "__main__":
    main()
