"""The stance-set key tables of the Sword, Staff, Wand, Spear, Warhammer, Scythe, Bow, Sniper, Shuriken and Fist
(2026-10-10): each weapon's combat idle, its running arms, and the still poses its put-away is blended through by
paths/pose_chain_blend.cs. Run from the skill's folder: python paths/stance_sets.py — it rewrites keys/<weapon>_*.json.
Every direction is in the character's frame (x right, y up, z forward); every position a share of body height.
Measured on the standing default body (2026-10-10): right shoulder (0.098, 0.552, 0.013), left (-0.091, 0.576, 0.006),
an arm 0.278 long from shoulder to wrist."""
import json, math, os
SP = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RS, LS, ARM = [0.098, 0.552, 0.013], [-0.091, 0.576, 0.006], 0.278

def norm(v):
    l = math.sqrt(sum(c * c for c in v)); return [round(c / l, 4) for c in v]
def reach(shoulder, wrist, cap=1.0):
    """the arm's direction and reach that put the wrist at a point"""
    v = [wrist[i] - shoulder[i] for i in range(3)]; l = math.sqrt(sum(c * c for c in v))
    return norm(v), round(min(l / ARM, cap), 4)
def perp(v, n):
    """the part of v square to n, as a unit direction"""
    n = norm(n); d = sum(v[i] * n[i] for i in range(3)); return norm([v[i] - d * n[i] for i in range(3)])
def upright(v, lean, yaw):
    """a direction of a leaning, turned idle carried onto the standing body (the greatsword hold's recipe)"""
    a, b = math.radians(-lean), math.radians(-yaw)
    x, y, z = v[0], v[1] * math.cos(a) - v[2] * math.sin(a), v[1] * math.sin(a) + v[2] * math.cos(a)
    return [round(x * math.cos(b) + z * math.sin(b), 4), round(y, 4), round(-x * math.sin(b) + z * math.cos(b), 4)]
def twisted(p, deg):
    """a shoulder carried round by a torso twist (degrees to the character's right)"""
    t = math.radians(deg); return [p[0] * math.cos(t) + p[2] * math.sin(t), p[1], -p[0] * math.sin(t) + p[2] * math.cos(t)]

STAND = {"hipF": 0, "hipD": 0, "lean": 0, "yaw": 0, "rf": [0.09, 0, -0.04], "lf": [-0.09, 0, 0.0]}
HANG_L = {"ld": [-0.2349, -0.9706, 0.053], "lr": 0.9503}
HANG_R = {"rd": [0.149, -0.989, 0.0], "rr": 0.917}
GUARD_L = {"ld": [0.341, -0.398, 0.852], "lr": 0.5}
tables = {}
def table(name, clip, keys, note, **top):
    tables[name] = dict({"clip": clip, "note": note, "stepLift": False}, **top, keys=keys)
def still(k): return [dict(k, f=0), dict(k, f=4)]
def idle_and_carry(w, W, key, note, carry=None, **top):
    """the combat idle (one key; the shared breath is added by point-weapon-gen.py) and the running arms"""
    table(w + "_idle", "Idle" + W, [dict(key, f=0)], note, loop=True, smooth=True, **top)
    table(w + "_carry", "Carry" + W, still(dict(key, **(carry or {}))), "The running arms of the " + W + ": " + (
        "its own carry hold over the idle's body." if carry else "the idle's arms, held over the run."), **top)
def hold_upright(w, W, key, **top):
    """the idle's arms on the STANDING body — the first pose of the put-away (a put-away plays over the shared idle or the run)"""
    k = dict(STAND)
    for n in ("rd", "ld", "rw", "re", "lw", "le", "rp", "lp"):
        if n in key: k[n] = upright(key[n], key.get("lean", 0), key.get("yaw", 0))
    for n in ("rr", "lr"):
        if n in key: k[n] = key[n]
    table(w + "_hold_upright", W + "HoldUpright", still(k), "The " + W + " idle's arms on the standing body: the first pose of its put-away.", **top)
    return k

# ── SWORD ───────────────────────────────────────────────────────────────────────────────────────────────────────────
BODY1 = {"hipF": 0.03, "hipD": 0.08, "lean": 14, "yaw": -15, "rf": [0.09, 0, 0.18], "lf": [-0.1, 0, -0.1]}   # the dagger idle's body
sword = dict(BODY1, rd=[0.25, -0.35, 0.9], rr=0.8, rw=norm([0.0, 0.5, 0.87]), re=norm([0.0, -0.87, 0.5]), **GUARD_L)
idle_and_carry("sword", "Sword", sword, "Battle-ready on the dagger idle's body: the sword arm forward at chest height, the blade up and forward, the other arm on guard.",
               carry={"rd": [0.25, -0.921, -0.3], "rr": 1.0, "rw": norm([0.2, -0.3, 0.93]), "re": norm([0.0, -0.95, -0.3])})
hold_upright("sword", "Sword", sword)

# ── STAFF and WAND ──────────────────────────────────────────────────────────────────────────────────────────────────
CASTER = {"hipD": 0.04, "lean": 6, "yaw": -10, "rf": [0.08, 0, 0.1], "lf": [-0.09, 0, -0.06]}
# the staff is a TWO-HANDED weapon (the user, 2026-10-11): both hands on it in the idle, the run and the attack
STAFF2 = {"twoHand": True}
# of 128 two-handed holds scanned, one both hands reach with nothing in the body (second hand 2% of body height off)
staff = dict(CASTER, rd=norm([0.1, -0.3, 0.95]), rr=0.65, rw=norm([0.3, 0.9, 0.3]), re=[0.0, -0.3162, 0.9487], ld=norm([-0.25, -0.55, 0.6]), lr=0.6)
idle_and_carry("staff", "Staff", staff, "The staff held up before the body in both hands, leaning a little to the right and forward.", **STAFF2)
hold_upright("staff", "Staff", staff, **STAFF2)
wand = dict(CASTER, rd=norm([0.35, -0.3, 0.88]), rr=0.8, rw=norm([0.05, 0.85, 0.52]), re=norm([0.0, -0.52, 0.85]), ld=norm([-0.3, -0.45, 0.7]), lr=0.55)
idle_and_carry("wand", "Wand", wand, "The wand held forward and up before the right shoulder; the other hand open and low.")
hold_upright("wand", "Wand", wand)

# ── SPEAR (two hands) ───────────────────────────────────────────────────────────────────────────────────────────────
spear = {"hipD": 0.08, "lean": 12, "rf": [0.14, 0, 0.12], "lf": [-0.14, 0, -0.06], "rd": [0.25, -0.4, 0.5], "rr": 0.75, "ld": [0.3, -0.6, 0.6], "lr": 0.6, "rw": [0, 0.05, 1]}
idle_and_carry("spear", "Spear", spear, "The ready pose its attack thrusts from (keys/spear.json, frame 6): level at the right hip, both hands on it.", twoHand=True, secondGrip=-0.06)
hold_upright("spear", "Spear", spear, twoHand=True, secondGrip=-0.06)

# ── WARHAMMER (two hands) ───────────────────────────────────────────────────────────────────────────────────────────
hammer = {"hipD": 0.06, "lean": 10, "yaw": 10, "rf": [0.11, 0, -0.06], "lf": [-0.11, 0, 0.1], "rd": norm([0.0, -0.35, 0.93]), "rr": 0.6,
          "rw": norm([0.6, 0.75, 0.0]), "re": norm([0.0, 0.3, 0.95]), "ld": [0.3, -0.6, 0.6], "lr": 0.6}
idle_and_carry("warhammer", "Warhammer", hammer, "Held in both hands before the belly, the head up and out to the right (the hold of 24 tried that the hands reach exactly).", twoHand=True)
hold_upright("warhammer", "Warhammer", hammer, twoHand=True)

# ── SCYTHE ──────────────────────────────────────────────────────────────────────────────────────────────────────────
# of 128 two-handed holds scanned, the one both hands reach exactly with nothing in the body; the blade faces left
scythe = dict({"hipD": 0.05, "lean": 8, "yaw": -12, "rf": [0.09, 0, 0.14], "lf": [-0.1, 0, -0.08]}, rd=norm([0.15, -0.45, 0.88]), rr=0.7,
              rw=norm([0.3, 0.9, 0.3]), re=[-0.9535, 0.286, 0.0953], **GUARD_L)
# held in TWO hands in the idle and the run (the user, 2026-10-11); the right fist is near the shaft's lower end, so
# the left hand takes it higher up (secondGrip toward the blade)
SCYTHE2 = {"twoHand": True, "secondGrip": 0.12}
idle_and_carry("scythe", "Scythe", scythe, "The scythe held up before the body in both hands, leaning a little to the right.", **SCYTHE2)
hold_upright("scythe", "Scythe", scythe, **SCYTHE2)

# ── BOW (RIGHT hand since 2026-10-11, the user: "the right hand must hold the bow's handle ... the left hand must hold
# the bow's string") — its grip was mirrored into the right hand and its attack plays mirrored ─────────────────────────
# Held low and level in the middle of the body (the user, 2026-10-11: "move the bow to the center of the character
# ... make it face slightly downward"): its middle on the body's centre line, its face tilted 20 degrees down, so the
# string side (the model's +x) is toward the archer and a little up; the other hand at the string ready to draw — in
# the idle AND the run, so the carry holds both arms. The model has no string: its limb tips sit 0.12 of the bow's
# length behind the handle, so the string hand goes 0.14 of body height from the handle toward the string side.
bsd, bsr = reach(LS, [-0.073, 0.424, 0.165])   # puts the left fist on the string (measured)
bw_d, bw_r = reach(RS, [0.0, 0.36, 0.22])
bow = {"hipD": 0.05, "lean": 6, "yaw": -25, "rf": [0.09, 0, 0.14], "lf": [-0.1, 0, -0.08], "ld": bsd, "lr": bsr,
       "rd": bw_d, "rr": bw_r, "rp": [0.6, -0.6, -0.5], "rw": norm([1, 0, 0.1]), "re": norm([0, 0.34, -0.94])}
idle_and_carry("bow", "Bow", bow, "Side-on, the bow low and level in the middle of the body in the right hand, its face tilted a little down, the left hand at the string ready to draw.")
hold_upright("bow", "Bow", bow)

# ── SNIPER (a rifle, two hands) ─────────────────────────────────────────────────────────────────────────────────────
RIFLE = {"rifle": True, "twoHand": True, "secondGrip": 0.1}
sniper = {"hipD": 0.06, "lean": 10, "rf": [0.12, 0, 0.1], "lf": [-0.12, 0, -0.08], "rd": norm([0.25, -0.5, 0.5]), "rr": 0.55,
          "rw": norm([-0.2, -0.25, 0.95]), "re": [0, 1, 0], "ld": [0.35, -0.3, 1], "lr": 0.8}
idle_and_carry("sniper", "Sniper", sniper, "Low ready: the stock by the right hip, the muzzle forward and down, the left hand under the barrel.", **RIFLE)
hold_upright("sniper", "Sniper", sniper, **RIFLE)

# ── SHURIKEN ────────────────────────────────────────────────────────────────────────────────────────────────────────
shuriken = dict({"hipD": 0.06, "lean": 8, "yaw": -15, "rf": [0.09, 0, 0.14], "lf": [-0.1, 0, -0.08]}, rd=norm([0.92, -0.25, 0.3]), rr=0.92,
                rw=[0.0, 1.0, 0.0], re=[0.0, 0.0, 1.0], **HANG_L)
idle_and_carry("shuriken", "Shuriken", shuriken, "Held out at the right side at its middle, upright beside the body; the other arm hangs at the side, as it does in the run (the user, 2026-10-11).")
hold_upright("shuriken", "Shuriken", shuriken)

# ── FIST (nothing in the hand) ──────────────────────────────────────────────────────────────────────────────────────
fist = {"hipD": 0.04, "lean": 5, "yaw": -10, "rf": [0.08, 0, 0.06], "lf": [-0.09, 0, -0.06], "rd": [0.1, -0.2, 0.6], "rr": 0.5, "ld": [-0.2, -0.1, 0.6], "lr": 0.55}
idle_and_carry("fist", "Fist", fist, "The guard its punch starts from (keys/fist.json, frame 6): both fists up before the chest.")

# ── PUT-AWAY POSES ──────────────────────────────────────────────────────────────────────────────────────────────────
# Every put-away is blended joint by joint through still poses (paths/pose_chain_blend.cs); CHAINS below lists each
# weapon's stops as (pose clip, frame at 60 per second, the move stops there). The clip is as long as the game plays
# it: 0.2 s + 0.006 s per point of weight (weight 90 = 44 frames, weight 60 = 34), so the video shows the game's speed.
# The take-out is the put-away reversed (reverse-clip.cs). HAND is the frame the hand lets go.
def pose(w, W, name, key, note, **top):
    table("%s_putaway_%s" % (w, name.lower()), "%sPutAwayPose%s" % (W, name), still(dict(STAND, **key)), note, **top)
    return "%sPutAwayPose%s" % (W, name)
CHAINS, HAND, SPIN = {}, {}, {}
HANG = "AxeSheathe"   # its LAST pose is the standing body with both arms hanging

# The poses the greatsword's put-away already proved for a weapon gripped like a sword in the right hand
# (keys/greatsword_putaway_*.json): upright behind the right shoulder, tipped over to the left behind the back, and
# level across the upper back.
GS = {"Back": dict(rd=[0.634, -0.588, -0.502], rr=0.806, rw=[0.01, 0.95, -0.3], re=[0.99, -0.05, -0.13], rp=[0, 0, -1]),
      "Diagonal": dict(rd=[0.3541, -0.5673, -0.7435], rr=0.8333, rw=[-0.55, 0.81, -0.17], re=[0.82, 0.57, 0.04], rp=[-0.5, 0, -1]),
      "Level": dict(rd=[0.6684, 0.0304, -0.7432], rr=0.9785, rw=[-1, 0.0, -0.06], re=[0.08, 0.91, -0.41], rp=[0.5, 0, -1])}
def gs(w, W, name): return pose(w, W, name, dict(GS[name], **HANG_L), "A still pose of the %s put-away, taken from the greatsword's (same grip, same hand)." % W)

# SWORD (the user, 2026-10-10): the left hand holds the left waist as if a sheath hung there; the right hand points the
# sword at the enemy with the body turned left and the head to the front; the tip is brought to the sheath's mouth,
# held a moment, and the sword slides in.
# What the body allows (all measured): the sword is 0.65 of body height from fist to tip and the arm 0.28, so the fist
# cannot hold the tip exactly at the left waist with the blade on the sheath's line. The sheath's line runs back and
# down along the left hip (D_HOME); with the sword home the hilt is at the left waist (HOME_WRIST). The furthest back
# up that line the fist reaches is 0.3 of body height — that is the pose "at the mouth": the blade already lies
# along the left hip through the left hand, 46% of it still to go in. Then the fist slides those 0.3 down the line.
# Every pose uses the roll of the blade that keeps the forearm's twist the same (the poses were chosen by comparing
# their arm joints): blended between poses with opposite twists, the blade swung through the head and the ground.
HOME_WRIST, D_HOME = [-0.18, 0.42, 0.06], norm([-0.2, -0.5, -0.84])
SLIDE = 0.3
LEFT_AT_MOUTH = [-0.24, 0.39, -0.01]                 # the left wrist, at the left waist where the blade passes
def sword_pose(name, twist, right, note):
    ld, lr = reach(twisted(LS, twist), LEFT_AT_MOUTH)
    return pose("sword", "Sword", name, dict(right, twist=twist, look=twist, ld=ld, lr=lr, lp=[-0.7, 0, -0.7]), note)
def on_line(name, back, note):
    d, r = reach(twisted(RS, -60), [HOME_WRIST[i] - back * D_HOME[i] for i in range(3)])
    return sword_pose(name, -60, dict(rd=d, rr=r, rw=D_HOME, re=[-0.3859, -0.7495, 0.538]), note)
s1 = sword_pose("Present", -55, dict(rd=norm([0.1, -0.05, 1.0]), rr=1.0, rw=[0, 0, 1], re=[0.5, 0.866, 0]),
                "The sword pointed at the enemy, the body turned 55 degrees left over the hips, the head to the front, the left hand at the left waist.")
s2 = on_line("Mouth", SLIDE, "The blade laid along the left hip through the left hand, the fist as far back up the sheath's line as it reaches.")
s3 = on_line("Home", 0, "The hilt at the left waist, the blade running back and down along the left hip: the sword is home.")
CHAINS["Sword"] = [("SwordHoldUpright", 0, True), (s1, 9, True), (s1, 13, True), (s2, 22, True), (s2, 25, True),
                   (on_line("SlideA", SLIDE * 2 / 3, "The sword sliding in: a third of the way."), 28, False), (on_line("SlideB", SLIDE / 3, "The sword sliding in: two thirds of the way."), 31, False),
                   (s3, 35, True), (s3, 37, True), (HANG, 44, True)]
HAND["Sword"] = 35

# STAFF and WAND (the user): the right arm rises out to the right, and the weapon moves to the back by itself (the
# game's Blink Step effect is played there when they are wired in). The clip only raises the arm, holds and lowers it.
for w, W in (("staff", "Staff"), ("wand", "Wand")):
    o = pose(w, W, "Out", dict(rd=[1, 0, 0.05], rr=1.0, rp=[0, -1, 0], rw=[0, 1, 0], re=[1, 0, 0], **HANG_L), "The right arm straight out to the right, the %s upright in the fist." % w)
    # the arm is up by frame 10 and HOLDS until the weapon goes at 24 — a clear pause before it moves (the user, 2026-10-11)
    CHAINS[W] = [(W + "HoldUpright", 0, True), (o, 10, True), (o, 28, True), (HANG, 44, True)]
    HAND[W] = 24

# SPEAR and SCYTHE (the user): the arm rises to the right, the weapon spins, then the hand carries it to the back.
# Turned about the fist, a weapon this long goes through the head or the ground in every hold the arm reaches (36
# holds measured), so it turns about its own MIDDLE, in an upright wheel beside the right arm: the fist up and out to
# the right, the forearm forward. Then upright behind the right shoulder, then tipped over behind the back.
for w, W, top in (("spear", "Spear", {}), ("scythe", "Scythe", {})):
    wd, wr = reach(RS, [0.27, 0.74, 0.14])
    wh = pose(w, W, "Wheel", dict(rd=wd, rr=wr, rp=[0.6, -0.8, 0], rw=[0, 0.985, 0.174], re=[0, -0.174, 0.985], **HANG_L),
              "The hold the %s spins in: the fist up and out to the right, the weapon upright before it." % w)
    CHAINS[W] = [(W + "HoldUpright", 0, True), (wh, 7, True), (wh, 24, True), (gs(w, W, "Back"), 30, False), (gs(w, W, "Diagonal"), 36, True), (W + "PutAwayPoseDiagonal", 37, True), (HANG, 44, True)]
    HAND[W] = 36
    SPIN[W] = dict(start=8, end=23, turns=1, lift=0.3, middle=True)

# WARHAMMER (the user: like the greatsword, level across the back).
# The handle reaches 0.64 of body height below the fist, so the greatsword's low path does not fit (upright behind the
# hip, the handle end was 25% of body height under the ground; kept low and carried round, it went through the legs):
# the hammer is lifted upright beside the right shoulder, tipped over to the left behind the neck and laid level.
ud, ur = reach(RS, [0.28, 0.70, 0.0]); td, tr = reach(RS, [0.28, 0.68, -0.15])
hm = [pose("warhammer", "Warhammer", "Up", dict(rd=ud, rr=ur, rp=[0.7, -0.5, -0.5], rw=norm([0.1, 0.97, -0.2]), re=[0, 0.202, 0.979], **HANG_L), "The hammer lifted upright, the fist up beside the right shoulder."),
      pose("warhammer", "Warhammer", "Tip", dict(rd=td, rr=tr, rp=[0.5, 0, -1], rw=norm([-0.6, 0.75, -0.25]), re=[0.697, 0.654, 0.291], **HANG_L), "The hammer tipping over to the left behind the neck.")]
fd, fr = reach(RS, [0.2, 0.62, 0.15])
lift = pose("warhammer", "Warhammer", "Lift", dict(rd=fd, rr=fr, rw=norm([0.45, 0.88, -0.1]), re=[0.218, 0, 0.976], **HANG_L), "The hammer lifted before the chest in the right hand, its handle end clear of the legs and the ground.")
CHAINS["Warhammer"] = [("WarhammerHoldUpright", 0, True), (lift, 6, False), (hm[0], 13, False), (hm[1], 22, False), (gs("warhammer", "Warhammer", "Level"), 30, True), ("WarhammerPutAwayPoseLevel", 34, True), (HANG, 44, True)]
HAND["Warhammer"] = 30

# BOW (the user, 2026-10-11): on the LOWER back like the katar, flat against the body, and the right hand — the bow
# hand — puts it there itself. Its front points straight down (8 degrees to the right); the hold of 32 tried that the
# right hand reaches within 20 degrees. "hug": the capsule estimate may not push it off the back (3-4% of body height).
bd, br = reach(RS, [0.05, 0.42, -0.13])
bs = pose("bow", "Bow", "LowerBack", dict(rd=bd, rr=br, rw=norm([-0.98, 0.2, 0]), re=norm([-0.2, -0.98, 0]), **HANG_L),
          "The bow flat across the lower back in the right hand, its front down.", hug=True)
# On the way (the user, 2026-10-11: "make the right arms extend to the right, same with how wand and staff raise the
# right arm, then put the bow at the back, so no overlapping"): the right arm goes straight out to the side with the
# bow level, pointing forward, held a moment, then the hand takes it behind the hip and lays it across the lower back.
# Nothing of it enters the body in any pose; its far end dips 0.9% of body height under the ground for one pose
# behind the hip, measured.
def roll8(t, j, n=8):
    t = norm(t); u = norm([t[1] * 0 - t[2] * 1, t[2] * 0 - t[0] * 0, t[0] * 1 - t[1] * 0]) if abs(t[1]) < 0.9 else None
    v = norm([t[1] * u[2] - t[2] * u[1], t[2] * u[0] - t[0] * u[2], t[0] * u[1] - t[1] * u[0]])
    return norm([u[k] * math.cos(2 * math.pi * j / n) + v[k] * math.sin(2 * math.pi * j / n) for k in range(3)])
def bow_mid(name, rd, rr, rp, rw, j, note, **top):
    return pose("bow", "Bow", name, dict(rd=rd, rr=rr, rw=norm(rw), re=roll8(rw, j), **({"rp": rp} if rp else {}), **HANG_L), note, **top)
bo = bow_mid("Out", [1, 0, 0.05], 1.0, [0, -1, 0], [0, 0, 1], 1, "The right arm straight out to the right, the bow level in the fist, pointing forward.")
b3d, b3r = reach(RS, [0.12, 0.42, -0.14])
b3 = bow_mid("Behind", b3d, b3r, None, [-0.9, 0.15, -0.4], 4, "The bow behind the right hip, pointing back and to the left.", hug=True)
CHAINS["Bow"] = [("BowHoldUpright", 0, True), (bo, 9, True), (bo, 11, True), (b3, 17, False), (bs, 21, True), (bs, 23, True), (HANG, 34, True)]
HAND["Bow"] = 21

# SNIPER (the user, 2026-10-11): on the back DIAGONALLY, the stock up at the top right, the muzzle down at the lower
# left, put there with the elbow going up. Held at its trigger grip, the rifle's barrel runs along the hand: the right
# hand goes up behind the right shoulder blade with the elbow raised high and the hand turned down, which lays the
# barrel 32 degrees below level across the back (muzzle 4% of body height above the ground, nothing in the body,
# 2-7% off the back) — of 192 holds tried, the one the hand reaches within 1 degree.
sd, sr = reach(RS, [0.22, 0.56, -0.14])
sn = pose("sniper", "Sniper", "BackDiagonal", dict(rd=sd, rr=sr, rp=[0.5, 0.6, -0.6], rw=norm([-0.83, -0.55, -0.1]), re=[0.5457, -0.8354, 0.0657], **HANG_L),
          "The rifle diagonal across the back: stock up by the right shoulder, muzzle down at the left hip.", rifle=True, hug=True)
# On the way the rifle stays level: swung out to the right side (forward-out, then straight out), round behind the
# right hip pointing back, then the hand goes up behind the shoulder blade. Swung straight there, or over the
# shoulder, its 0.93-long barrel went 30-57% of body height into the ground (24 ways tried). This way it never
# touches the ground; up to 5% of it passes the hips and the back in poses 4-12, measured.
def rifle_mid(name, wrist, rw, j, rp, note):
    d, r = reach(RS, wrist); return pose("sniper", "Sniper", name, dict(rd=d, rr=r, rp=rp, rw=norm(rw), re=roll8(rw, j, 4), **HANG_L), note, rifle=True)
s1 = rifle_mid("Forward", [0.26, 0.42, 0.04], [0.8, -0.15, 0.55], 3, [0.5, 0, -1], "The rifle swung out to the right, the muzzle forward and out.")
s2 = rifle_mid("Out", [0.26, 0.42, 0.04], [1, -0.1, 0], 0, [0.5, 0, -1], "The rifle level beside the right hip, the muzzle straight out to the right.")
s3 = rifle_mid("Behind", [0.24, 0.48, -0.12], [0, -0.3, -0.95], 1, [0.5, 0.6, -0.6], "The rifle behind the right hip, the muzzle pointing back.")
CHAINS["Sniper"] = [("SniperHoldUpright", 0, True), (s1, 5, False), (s2, 9, False), (s3, 14, False), (sn, 20, True), (sn, 22, True), (HANG, 34, True)]
HAND["Sniper"] = 20

# SHURIKEN: the hand carries it behind the back and leaves it flat against the middle of the back, held at its hub.
hd2, hr2 = reach(RS, [0.05, 0.5, -0.15])
sh = pose("shuriken", "Shuriken", "Back", dict(rd=hd2, rr=hr2, rp=[0.6, 0, -0.8], rw=[1, 0, 0], re=[0, -1, 0], **HANG_L), "The shuriken flat against the middle of the back, the hand on its hub.")
# On the way it stays upright past the right hip and turns flat only behind the body (carried straight there, it went
# through the right thigh).
da, ra = reach(RS, [0.3, 0.5, -0.12]); db, rb = reach(RS, [0.2, 0.5, -0.17])
sha = pose("shuriken", "Shuriken", "Beside", dict(rd=da, rr=ra, rp=[0.6, 0, -0.8], rw=[0, 0, 1], re=[0, -1, 0], **HANG_L), "The shuriken upright beside and behind the right hip.")
shb = pose("shuriken", "Shuriken", "Turning", dict(rd=db, rr=rb, rp=[0.6, 0, -0.8], rw=[0, 1, 0], re=[0.707, 0, 0.707], **HANG_L), "The shuriken half turned toward the back.")
CHAINS["Shuriken"] = [("ShurikenHoldUpright", 0, True), (sha, 8, False), (shb, 14, False), (sh, 20, True), (sh, 24, True), (HANG, 44, True)]
HAND["Shuriken"] = 20

for name, t in tables.items():
    json.dump(t, open(os.path.join(SP, "keys", name + ".json"), "w"))
print("ok", len(tables), "tables")
# python paths/stance_sets.py <folder>: also writes one filled pose_chain_blend.cs per weapon there (<Weapon>Sheathe.cs)
# and prints each weapon's handover frame and spin — what its stance library row and its videos need.
# A stop is (pose clip, frame, the move stops there[, the time in that clip in seconds; -1 = its last pose]).
import sys
if len(sys.argv) > 1:
    src = open(os.path.join(SP, "paths", "pose_chain_blend.cs"), encoding="utf-8").read()
    for W, chain in CHAINS.items():
        at = lambda st: st[3] if len(st) > 3 else (-1 if st[0] == HANG else 0)
        stops = ", ".join('("%s", %sf, %d, %s)' % (st[0], round(at(st), 5), st[1], "true" if st[2] else "false") for st in chain)
        open(os.path.join(sys.argv[1], W + "Sheathe.cs"), "w", encoding="utf-8").write(src.replace("__OUT__", W + "Sheathe").replace("__CHAIN__", stops))
        print(W, "frames", chain[-1][1], "hand lets go at", HAND[W], "take-out's handover", chain[-1][1] - HAND[W], "spin", SPIN.get(W, "none"))
