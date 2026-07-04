"""
Batch auto-rig + download for Mixamo.

Feeds every mesh-only FBX (produced by tools/export_mesh_for_rigging.py) through
Mixamo's Auto-Rigger WITHOUT the manual marker-placement UI, then downloads the
rigged FBX. This works ONLY because every Quaternius mesh is exported identically
(transform applied, same scale/orientation/pose) so a single captured marker set
("rigging_inputs") lands correctly on all of them.

Pipeline per file (4 HTTP steps):
    1. POST   /api/v1/characters            upload FBX            -> new character id
    2. PUT    /api/v1/characters/{id}/rig   submit canned markers -> 202 (job queued)
    3. GET    /api/v1/characters/{id}/monitor  poll               -> 202 working / 200 done
    4. POST   /api/v1/animations/export     queue T-pose export, poll monitor, GET file

Steps 2 and 3 are taken verbatim from your captured requests. Steps 1 and 4 are
Mixamo's standard flow but I have NOT verified the exact request shape against your
session -- if a step 4xx/5xx's, open DevTools, do ONE manual rig+download, "Copy as
fetch" the upload and export requests, and adjust UPLOAD_* / EXPORT_* below.

COLOUR: Mixamo's auto-rigger re-exports the mesh and drops the embedded texture (it
keeps the UVs but hands back a plain material), so the rigged FBX comes back grey. The
colour survives as the baked atlas PNG that export_mesh_for_rigging.py saved beside each
UPLOAD FBX (<in>/textures/<base>.png). After each download we copy that PNG next to the
rigged FBX (<out>/textures/<base>.png) so the look can be re-applied by UV in Unity.

AUTH: the Bearer token is a short-lived JWT (~1h). Grab a fresh one from any Mixamo
API request's Authorization header and pass it via --token or the MIXAMO_TOKEN env var.
Re-run with a new token if the batch dies mid-way (already-done files are skipped).

Usage:
    pip install requests
    export MIXAMO_TOKEN='eyJhbGci...'          # or pass --token
    python tools/mixamo_batch_rig.py \
        --in  MixamoUpload \
        --out unity/Assets/Art/Quat/Rigged \
        --markers tools/mixamo_markers.json     # optional; omit to use EMBEDDED default
"""

import argparse
import json
import os
import shutil
import sys
import time

try:
    import requests
except ImportError:
    sys.exit("Missing dependency: pip install requests")


BASE = "https://www.mixamo.com/api/v1"

# --- headers shared by every request (from your captured fetch) ---------------
def make_headers(token, json_body=True):
    h = {
        "accept": "application/json, text/javascript, */*; q=0.01",
        "authorization": "Bearer " + token,
        "x-api-key": "mixamo2",
        "x-requested-with": "XMLHttpRequest",
        "origin": "https://www.mixamo.com",
        "referer": "https://www.mixamo.com/",
    }
    if json_body:
        h["content-type"] = "application/json; charset=UTF-8"
    return h


# --- canned marker set (from your captured PUT /rig body) ---------------------
# Reused across ALL meshes. Valid only because every FBX is exported identically.
# Override per-run with --markers pointing at a JSON file of the same shape.
Z = 524.6832360734485
EMBEDDED_MARKERS = {
    "rigging_inputs": {
        "chin":   {"x": 0,                   "y": 220.786394529533,  "z": Z},
        "larm":   {"x": -120.20789502999402, "y": 188.2439235674303, "z": Z},
        "rarm":   {"x": 120.20789502999402,  "y": 188.2439235674303, "z": Z},
        "lelbow": {"x": -85.00889814275821,  "y": 188.2439235674303, "z": Z},
        "relbow": {"x": 85.00889814275821,   "y": 188.2439235674303, "z": Z},
        "lknee":  {"x": -26.565280669611933, "y": 52.76098323541095, "z": Z},
        "rknee":  {"x": 26.565280669611962,  "y": 52.76098323541095, "z": Z},
        "groin":  {"x": 0,                   "y": 104.56328395059478, "z": Z},
        "orientation": {"x": 0, "y": 0, "z": 0, "w": 1},
        "symmetric": True,
        "for": "both",
        "skeleton-lod": "NoFingers",
    },
}

POLL_INTERVAL = 2.0    # seconds between monitor polls
POLL_TIMEOUT = 180     # seconds to wait for a single job before giving up
REQUEST_GAP = 1.5      # seconds between characters (be gentle on their API)


def upload(session, token, fbx_path):
    """Step 1: upload mesh-only FBX, return new character id.
    NOTE: request shape inferred from Mixamo's standard upload -- verify if it 4xx's."""
    url = BASE + "/characters"
    with open(fbx_path, "rb") as f:
        files = {"file": (os.path.basename(fbx_path), f, "application/octet-stream")}
        # multipart -> DON'T set json content-type
        r = session.post(url, headers=make_headers(token, json_body=False), files=files)
    r.raise_for_status()
    data = r.json()
    # Upload is async: it queues a "character_mapper" job. The character id is `uuid`.
    cid = data.get("uuid") or data.get("id") or data.get("character", {}).get("id")
    if not cid:
        raise RuntimeError("upload ok but no character id in response: %s" % data)
    return cid


def submit_rig(session, token, cid, markers):
    """Step 2: PUT canned markers. Returns when accepted (202)."""
    url = "%s/characters/%s/rig" % (BASE, cid)
    r = session.put(url, headers=make_headers(token), data=json.dumps(markers))
    r.raise_for_status()   # expect 202


def poll_monitor(session, token, cid, want="rigging", need_url=False):
    """Poll monitor until 200 (done). 202 = still working. When need_url is set (export),
    keep polling until the 200 body actually carries a download url -- guards against the
    stale previous-job 200 that can appear right after the export POST."""
    url = "%s/characters/%s/monitor" % (BASE, cid)
    deadline = time.time() + POLL_TIMEOUT
    while time.time() < deadline:
        r = session.get(url, headers=make_headers(token, json_body=False))
        if r.status_code == 200:
            body = r.json()
            if not need_url:
                return body
            dl = body.get("job_result") or body.get("download_url") or body.get("url")
            if dl and str(dl).startswith("http"):
                return body
        elif r.status_code != 202:
            r.raise_for_status()
        time.sleep(POLL_INTERVAL)
    raise TimeoutError("monitor for %s (%s) exceeded %ss" % (cid, want, POLL_TIMEOUT))


# Mixamo exports even a bare rigged character as a "Motion" carrying the standard
# T-Pose animation. This model-id is that T-Pose product -- constant for all characters.
TPOSE_MODEL_ID = 115510901


def export_and_download(session, token, cid, dst_fbx):
    """Step 4: queue a T-pose FBX export, poll monitor for the download url, save it."""
    url = BASE + "/animations/export"
    payload = {
        "gms_hash": [{
            "model-id": TPOSE_MODEL_ID, "mirror": False, "trim": [0, 100],
            "overdrive": 0, "params": "1,0", "arm-space": 0, "inplace": False,
        }],
        "preferences": {"format": "fbx7_2019", "skin": "true", "fps": "30", "reducekf": "0"},
        "character_id": cid,
        "type": "Motion",
        "product_name": "T-Pose",
    }
    r = session.post(url, headers=make_headers(token), data=json.dumps(payload))
    r.raise_for_status()

    # Same per-character monitor; on completion its 200 body carries `job_result` = file url.
    job = poll_monitor(session, token, cid, want="export", need_url=True)
    dl = job.get("job_result") or job.get("download_url") or job.get("url")
    if not dl or not str(dl).startswith("http"):
        raise RuntimeError("export done but no download url in monitor body: %s" % job)

    # `dl` is a presigned S3 URL -- it already carries its auth in the query string.
    # Sending Mixamo's Authorization header makes S3 reject it ("only one auth mechanism
    # allowed"), so download with a bare request and NO headers.
    fbx = requests.get(dl)
    fbx.raise_for_status()
    with open(dst_fbx, "wb") as f:
        f.write(fbx.content)


def carry_over_atlas(in_dir, out_dir, name):
    """Mixamo's auto-rigger re-exports the mesh and drops the embedded texture (it keeps the
    UVs but returns a plain material), so the rigged FBX comes back colourless. The colour still
    exists as the baked atlas PNG that export_mesh_for_rigging.py saved next to the UPLOAD FBX
    (in <in_dir>/textures/<base>.png). Copy that PNG next to the rigged FBX so the look can be
    re-applied by UV in Unity. Returns the copied path, or None if no atlas was found."""
    base = os.path.splitext(name)[0]
    src_png = os.path.join(in_dir, "textures", base + ".png")
    if not os.path.exists(src_png):
        return None
    dst_tex_dir = os.path.join(out_dir, "textures")
    os.makedirs(dst_tex_dir, exist_ok=True)
    dst_png = os.path.join(dst_tex_dir, base + ".png")
    shutil.copy2(src_png, dst_png)
    return dst_png


def report(in_dir, out_dir):
    """Compare input meshes against downloaded rigs: done (in out/) vs pending vs orphans."""
    in_dir = os.path.abspath(in_dir)
    out_dir = os.path.abspath(out_dir)
    src = {f for f in os.listdir(in_dir) if f.lower().endswith(".fbx")} if os.path.isdir(in_dir) else set()
    done = {f for f in os.listdir(out_dir) if f.lower().endswith(".fbx")} if os.path.isdir(out_dir) else set()

    pending = sorted(src - done)
    finished = sorted(src & done)
    orphans = sorted(done - src)   # rigged but no matching source (renamed/removed input)

    total = len(src)
    print("Mixamo rig status  (in: %s  ->  out: %s)" % (in_dir, out_dir))
    print("  DONE   : %d/%d" % (len(finished), total))
    print("  PENDING: %d" % len(pending))
    for f in pending:
        print("    - " + f)
    if orphans:
        print("  ORPHANS (in out/, no source): %d" % len(orphans))
        for f in orphans:
            print("    ? " + f)
    if total and not pending:
        print("  ✅ all source meshes rigged.")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--in", dest="in_dir", required=True, help="folder of mesh-only FBX to rig")
    p.add_argument("--out", dest="out_dir", required=True, help="folder to write rigged FBX")
    p.add_argument("--token", default=os.environ.get("MIXAMO_TOKEN"),
                   help="Mixamo Bearer JWT (or set MIXAMO_TOKEN)")
    p.add_argument("--markers", help="JSON file overriding the embedded marker set")
    p.add_argument("--report", action="store_true",
                   help="just print done vs pending (in - out) and exit; no token/upload needed")
    args = p.parse_args()

    if args.report:
        report(args.in_dir, args.out_dir)
        return

    if not args.token:
        sys.exit("No token. Pass --token or set MIXAMO_TOKEN (grab a fresh JWT from DevTools).")

    markers = EMBEDDED_MARKERS
    if args.markers:
        with open(args.markers, "r", encoding="utf-8") as f:
            markers = json.load(f)

    in_dir = os.path.abspath(args.in_dir)
    out_dir = os.path.abspath(args.out_dir)
    os.makedirs(out_dir, exist_ok=True)

    fbxs = sorted(f for f in os.listdir(in_dir) if f.lower().endswith(".fbx"))
    if not fbxs:
        sys.exit("No FBX in " + in_dir)

    session = requests.Session()
    ok = 0
    for name in fbxs:
        dst = os.path.join(out_dir, name)
        if os.path.exists(dst):
            print("= skip (already rigged):", name)
            if not os.path.exists(os.path.join(out_dir, "textures", os.path.splitext(name)[0] + ".png")):
                if carry_over_atlas(in_dir, out_dir, name):
                    print("    backfilled colour atlas")
            ok += 1
            continue
        print("- rigging:", name)
        try:
            cid = upload(session, args.token, os.path.join(in_dir, name))
            print("    uploaded, id =", cid, "-- waiting for mesh processing...")
            poll_monitor(session, args.token, cid, want="mapper")
            submit_rig(session, args.token, cid, markers)
            poll_monitor(session, args.token, cid, want="rigging")
            print("    rig done, exporting...")
            export_and_download(session, args.token, cid, dst)
            print("    saved ->", dst)
            atlas = carry_over_atlas(in_dir, out_dir, name)
            if atlas:
                print("    colour atlas ->", atlas)
            else:
                print("    (no baked atlas found next to upload; rigged FBX will be plain)")
            ok += 1
        except Exception as e:
            print("    !! FAILED:", repr(e))
            if isinstance(e, requests.HTTPError) and e.response is not None:
                print("       status", e.response.status_code, e.response.text[:300])
                if e.response.status_code in (401, 403):
                    sys.exit("Auth failed -- token likely expired. Grab a fresh JWT and re-run.")
        time.sleep(REQUEST_GAP)

    print("Done: %d/%d rigged into %s" % (ok, len(fbxs), out_dir))


if __name__ == "__main__":
    main()
