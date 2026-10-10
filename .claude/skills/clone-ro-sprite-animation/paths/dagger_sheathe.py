"""The dagger's put-away, first half (2026-10-10, the user's request): from the combat idle the right arm rises straight
out to the right at shoulder height, palm up and blade forward, and HOLDS there while the dagger spins. The spin itself
is not in the clip: the game turns the dagger flat over the fist while the arm is held (the Dagger's stance library
row: spinStartFrame / spinEndFrame / spinTurns / spinLift, drawn by WeaponSocket). The second half — down to the hip,
where the dagger is left slanted BACKWARD under the arm, and the arm hanging — is blended by
paths/dagger_sheathe_blend.cs from this clip's last pose and the still pose keys/dagger_putaway_handover.json.
Run from the skill's folder: python paths/dagger_sheathe.py"""
import json, math
def norm(v): l = math.sqrt(sum(c*c for c in v)); return [round(c/l, 3) for c in v]
def lerp(a, b, t): return [a[i] + (b[i]-a[i])*t for i in range(len(a))]
ARM = 0.278
idle = json.load(open("keys/dagger_idle.json"))["keys"][0]
LS, LHE = [-0.0909, 0.5762, 0.0061], [-0.1530, 0.3196, 0.0201]   # upright left shoulder and hanging left wrist (measured)
v = [LHE[i] - LS[i] for i in range(3)]; L = math.sqrt(sum(c*c for c in v)); LD, LR = norm(v), round(min(L/ARM, 0.97), 3)
OUT = {"rd": [1.0, 0.0, 0.0], "rr": 1.0, "rp": [0.0, -1.0, 0.0]}     # the arm straight out to the right, elbow under
# held from frame 6 to 25 (30 per second): blade forward and a little up — as far as the wrist turns (a wish of
# straight forward or lower ends in this same pose) — with its edge outward along the arm. That turns the palm up, 26
# degrees off level, so the game's spin (about the palm's own facing) is close to level with the ground.
HOLD = [(6, norm([0, 0.38, 0.92])), (25, norm([0, 0.38, 0.92]))]
keys = [dict(idle, f=0)]
for f, blade in HOLD:
    u = min(1.0, f / 6.0)
    keys.append({"f": f, "hipF": round(idle["hipF"] * (1 - u), 3), "hipD": round(idle["hipD"] * (1 - u), 3), "lean": round(idle["lean"] * (1 - u), 2),
                 "yaw": round(idle["yaw"] * (1 - u), 2), "rf": idle["rf"], "lf": idle["lf"], "rw": blade, "re": [1.0, 0.0, 0.0], "ld": LD, "lr": LR, **OUT})
json.dump({"clip": "DaggerFlourish", "note": "First half of DaggerSheathe (the arm held out for the spin); see paths/dagger_sheathe.py.", "stepLift": False, "smooth": True, "keys": keys},
          open("keys/dagger_flourish.json", "w"))
# the still pose the dagger is let go in: hand in front of the hip, elbow back, blade slanted down and BACK under the
# arm — reach-tested: the blade points (0.24, -0.44, -0.87), 63 degrees back from straight down, 15 off the plan
RS = [0.0978, 0.5517, 0.0126]; W = [0.15, 0.40, 0.08]
v = [W[i] - RS[i] for i in range(3)]; L = math.sqrt(sum(c*c for c in v))
k = {"hipF": 0, "hipD": 0, "lean": 0, "yaw": 0, "rf": [0.09, 0, -0.04], "lf": [-0.1, 0, -0.1], "rd": norm(v), "rr": round(min(L/ARM, 0.97), 3),
     "rw": norm([0.0, -0.55, -0.83]), "rp": [0.0, -0.4, -0.9], "ld": LD, "lr": LR}
json.dump({"clip": "DaggerPutAwayPoseHandover", "note": "The still pose the dagger put-away hands the dagger over in; see paths/dagger_sheathe.py.",
           "stepLift": False, "keys": [dict(k, f=0), dict(k, f=4)]}, open("keys/dagger_putaway_handover.json", "w"))
print("ok", len(keys), "flourish keys")
