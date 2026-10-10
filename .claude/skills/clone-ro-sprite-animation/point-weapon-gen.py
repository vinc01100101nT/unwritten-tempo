"""Fill point-weapon-build.cs from the key tables and write the runnable script.
Usage: python point-weapon-gen.py <out .cs> <write 0|1> <aim 0|1> [weapon ...]   (key tables: keys/ beside this file)
write 0 = build in memory and report the difference from the saved clip; aim 0 = the plain build with no weapon pointing
(run it once first: every clip must report a difference of 0.0000, which proves the key tables still match the saved clips)."""
import json, os, sys
SP = os.path.dirname(os.path.abspath(__file__))
ITEM = {"sword": "Sword", "axe": "Axe", "warhammer": "Warhammer", "spear": "Spear", "fist": "Fist", "dagger": "Dagger",
        "katar": "Katar", "shuriken": "Shuriken", "sniper": "Sniper", "staff": "Staff", "wand": "Wand", "scythe": "Scythe",
        "greatsword": "Greatsword"}
KEYS = os.path.join(SP, "keys")
OUT = sys.argv[1]
write, aim = sys.argv[2] == "1", sys.argv[3] == "1"
names = sys.argv[4:] or list(ITEM)
# THE BREATH every weapon's standing idle shares (user, 2026-10-10: "standardize it to all weapons", taken from the
# dagger's): a looping table holds ONE key, the stance; halfway through the loop the hips rise BREATH_RISE (share of
# body height) and the torso straightens BREATH_STRAIGHTEN degrees, then it settles back.
BREATH_FRAMES, BREATH_RISE, BREATH_STRAIGHTEN = 66, 0.032, 4

def breathe(c):
    if not c.get("loop"): return c["keys"]
    if len(c["keys"]) != 1: sys.exit("%s: a looping idle holds exactly one key (the stance); the breath is added here" % c["clip"])
    k = c["keys"][0]
    mid = dict(k, f=BREATH_FRAMES // 2, hipD=round(k.get("hipD", 0) - BREATH_RISE, 4), lean=k.get("lean", 0) - BREATH_STRAIGHTEN)
    return [dict(k, f=0), mid, dict(k, f=BREATH_FRAMES)]

def row(k):
    v = lambda n: k.get(n, [0, 0, 0])
    vals = [k["f"], 1 if k.get("idle") else 0, k.get("hipF", 0), k.get("hipD", 0), k.get("lean", 0), k.get("yaw", 0),
            *v("rf"), *v("lf"), *v("rd"), k.get("rr", 0), *v("ld"), k.get("lr", 0),
            1 if "rw" in k else 0, *k.get("rw", [0, 0, 0]), 1 if "re" in k else 0, *k.get("re", [0, 0, 0]),
            1 if "lw" in k else 0, *k.get("lw", [0, 0, 0]), 1 if "le" in k else 0, *k.get("le", [0, 0, 0]),
            1 if "rp" in k else 0, *k.get("rp", [0, 0, 0]), 1 if "lp" in k else 0, *k.get("lp", [0, 0, 0]),
            k.get("look", 0), 1 if k.get("pass") else 0]
    return "new float[]{" + ",".join("%gf" % x for x in vals) + "}"

out = []
for w in names:
    c = json.load(open(os.path.join(KEYS, w + ".json")))
    out.append('  ("%s", "%s", %s, %s, %gf, %s, %s, %s, %s, %gf, %d, new float[][]{ %s }),' % (
        c["clip"], ITEM[w.split("_")[0]], "true" if c.get("stepLift", True) else "false", "true" if c.get("twoHand") else "false",
        c.get("secondGrip", -0.11), "true" if c.get("rifle") else "false", "true" if c.get("loop") else "false",
        "true" if c.get("smooth") else "false", "true" if c.get("steadyArms") else "false", c.get("wristStep", 0), c.get("steadyPasses", 0), ", ".join(row(k) for k in breathe(c))))
src = open(os.path.join(SP, "point-weapon-build.cs"), encoding="utf-8").read()
src = src.replace("__CLIPS__", "\n".join(out)).replace("__WRITE__", "true" if write else "false").replace("__AIM__", "true" if aim else "false")
open(OUT, "w", encoding="utf-8").write(src)
print("ok", names, "write", write, "aim", aim)
