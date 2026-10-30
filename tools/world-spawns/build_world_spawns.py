"""THE WORLD'S CREATURES — where every creature of the seamless world "world" lives, worked out from the world layout.

Writes the world's rows and areas of unity/Assets/3_Data/Resources/WorldSpawns.asset (rows of any other map are kept as
they are), moves the 18 summoners of WorldTownsfolk.asset to their places in the world, and lays the patches over the
layout's own map, data/world-layout.html, behind a "Show creature patches" toggle — one map, never a second copy. The
layout tool runs this at the end of every run, so the spawns always follow the layout; run it alone after a creature
roster change. The same inputs always give the same spawns.

    python tools/world-spawns/build_world_spawns.py              (writes the data and the map)
    python tools/world-spawns/build_world_spawns.py --preview    (writes nothing: draws ~/Downloads/world-spawn-preview.html)

The rules (decided with the user, 2026-10-29):
  - the land is cut into PATCHES that follow its own shape, with no gaps between them: each patch is one kind of ground
    (dark isle, desert, coast, riverbank, high, forest, meadow) inside one town's land, seeded every 90 units; a patch
    under 500 square units joins its neighbour. Towns (their walls plus 3), cliff faces, rivers, ponds and the rock
    inside a gorge hold none;
  - its RESIDENTS are the original creatures nearest the level it aims at (the aim ranked by distance among its own
    ground's patches), up to three, from its own ground or a neighbouring one when that is closer by 6 levels or more,
    and never more than 12 levels apart. Population is the patch's area / 49 (one creature every 7 units) times the mob
    density of data/world-spawns-settings.json, at least 3, split between them;
  - its VERSION is decided inside each level band of 6 patches of one ground: the nearest half to a town hold
    originals, the next 30% Tainted, the farthest Blighted — and nothing Blighted lies within 150 units of a town;
  - EVERY ORIGINAL lives as itself in at least one original patch, and every Tainted and Blighted version lives in a
    patch or on a dungeon floor, each joining the place of its version nearest its level;
  - one VISITOR per patch: never one of its residents, 10+ levels above them (its own or a neighbouring ground first,
    then any ground), one version tougher (Tainted at most inside the ring);
  - each of the 14 field ELITES lives in the 2 patches nearest its level, outside the ring, of its own ground first
    (a neighbouring ground's count 6 levels further), at most one elite a patch;
  - DUNGEONS: every dungeon door and cave but the Mountain's Root leads down 3 floors climbing from the lowest level
    within 150 units of its entrance — Tainted above, Blighted at the bottom, each floor within 12 levels. A version
    no place can take opens one more floor of its version: Tainted floors always stay above Blighted ones, each
    group climbs in level going down, and the deepest is always Blighted. A deepest floor keeps a field elite only
    when one is within 12 levels of its top;
  - SUMMONERS: each shadow's at its place (its landmark, Tharos's at the Mountain's Root cave on the gorge's rock), the
    four summon-only elites' at the dark isle's four corners, each at least 80 units from every other summoner.
"""
import glob, json, math, os, re, sys
from collections import defaultdict, deque, Counter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "unity", "Assets", "3_Data", "Resources")
SPAWNS = os.path.join(RES, "WorldSpawns.asset")
FOLK = os.path.join(RES, "WorldTownsfolk.asset")
OUT_MAP = os.path.join(ROOT, "data", "world-layout.html")
SETTINGS = os.path.join(ROOT, "data", "world-spawns-settings.json")
MAP = "world"
# --preview: work everything out and draw it to PREVIEW_MAP, writing no game data and leaving the layout's map alone
PREVIEW = "--preview" in sys.argv
PREVIEW_MAP = os.path.join(os.path.expanduser("~"), "Downloads", "world-spawn-preview.html")

L = json.load(open(os.path.join(ROOT, "data", "world-layout.json")))
W, H = L["cells"]; C = L["cell"]
hts = L["heights"]; th = L["theme_cells"]; themes = L["themes"]
BASE = next(t["height"] for t in L["towns"] if t["id"] == "lastline")
idx = lambda x, y: y * W + x
land = [h >= 0 for h in hts]
def scene(lx, ly, h=None):
    """Layout x, y (and height in units) to the map's own coordinates, as the layout file says."""
    return (lx - 640, (h if h is not None else 0) - BASE, 640 - ly)

# ---------------------------------------------------------------- the creatures
BIOMES = ["Grassland", "Snow", "Desert", "Volcanic", "Corrupted", "None", "Forest", "Highland", "Peaks", "Steppe", "Coast",
          "Swamp", "Sacred", "Haunted", "Mist"]
guid_of = {}; mons = []
for f in sorted(glob.glob(os.path.join(RES, "Monsters", "*.asset"))):
    t = open(f, encoding="utf-8").read()
    g = re.search(r"guid: (\w+)", open(f + ".meta").read()).group(1)
    def fld(n, d=None):
        m = re.search(r"^  " + n + r": (.*)$", t, re.M); return m.group(1).strip() if m else d
    vo = re.search(r"^  versionOf: \{fileID: \d+(?:, guid: (\w+))?", t, re.M)
    m = dict(id=fld("id"), name=fld("displayName"), kind=int(fld("kind", 0)), biome=BIOMES[int(fld("biome", 5))],
             level=int(fld("level", 1)), element=int(fld("element", 0)), mult=float(fld("multiplier", 1)),
             versionOf=vo.group(1) if vo else None)
    mons.append(m); guid_of[g] = m
byid = {m["id"]: m for m in mons}
summon_only = set(re.findall(r"bossId: (\S+)", open(os.path.join(RES, "SummonRecipes.asset"), encoding="utf-8").read()))
originals = [m for m in mons if m["kind"] == 0 and not m["versionOf"]]
versions = defaultdict(dict)
for m in mons:
    if m["versionOf"]:
        versions[guid_of[m["versionOf"]]["id"]]["tainted" if m["mult"] < 8 else "blighted"] = m

def ground_of_creature(m):
    b = m["biome"]
    if b in ("Grassland", "Steppe"): return "meadow"
    if b == "Forest": return "forest"
    if b in ("Highland", "Peaks", "Mist", "Sacred"): return "high"
    if b == "Coast": return "coast"
    if b == "Swamp": return "riverbank"
    if b == "Desert": return "desert"
    if b in ("Haunted", "Volcanic", "Corrupted", "None"): return "dark isle"
    if b == "Snow": return "riverbank" if m["element"] == 1 else "meadow"   # no snow land: Water ones by the water
    raise SystemExit("no ground for biome " + b)
NEIGHBOUR = {"meadow": ["forest", "riverbank", "high", "desert", "coast"], "forest": ["meadow", "high", "riverbank"],
             "high": ["meadow", "forest"], "coast": ["riverbank", "meadow"], "riverbank": ["meadow", "coast", "forest"],
             "desert": ["meadow"], "dark isle": []}
pool = defaultdict(list)
for m in originals: pool[ground_of_creature(m)].append(m)
for g in pool: pool[g].sort(key=lambda m: (m["level"], m["id"]))

# ---------------------------------------------------------------- ground per cell
sea_d = [99] * (W * H); q = deque()
for i in range(W * H):
    if not land[i]: sea_d[i] = 0; q.append(i)
while q:
    i = q.popleft(); x, y = i % W, i // W
    if sea_d[i] >= 3: continue
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < W and 0 <= ny < H and sea_d[idx(nx, ny)] > sea_d[i] + 1:
            sea_d[idx(nx, ny)] = sea_d[i] + 1; q.append(idx(nx, ny))
near_water = [False] * (W * H); no_spawn = [False] * (W * H)
def stamp(px, py, r, arr):
    cx, cy = int(px / C), int(py / C); rc = int(r / C) + 1
    for y in range(max(0, cy - rc), min(H, cy + rc + 1)):
        for x in range(max(0, cx - rc), min(W, cx + rc + 1)):
            if ((x + .5) * C - px) ** 2 + ((y + .5) * C - py) ** 2 <= r * r: arr[idx(x, y)] = True
for r in L["rivers"]:
    xy = r["xy"]
    for k in range(0, len(xy) - 1, 2):
        stamp(xy[k], xy[k + 1], 20, near_water); stamp(xy[k], xy[k + 1], 5, no_spawn)
for p in L["ponds"]:
    stamp(p["x"], p["y"], p["radius"] + 15, near_water); stamp(p["x"], p["y"], p["radius"], no_spawn)
for g in L.get("gorges", []):
    stamp(g["x"], g["y"], g["rock_radius"] + g["width"], no_spawn)
FOREST = {"pennaryl", "harrowen", "parley", "tornel", "velmora", "gemmara"}
ground = [None] * (W * H)
for i in range(W * H):
    if not land[i]: continue
    t = themes[th[i]] if th[i] >= 0 else ""
    if t in ("kharnel", "cindrel"): g = "dark isle"
    elif t == "hazemere": g = "desert"
    elif sea_d[i] <= 3: g = "coast"
    elif near_water[i]: g = "riverbank"
    elif hts[i] > 240: g = "high"
    elif t in FOREST: g = "forest"
    else: g = "meadow"
    ground[i] = g

TOWN_HALF = (22 + 3, 28.5 + 3)            # a town's walls (Lastline's, the one measured: 44 x 57) plus 3
town_box = [(t["x"] - TOWN_HALF[0], t["x"] + TOWN_HALF[0], t["y"] - TOWN_HALF[1], t["y"] + TOWN_HALF[1]) for t in L["towns"]]
def in_town(px, py): return any(a <= px <= b and c <= py <= d for a, b, c, d in town_box)
CLIFF = 15                                # tenths: the layout's cliff, a step of more than 1.5 units
ok = [False] * (W * H)
for y in range(H):
    for x in range(W):
        i = idx(x, y)
        if not land[i] or no_spawn[i] or in_town((x + .5) * C, (y + .5) * C): continue
        if any(0 <= x + dx < W and 0 <= y + dy < H and land[idx(x + dx, y + dy)] and abs(hts[idx(x + dx, y + dy)] - hts[i]) > CLIFF
               for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))): continue
        ok[i] = True

# ---------------------------------------------------------------- patches
pid = [-1] * (W * H); key = lambda i: (th[i], ground[i])
seeds = []; STEP = 90; row = 0; yy = 0.0
while yy < L["size"][1]:
    xx = (STEP / 2) if row % 2 else 0.0
    while xx < L["size"][0]:
        cx, cy = int(xx / C), int(yy / C)
        if cx < W and cy < H and ok[idx(cx, cy)]: seeds.append(idx(cx, cy))
        xx += STEP
    yy += STEP * 0.866; row += 1
def grow(seed_list, start_id):
    q = deque()
    for n, s in enumerate(seed_list):
        if pid[s] == -1: pid[s] = start_id + n; q.append(s)
    while q:
        i = q.popleft(); x, y = i % W, i // W
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < W and 0 <= ny < H:
                j = idx(nx, ny)
                if ok[j] and pid[j] == -1 and key(j) == key(i): pid[j] = pid[i]; q.append(j)
grow(seeds, 0); nxt = len(seeds)
for i in range(W * H):
    if ok[i] and pid[i] == -1: grow([i], nxt); nxt += 1
cells = defaultdict(list)
for i in range(W * H):
    if pid[i] >= 0: cells[pid[i]].append(i)
MIN_CELLS = 500 / (C * C)
# A PATCH UNDER 500 SQUARE UNITS JOINS ITS NEIGHBOUR — run again after the cut below, whose pieces can be that small.
def join_small():
  changed = True
  while changed:
    changed = False
    for p in sorted(cells, key=lambda p: (len(cells[p]), p)):
        if p not in cells or len(cells[p]) >= MIN_CELLS: continue
        g = ground[cells[p][0]]; nb = defaultdict(int)
        for i in cells[p]:
            x, y = i % W, i // W
            for dx in range(-15, 16, 3):
                for dy in range(-15, 16, 3):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < W and 0 <= ny < H:
                        j = idx(nx, ny); o = pid[j]
                        if o >= 0 and o != p and o in cells and len(cells[o]) > len(cells[p]): nb[o] += 1 + (4 if ground[j] == g else 0) * (abs(dx) + abs(dy) <= 3)
        if not nb: continue
        same = [o for o in nb if ground[cells[o][0]] == g]
        tgt = max(same or list(nb), key=lambda o: (nb[o], -o))
        for i in cells[p]: pid[i] = tgt
        cells[tgt] += cells.pop(p); changed = True
join_small()

# A PATCH REACHES AT MOST MAX_REACH FROM ITS MIDDLE: the server holds an awake area's whole ground loaded, so a long
# coast strip would hold hundreds of squares for one patch. A wider one is cut into pieces grown from spread seeds.
MAX_REACH = 100
def middle_cell(cl):
    sx = sum(i % W for i in cl) / len(cl); sy = sum(i // W for i in cl) / len(cl)
    return min(cl, key=lambda i: ((i % W - sx) ** 2 + (i // W - sy) ** 2, i))
def reach_of(cl):
    m = middle_cell(cl)
    return max(math.hypot(i % W - m % W, i // W - m // W) for i in cl) * C
for _ in range(10):
    wide = [p for p in sorted(cells) if reach_of(cells[p]) > MAX_REACH]
    if not wide: break
    for p in wide:
        cl = cells.pop(p); inside = set(cl); m = middle_cell(cl)
        seeds_ = [max(cl, key=lambda i: (math.hypot(i % W - m % W, i // W - m // W), i))]
        while True:
            far = max(cl, key=lambda i: (min(math.hypot(i % W - s % W, i // W - s // W) for s in seeds_), i))
            if min(math.hypot(far % W - s % W, far // W - s // W) for s in seeds_) * C <= MAX_REACH * 0.7: break
            seeds_.append(far)
        q = deque()
        for s in seeds_: pid[s] = nxt; cells[nxt] = [s]; q.append(s); nxt += 1
        claimed = set(seeds_)
        while q:
            i = q.popleft(); x, y = i % W, i // W
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                j = idx(x + dx, y + dy) if 0 <= x + dx < W and 0 <= y + dy < H else -1
                if j in inside and j not in claimed: claimed.add(j); pid[j] = pid[i]; cells[pid[i]].append(j); q.append(j)
# A CUT PATCH IN SEVERAL UNJOINED PARTS leaves the parts its seeds cannot reach with no patch: each grows as its own
# patch, so no ground is left without one, and then every piece under 500 square units joins its neighbour.
lost = [i for i in range(W * H) if pid[i] >= 0 and pid[i] not in cells]
for i in lost: pid[i] = -1
for i in lost:
    if pid[i] == -1: grow([i], nxt); nxt += 1
for i in lost: cells[pid[i]].append(i)
join_small()

towns = L["towns"]
def tdist(px, py): return min(math.hypot(px - t["x"], py - t["y"]) for t in towns)
P = []
for p in sorted(cells):
    cl = sorted(cells[p])
    sx = sum((i % W + .5) * C for i in cl) / len(cl); sy = sum((i // W + .5) * C for i in cl) / len(cl)
    li = min(cl, key=lambda i: ((i % W + .5) * C - sx) ** 2 + ((i // W + .5) * C - sy) ** 2)
    cx, cy = (li % W + .5) * C, (li // W + .5) * C
    rad = max(math.hypot((i % W + .5) * C - cx, (i // W + .5) * C - cy) for i in cl) + C * 0.75
    P.append(dict(id=p, cells=cl, ground=ground[cl[0]], cx=cx, cy=cy, h=hts[li] / 10, radius=rad, dist=tdist(sx, sy), area=len(cl) * C * C))
ds = sorted(p["dist"] for p in P); d95 = ds[int(len(ds) * 0.95)]
TIERS = ["original", "tainted", "blighted"]
for p in P: p["f"] = min(1.0, p["dist"] / d95)
def as_tier(o, tier): return o if tier == "original" else versions[o["id"]][tier]

NEIGH_COST = 6
# How many creatures each patch keeps, set in data/world-spawns-settings.json: 1 = one creature every MOB_ROOM square
# units of patch (one every 7 units, 7 s of running), 1.5 = half as many again.
MOB_ROOM = 49
DENSITY = float(json.load(open(SETTINGS))["mob_density"])
SPREAD = 12                               # the most levels a patch's or a floor's creatures may lie apart
for p in P:
    pl = pool[p["ground"]]
    fs = sorted(q["f"] for q in P if q["ground"] == p["ground"])
    rank = fs.index(p["f"]) / max(1, len(fs) - 1)
    p["aim"] = pl[0]["level"] + rank * (pl[-1]["level"] - pl[0]["level"])
    cand = [(abs(o["level"] - p["aim"]), o) for o in pl] + \
           [(abs(o["level"] - p["aim"]) + NEIGH_COST, o) for g in NEIGHBOUR[p["ground"]] for o in pool[g]]
    cand.sort(key=lambda c: (c[0], c[1]["id"]))
    seen = []
    for _, o in cand:
        # nearest first, and only while the residents stay within SPREAD levels of each other
        if all(o["id"] != s["id"] for s in seen) and max([o["level"]] + [s["level"] for s in seen]) - min([o["level"]] + [s["level"] for s in seen]) <= SPREAD:
            seen.append(o)
        if len(seen) == 3: break
    p["picks"] = sorted(seen, key=lambda m: (m["level"], m["id"]))
# THE VERSION IS DECIDED INSIDE EACH LEVEL BAND, never by distance alone: a ground's patches, in the order of the level
# they aim at, are taken BAND at a time, and inside a band the nearest to a town hold originals, the next Tainted, the
# farthest Blighted — so every level of every ground has its originals first, the dark isle's included. No Blighted
# (x10) patch lies within RING of a town: a band's far patch that close holds Tainted instead.
BAND = 6; RING = 150
for g in sorted({p["ground"] for p in P}):
    mine = sorted([p for p in P if p["ground"] == g], key=lambda p: (p["aim"], p["id"]))
    bands = [mine[k:k + BAND] for k in range(0, len(mine), BAND)]
    if len(bands) > 1 and len(bands[-1]) < 3:
        last = bands.pop(); bands[-1] += last
    for band in bands:
        band.sort(key=lambda p: (p["dist"], p["id"]))
        n = len(band); n_orig = max(1, round(n * 0.5)); n_taint = max(1 if n >= 2 else 0, round(n * 0.3))
        for k, p in enumerate(band):
            p["tier"] = "original" if k < n_orig else ("tainted" if k < n_orig + n_taint or p["dist"] < RING else "blighted")
# EVERY ORIGINAL LIVES AS ITSELF: one missing from every original patch joins the original patch, of its own ground
# first, whose level is nearest its own — taking the place of the farthest-off resident that lives as itself elsewhere
# too, or living there as a fourth when every resident there has no other home.
def homes(o): return sum(1 for p in P if p["tier"] == "original" for m in p["picks"] if m["id"] == o["id"])
for o in originals:
    if homes(o): continue
    g = ground_of_creature(o)
    cands = [p for p in P if p["tier"] == "original" and p["ground"] == g] or \
            [p for p in P if p["tier"] == "original" and g in NEIGHBOUR[p["ground"]]]
    p = min(cands, key=lambda p: (abs(p["aim"] - o["level"]), p["id"]))
    spare = [m for m in p["picks"] if homes(m) > 1]
    keep = [m for m in p["picks"] if m is not max(spare, key=lambda m: (abs(m["level"] - p["aim"]), m["id"]))] if spare else p["picks"]
    p["picks"] = sorted(keep + [o], key=lambda m: (m["level"], m["id"]))
for p in P:
    picks = p["picks"]
    pop = max(3, round(p["area"] * DENSITY / MOB_ROOM))
    per = [pop // len(picks) + (1 if k < pop % len(picks) else 0) for k in range(len(picks))]
    p["res"] = [dict(m=as_tier(o, p["tier"]), tier=p["tier"], n=c) for o, c in zip(picks, per)]
    top = max(o["level"] for o in picks)
    # never one of its own residents; its own and neighbouring grounds first, then any ground, while one is 10+ above
    kin = {o["id"] for o in picks}
    vpool = sorted([o for o in pool[p["ground"]] + [o for g in NEIGHBOUR[p["ground"]] for o in pool[g]] if o["id"] not in kin],
                   key=lambda m: (m["level"], m["id"]))
    anyg = sorted([o for o in originals if o["id"] not in kin], key=lambda m: (m["level"], m["id"]))
    higher = [o for o in vpool if o["level"] >= top + 10] or [o for o in anyg if o["level"] >= top + 10]              or [o for o in anyg if o["level"] > top] or [anyg[-1]]
    vt = TIERS[min(2, TIERS.index(p["tier"]) + 1)]
    if vt == "blighted" and p["dist"] < RING: vt = "tainted"          # nothing Blighted inside the ring, visitors included
    p["visitor"] = dict(m=as_tier(higher[0], vt), tier=vt, n=1)
    p["lv"] = (min(o["level"] for o in picks), top)

elites = [m for m in mons if m["kind"] == 1 and m["id"] not in summon_only]
has_elite = set(); elite_marks = []
for e in sorted(elites, key=lambda m: (m["level"], m["id"])):
    g = ground_of_creature(e)
    # x30: never inside the ring. Its own ground first; a neighbouring ground's patches count NEIGH_COST levels further.
    cand = sorted([p for p in P if p["id"] not in has_elite and p["dist"] >= RING and (p["ground"] == g or p["ground"] in NEIGHBOUR[g])],
                  key=lambda p: (abs((p["lv"][0] + p["lv"][1]) / 2 - e["level"]) + (0 if p["ground"] == g else NEIGH_COST), p["id"]))
    if len(cand) < 2: raise SystemExit("fewer than 2 patches of %s ground free for the elite %s" % (g, e["id"]))
    for p in cand[:2]:
        has_elite.add(p["id"]); p["elite"] = e; elite_marks.append((p, e))

# ---------------------------------------------------------------- dungeons: every door and cave leads down
# Each dungeon has FLOORS floors under its entrance, their levels climbing from the lowest level of the field round
# the door (every patch within 150 units of it). Its floors
# hold only Tainted and Blighted versions (decided 2026-10-20): the upper floors Tainted, the deepest Blighted, and the
# deepest floor keeps a field elite. The Mountain's Root is Tharos's place, not a dungeon.
FLOORS = 3; FLOOR_KINDS = 3; FLOOR_STEP = 5
def nearest_patch(x, y):
    return min(P, key=lambda p: (min(math.hypot((i % W + .5) * C - x, (i // W + .5) * C - y) for i in p["cells"][::7]), p["id"]))
CAVE_NAMES = {"sea_cave": "Sea Cave", "forest_cave": "Forest Cave", "dark_cave": "Dark Cave"}
dungeons = [dict(name=next(t["name"] for t in L["towns"] if t["id"] == d["town"]) + " Dungeon", x=d["x"], y=d["y"]) for d in L["dungeon_doors"]] + \
           [dict(name=CAVE_NAMES.get(c["id"], c["id"]), x=c["x"], y=c["y"]) for c in L["caves"] if c["id"] != "mountains_root"]
for d in dungeons:
    near = nearest_patch(d["x"], d["y"]); d["ground"] = near["ground"]
    base = min(p["lv"][0] for p in P if math.hypot(p["cx"] - d["x"], p["cy"] - d["y"]) <= 150 or p is near)
    pool_d = sorted(pool[d["ground"]] + [o for g in NEIGHBOUR[d["ground"]] for o in pool[g]], key=lambda m: (m["level"], m["id"]))
    d["floors"] = []
    for k in range(1, FLOORS + 1):
        aim = base + FLOOR_STEP * k
        picks = sorted(sorted(pool_d, key=lambda o: (abs(o["level"] - aim), o["id"]))[:FLOOR_KINDS], key=lambda m: (m["level"], m["id"]))
        tier = "blighted" if k == FLOORS else "tainted"
        d["floors"].append(dict(aim=aim, tier=tier, kinds=[as_tier(o, tier) for o in picks]))

# EVERY TAINTED AND BLIGHTED VERSION LIVES SOMEWHERE, lowest level first: one that no field patch and no floor holds
# joins the field patch or the floor of its version whose level is nearest its own (a ground not its own counts
# NEIGH_COST levels further) — only where it leaves that place's levels within SPREAD of each other (or no wider than
# they already were) and fewer than MOST kinds there. When no place takes it, the dungeon whose floors of its version
# come nearest its level opens one more floor of that version for it: Tainted floors stay above Blighted ones, each
# group climbs in level going down, and the deepest keeps the elite.
MOST = 5
def held(m):
    return any(r["m"]["id"] == m["id"] for p in P for r in p["res"]) or any(p["visitor"]["m"]["id"] == m["id"] for p in P)         or any(m["id"] == x["id"] for d in dungeons for f in d["floors"] for x in f["kinds"])
def fits(levels, lvl):
    lo, hi = min(levels), max(levels)
    return max(hi, lvl) - min(lo, lvl) <= max(SPREAD, hi - lo)
missing = sorted([(versions[o["id"]][t], o, t) for o in originals for t in ("tainted", "blighted") if not held(versions[o["id"]][t])],
                 key=lambda x: (x[0]["level"], x[0]["id"]))
for v, o, tier in missing:
    g = ground_of_creature(o)
    homes_ = [(abs(p["aim"] - v["level"]) + (0 if p["ground"] == g else NEIGH_COST), 0, p["id"], p, None) for p in P
              if p["tier"] == tier and len(p["res"]) < MOST and fits([r["m"]["level"] for r in p["res"]], v["level"])] +              [(abs(f["aim"] - v["level"]) + (0 if d["ground"] == g else NEIGH_COST), 1, n, None, f)
              for n, d in enumerate(dungeons) for f in d["floors"] if f["tier"] == tier and len(f["kinds"]) < MOST
              and fits([m["level"] for m in f["kinds"]], v["level"])]
    if homes_:
        _, _, _, p, f = min(homes_, key=lambda h: h[:3])
        if f is not None: f["kinds"].append(v); continue
        p["res"].append(dict(m=v, tier=tier, n=2))
        p["lv"] = (min(p["lv"][0], v["level"]), max(p["lv"][1], v["level"]))
        continue
    d = min(dungeons, key=lambda d: (min(abs(f["aim"] - v["level"]) for f in d["floors"] if f["tier"] == tier), d["name"]))
    d["floors"].append(dict(aim=v["level"], tier=tier, kinds=[v]))
    # Tainted floors above, Blighted below, each group climbing in level going down
    d["floors"].sort(key=lambda f: (f["tier"] == "blighted", max(m["level"] for m in f["kinds"])))
for d in dungeons:
    for f in d["floors"]: f["kinds"].sort(key=lambda m: (m["level"], m["id"]))
# A DEEPEST FLOOR KEEPS AN ELITE ONLY WHERE ONE FITS: the field elite nearest its levels, each elite on one floor at most,
# and only when it is within SPREAD of the floor's highest level. The elites are levels 50-70, so a low dungeon has none.
taken = set()
for d in sorted(dungeons, key=lambda d: (-max(m["level"] for m in d["floors"][-1]["kinds"]), d["name"])):
    top_ = max(m["level"] for m in d["floors"][-1]["kinds"])
    free = [e for e in elites if e["id"] not in taken and abs(e["level"] - top_) <= SPREAD]
    d["elite"] = min(free, key=lambda e: (abs(e["level"] - top_), e["id"])) if free else None
    if d["elite"]: taken.add(d["elite"]["id"])

# ---------------------------------------------------------------- summoners
SHADOW_PLACE = {"seraphine": "hill_of_the_flame", "veyra": "thousand_gates", "tharos": "mountains_root", "velmora": "masked_ballroom",
    "varkhaz": "burning_keel", "kaelen": "everburning_plain", "celestia": "sky_cairn", "mordane": "tide_temple", "vael": "dusk_tower",
    "nysara": "walking_road", "morwen": "drowned_chapel", "asterion": "aurora_steps", "riven": "unmarked_path", "orion": "narrowbend"}
places = {l["id"]: (l["x"], l["y"]) for l in L["landmarks"]}
for c in L["caves"]:
    g = next((g for g in L.get("gorges", []) if g["cave"] == c["id"]), None)
    # a gorge's cave: the summoner waits on the rock in front of the mouth, between it and the bridge
    places[c["id"]] = (c["x"] - c["into"][0] * 8, c["y"] - c["into"][1] * 8) if g else (c["x"], c["y"])
summoners = []
for s, pl in SHADOW_PLACE.items():
    summoners.append(dict(boss=byid["mob.shadow." + s], at=places[pl]))
isle = [i for i in range(W * H) if ok[i] and ground[i] == "dark isle"]
CORNER = {"north-west": lambda x, y: -(x + y), "south-east": lambda x, y: x + y,
          "north-east": lambda x, y: x - y, "south-west": lambda x, y: y - x}
KEEP_APART = 80
special = sorted([byid[b] for b in summon_only if not b.startswith("mob.shadow.")], key=lambda m: m["name"])
for (cn, fn), m in zip(CORNER.items(), special):
    free = [i for i in isle if all(math.hypot((i % W + .5) * C - s["at"][0], (i // W + .5) * C - s["at"][1]) >= KEEP_APART for s in summoners)]
    i = max(free, key=lambda i: (fn(i % W, i // W), -i))
    summoners.append(dict(boss=m, at=((i % W + .5) * C, (i // W + .5) * C)))
def ground_h(lx, ly): return hts[idx(min(W - 1, int(lx / C)), min(H - 1, int(ly / C)))] / 10

# ---------------------------------------------------------------- WorldSpawns.asset: the world's rows and every area
def f3(v): return ("%.3f" % v).rstrip("0").rstrip(".")
def runs_of(cl):
    rows = defaultdict(list)
    for i in cl: rows[i // W].append(i % W)
    out = []
    for y in sorted(rows):
        xs = sorted(rows[y]); s = prev = xs[0]
        for x in xs[1:] + [None]:
            if x is None or x != prev + 1:
                out += [int(s * C - 640), int(640 - (y + 1) * C), prev - s + 1]   # west edge, south edge, squares
                s = x
            if x is not None: prev = x
    return out
text = open(SPAWNS, encoding="utf-8").read()
head, body = text.split("\n  rows:\n", 1)
body = body.split("\n  areas:", 1)[0]
kept = [b for b in re.split(r"(?m)^  - (?=map: )", body) if b.strip() and not b.startswith("map: %s\n" % MAP)]
out = [head, "\n  rows:\n"] + ["  - " + b.rstrip("\n") + "\n" for b in kept]
def row(creature, pop, area):
    out.append("  - map: %s\n    creature: %s\n    biome: 0\n    minLevel: 1\n    maxLevel: 99\n    includeCommons: 0\n"
               "    population: %d\n    area: %d\n    center: {x: 0, y: 0, z: 0}\n    radius: 0\n    playerClearRadius: 6\n"
               % (MAP, creature, pop, area))
for n, p in enumerate(P):
    for r in p["res"]: row(r["m"]["id"], r["n"], n)
    row(p["visitor"]["m"]["id"], 1, n)
    if p.get("elite"): row(p["elite"]["id"], 1, n)
out.append("  areas:\n")
for p in P:
    x, y, z = scene(p["cx"], p["cy"], p["h"])
    lv = [r["m"]["level"] for r in p["res"]] + [p["visitor"]["m"]["level"]] + ([p["elite"]["level"]] if p.get("elite") else [])
    out.append("  - center: {x: %s, y: %s, z: %s}\n    radius: %s\n    lowLevel: %d\n    highLevel: %d\n    runs:\n"
               % (f3(x), f3(y), f3(z), f3(p["radius"]), min(lv), max(lv)))
    out.append("".join("    - %d\n" % v for v in runs_of(p["cells"])))
if not PREVIEW: open(SPAWNS, "w", encoding="utf-8", newline="\n").write("".join(out))

# ---------------------------------------------------------------- WorldTownsfolk.asset: the summoners to their places
folk = open(FOLK, encoding="utf-8").read()
for s in summoners:
    pid_ = "summoner-" + s["boss"]["id"].replace("mob.", "", 1).replace(".", "-")
    blk = re.search(r"(?ms)^  - id: %s\n.*?(?=^  - id: |\Z)" % re.escape(pid_), folk)
    if not blk: raise SystemExit("no townsfolk row '%s'" % pid_)
    if ("summons: " + s["boss"]["id"]) not in blk.group(0): raise SystemExit("'%s' does not summon %s" % (pid_, s["boss"]["id"]))
    lx, ly = s["at"]; h = ground_h(lx, ly)
    bx, by, bz = scene(lx, ly, h)
    b = blk.group(0)
    b = re.sub(r"(?m)^    map: .*$", "    map: " + MAP, b)
    b = re.sub(r"(?m)^    home: .*$", "    home: {x: %s, y: %s, z: %s}" % (f3(bx + 3), f3(by), f3(bz)), b)
    b = re.sub(r"(?m)^    summonAt: .*$", "    summonAt: {x: %s, y: %s, z: %s}" % (f3(bx), f3(by), f3(bz)), b)
    folk = folk[:blk.start()] + b + folk[blk.end():]
    s["id"] = pid_
if not PREVIEW: open(FOLK, "w", encoding="utf-8", newline="\n").write(folk)

# ---------------------------------------------------------------- the patches, laid over the layout's map
COL = {"original": "#3cb44b", "tainted": "#9b4dff", "blighted": "#e6194b"}
svg = []
for n, p in enumerate(P):
    rows = defaultdict(list)
    for i in p["cells"]: rows[i // W].append(i % W)
    d = []
    for y, xs in sorted(rows.items()):
        s = prev = xs[0]
        for x in xs[1:] + [None]:
            if x is None or x != prev + 1:
                d.append("M%d,%dh%dv%dh%dz" % (s * C, y * C, (prev - s + 1) * C, C, -(prev - s + 1) * C)); s = x
            if x is not None: prev = x
    svg.append('<path class="pt" data-i="%d" d="%s" fill="%s" fill-opacity="0.33"/>' % (n, "".join(d), COL[p["tier"]]))
edges = []
for y in range(H):
    for x in range(W):
        a = pid[idx(x, y)]
        if a < 0: continue
        if x + 1 < W and pid[idx(x + 1, y)] >= 0 and pid[idx(x + 1, y)] != a: edges.append("M%d,%dv%d" % ((x + 1) * C, y * C, C))
        if y + 1 < H and pid[idx(x, y + 1)] >= 0 and pid[idx(x, y + 1)] != a: edges.append("M%d,%dh%d" % (x * C, (y + 1) * C, C))
svg.append('<path d="%s" stroke="#222" stroke-width="0.8" stroke-opacity="0.55" fill="none" pointer-events="none"/>' % "".join(edges))
LBL = 'font-weight="bold" text-anchor="middle" stroke="#fff" stroke-width="2.5" paint-order="stroke" pointer-events="none"'
# NOTHING DRAWN COVERS ANYTHING ELSE: every mark and name the layout already drew is a box, and every star, level number
# and name added here takes the first free spot round where it belongs, nearest first; a name set more than one step
# away from its mark gets a thin line back to it.
boxes = []; placed = []
def box(x, y, size, text): w = 0.58 * size * len(text) + 4; return (x - w / 2, y - size, x + w / 2, y + 3)
def hits(b): return sum(1 for o in boxes if b[0] < o[2] and o[0] < b[2] and b[1] < o[3] and o[1] < b[3])
def square(x, y, r): return (x - r, y - r, x + r, y + r)
for t in L["towns"]: boxes += [box(t["x"], t["y"] - 18, 17, t["name"]), square(t["x"], t["y"], 13)]
for m in L["landmarks"]: boxes += [box(m["x"], m["y"] + 25, 13, m["name"]), square(m["x"], m["y"], 10)]
for c in L["caves"]: boxes += [box(c["x"], c["y"] + 24, 12, "cave"), square(c["x"], c["y"], 11)]
for d in L["dungeon_doors"]: boxes.append(square(d["x"], d["y"], 10))
for s_ in summoners: boxes.append(square(s_["at"][0], s_["at"][1], 9))
def spots_round(r, size, half, steps):
    ring = [(0, -r), (0, r + size), (r + half, size / 3), (-r - half, size / 3),
            (r + half, -r), (-r - half, -r), (r + half, r + size), (-r - half, r + size)]
    out = []
    for f in range(steps):
        g = f * (r + size)
        out += [(dx + g * (dx > 0) - g * (dx < 0), dy + g * (dy > size / 3) - g * (dy < 0)) for dx, dy in ring]
    return ring, out
def place(x, y, size, text, r, steps=4, line=True, at_mark=False):
    ring, spots = spots_round(r, size, 0.29 * size * len(text) + 4, steps)
    if at_mark: spots = [(0, size / 3)] + spots
    b_ = lambda k: box(x + spots[k][0], y + spots[k][1], size, text)
    best = min(range(len(spots)), key=lambda k: (hits(b_(k)), k))
    tx, ty = x + spots[best][0], y + spots[best][1]; placed.append(b_(best)); boxes.append(placed[-1])
    if line and best - (1 if at_mark else 0) >= len(ring):
        svg.append('<line x1="%.0f" y1="%.0f" x2="%.0f" y2="%.0f" stroke="#000" stroke-width="0.8" pointer-events="none"/>' % (x, y, tx, ty - size / 3))
    return tx, ty
def star(x, y, r):
    pts = []
    for k in range(10):
        a = -math.pi / 2 + k * math.pi / 5; rr = r if k % 2 == 0 else r * 0.42
        pts.append("%.1f,%.1f" % (x + rr * math.cos(a), y + rr * math.sin(a)))
    return " ".join(pts)
for d in dungeons: boxes.append(square(d["x"], d["y"], 10))
stars = []
for p, e in elite_marks:                                  # a star: the free spot nearest above its patch's middle
    ring, spots = spots_round(6, 10, 10, 3); spots = [(0, -16)] + spots
    k = min(range(len(spots)), key=lambda k: (hits(square(p["cx"] + spots[k][0], p["cy"] + spots[k][1], 9)), k))
    x, y = p["cx"] + spots[k][0], p["cy"] + spots[k][1]; boxes.append(square(x, y, 9)); stars.append((x, y, e))
for p in P:
    if len(p["cells"]) >= 60:
        text = "%d–%d" % p["lv"]; tx, ty = place(p["cx"], p["cy"], 11, text, 6, steps=2, line=False, at_mark=True)
        svg.append('<text x="%.0f" y="%.0f" font-size="11" fill="#000" %s>%s</text>' % (tx, ty, LBL, text))
for x, y, e in stars:
    text = "%s %d" % (e["name"], e["level"])
    svg.append('<polygon points="%s" fill="#ffd700" stroke="#000" stroke-width="1" pointer-events="none"/>' % star(x, y, 8))
    tx, ty = place(x, y, 10.5, text, 11)
    svg.append('<text x="%.0f" y="%.0f" font-size="10.5" fill="#7a4b00" %s>%s</text>' % (tx, ty, LBL, text))
for s in summoners:
    x, y = s["at"]; text = "%s %d" % (s["boss"]["name"].split(",")[0], s["boss"]["level"])
    svg.append('<polygon points="%.0f,%.0f %.0f,%.0f %.0f,%.0f %.0f,%.0f" fill="#ff00c8" stroke="#000" stroke-width="1.2"/>' % (x, y - 8, x + 8, y, x, y + 8, x - 8, y))
    tx, ty = place(x, y, 11, text, 11)
    svg.append('<text x="%.0f" y="%.0f" font-size="11" fill="#000" %s>%s</text>' % (tx, ty, LBL, text))
for n, d in enumerate(dungeons):
    x, y = d["x"], d["y"]; lvls = [m["level"] for f in d["floors"] for m in f["kinds"]]; text = "%s · Lv %d–%d" % (d["name"], min(lvls), max(lvls))
    svg.append('<rect class="dg" data-i="%d" x="%.0f" y="%.0f" width="16" height="16" fill="#222" stroke="#ffd700" stroke-width="2.5"/>' % (n, x - 8, y - 8))
    tx, ty = place(x, y, 11, text, 11)
    svg.append('<text x="%.0f" y="%.0f" font-size="11" fill="#000" %s>%s</text>' % (tx, ty, LBL, text))
covered = sum(1 for a in placed + [square(x, y, 9) for x, y, _ in stars] if sum(1 for b in boxes if a[0] < b[2] and b[0] < a[2] and a[1] < b[3] and b[1] < a[3]) > 1)
print("stars, level numbers and names covering something else:", covered, "of", len(placed) + len(stars))
lx, ly = 1720, 850
svg.append('<text x="%d" y="%d" font-size="16" font-weight="bold">Creature spawns</text>' % (lx, ly))
def mults(ms):
    v = sorted({m["mult"] for m in ms}); return " or ".join("×%g" % x for x in v)
for k, (t, txt) in enumerate([("original", "Original %s — the nearest patches of each level" % mults(originals)),
                              ("tainted", "Tainted %s — farther" % mults([versions[o["id"]]["tainted"] for o in originals])),
                              ("blighted", "Blighted %s — farthest, never within 150 of a town" % mults([versions[o["id"]]["blighted"] for o in originals]))]):
    svg.append('<rect x="%d" y="%d" width="18" height="14" fill="%s" fill-opacity="0.6" stroke="#222"/><text x="%d" y="%d" font-size="14">%s</text>'
               % (lx, ly + 14 + k * 24, COL[t], lx + 26, ly + 26 + k * 24, txt))
y0 = ly + 14 + 3 * 24
svg.append('<text x="%d" y="%d" font-size="14">12–20 = levels of the creatures that live there (visitors not counted)</text>' % (lx, y0 + 12))
svg.append('<polygon points="%s" fill="#ffd700" stroke="#000"/><text x="%d" y="%d" font-size="14">elite living in that patch (name and level)</text>' % (star(lx + 9, y0 + 29, 8), lx + 26, y0 + 34))
svg.append('<polygon points="%d,%d %d,%d %d,%d %d,%d" fill="#ff00c8" stroke="#000"/><text x="%d" y="%d" font-size="14">summoner — calls the named boss for materials</text>'
           % (lx + 9, y0 + 44, lx + 17, y0 + 52, lx + 9, y0 + 60, lx + 1, y0 + 52, lx + 26, y0 + 57))
svg.append('<rect x="%d" y="%d" width="16" height="16" fill="#222" stroke="#ffd700" stroke-width="2.5"/><text x="%d" y="%d" font-size="14">dungeon — hover for its floors</text>' % (lx + 1, y0 + 66, lx + 26, y0 + 79))
svg.append('<text x="%d" y="%d" font-size="13">Hover a patch for its creatures and counts.</text>' % (lx, y0 + 100))
ey = y0 + 128
def as_itself(o): return any(o["id"] == r["m"]["id"] for p in P for r in p["res"])
cover = [("Originals living as themselves", sum(1 for o in originals if as_itself(o)), len(originals)),
         ("Tainted versions placed", sum(1 for o in originals if held(versions[o["id"]]["tainted"])), len(originals)),
         ("Blighted versions placed", sum(1 for o in originals if held(versions[o["id"]]["blighted"])), len(originals)),
         ("Field elites placed", len({e["id"] for _, e in elite_marks}), len(elites)),
         ("Summon-only elites with a summoner", sum(1 for s_ in summoners if s_["boss"]["kind"] == 1), len([b for b in summon_only if not b.startswith("mob.shadow.")])),
         ("Shadows with a summoner", sum(1 for s_ in summoners if s_["boss"]["kind"] == 2), len([b for b in summon_only if b.startswith("mob.shadow.")]))]
svg.append('<text x="%d" y="%d" font-size="15" font-weight="bold">The whole roster</text>' % (lx, ey))
for k, (what, got, of) in enumerate(cover):
    svg.append('<text x="%d" y="%d" font-size="13" fill="%s">%s: %d of %d</text>' % (lx, ey + 20 + k * 19, "#1a7a2e" if got == of else "#c0392b", what, got, of))
ey += 20 + len(cover) * 19 + 16
svg.append('<text x="%d" y="%d" font-size="15" font-weight="bold">Elites</text>' % (lx, ey))
by_e = defaultdict(list)
for p, e in elite_marks: by_e[e["id"]].append(p)
for k, e in enumerate(sorted({e["id"]: e for _, e in elite_marks}.values(), key=lambda e: (e["level"], e["id"]))):
    deep = [d["name"] for d in dungeons if d["elite"] and d["elite"]["id"] == e["id"]]
    svg.append('<text x="%d" y="%d" font-size="13">★ %s — Lv %d · %s · %d patches%s</text>' % (lx, ey + 20 + k * 19, e["name"], e["level"], by_e[e["id"]][0]["ground"],
               len(by_e[e["id"]]), (" · deepest floor of " + ", ".join(deep)) if deep else ""))
legend_bottom = ey + 20 + len(by_e) * 19 + 10
info = []
for p in P:
    rs = [dict(n=r["m"]["name"], l=r["m"]["level"], t=r["tier"], x="%g" % r["m"]["mult"], c=r["n"], v=0) for r in p["res"]]
    v = p["visitor"]; rs.append(dict(n=v["m"]["name"], l=v["m"]["level"], t=v["tier"], x="%g" % v["m"]["mult"], c=1, v=1))
    if p.get("elite"):
        e = p["elite"]; rs.append(dict(n=e["name"], l=e["level"], t="elite", x="%g" % e["mult"], c=1, v=2))
    info.append(dict(g=p["ground"], a=round(p["area"]), r=rs))
dinfo = [dict(n=d["name"], f=[dict(a="%d–%d" % (min(m["level"] for m in f["kinds"]), max(m["level"] for m in f["kinds"])), t=f["tier"], k=[dict(n=m["name"], l=m["level"], x="%g" % m["mult"]) for m in f["kinds"]]) for f in d["floors"]],
              e=dict(n=d["elite"]["name"], l=d["elite"]["level"], x="%g" % d["elite"]["mult"]) if d["elite"] else None) for d in dungeons]
js = """<div id="tip" style="position:fixed;display:none;background:#fff;border:1px solid #444;border-radius:6px;padding:6px 9px;font:13px Segoe UI,Arial;pointer-events:none;box-shadow:0 2px 6px #0004;z-index:9"></div>
<script>const INFO=%s;const COL={original:'#3cb44b',tainted:'#9b4dff',blighted:'#e6194b',elite:'#b8860b'};const tip=document.getElementById('tip');
document.querySelectorAll('.pt').forEach(el=>{el.addEventListener('mousemove',ev=>{const p=INFO[+el.dataset.i];
tip.innerHTML='<b>'+p.g+'</b> · '+p.a+' sq units<br>'+p.r.map(r=>'<span style="color:'+COL[r.t]+'">■</span> '+r.n+' — Lv '+r.l+' · '+r.t+' ×'+r.x+' · '+r.c+' alive'+(r.v==1?' <i>(visitor)</i>':r.v==2?' <i>(elite)</i>':'')).join('<br>');
tip.style.display='block';tip.style.left=(ev.clientX+14)+'px';tip.style.top=(ev.clientY+14)+'px';});el.addEventListener('mouseleave',()=>tip.style.display='none');});
const DUN=%s;document.querySelectorAll('.dg').forEach(el=>{el.addEventListener('mousemove',ev=>{const d=DUN[+el.dataset.i];
tip.innerHTML='<b>'+d.n+'</b>'+d.f.map((f,i)=>'<br><u>Floor '+(i+1)+(i==d.f.length-1?' (deepest)':'')+' · Lv '+f.a+'</u>'+f.k.map(k=>'<br><span style="color:'+COL[f.t]+'">■</span> '+k.n+' — Lv '+k.l+' · '+f.t+' ×'+k.x).join('')).join('')+(d.e?'<br><span style="color:'+COL.elite+'">★</span> '+d.e.n+' — Lv '+d.e.l+' · elite ×'+d.e.x+' (deepest floor)':'');
tip.style.display='block';tip.style.left=(ev.clientX+14)+'px';tip.style.top=(ev.clientY+14)+'px';});el.addEventListener('mouseleave',()=>tip.style.display='none');});</script>""" % (json.dumps(info), json.dumps(dinfo))
html = open(OUT_MAP, encoding="utf-8").read()
html = re.sub(r"(?s)<!--spawns-->.*?<!--/spawns-->", "", html)          # the last run's patches, if any
# the picture grows to hold the whole key, never shorter than the layout's own map
map_h = int(re.search(r'<image [^>]*?height="(\d+)"', html).group(1)); full_h = max(map_h, legend_bottom)
html = re.sub(r'(<svg [^>]*?height=")\d+(" viewBox="0 0 \d+ )\d+(")', lambda m: m.group(1) + str(full_h) + m.group(2) + str(full_h) + m.group(3), html, count=1)
toggle = "document.getElementById('spawns').style.display = this.checked ? '' : 'none'"
shown = PREVIEW                                                          # a preview opens with the patches showing
html = html.replace("</svg>", '<!--spawns--><g id="spawns" style="display:%s">' % ("" if shown else "none") + "".join(svg) + "</g><!--/spawns--></svg>", 1)
html = html.replace("</body></html>", '<!--spawns--><label style="top:52px"><input type="checkbox"%s onchange="%s"> Show creature patches</label>'
                    % (" checked" if shown else "", toggle) + js + "<!--/spawns--></body></html>", 1)
open(PREVIEW_MAP if PREVIEW else OUT_MAP, "w", encoding="utf-8").write(html)

print("patches:", len(P), " rows:", sum(len(p["res"]) + 1 + (1 if p.get("elite") else 0) for p in P),
      " creatures alive:", sum(sum(r["n"] for r in p["res"]) + 1 for p in P) + len(elite_marks))
print("versions:", dict(Counter(p["tier"] for p in P)), " field elites:", len(elite_marks), " summoners placed:", len(summoners))
for what, got, of in cover: print("%s: %d of %d" % (what, got, of))
print("elite patches within", RING, "of a town:", sum(1 for p, e in elite_marks if p["dist"] < RING))
bad_order = [d["name"] for d in dungeons if any(a["tier"] == b["tier"] and max(m["level"] for m in a["kinds"]) > max(m["level"] for m in b["kinds"]) for a, b in zip(d["floors"], d["floors"][1:]))]
bad_tier = [d["name"] for d in dungeons if any(a["tier"] == "blighted" and b["tier"] == "tainted" for a, b in zip(d["floors"], d["floors"][1:]))]
print("dungeons whose Tainted or Blighted floors drop in level going down:", bad_order, " Blighted floor above a Tainted one:", bad_tier,
      " deepest floor not Blighted:", [d["name"] for d in dungeons if d["floors"][-1]["tier"] != "blighted"])
print("blighted visitors within", RING, "of a town:", sum(1 for p in P if p["visitor"]["tier"] == "blighted" and p["dist"] < RING))
print("blighted patches within", RING, "of a town:", sum(1 for p in P if p["tier"] == "blighted" and p["dist"] < RING), "of", sum(1 for p in P if p["tier"] == "blighted"))
sp = sorted(p["lv"][1] - p["lv"][0] for p in P); print("patch level spread: median", sp[len(sp) // 2], "max", sp[-1], " over 20:", sum(1 for x in sp if x > 20))
for d in dungeons: print(d["name"], [(f["tier"], len(f["kinds"]), min(m["level"] for m in f["kinds"]), max(m["level"] for m in f["kinds"])) for f in d["floors"]], d["elite"] and (d["elite"]["name"], d["elite"]["level"]))
fs = [max(m["level"] for m in f["kinds"]) - min(m["level"] for m in f["kinds"]) for d in dungeons for f in d["floors"]]; print("floor level spread: max", max(fs))
print("dungeons:", len(dungeons), " kinds on their floors:", sum(len(f["kinds"]) for d in dungeons for f in d["floors"]))
if PREVIEW: print("preview only, no game data written - drew", PREVIEW_MAP)
else: print("wrote", SPAWNS); print("wrote", FOLK); print("drew", OUT_MAP)
