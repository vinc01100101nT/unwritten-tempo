"""Writes keys/greatsword_flourish.json (or a trial name): the blade's tip traces a horizontal figure-8 (a Gerono
lemniscate in yaw/pitch around a centre direction), starting and ending exactly at the idle key, one key per frame at 30/s.
arg: JSON of parameters (all optional)."""
import json, math, os, sys
K = r"D:/tempo/projects/roguelike-mmo/.claude/skills/clone-ro-sprite-animation/keys"
P = dict(name="greatsword_flourish", clip="GreatswordFlourish", frames=48, ramp=0.18, ay=24.0, hx=0.10, hy=0.12, hz=0.0,
         yawFollow=0.0, edge="tangent")
if len(sys.argv) > 1: P.update(json.loads(sys.argv[1]))
idle = json.load(open(os.path.join(K, "greatsword_idle.json")))["keys"][0]
SH = [0.1105, 0.4954, 0.1080]   # the built right shoulder in the idle (hips 3% down), measured 2026-10-10
W0 = [0.0703, 0.2826, 0.2084]   # the built right wrist in the idle
ARM = 0.2786
d0 = idle["rw"]; n = math.sqrt(sum(x * x for x in d0)); d0 = [x / n for x in d0]
yaw0 = math.atan2(d0[0], d0[2]); pit0 = math.asin(d0[1])
# lemniscate: yaw = yawC + AX cos t, pitch = pC + AY sin 2t; the idle sits at t = -pi/4 (low right)
AY = math.radians(P["ay"]); AX = yaw0 / math.cos(math.pi / 4) if "ax" not in P else math.radians(P["ax"])
yawC = yaw0 - AX * math.cos(-math.pi / 4); pC = pit0 - AY * math.sin(-math.pi / 2)
def direction(t):
    y = yawC + AX * math.cos(t); p = pC + AY * math.sin(2 * t)
    return [math.sin(y) * math.cos(p), math.sin(p), math.cos(y) * math.cos(p)], y, p
def ss(x): x = min(max(x, 0.0), 1.0); return x * x * (3 - 2 * x)
# speed profile: eases up from rest, cruises, eases down to rest; g(u) = its normalised integral
NS = 4000; v = [ss(i / NS / P["ramp"]) * ss((1 - i / NS) / P["ramp"]) for i in range(NS + 1)]
cum = [0.0]
for i in range(NS): cum.append(cum[-1] + (v[i] + v[i + 1]) / 2)
def g(u): x = u * NS; i = min(int(x), NS - 1); f = x - i; return (cum[i] + (cum[i + 1] - cum[i]) * f) / cum[-1]
F = P["frames"]; keys = []; prevE = idle["re"]
def norm(a): n = math.sqrt(sum(x * x for x in a)); return [x / n for x in a]
def perp(a, d): k = sum(a[i] * d[i] for i in range(3)); return norm([a[i] - k * d[i] for i in range(3)])
for f in range(F + 1):
    if f == 0 or f == F:
        kk = dict(idle, f=f)
        if f == F and P.get("endNoRp"): kk.pop("rp")
        keys.append(kk); continue
    t = -math.pi / 4 + 2 * math.pi * g(f / F)
    d, y, p = direction(t)
    if "anchors" in P:
        # the hands follow a closed smooth curve through anchors at t = -pi/4 + k*pi/4 (k = 0..7), the first being the idle wrist
        A8 = [W0] + P["anchors"]; u = (t + math.pi / 4) / (math.pi / 4); k0 = int(math.floor(u)) % 8; fr = u - math.floor(u)
        p0, p1, p2, p3 = A8[(k0 - 1) % 8], A8[k0], A8[(k0 + 1) % 8], A8[(k0 + 2) % 8]
        hand = [0.5 * (2 * p1[i] + (-p0[i] + p2[i]) * fr + (2 * p0[i] - 5 * p1[i] + 4 * p2[i] - p3[i]) * fr * fr + (-p0[i] + 3 * p1[i] - 3 * p2[i] + p3[i]) * fr ** 3) for i in range(3)]
    else:
        hand = [W0[0] + P["hx"] * (math.sin(y) - math.sin(yaw0)), W0[1] + P["hy"] * (math.sin(p) - math.sin(pit0)), W0[2] + P["hz"] * (math.cos(y) - math.cos(yaw0))]
    vv = [hand[i] - SH[i] for i in range(3)]; m = math.sqrt(sum(x * x for x in vv))
    k = {kk: idle[kk] for kk in ("hipF", "hipD", "lean", "rf", "lf", "ld", "lr")}
    if P.get("rpAll"): k["rp"] = idle["rp"]
    if "rp" in P: k["rp"] = P["rp"]
    k["yaw"] = idle["yaw"] + P["yawFollow"] * math.degrees(y - yaw0)
    k.update(f=f, rd=[round(x / m, 4) for x in vv], rr=round(min(m / ARM, 0.97), 4), rw=[round(x, 4) for x in d])
    # the edge leads along the tip's travel (a figure-8 cuts with both edges), the sign kept nearest the last key's
    if P["edge"] == "transport":
        # the blade does not roll more than the path makes it: the last key's edge carried over, kept square to the blade
        tg = perp(prevE, d); k["re"] = [round(x, 4) for x in tg]; prevE = tg
    if P["edge"] == "tangent":
        d2, _, _ = direction(t + 0.01); tg = perp([d2[i] - d[i] for i in range(3)], d)
        if sum(tg[i] * prevE[i] for i in range(3)) < 0: tg = [-x for x in tg]
        k["re"] = [round(x, 4) for x in tg]; prevE = tg
    if m / ARM > 0.97: print("reach clipped at f", f, round(m / ARM, 3), file=sys.stderr)
    keys.append(k)
last = keys[-2].get("re"); ie = perp(idle["re"], d0)
if last: print("edge at the last moving key vs the idle's edge: dot %.2f" % sum(last[i] * ie[i] for i in range(3)), file=sys.stderr)
print("yaw range %.0f..%.0f deg, pitch range %.0f..%.0f deg" % (math.degrees(yawC - AX), math.degrees(yawC + AX), math.degrees(pC - AY), math.degrees(pC + AY)), file=sys.stderr)
json.dump({"clip": P["clip"], "stepLift": False, "twoHand": True, "smooth": True, "steadyArms": True, "keys": keys}, open(os.path.join(K, P["name"] + ".json"), "w"))
print(P["name"])
