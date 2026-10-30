"""TOWN PLANS — each town's plan, and the top-down picture drawn from it.

Writes data/towns/<town>.json (the plan a town piece is built from) and data/towns/<town>.html (the picture of exactly
that plan) for every town in PLANS, so the plan and its picture can never disagree. Run it again after changing a plan;
the same plan always gives the same file.

    python tools/town-plan/build_town_plan.py

Units are world units, town-local: x east, z north, the town's middle at 0, 0. Our person is 0.327 tall. A town is
the same size as the hand-made Lastline (49 x 62), the flat ground the world layout gives every town.

A plan here only STARTS a town: its first draft is written once, when the town has no plan file yet. From then on the
plan file is the town, edited by hand in the town editor (tools/town-editor), and this tool never writes over it.
"""
import json, math, os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "data", "towns")
HALF_X, HALF_Z = 24.5, 31.0          # the town's ground, as Lastline's
WALL_LINE = 2.5                      # how far inside the edge the wall runs, as Lastline's (measured: x +-22, z +-28.5)
WALL_DEEP = 0.61                     # the kit's wall piece, front to back
WALL_IN = WALL_LINE + WALL_DEEP / 2 + 0.04   # how far inside the edge its inner face stands
PERSON = 0.327
GATE = 3.4                           # a gate's opening between its two towers, as Lastline's
TOWER_EVERY = 11.0                   # a small tower about this often along a side of a first draft's wall

OUTDOOR = "Assets/1_Authored/Prefabs/Props/Outdoor/"
NATURE = "Assets/1_Authored/Nature/Idyllic Fantasy Nature/Prefabs/"
NATURE_SCALE = 0.327 / 1.9           # our person / the nature pack's person (read from the pack's walking body)
# Every piece a plan may use: its prefab, and its footprint (x wide, z deep) at the scale it is placed, measured from
# its renderers in the editor on 2026-10-28. Buildings and town props keep the kit's own scale, as Lastline's do;
# nature pieces take the world's scale against a person.
KIT = {
    "House_A": (OUTDOOR + "House_A.prefab", 1.49, 2.00, "house"),
    "House_B": (OUTDOOR + "House_B.prefab", 1.66, 1.71, "house"),
    "House_C": (OUTDOOR + "House_C.prefab", 2.97, 2.99, "house"),
    "House_D": (OUTDOOR + "House_D.prefab", 3.40, 1.84, "house"),
    "House1": (OUTDOOR + "House1.prefab", 2.46, 2.25, "house"),
    "House2": (OUTDOOR + "House2.prefab", 2.75, 2.30, "house"),
    "House3": (OUTDOOR + "House3.prefab", 1.17, 2.31, "house"),
    "Tavern": (OUTDOOR + "Tavern_LOD0.prefab", 9.50, 7.10, "landmark"),
    "Fountain": (OUTDOOR + "Central_Fountain.prefab", 2.00, 2.00, "landmark"),
    "Statue": (OUTDOOR + "Hero_Statue.prefab", 1.44, 1.30, "landmark"),
    "Cart": (OUTDOOR + "cart4_LOD0.prefab", 0.84, 2.16, "market"),
    "Handcart": (OUTDOOR + "cart2_LOD0.prefab", 0.61, 1.57, "market"),
    "Barrel": (OUTDOOR + "storage_barrel.prefab", 0.29, 0.29, "market"),
    "Basket": (OUTDOOR + "storage_basket.prefab", 0.26, 0.24, "market"),
    "Sack": (OUTDOOR + "storage_bag.prefab", 0.29, 0.19, "market"),
    "Blossom": (NATURE + "BlossomTree_0{n}.prefab", 1.10, 1.10, "tree"),
    "PinkWillow": (NATURE + "WillowTree_0{n}_Pink.prefab", 0.95, 0.95, "tree"),
    "BlueBroadleaf": (NATURE + "BroadleafTree_0{n}_Blue.prefab", 1.10, 1.10, "tree"),
    "GreenBroadleaf": (NATURE + "BroadleafTree_0{n}_Green.prefab", 1.10, 1.10, "tree"),
    "PurpleBroadleaf": (NATURE + "BroadleafTree_0{n}_Purple.prefab", 1.10, 1.10, "tree"),
    "RedBroadleaf": (NATURE + "BroadleafTree_0{n}_Red.prefab", 1.10, 1.10, "tree"),
    "BlueWillow": (NATURE + "WillowTree_0{n}_Blue.prefab", 0.95, 0.95, "tree"),
    "GreenWillow": (NATURE + "WillowTree_0{n}_Green.prefab", 0.95, 0.95, "tree"),
    "PurpleWillow": (NATURE + "WillowTree_0{n}_Purple.prefab", 0.95, 0.95, "tree"),
    "RedWillow": (NATURE + "WillowTree_0{n}_Red.prefab", 0.95, 0.95, "tree"),
    "Fir": (NATURE + "Fir_0{n}.prefab", 0.60, 0.60, "tree"),
    # Lastline's own trees, at their prefab's 0.3.
    "Pine_tree1": ("Assets/1_Authored/Prefabs/Props/Trees/Pine_tree1.prefab", 3.52, 3.52, "tree"),
    "Pine_tree3": ("Assets/1_Authored/Prefabs/Props/Trees/Pine_tree3.prefab", 1.52, 1.52, "tree"),
    "Willow_tree2": ("Assets/1_Authored/Prefabs/Props/Trees/Willow_tree2.prefab", 3.80, 3.80, "tree"),
    # The pack's rose-topped bush (its top colour #FF3B59, green below); at the world's scale it stands about knee-to-chest high on a person.
    "RoseBush": (NATURE + "Bush_03_0{n}.prefab", 0.36, 0.36, "bush"),
    # The pack's floating leaves: a box 100 x 100 of the pack's units drifting leaves, about 17 x 17 at the world's scale.
    "FloatingLeaves": (NATURE + "FloatingLeafs_Green.prefab", 17.2, 17.2, "effect"),
    # The tavern's own tables and chairs, set out in front of it, at the kit's size (the kit's outdoor pieces are all
    # 0.3 of their models; these indoor ones come at 1 and are placed at 0.3 to match).
    "Table": ("Assets/1_Authored/Prefabs/Props/Indoor/Furniture_table_round.prefab", 0.38, 0.39, "market"),
    "Chair": ("Assets/1_Authored/Prefabs/Props/Indoor/Furniture_chair1.prefab", 0.16, 0.17, "market"),
}
# A piece placed at other than its model's own size.
SCALE = {"Table": 0.3, "Chair": 0.3}
LOOKS = {"pit": "#6b4a2b", "planter": "#6b4a2b", "water": "#5aa7d6", "stone": "#c9c3b8", "path": "#ecdcbc", "court": "#e6cfd3", "grass": "#86b45a", "meadow": "#f0a8c4",
         "dirt": "#b98d5c"}


# ---------------------------------------------------------------- shapes
def arc(cx, cz, r, a0, a1, n=48):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cz + r * math.sin(math.radians(a0 + (a1 - a0) * i / n)))
            for i in range(n + 1)]


def seg_dist(p, a, b):
    (px, pz), (ax, az), (bx, bz) = p, a, b
    dx, dz = bx - ax, bz - az; L2 = dx * dx + dz * dz
    t = 0 if L2 == 0 else max(0, min(1, ((px - ax) * dx + (pz - az) * dz) / L2))
    return math.hypot(px - ax - t * dx, pz - az - t * dz)


def corners(x, z, w, d, rot):
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    out = []
    for ox, oz in ((-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)):
        out.append((x + ox * c + oz * s, z - ox * s + oz * c))     # Unity's turn about y: clockwise seen from above
    return out


def overlap(pa, pb):
    for poly in (pa, pb):
        for i in range(4):
            (x1, z1), (x2, z2) = poly[i], poly[(i + 1) % 4]
            nx, nz = z1 - z2, x2 - x1
            a = [nx * x + nz * z for x, z in pa]; b = [nx * x + nz * z for x, z in pb]
            if max(a) <= min(b) or max(b) <= min(a): return False
    return True


# Which way each building's front door faces in its own model, as a compass bearing (0 = +z north, 90 = +x east), read
# off pictures of every model from all four sides (2026-10-28). A building is turned so its front faces its street.
FRONT = {"House_A": 270.0, "Tavern": 180.0}


def bearing(dx, dz):
    return math.degrees(math.atan2(dx, dz)) % 360.0


def rounded_rect(x0, z0, x1, z1, r, n=10):
    """A closed street along a rectangle with round corners, as a list of points."""
    pts = []
    for cx, cz, a0 in ((x1 - r, z1 - r, 0), (x0 + r, z1 - r, 90), (x0 + r, z0 + r, 180), (x1 - r, z0 + r, 270)):
        for i in range(n + 1):
            a = math.radians(a0 + 90.0 * i / n)
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    pts.append(pts[0])
    return pts


BRIDGE_ROCK_LENGTH, BRIDGE_ROCK_WIDTH = 12.31, 6.54     # the stone bridge's model, end to end and side to side (measured)
BRIDGE_LAND = 1.0                                        # how far each end of a bridge stands onto dry ground


def bridge(P, canal, near):
    """A stone bridge straight across a canal at the point of it nearest `near`: square to the water's flow there, as long
    as the water is wide plus BRIDGE_LAND onto each bank, the model scaled to that length."""
    c = P["canals"][canal]; pts = c["points"]
    best = None
    for a, b in zip(pts, pts[1:]):
        dx, dz = b[0] - a[0], b[1] - a[1]; L2 = dx * dx + dz * dz
        t = max(0.0, min(1.0, ((near[0] - a[0]) * dx + (near[1] - a[1]) * dz) / L2))
        q = (a[0] + dx * t, a[1] + dz * t); d = math.dist(q, near)
        if best is None or d < best[0]: best = (d, q, (dx, dz))
    _, q, tan = best
    length = c["width"] + 2 * BRIDGE_LAND
    scale = length / BRIDGE_ROCK_LENGTH
    P["bridges"].append({"kind": "BridgeRock", "x": round(q[0], 2), "z": round(q[1], 2),
                         "bearing": round((bearing(*tan) + 90.0) % 360.0, 1), "length": round(length, 2),
                         "width": round(BRIDGE_ROCK_WIDTH * scale, 2), "scale": round(scale, 3), "canal": canal})


def mound(P, b, away, length, width):
    L = math.hypot(*away)
    P["mounds"].append({"x": b["x"], "z": b["z"], "dir": [round(away[0] / L, 3), round(away[1] / L, 3)],
                        "length": length, "width": width})


def ground_look(P, x, z):
    """What the ground is at a point, as the town builder paints it: the lawn outside the wall, then each area in its
    order, then the streets."""
    if abs(x) > HALF_X - WALL_LINE or abs(z) > HALF_Z - WALL_LINE: return "grass"
    look = "grass"
    for a in P["areas"]:
        if "circle" in a and math.hypot(x - a["circle"][0], z - a["circle"][1]) <= a["circle"][2]: look = a["look"]
        elif "rect" in a and a["rect"][0] <= x <= a["rect"][2] and a["rect"][1] <= z <= a["rect"][3]: look = a["look"]
    for st in P["streets"]:
        pts = st["points"]
        if min(seg_dist((x, z), a, b) for a, b in zip(pts, pts[1:])) <= st["width"] / 2: look = "path"
    for a in P["areas"]:
        if a["look"] == "court" and "circle" in a and math.hypot(x - a["circle"][0], z - a["circle"][1]) <= a["circle"][2]: look = "court"
        elif a["look"] == "court" and "rect" in a and a["rect"][0] <= x <= a["rect"][2] and a["rect"][1] <= z <= a["rect"][3]: look = "court"
    return look


class Town:
    """A plan being laid out: every footprint placed so far, so nothing is put where something already stands."""

    def __init__(self, P):
        self.P = P
        self.taken = []

    def low(self, x, z, pad=0.0):
        """Is the point on a street, the court, a yard or past the wall: somewhere a building never stands."""
        if abs(x) > HALF_X - WALL_IN - pad or abs(z) > HALF_Z - WALL_IN - pad: return True
        for s in self.P["streets"] + self.P.get("canals", []):
            pts = s["points"]
            if min(seg_dist((x, z), a, b) for a, b in zip(pts, pts[1:])) < s["width"] / 2 + pad: return True
        for a in self.P["areas"]:
            if a["look"] not in ("court", "dirt", "street"): continue
            if "circle" in a and math.hypot(x - a["circle"][0], z - a["circle"][1]) < a["circle"][2] + pad: return True
            if "rect" in a and a["rect"][0] - pad <= x <= a["rect"][2] + pad and a["rect"][1] - pad <= z <= a["rect"][3] + pad: return True
        return False

    def fits(self, poly, pad=0.15, ground=True):
        if ground:
            xs = [x for x, _ in poly]; zs = [z for _, z in poly]
            samples = poly + [((poly[0][0] + poly[2][0]) / 2, (poly[0][1] + poly[2][1]) / 2)]
            samples += [((poly[i][0] + poly[(i + 1) % 4][0]) / 2, (poly[i][1] + poly[(i + 1) % 4][1]) / 2) for i in range(4)]
            if any(self.low(x, z, pad) for x, z in samples): return False
        return not any(overlap(poly, o) for o in self.taken)

    def put(self, kind, x, z, facing=0.0, n=1, ground=True, solid=True):
        """Place a piece turned so its front faces `facing` (a compass bearing). False if it would not fit."""
        path, w, d, role = KIT[kind]
        rot = (facing - FRONT.get(kind, 0.0)) % 360.0
        poly = corners(x, z, w, d, rot) if role not in ("tree", "bush") else corners(x, z, 0.3, 0.3, 0)
        if solid and not self.fits(poly, ground=ground): return False
        self.P["pieces"].append({"kind": kind, "variant": n, "x": round(x, 2), "z": round(z, 2), "rot": round(rot, 1)})
        if solid: self.taken.append(poly if role not in ("tree", "bush") else corners(x, z, 0.6, 0.6, 0))
        return True

    def row(self, pts, side, setback, kinds, gap=0.35, start=0.6, end=0.6, seed=1):
        """HOUSES SIDE BY SIDE ALONG A STREET, fronts on the street, as a real street is built: walking the street's
        line, each next house stands `setback` back from the street's edge on `side` (+1 left of the line's direction,
        -1 right), its front turned to face the street, with a narrow gap to its neighbour. A spot taken by a crossing
        street or a building already there is stepped past, half a unit at a time."""
        segs = [(a, b, math.dist(a, b)) for a, b in zip(pts, pts[1:]) if math.dist(a, b) > 1e-6]
        total = sum(L for _, _, L in segs)

        def at(t):
            for a, b, L in segs:
                if t <= L:
                    u = t / L
                    return (a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u), ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
                t -= L
            a, b, L = segs[-1]
            return b, ((b[0] - a[0]) / L, (b[1] - a[1]) / L)

        t, k, placed = start, seed, 0
        while t < total - end:
            kind = kinds[k % len(kinds)]
            path, w, d, role = KIT[kind]
            p, tan = at(t + w / 2)
            nrm = (-tan[1] * side, tan[0] * side)                      # from the street toward the houses
            off = setback + d / 2
            cx, cz = p[0] + nrm[0] * off, p[1] + nrm[1] * off
            facing = bearing(-nrm[0], -nrm[1])
            if self.put(kind, cx, cz, facing):
                t += w + gap; k += 1; placed += 1
            else:
                t += 0.5
        return placed


# ---------------------------------------------------------------- PARLEY (Sword, Blossom Wind), planned 2026-10-28
# A town built along water. The Petal Stream comes out from under a grassy bank in the east and winds south-west across
# the whole town to slip under another bank in the west — at each end a stone bridge stands over it and the town's ground
# rises behind the bridge into a mound over the water, planted with blossoms, so the stream runs on under the earth;
# parting round an island — the Duelist's Ring, the sword town's fighting circle, reached by two bridges — before it
# leaves under the west wall. Nothing runs square: Blossom Road climbs from the south gate on a slant to Petal Square, a
# round square off the town's middle with the sword hero at its heart; from there the High Street crosses the stream on
# the Old Bridge to the north gate, and Sword Lane slants to the east gate. Willow Walk follows the stream's south bank.
# Houses stand side by side along every street, doors on the street. Blossom trees everywhere: an orchard (Petal Grove)
# fills the north-west, both banks of the stream are lined with them, and they ring the square and stand in every garden.
def parley():
    P = {"id": "parley", "name": "Parley", "weapon": "Sword", "theme": "Blossom Wind",
         # The bushes and the floating leaves wear the blossom trees' own pink (the user, 2026-10-28).
         "tint": NATURE + "BlossomTree_01.prefab",
         "gates": [{"side": "south", "x": -6.0, "z": -HALF_Z, "road": "Lastline"},
                   {"side": "north", "x": 8.0, "z": HALF_Z, "road": "Tornel and Gemmara"},
                   {"side": "east", "x": HALF_X, "z": -8.0, "road": ""}],
         "areas": [], "streets": [], "canals": [], "bridges": [], "mounds": [], "pieces": []}
    T = Town(P)
    area = lambda look, label, **shape: P["areas"].append(dict(look=look, label=label, **shape))
    street = lambda name, pts, width: P["streets"].append({"name": name, "width": width, "points": [[round(x, 3), round(z, 3)] for x, z in pts]})
    canal = lambda name, pts, width: P["canals"].append({"name": name, "width": width, "points": [[round(x, 3), round(z, 3)] for x, z in pts]})

    # ---- the ground: a lawn round the outside of the wall, stone paving inside
    area("stone", "", rect=[-HALF_X + WALL_LINE, -HALF_Z + WALL_LINE, HALF_X - WALL_LINE, HALF_Z - WALL_LINE])
    area("grass", "Petal Grove", rect=[-21.6, 13.0, -4.0, 28.1])            # the orchard
    area("grass", "Blossom Garden", rect=[9.0, -28.1, 21.6, -18.5])
    area("meadow", "", circle=[17.6, -25.0, 1.5]); area("meadow", "", circle=[12.2, -21.0, 1.2])
    area("grass", "", circle=[-12.5, -1.0, 5.2])                            # the island's lawn

    # ---- the Petal Stream: out from under its east bank, round the island, under its west bank
    canal("Petal Stream", [(19.0, 18.9), (16.0, 17.5), (9.0, 14.0), (3.0, 11.0), (-3.0, 9.2), (-8.0, 6.5), (-12.5, 5.5)], 3.0)
    canal("", [(-12.5, 5.5), (-17.5, 2.5), (-18.4, -2.5), (-15.5, -7.0), (-12.5, -7.6)], 2.4)     # its west arm
    canal("", [(-12.5, 5.5), (-7.6, 2.0), (-6.8, -3.0), (-9.4, -6.8), (-12.5, -7.6)], 2.4)        # its east arm
    canal("", [(-12.5, -7.6), (-16.5, -10.5), (-19.2, -12.9)], 3.0)
    area("dirt", "Duelist's Ring", circle=[-12.5, -1.0, 3.0])

    # ---- the streets, all on a slant or a curve
    street("Blossom Road", [(-6.0, -HALF_Z), (-5.0, -22.0), (-1.5, -14.0), (2.5, -8.0)], 3.0)
    area("court", "Petal Square", circle=[5.5, -2.5, 6.8])
    street("High Street", [(8.5, 4.0), (7.6, 9.0), (6.4, 13.0), (6.6, 18.0), (8.0, 23.0), (8.0, HALF_Z)], 3.0)
    street("Sword Lane", [(12.0, -4.8), (17.0, -6.6), (HALF_X, -8.0)], 2.6)
    street("Willow Walk", [(-0.8, -3.5), (-3.8, 3.6), (-4.0, 6.0)], 2.2)                     # along the stream's south bank
    street("Island Lane", [(-1.8, -13.0), (-6.0, -12.2), (-10.0, -11.0)], 2.0)                # down towards the island
    street("Orchard Lane", [(3.4, 12.8), (-2.0, 14.2), (-6.0, 17.5), (-9.0, 22.0)], 2.0)       # north of the stream, past the grove
    street("Mill Row", [(9.0, 20.5), (14.0, 22.5), (19.0, 24.5)], 2.0)
    street("Weaver's Lane", [(-4.6, -21.0), (-10.0, -19.5), (-15.0, -21.0), (-19.5, -20.0)], 2.0)

    # ---- the bridges: each straight across the water where it is, square to its flow, one bank to the other, the stone
    # bridge fitted to that crossing — so both its ends stand on dry ground, never in the stream.
    bridge(P, 0, (6.9, 12.4))           # the Old Bridge, carrying the High Street
    bridge(P, 0, (-4.3, 8.6))           # from Willow Walk to Orchard Lane
    bridge(P, 2, (-7.4, 0.6))           # onto the island from the east bank
    bridge(P, 1, (-18.3, -1.0))         # onto the island from the west bank
    bridge(P, 0, (15.2, 17.1))          # the east bank's bridge, the mound rising behind it
    bridge(P, 3, (-17.6, -11.4))        # the west bank's bridge, the mound rising behind it
    # The mounds: the town's ground rising behind each end bridge to the height of its arch, the stream running on under
    # it, then easing back down to the paving — the length it takes to fall, and how wide it spreads.
    mound(P, P["bridges"][4], away=(19.0 - 15.2, 18.9 - 17.1), length=6.0, width=6.5)
    mound(P, P["bridges"][5], away=(-20.0 + 17.6, -13.5 + 11.4), length=4.2, width=5.5)

    # ---- Petal Square: the sword hero in the middle, eight blossoms round its edge, the market on its west side
    T.put("Statue", 5.5, -2.5, 210.0, ground=False)
    # The market: carts on the square's north-west and south sides, where no street comes in, facing the hero.
    for i, b in enumerate((300.0, 330.0, 140.0, 165.0)):
        r = math.radians(b)
        x, z, face = 5.5 + 3.7 * math.sin(r), -2.5 + 3.7 * math.cos(r), (b + 90.0 + (12 if i % 2 else -9)) % 360
        T.put("Cart" if i != 1 else "Handcart", x, z, face, ground=False)
        r = math.radians(face + 90.0)
        for j, k in enumerate(("Barrel", "Basket", "Sack")):
            if (i + j) % 3 == 2: continue
            T.put(k, x + math.sin(r) * (0.6 + 0.1 * j) + math.sin(math.radians(face)) * (0.4 * j - 0.4),
                  z + math.cos(r) * (0.6 + 0.1 * j) + math.cos(math.radians(face)) * (0.4 * j - 0.4), (face + 41 * j) % 360, ground=False)
    def clear_of_streets(x, z, pad):
        return all(min(seg_dist((x, z), a, b) for a, b in zip(st["points"], st["points"][1:])) > st["width"] / 2 + pad for st in P["streets"])
    for i in range(12):
        a = math.radians(15 + 30 * i)
        x, z = 5.5 + 5.7 * math.sin(a), -2.5 + 5.7 * math.cos(a)
        if clear_of_streets(x, z, 0.8): T.put("Blossom", x, z, 0.0, 1 + i % 5, ground=False)
    # The tavern on the square's east side, its door on the square, tables and chairs out in front of it.
    T.put("Tavern", 16.4, 1.6, 250.0)
    for tx, tz in ((12.9, -1.0), (12.6, 0.9), (13.4, 2.6)):
        if not clear_of_streets(tx, tz, 0.6): continue
        T.put("Table", tx, tz, 0.0, ground=False)
        for a in (0, 120, 240):
            T.put("Chair", tx + 0.4 * math.sin(math.radians(a + tx * 10)), tz + 0.4 * math.cos(math.radians(a + tx * 10)), (a + tx * 10 + 180) % 360, ground=False)

    # ---- the Duelist's Ring: barrels round its edge where fighters wait
    for a in (20, 70, 200, 250, 300):
        T.put("Barrel", -12.5 + 3.4 * math.sin(math.radians(a)), -1.0 + 3.4 * math.cos(math.radians(a)), 0.0, ground=False)

    # ---- the houses, street by street, doors on the street
    big = ["House_C", "House2", "House_D", "House1", "House_B", "House2", "House_C", "House1"]
    mid = ["House1", "House_B", "House2", "House_A", "House_D", "House_B", "House1", "House3"]
    small = ["House_B", "House3", "House_A", "House1", "House3", "House_B", "House_A"]
    for side in (1, -1):
        T.row([tuple(p) for p in P["streets"][0]["points"]], side, 1.5 + 1.0, big, seed=3 if side > 0 else 0)
        T.row([tuple(p) for p in P["streets"][1]["points"]], side, 1.5 + 1.0, big, seed=1 if side > 0 else 5)
        T.row([tuple(p) for p in P["streets"][2]["points"]], side, 1.3 + 0.9, mid, seed=2)
        T.row([tuple(p) for p in P["streets"][4]["points"]], side, 1.0 + 0.8, small, seed=4)
        T.row([tuple(p) for p in P["streets"][5]["points"]], side, 1.0 + 0.8, small, seed=6)
        T.row([tuple(p) for p in P["streets"][6]["points"]], side, 1.0 + 0.8, mid, seed=1)
    T.row([tuple(p) for p in P["streets"][3]["points"]], -1, 1.1 + 0.8, mid, seed=3)       # Willow Walk: houses away from the water
    for side in (1, -1):
        T.row([tuple(p) for p in P["streets"][7]["points"]], side, 1.0 + 0.8, small, seed=2 if side > 0 else 5)
        # Houses facing the water along the stream's east reach, behind the trees on its banks.
        T.row([tuple(p) for p in P["canals"][0]["points"]], side, 1.5 + 2.3, mid, seed=4 if side > 0 else 1)
    T.row(arc(5.5, -2.5, 6.8, 200, 440, 40), 1, 0.9, big, seed=2)                         # round the square

    # ---- blossom trees, a lot of them
    for i in range(7):                                                                     # Petal Grove: an orchard in rows
        for j in range(5):
            x, z = -20.0 + i * 2.5 + (j % 2) * 1.25, 14.6 + j * 2.9
            T.put("Blossom", x, z, 0.0, 1 + (i + j) % 5)
    for c in P["canals"]:                                                                  # both banks of the stream
        pts = c["points"]
        for a, b in zip(pts, pts[1:]):
            L = math.dist(a, b); n = max(1, int(L / 2.2))
            for k in range(n):
                t = (k + 0.5) / n
                x, z = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
                nx, nz = -(b[1] - a[1]) / L, (b[0] - a[0]) / L
                for side in (-1, 1):
                    kind = "PinkWillow" if (k + side) % 3 == 0 else "Blossom"
                    T.put(kind, x + nx * side * (c["width"] / 2 + 0.9), z + nz * side * (c["width"] / 2 + 0.9), 0.0, 1 + (k + int(abs(x))) % 4)
    for x, z in ((-1.0, -24.0), (-9.5, -24.0), (-15.0, -22.0), (-19.5, -26.0), (-18.0, -18.5), (-9.0, -18.0), (2.5, -20.5),
                 (5.5, -16.0), (-2.5, -27.5), (20.0, 2.0), (17.0, 6.0), (20.0, 12.0), (14.5, 10.0), (12.0, 27.0), (18.0, 27.5),
                 (2.5, 26.5), (-1.5, 22.5), (2.0, 19.0), (-20.5, 8.5), (-20.5, -6.5), (20.5, -14.0), (11.5, 2.5), (-6.0, -19.0)):
        T.put("Blossom", x, z, 0.0, 1 + int(abs(x * 3 + z)) % 5)
    for m in P["mounds"]:                                                                   # blossoms on the mounds
        for i in range(7):
            u, v = (i % 3 - 1) * 0.3, 0.25 + 0.12 * i
            x = m["x"] + m["dir"][0] * m["length"] * v + m["dir"][1] * m["width"] * u
            z = m["z"] + m["dir"][1] * m["length"] * v - m["dir"][0] * m["width"] * u
            T.put("Blossom", x, z, 0.0, 1 + i % 5, ground=False)
    # The garden: the fountain, willows, bushes round it.
    T.put("Fountain", 15.0, -23.0, 0.0)
    for x, z, n in ((11.0, -26.5, 1), (19.8, -20.0, 2), (11.0, -19.8, 3), (19.8, -27.0, 4)):
        T.put("PinkWillow", x, z, 0.0, n)
    for i in range(12):
        a = math.radians(i * 30)
        T.put("RoseBush", 15.0 + 1.9 * math.cos(a), -23.0 + 1.9 * math.sin(a), 0.0, 1 + i % 2)
    # Bushes by doors, a few, never a line.
    for p in list(P["pieces"]):
        if KIT[p["kind"]][3] != "house": continue
        w, d = KIT[p["kind"]][1], KIT[p["kind"]][2]
        face = math.radians(p["rot"] + FRONT.get(p["kind"], 0.0))
        fx, fz = math.sin(face), math.cos(face); sx, sz = fz, -fx
        s = 1 if int(p["x"] * 7 + p["z"] * 3) % 2 else -1
        if int(abs(p["x"] * 5 - p["z"])) % 3 == 0: continue
        T.put("RoseBush", p["x"] + fx * (d / 2 + 0.25) + sx * s * (w / 2 - 0.2), p["z"] + fz * (d / 2 + 0.25) + sz * s * (w / 2 - 0.2), 0.0, 1 + (s > 0))

    # Blossom drifting over the whole town: six patches of floating leaves, two across and three up, covering it.
    for x in (-12.0, 12.0):
        for z in (-19.0, 0.0, 19.0):
            T.put("FloatingLeaves", x, z, solid=False)
    return P


PLANS = [parley]


# ---------------------------------------------------------------- checks: every piece inside the wall, off the streets, clear of the others
def check(P):
    problems = []
    foot = []
    for p in P["pieces"]:
        path, w, d, role = KIT[p["kind"]]
        if role == "effect": continue
        poly = corners(p["x"], p["z"], w, d, p["rot"]) if role not in ("tree", "bush") else corners(p["x"], p["z"], 0.3, 0.3, 0)
        foot.append((p, poly, role))
        if any(abs(x) > HALF_X - WALL_IN or abs(z) > HALF_Z - WALL_IN for x, z in poly):
            problems.append("%s at %.1f, %.1f stands in the wall" % (p["kind"], p["x"], p["z"]))
        for s in P["streets"]:
            pts = s["points"]
            if any(min(seg_dist(c, a, b) for a, b in zip(pts, pts[1:])) < s["width"] / 2 for c in poly + [(p["x"], p["z"])]):
                problems.append("%s at %.1f, %.1f stands on %s" % (p["kind"], p["x"], p["z"], s["name"])); break
    for b in P.get("bridges", []):
        r = math.radians(b["bearing"])
        for end in (-1, 1):
            ex, ez = b["x"] + math.sin(r) * b["length"] / 2 * end, b["z"] + math.cos(r) * b["length"] / 2 * end
            for c in P.get("canals", []):
                pts = c["points"]
                if min(seg_dist((ex, ez), a, q) for a, q in zip(pts, pts[1:])) < c["width"] / 2 + 0.2:
                    problems.append("the bridge at %.1f, %.1f has an end in the water" % (b["x"], b["z"]))
    for i in range(len(foot)):
        for j in range(i + 1, len(foot)):
            a, b = foot[i], foot[j]
            if a[2] in ("tree", "bush") and b[2] in ("tree", "bush"): continue
            if overlap(a[1], b[1]):
                problems.append("%s at %.1f, %.1f overlaps %s at %.1f, %.1f" % (a[0]["kind"], a[0]["x"], a[0]["z"], b[0]["kind"], b[0]["x"], b[0]["z"]))
    return problems


# ---------------------------------------------------------------- the picture
def draw(P):
    S = 16                                                   # pixels per unit
    W, H = int(2 * HALF_X * S), int(2 * HALF_Z * S)
    X = lambda x: (x + HALF_X) * S
    Z = lambda z: (HALF_Z - z) * S
    s = ['<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 %d %d" data-mocks="%s plan">' % (W, H, W, H, P["name"])]
    s.append('<rect width="%d" height="%d" fill="#86b45a"/>' % (W, H))
    for a in P["areas"]:
        col = LOOKS[a["look"]]
        if "rect" in a:
            x0, z0, x1, z1 = a["rect"]; s.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="%s"/>' % (X(x0), Z(z1), (x1 - x0) * S, (z1 - z0) * S, col))
        else:
            cx, cz, r = a["circle"]; s.append('<circle cx="%.1f" cy="%.1f" r="%.1f" fill="%s"/>' % (X(cx), Z(cz), r * S, col))
    for st in P["streets"]:
        d = " ".join(("M" if i == 0 else "L") + "%.1f %.1f" % (X(x), Z(z)) for i, (x, z) in enumerate(st["points"]))
        s.append('<path d="%s" fill="none" stroke="%s" stroke-width="%.1f" stroke-linejoin="round" stroke-linecap="round"/>' % (d, LOOKS["path"], st["width"] * S))
    for c in P.get("canals", []):                          # water, with a stone edge
        d = " ".join(("M" if i == 0 else "L") + "%.1f %.1f" % (X(x), Z(z)) for i, (x, z) in enumerate(c["points"]))
        s.append('<path d="%s" fill="none" stroke="#7d7468" stroke-width="%.1f" stroke-linejoin="round" stroke-linecap="round"/>' % (d, (c["width"] + 0.3) * S))
        s.append('<path d="%s" fill="none" stroke="%s" stroke-width="%.1f" stroke-linejoin="round" stroke-linecap="round"/>' % (d, LOOKS["water"], c["width"] * S))
    for m in P.get("mounds", []):                          # a mound over the stream, falling away from its bridge
        dx, dz = m["dir"]; nx, nz = dz, -dx
        ring = []
        for i in range(25):
            a = math.pi * i / 24
            u, v = math.cos(a) * m["width"] / 2, math.sin(a) * m["length"]
            ring.append((m["x"] + nx * u + dx * v, m["z"] + nz * u + dz * v))
        s.append('<polygon points="%s" fill="#6f9e45" stroke="#3f6b22" stroke-width="2" stroke-dasharray="5 3"/>' % " ".join("%.1f,%.1f" % (X(x), Z(z)) for x, z in ring))
    for b in P.get("bridges", []):                         # a bridge: its deck, its length across the water
        poly = corners(b["x"], b["z"], b["width"], b["length"], b["bearing"])
        s.append('<polygon points="%s" fill="#a08a6a" stroke="#4e3d27" stroke-width="2"/>' % " ".join("%.1f,%.1f" % (X(x), Z(z)) for x, z in poly))
    for a in P["areas"]:                                   # the court sits over the streets that meet in it
        if a["look"] == "court" and "circle" in a:
            cx, cz, r = a["circle"]; s.append('<circle cx="%.1f" cy="%.1f" r="%.1f" fill="%s" stroke="#c99aa4" stroke-width="3"/>' % (X(cx), Z(cz), r * S, LOOKS["court"]))
        elif a["look"] == "court":
            x0, z0, x1, z1 = a["rect"]; s.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="%s" stroke="#c99aa4" stroke-width="3"/>' % (X(x0), Z(z1), (x1 - x0) * S, (z1 - z0) * S, LOOKS["court"]))
    # the wall, with a gap at each gate
    t, o = WALL_DEEP * S, WALL_LINE * S
    s.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="none" stroke="#7d7468" stroke-width="%.1f"/>' % (o, o, W - 2 * o, H - 2 * o, t))
    for g in P["gates"]:
        if g["side"] in ("north", "south"):
            s.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="%s"/>' % (X(g["x"] - GATE / 2), (o if g["side"] == "north" else H - o) - t / 2 - 1, GATE * S, t + 2, LOOKS["path"]))
        else:
            s.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="%s"/>' % ((o if g["side"] == "west" else W - o) - t / 2 - 1, Z(g["z"] + GATE / 2), t + 2, GATE * S, LOOKS["path"]))
    for x, z in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        s.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="#6a6258"/>' % (X(x * (HALF_X - WALL_LINE)) - 0.45 * S, Z(z * (HALF_Z - WALL_LINE)) - 0.45 * S, 0.9 * S, 0.9 * S))
    fills = {"house": ("#b5654a", "#6e3826"), "landmark": ("#7a8fb0", "#3d4f6e"), "market": ("#c79a52", "#6b4f22")}
    for p in P["pieces"]:
        path, w, d, role = KIT[p["kind"]]
        if role == "effect":
            continue
        if role == "bush":
            s.append('<circle cx="%.1f" cy="%.1f" r="%.1f" fill="#e2456a" stroke="#6b8a1e" stroke-width="1.5"/>' % (X(p["x"]), Z(p["z"]), w / 2 * S))
            continue
        if role == "tree":
            col = "#f39cc0" if p["kind"] == "Blossom" else "#e486b2"
            s.append('<circle cx="%.1f" cy="%.1f" r="%.1f" fill="%s" stroke="#b25a83" stroke-width="1.5" opacity="0.92"/>' % (X(p["x"]), Z(p["z"]), w / 2 * S, col))
            continue
        poly = corners(p["x"], p["z"], w, d, p["rot"])
        fill, line = fills[role]
        s.append('<polygon points="%s" fill="%s" stroke="%s" stroke-width="1.5"/>' % (" ".join("%.1f,%.1f" % (X(x), Z(z)) for x, z in poly), fill, line))
        if role in ("house", "landmark") and p["kind"] in FRONT or role == "house" or p["kind"] == "Tavern":
            face = math.radians(p["rot"] + FRONT.get(p["kind"], 0.0))       # the front door, a dark mark on that side
            dx, dz = p["x"] + math.sin(face) * d / 2, p["z"] + math.cos(face) * d / 2
            s.append('<circle cx="%.1f" cy="%.1f" r="3.5" fill="#2a1a10" stroke="#fff" stroke-width="1"/>' % (X(dx), Z(dz)))
        if role in ("house", "landmark") and w * d > 1.5:
            s.append('<text x="%.1f" y="%.1f" font-size="10" text-anchor="middle" fill="#fff" font-family="sans-serif">%s</text>' % (X(p["x"]), Z(p["z"]) + 3, p["kind"]))
    for a in P["areas"]:
        if a["label"]:
            cx, cz = (a["circle"][0], a["circle"][1]) if "circle" in a else ((a["rect"][0] + a["rect"][2]) / 2, a["rect"][3])
            dz = (a["circle"][2] + 0.4) if "circle" in a else 0.4
            s.append('<text x="%.1f" y="%.1f" font-size="15" font-weight="bold" text-anchor="middle" fill="#3a2a30" font-family="sans-serif" '
                     'stroke="#fff" stroke-width="3" paint-order="stroke">%s</text>' % (X(cx), Z(cz + dz) - 4, a["label"]))
    named = set()
    for st in P["streets"]:
        if st["name"] in named: continue
        named.add(st["name"])
        pts = st["points"]
        x, z = ((pts[0][0] + pts[-1][0]) / 2, (pts[0][1] + pts[-1][1]) / 2) if len(pts) == 2 else pts[len(pts) // 3]
        s.append('<text x="%.1f" y="%.1f" font-size="13" font-style="italic" text-anchor="middle" fill="#5a4630" font-family="sans-serif" '
                 'stroke="#fff" stroke-width="3" paint-order="stroke">%s</text>' % (X(x) + (40 if st["name"] == "Petal Walk" else 0), Z(z) + 4, st["name"]))
    for g in P["gates"]:
        if g["road"]:
            s.append('<text x="%.1f" y="%.1f" font-size="12" text-anchor="middle" fill="#fff" font-family="sans-serif" stroke="#000" stroke-width="3" '
                     'paint-order="stroke">to %s</text>' % (X(g["x"]), Z(g["z"]) + (16 if g["side"] == "north" else -8), g["road"]))
    # a person, to scale, by the south gate
    px, pz = 2.6, -29.0
    s.append('<circle cx="%.1f" cy="%.1f" r="%.1f" fill="#2b6cd4" stroke="#fff" stroke-width="1"/>' % (X(px), Z(pz), 0.16 * S))
    s.append('</svg>')
    counts = {}
    for p in P["pieces"]: counts[p["kind"]] = counts.get(p["kind"], 0) + 1
    rows = "".join("<tr><td>%s</td><td>%d</td></tr>" % (k, v) for k, v in sorted(counts.items()))
    legend = "".join('<div><span style="background:%s"></span>%s</div>' % (c, n) for n, c in (
        ("stone paving", LOOKS["stone"]), ("street (path)", LOOKS["path"]), ("court", LOOKS["court"]), ("garden grass", LOOKS["grass"]),
        ("pink meadow", LOOKS["meadow"]), ("yard (dirt)", LOOKS["dirt"]), ("house", "#b5654a"), ("landmark", "#7a8fb0"),
        ("market goods", "#c79a52"), ("front door", "#2a1a10"), ("water", "#5aa7d6"), ("bridge", "#a08a6a"), ("mound over the stream", "#6f9e45"), ("blossom tree", "#f39cc0"), ("pink willow", "#e486b2"), ("rose bush", "#e2456a"), ("a person, to scale", "#2b6cd4")))
    return ("""<!doctype html><html><head><meta charset="utf-8"><title>%s plan</title><style>
body{margin:0;background:#2f3a2c;color:#eee;font-family:sans-serif;display:flex;gap:24px;padding:16px;align-items:flex-start}
.legend div{display:flex;align-items:center;gap:8px;margin:3px 0;font-size:14px}.legend span{width:16px;height:16px;display:inline-block;border:1px solid #0006}
table{border-collapse:collapse;font-size:13px;margin-top:12px}td{padding:2px 10px 2px 0}h1{font-size:20px;margin:0 0 6px}
</style></head><body>%s<div class="legend"><h1>%s — %s (%s)</h1><div>Top-down, north up. %d x %d units; 1 unit = %d px.</div><br>%s
<table>%s</table></div></body></html>""" % (P["name"], "".join(s), P["name"], P["theme"], P["weapon"], int(2 * HALF_X), int(2 * HALF_Z), S, legend, rows))


# ---- levels, tiles and plants, as the town editor and the town builder read them -----------------------------------------

HALF = (24.5, 31.0)


def _inside(shape, x, z):
    if "xz" in shape:   # a street
        xz = shape["xz"]
        for k in range(0, len(xz) - 3, 2):
            ax, az, dx, dz = xz[k], xz[k + 1], xz[k + 2] - xz[k], xz[k + 3] - xz[k + 1]
            L2 = dx * dx + dz * dz
            t = 0.0 if L2 == 0 else max(0.0, min(1.0, ((x - ax) * dx + (z - az) * dz) / L2))
            if math.hypot(x - ax - dx * t, z - az - dz * t) <= shape["width"] / 2: return True
        return False
    if shape.get("rect") and len(shape["rect"]) == 4:
        r = shape["rect"]; return r[0] <= x <= r[2] and r[1] <= z <= r[3]
    if shape.get("circle") and len(shape["circle"]) == 3:
        c = shape["circle"]; return math.hypot(x - c[0], z - c[1]) <= c[2]
    p = shape.get("poly") or []
    inside, n = False, len(p) // 2
    for i in range(n):
        j = (i - 1) % n
        xi, zi, xj, zj = p[2 * i], p[2 * i + 1], p[2 * j], p[2 * j + 1]
        if (zi > z) != (zj > z) and x < (xj - xi) * (z - zi) / (zj - zi) + xi: inside = not inside
    return inside


def stack_levels(plan):
    """A draft says each shape's height outright ("raised" one curb up, "low" at the ground's own height, the later shape
    winning). The town builder stacks instead (the user, 2026-10-29): each shape is "high", "normal" or "low" — a curb
    above, level with, or a curb below what it is laid on. Areas stack on each other in their order; streets and courts
    are paths, each measured from the areas' ground under it, so crossing streets meet level and a court covers the streets
    running into it. This gives every shape the level that builds the same ground, and returns the shapes no one level can."""
    line, step = plan["wall"]["line"], 0.25
    pts = [(-HALF[0] + (i + 0.5) * step, -HALF[1] + (j + 0.5) * step)
           for i in range(int(2 * HALF[0] / step)) for j in range(int(2 * HALF[1] / step))]
    pts = [(x, z) for x, z in pts if abs(x) < HALF[0] - line and abs(z) < HALF[1] - line]
    areas = {p: 1 for p in pts}   # inside the wall the ground starts one curb up, as the builder lays it
    problems = []
    def relative(s, under_of):
        mine = [p for p in pts if _inside(s, *p)]
        want = 1 if s["level"] == "raised" else 0
        under = {under_of[p] for p in mine}
        if len(under) > 1: problems.append(f"{s.get('name') or s.get('label') or s['look']} lies on ground of levels {sorted(under)}")
        rel = want - (min(under) if under else want)
        if rel not in (-1, 0, 1): problems.append(f"{s.get('name') or s.get('label') or s['look']} would need {rel} curbs")
        s["level"] = {1: "high", 0: "normal", -1: "low"}.get(rel, "normal")
        return mine
    for a in plan["areas"]:
        if a["look"] == "court": continue
        for p in relative(a, areas): areas[p] += {"high": 1, "normal": 0, "low": -1}[a["level"]]
    for s in plan["streets"] + [a for a in plan["areas"] if a["look"] == "court"]: relative(s, areas)
    return problems


# The ground tiles a first draft wears, by look: the town's own paving, and the world's own grass at the world's scale, so
# its lawns and the fields round it are one grass (its tiling is the world ground material's).
LOOK_TILES = {"street": "BASE_OR_SIDEWALK_plaza", "court": "BASE_OR_SIDEWALK_plaza", "stone": "PATH_OR_SIDEWALK_flagstone",
              "grass": "BASE_OR_PATCH_painted_grass", "meadow": "BASE_OR_PATCH_painted_grass", "dirt": "BASE_OR_PATCH_OR_PATH_soil"}
TILINGS = {"BASE_OR_SIDEWALK_plaza": 0.4, "PATH_OR_SIDEWALK_flagstone": 0.4, "BASE_OR_PATCH_painted_grass": 1.4525994,
           "BASE_OR_PATCH_OR_PATH_soil": 0.4}
# The plant brush's plants: the Idyllic Fantasy Nature pack's own, and nothing else (the user, 2026-10-29).
NATURE = "Assets/1_Authored/Nature/Idyllic Fantasy Nature/Prefabs/"
PLANTS = (["Grass_01", "Grass_02", "Grass_03"]
          + ["FlowerMeadow_" + c for c in ("Blue", "BluePurple", "Orange", "OrangePinkRedPurpleBlue", "Pink", "Purple",
                                           "PurpleRedPink", "Red", "RedOrange", "RedPink", "RedPurple", "White")]
          + ["Flower_" + c for c in ("Blue_01", "Blue_02", "Orange", "Pink", "Purple", "Red", "White", "Yellow", "YellowRed")]
          + ["Bush_01_01", "Bush_01_02", "Bush_02_01", "Bush_02_02", "Bush_03_01", "Bush_03_02"]
          + ["Plant_0%d" % n for n in range(1, 9)] + ["Plants"] + ["Reeds_01", "Reeds_02", "Reeds_03"])


def give_tiles_and_plants(plan):
    """Every street and area its tile, the town's tile list, its lawn and soil tiles, and its plant list."""
    for s in plan["streets"]: s.setdefault("tile", LOOK_TILES["street"])
    for a in plan["areas"]: a.setdefault("tile", LOOK_TILES[a["look"]])
    plan.setdefault("lawnTile", LOOK_TILES["grass"])
    plan.setdefault("soilTile", LOOK_TILES["dirt"])
    used = [plan["lawnTile"], plan["soilTile"]] + [s["tile"] for s in plan["streets"] + plan["areas"]]
    names = [t["name"] for t in plan.get("tiles", [])]
    plan["tiles"] = plan.get("tiles", []) + [{"name": n, "tiling": TILINGS[n]} for n in dict.fromkeys(used) if n not in names]
    plan.setdefault("plants", [{"kind": k, "prefab": NATURE + k + ".prefab"} for k in PLANTS])


def blank(town_id):
    """A town on the world layout with nothing in it yet: paving inside its wall, a lawn outside, no gates."""
    with open(os.path.join(ROOT, "data", "world-layout.json"), encoding="utf-8") as f:
        t = next((t for t in json.load(f)["towns"] if t["id"] == town_id), None)
    if t is None: raise ValueError("no town %r on the world layout" % town_id)
    P = {"id": t["id"], "name": t["name"], "weapon": t["weapon"], "theme": t["theme"],
         "gates": [], "areas": [], "streets": [], "canals": [], "bridges": [], "mounds": [], "pieces": []}
    P["areas"].append({"look": "stone", "label": "", "rect": [-HALF_X + WALL_LINE, -HALF_Z + WALL_LINE, HALF_X - WALL_LINE, HALF_Z - WALL_LINE]})
    return P


def rect_wall(gates, line=WALL_LINE):
    """A first draft's wall: one closed line round the town ground, a big tower at each corner and a small one about every
    TOWER_EVERY units along each side, clear of the gates; and its gates, each where it stands in that line. A draft
    names each gate's side and its place along it; the town editor draws and moves both afterwards."""
    wx, wz = HALF_X - line, HALF_Z - line
    on = {"south": lambda g: (g["x"], -wz), "north": lambda g: (g["x"], wz), "east": lambda g: (wx, g["z"]), "west": lambda g: (-wx, g["z"])}
    xz, towers = [], []
    for side, a, b in (("south", (-wx, -wz), (wx, -wz)), ("east", (wx, -wz), (wx, wz)), ("north", (wx, wz), (-wx, wz)), ("west", (-wx, wz), (-wx, -wz))):
        xz += [a[0], a[1]]; towers.append("Wall_tower2")
        length = math.dist(a, b); between = int(length // TOWER_EVERY)
        cuts = [math.dist(a, on[side](g)) for g in gates if g["side"] == side]
        for k in range(1, between):
            at = length * k / between
            if all(abs(c - at) >= GATE for c in cuts):
                xz += [round(a[0] + (b[0] - a[0]) * at / length, 2), round(a[1] + (b[1] - a[1]) * at / length, 2)]; towers.append("Wall_tower1")
    walls = [{"xz": xz, "closed": True, "towers": towers}]
    return walls, [{"x": on[g["side"]](g)[0], "z": on[g["side"]](g)[1], "road": g["road"]} for g in gates]


def write(P, whole_kit=False):
    """Writes the plan file and its picture, or returns its problems; a plan file that exists is never written over.
    A town's kit is the pieces its plan uses, or, for a town started blank in the editor, every piece there is."""
    if os.path.exists(os.path.join(OUT, P["id"] + ".json")):
        return [P["id"] + " has its plan file - it is edited in the town editor, never written over"]
    problems = check(P)
    if problems: return problems
    # The file is read by the town piece builder in Unity, whose reader takes flat lists only: a street's points as
    # x, z, x, z…, and the kit as a list.
    out = dict(P)
    # Every street and area says its level, raised or low; a first draft lays streets low and paving, grass and meadow raised.
    out["streets"] = [{"name": st["name"], "level": "low", "width": st["width"], "xz": [v for q in st["points"] for v in q]} for st in P["streets"]]
    out["areas"] = [dict(a, level="low" if a["look"] in ("street", "court", "dirt") else "raised") for a in P["areas"]]
    out["kit"] = [{"kind": k, "prefab": v[0], "footprint": [v[1], v[2]], "role": v[3], "scale": SCALE.get(k, 0.0)}
                  for k, v in KIT.items() if whole_kit or any(p["kind"] == k for p in P["pieces"])]
    out["mounds"] = P.get("mounds", [])
    out["canals"] = [{"name": c["name"], "width": c["width"], "xz": [v for q in c["points"] for v in q]} for c in P.get("canals", [])]
    out["wall"] = {"line": WALL_LINE, "gate": GATE}
    out["walls"], out["gates"] = rect_wall(P["gates"])
    problems = stack_levels(out)
    if problems: return ["levels: " + p for p in problems]
    give_tiles_and_plants(out)
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, P["id"] + ".json"), "w", encoding="utf-8") as f: json.dump(out, f, indent=1)
    with open(os.path.join(OUT, P["id"] + ".html"), "w", encoding="utf-8") as f: f.write(draw(P))
    return []


if __name__ == "__main__":
    failed = False
    for make in PLANS:
        P = make()
        problems = write(P)
        if problems and problems[0].endswith("never written over"):
            print(problems[0])
            continue
        if problems:
            failed = True
            print(P["name"], "PROBLEMS:"); [print("  -", p) for p in problems]
            continue
        print("wrote", P["id"], len(P["pieces"]), "pieces")
    raise SystemExit(1 if failed else 0)
