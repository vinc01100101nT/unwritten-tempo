"""THE WORLD LAYOUT — one file that says where everything big in the world is, and the map drawn from it.

Writes data/world-layout.json (the one source every area build will read) and draws the map from that file alone, so
the map and the world can never disagree. Run it again after changing a place below; the same inputs always give the
same world.

    python tools/world-layout/build_world_layout.py [map.html]

The map is written beside the file, data/world-layout.html, on every run — the one picture of the layout, never
out of date with it. At the end it runs tools/world-spawns/build_world_spawns.py, which works the creatures' patches out
of the new layout and lays them over the same map.

What is decided by hand (the PLACES block) and what is worked out from it:
  - land and sea: a noise coast grown round the places, with bays, a strait, and a ring of sea round each small island;
  - height: rising from the coast, mountain ranges, rolling hills, the ground flattened under each town;
  - rivers: from each spring, always downhill on the ground with its hollows filled, until they reach the sea;
  - themes: each land cell belongs to the nearest town on its own landmass, the border wavy, the colours blended;
  - roads: the cheapest walk between two towns over the ground (slopes cost more, crossing a river costs a bridge),
    then smoothed — so they wind round hills and cross rivers at one bridge each.
Units are world units (our person is 0.327 tall). Heights are in world units too.
"""
import heapq, json, math, os, random, sys, base64, struct, zlib

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_JSON = os.path.join(ROOT, "data", "world-layout.json")
OUT_MAP = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "data", "world-layout.html")
CHUNK = 32                                # the world is built and streamed in squares this wide (128 ground points of 0.25)

# ---------------------------------------------------------------- PLACES (decided with the user, 2026-10-27)
SEED = 7
W, H = 1700, 1480
C = 4                                   # one cell is 4 x 4 world units
TOWNS = [  # id, name, weapon, x, y, theme, map colour
    ("lastline", "Lastline", "", 640, 640, "green trees + green willow, mixed meadows", "#7cc46a"),
    ("old_oak", "Old Oak", "Sniper", 900, 560, "open grass, lone oaks", "#a8d470"),
    ("parley", "Parley", "Sword", 620, 470, "Blossom Wind: pink blossom + pink willow, pink meadows", "#f29ac0"),
    ("harrowen", "Harrowen", "Axe", 470, 700, "Wildfire: green woods, orange meadows, branches", "#e8913a"),
    ("pennaryl", "Pennaryl", "Bow", 300, 560, "thick forest + firs", "#2f7d3a"),
    ("gemmara", "Gemmara", "Warhammer", 660, 260, "Earthrend: firs, rocks, cliffs", "#5b8f7a"),
    ("hilt", "Hilt", "Greatsword", 1150, 740, "Raging Tide: beach, sand, rocks, cliffs", "#efdca0"),
    ("tornel", "Tornel", "Shuriken", 430, 330, "Red Autumn: red trees + red willow, yellow leaves", "#d1472f"),
    ("hazemere", "Hazemere", "Katar", 960, 800, "Veiled Mirage: desert (old props kept)", "#e3c27a"),
    ("pluvenne", "Pluvenne", "Spear", 1170, 480, "Bending River: blue willows, reeds, lilies", "#5aa0d8"),
    ("luminhal", "Luminhal", "Fist", 1560, 560, "Northern Lights: blue broadleaf, blue-purple meadows", "#6f8fe0"),
    ("halcyra", "Halcyra", "Wand", 250, 200, "Morning Mist: blue willows, white meadows", "#bcd8f0"),
    ("velmora", "Velmora", "Dagger", 1000, 330, "Nightshade Bloom: purple trees + purple willow, purple meadows", "#8e5bc8"),
    ("cindrel", "Cindrel", "Staff", 560, 1310, "volcanic (old props kept), dark isle", "#5a3b33"),
    ("kharnel", "Kharnel", "Scythe", 300, 1330, "haunted (old props kept), dark isle", "#4a4458"),
]
ROADS = [("lastline", "parley"), ("lastline", "harrowen"), ("lastline", "old_oak"), ("parley", "tornel"),
         ("parley", "gemmara"), ("harrowen", "pennaryl"), ("pennaryl", "tornel"), ("old_oak", "velmora"),
         ("old_oak", "pluvenne"), ("old_oak", "hazemere"), ("kharnel", "cindrel"), ("tornel", "halcyra"),
         ("lastline", "hazemere"), ("pluvenne", "hilt"), ("hazemere", "hilt")]
SPRINGS = [(700, 90), (1050, 220), (330, 250)]          # where the three rivers start, in the hills
RIVER_HINTS = [[(690, 200), (600, 300), (560, 420), (540, 560), (560, 680), (600, 800), (640, 910)],
               [(1080, 330), (1110, 420), (1150, 500), (1200, 530), (1250, 522), (1272, 560), (1242, 588), (1268, 618)],   # Narrowbend's hairpin past Pluvenne
               [(360, 380), (330, 480), (250, 560), (150, 600)]]   # the course agreed on the preview: the ground is shaped to it
# Channels joining the rivers into one water (2026-10-28): (river the water leaves, river it runs into, its course).
# Each course is the cheapest found over this ground, clear of towns, landmarks, ponds, caves and dungeon doors, along
# which the bed only ever falls — never raised over lower ground — and arrives no lower than the river it joins.
RIVER_LINKS = [(2, 0, [(338, 266), (378, 290), (418, 282), (458, 282), (498, 310), (538, 310)]),
               (0, 1, [(710, 254), (726, 294), (766, 334), (806, 350), (846, 358), (886, 358), (926, 354), (966, 366),
                       (1006, 378), (1046, 362)])]
# Each shadow's place stands in its own weapon town's land, on the far side from the town (decided 2026-10-29).
LANDMARKS = [("last_bridge", "Last Bridge", 536, 195), ("hill_of_the_flame", "Hill of the Flame", 312, 112),
             ("thousand_gates", "Thousand Gates", 440, 488), ("narrowbend", "Narrowbend", 1252, 556),
             ("masked_ballroom", "Masked Ballroom", 1168, 296), ("burning_keel", "Burning Keel", 376, 806),
             ("everburning_plain", "Everburning Plain", 500, 1400), ("sky_cairn", "Sky Cairn", 144, 584),
             ("tide_temple", "Tide Temple", 1300, 800), ("dusk_tower", "Dusk Tower", 976, 584),
             ("walking_road", "Walking Road", 984, 896), ("drowned_chapel", "Drowned Chapel", 208, 1352),
             ("aurora_steps", "Aurora Steps", 1575, 455), ("unmarked_path", "Unmarked Path", 472, 136)]
# The coast was agreed on these points (where the landmarks first stood, and the rivers' first courses): the land is
# grown round them, so a place or a river bend moving never moves the coast.
LAND_ANCHORS = [(590, 760), (1120, 250), (880, 110), (500, 420), (400, 1260), (640, 1390), (500, 1400), (300, 250),
                (1300, 800), (420, 1360), (780, 380), (200, 1260), (690, 150), (830, 720),
                (690, 200), (600, 300), (560, 420), (540, 560), (560, 680), (600, 800), (640, 910),
                (1080, 330), (1110, 420), (1150, 500), (1200, 530), (360, 380), (330, 480), (250, 560), (150, 600)]
DUNGEON_DOORS = [("lastline", 700, 690), ("old_oak", 940, 610), ("hazemere", 1010, 850), ("hilt", 1190, 790),
                 ("cindrel", 600, 1350), ("kharnel", 260, 1380)]
MOUNTAINS = [(620, 150, 220, 110, 34), (790, 120, 190, 100, 30), (1070, 180, 150, 100, 26),   # x, y, half-width, half-depth, peak height
             (440, 488, 40, 40, 16),        # the Thousand Gates' climb, in Parley's land
             (312, 112, 28, 28, 6),         # the Hill of the Flame's top, on Halcyra's high ground
             (976, 584, 30, 30, 6),         # the rise the Dusk Tower stands on, above Old Oak
             (1575, 455, 32, 32, 14),       # Luminhal's mountain, under the Aurora Steps
             (376, 830, 55, 55, 20)]        # Harrowen's volcano, the Burning Keel on its lip
CRATERS = [(376, 830, 14, 12)]
# THE MOUNTAIN'S ROOT (decided 2026-10-29): its cave stands on a round rock ringed by a sheer gorge at the mountain's
# foot, and the Last Bridge is the one way across. (cave id, rock middle x, y, rock radius, gorge width, how far the
# gorge's floor lies below the lowest ground round it,
# which way the bridge leaves the rock, the landmark on the bridge.) The cave's rock (48 x 44) sits on the rock's back
# half, its mouth facing the bridge.
GORGES = [("mountains_root", 580, 195, 36, 16, 14, (-1, 0), "last_bridge")]   # the bridge leaves westward, where the far side stands level with the rock              # a hollow in a mountain's top: x, y, radius, depth
# open flat ground, levelled like a town's: x, y, radius (the Sky Cairn's steppe at Pennaryl's forest edge)
CLEARINGS = [(144, 584, 50)]
BOATS = [("south_coast", "dark_isle", (400, 900), (380, 1210)), ("pluvenne", "luminhal", (1265, 500), (1500, 555))]
# a path across the sea at wading depth, from land to land (the world cuts its way down each sea cliff)
LOW_TIDE_PATHS = [("hilt", "tide_temple", (1160, 800), (1265, 800))]
# caves like the test map's (a block of rock with a hall inside and one way in), each opened out of the foot of the
# nearest cliff to its spot: (id, the town it belongs near, x, y)
# raised blocks with a cliff all round: Hilt's sea-cliff headland, and the forest ridge west of Pennaryl (x, y, radius, rise)
HEADLANDS = [(1245, 690, 45, 2.5), (200, 600, 50, 2.5)]
# high ground: a town raised as a tableland, its shore a sea cliff, its land falling in one long walkable slope toward
# another town (town, toward, height in world units, how far round it the rise reaches)
HIGH_GROUND = [("halcyra", "tornel", 24.0, 260)]
# ponds: a hollow of the ground filled with water, gentle banks all round (id, the town it belongs near, x, y, radius).
# Each is set down on the nearest spot to its x, y that is flat land, clear of rivers, towns and landmarks.
PONDS = [("lastline_pond", "lastline", 590, 700, 22), ("pluvenne_pond", "pluvenne", 1110, 560, 14),
         ("pennaryl_pond", "pennaryl", 360, 640, 20), ("hazemere_oasis", "hazemere", 950, 700, 18)]
CAVES = [("mountains_root", "gemmara", 650, 190), ("sea_cave", "hilt", 1240, 760),
         ("forest_cave", "pennaryl", 200, 600), ("dark_cave", "kharnel", 430, 1300)]
SMALL_ISLANDS = [[(1560, 560), (1563, 530), (1567, 500), (1572, 470)], [(1300, 800)]]   # each island the points it is grown round (Luminhal long enough for its mountain)
ISLAND_PTS = [p for isl in SMALL_ISLANDS for p in isl]
BAYS = [(150, 410, 110), (800, 990, 150), (1300, 330, 90), (40, 760, 120), (980, 40, 120)]

# ---------------------------------------------------------------- noise, the same every run
rng = random.Random(SEED)
G = 64
lattice = {(i, j): rng.random() for i in range(-1, W // G + 3) for j in range(-1, H // G + 3)}   # (the agreed preview's own noise, kept exactly)
def vnoise(x, y, scale, off):
    x, y = x / scale + off, y / scale + off * 0.7
    i, j = int(math.floor(x)), int(math.floor(y)); fx, fy = x - i, y - j
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    g = lambda a, b: lattice[((a % (W // G + 2)), (b % (H // G + 2)))]
    return (g(i, j) * (1 - fx) + g(i + 1, j) * fx) * (1 - fy) + (g(i, j + 1) * (1 - fx) + g(i + 1, j + 1) * fx) * fy
def fbm(x, y):
    return vnoise(x, y, 230, 0.0) * 0.55 + vnoise(x, y, 95, 3.3) * 0.3 + vnoise(x, y, 38, 7.1) * 0.15 - 0.5

# ---------------------------------------------------------------- land and sea
continent = [(t[3], t[4]) for t in TOWNS if t[4] < 950 and t[0] != "luminhal"] + \
            [(x, y) for x, y in LAND_ANCHORS if y < 950 and (x, y) != (1300, 800)] + SPRINGS
isle = [(t[3], t[4]) for t in TOWNS if t[4] >= 950] + [(x, y) for x, y in LAND_ANCHORS if y >= 950]
def gauss(x, y, pts, sigma):
    return max(math.exp(-((x - a) ** 2 + (y - b) ** 2) / (2 * sigma * sigma)) for a, b in pts)
def land_field(x, y):
    v = -0.55 + gauss(x, y, continent, 150) * 1.15 + gauss(x, y, isle, 110) * 1.05 + gauss(x, y, ISLAND_PTS, 45) * 0.95
    for a, b, r in BAYS: v -= math.exp(-((x - a) ** 2 + (y - b) ** 2) / (2 * r * r)) * 0.9
    if 920 < y < 1150: v -= 1.4 * (1 - abs(y - 1035) / 115)
    for isl in SMALL_ISLANDS:
        d = min(math.hypot(x - a, y - b) for a, b in isl)
        if 50 < d < 120: v -= 0.9 * math.sin((d - 50) / 70 * math.pi)
    edge = min(x, y, W - x, H - y)
    if edge < 90: v -= (90 - edge) / 90 * 1.2
    return v + fbm(x, y) * 0.95
NX, NY = W // C, H // C
def centre(i, j): return i * C + C / 2, j * C + C / 2
def cell_of(x, y): return min(NX - 1, max(0, int(x // C))), min(NY - 1, max(0, int(y // C)))
idx = lambda i, j: j * NX + i
land = [False] * (NX * NY)
for j in range(NY):
    for i in range(NX):
        land[idx(i, j)] = land_field(*centre(i, j)) > 0
for x, y in continent + isle + ISLAND_PTS:              # every place stands on land
    ci, cj = cell_of(x, y)
    for j in range(cj - 6, cj + 7):
        for i in range(ci - 6, ci + 7):
            if 0 <= i < NX and 0 <= j < NY: land[idx(i, j)] = True
N8 = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)]
def bfs_distance(sources):
    dist = [10 ** 9] * (NX * NY); q = list(sources)
    for k in q: dist[k] = 0
    h = 0
    while h < len(q):
        k = q[h]; h += 1; i, j = k % NX, k // NX
        for di, dj in N8[:4]:
            a, b = i + di, j + dj
            if 0 <= a < NX and 0 <= b < NY and dist[idx(a, b)] > dist[k] + 1:
                dist[idx(a, b)] = dist[k] + 1; q.append(idx(a, b))
    return dist
coast_dist = bfs_distance([k for k in range(NX * NY) if not land[k]])       # land: cells to the sea
sea_dist = bfs_distance([k for k in range(NX * NY) if land[k]])             # sea: cells to the land

# which landmass each land cell is on
mass = [-1] * (NX * NY); n_mass = 0
for k in range(NX * NY):
    if land[k] and mass[k] < 0:
        q = [k]; mass[k] = n_mass
        while q:
            c = q.pop(); i, j = c % NX, c // NX
            for di, dj in N8[:4]:
                a, b = i + di, j + dj
                if 0 <= a < NX and 0 <= b < NY and land[idx(a, b)] and mass[idx(a, b)] < 0:
                    mass[idx(a, b)] = n_mass; q.append(idx(a, b))
        n_mass += 1

# ---------------------------------------------------------------- height
height = [0.0] * (NX * NY)
plat = [0.0] * (NX * NY)
river_hint_pts = [p for hint in RIVER_HINTS for p in hint]
def seg_dist(px, py, a, b):
    (ax, ay), (bx, by) = a, b
    dx, dy = bx - ax, by - ay; L2 = dx * dx + dy * dy
    t = 0 if L2 == 0 else max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / L2))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)
hint_paths = [[s] + h for s, h in zip(SPRINGS, RIVER_HINTS)]
# THE LEVELS: the land is cut into plateaus, everywhere — a slow noise, a little higher inland, read in steps. Along
# part of each step's edge the ground jumps the whole step inside one cell (a cliff); along the rest it climbs as a
# walkable slope, so a cliff always ends somewhere and no level is shut in. Sea cliffs where a high step meets the shore.
# Each step rises 2.5 units (decided with the user 2026-10-27): no taller than the nature pack's tallest cliff rock at true
# size (3.1), so the rocks can cover every face.
LEVELS = [(0.50, 2.5), (0.60, 2.5), (0.70, 2.5), (0.80, 2.5), (0.90, 2.5)]   # (where the step starts, how much it rises in world units)
def plateau(x, y, to_sea):
    q = vnoise(x, y, 280, 41.0) * 0.65 + vnoise(x, y, 110, 47.0) * 0.35 + min(to_sea * C, 240) / 240 * 0.3
    cliff = vnoise(x, y, 170, 61.0) > 0.3                 # most of every edge is cliff; the rest a slope, so no level is shut in
    width = 0.002 if cliff else 0.16                      # how far across q the step takes: a wall, or a long slope
    out = 0.0
    for at, rise in LEVELS:
        t = max(0.0, min(1.0, (q - at) / width))
        out += rise * t * t * (3 - 2 * t)
    return out
for j in range(NY):
    for i in range(NX):
        x, y = centre(i, j); k = idx(i, j)
        if not land[k]:
            height[k] = -1.0 - min(sea_dist[k] * C, 200) * 0.03
            continue
        h = min(coast_dist[k] * C, 160) / 160 * 6.0
        mount = sum(peak * math.exp(-(((x - mx) / rx) ** 2 + ((y - my) / ry) ** 2)) for mx, my, rx, ry, peak in MOUNTAINS)
        h += mount
        h -= sum(dep * math.exp(-((x - cx) ** 2 + (y - cy) ** 2) / (cr * cr)) for cx, cy, cr, dep in CRATERS)
        h += (vnoise(x, y, 120, 11.0) - 0.5) * 8 + (vnoise(x, y, 40, 17.0) - 0.5) * 2
        plat[k] = plateau(x, y, coast_dist[k]) * max(0.0, 1 - mount / 8)   # mountains rise as one mass, not in steps
        for town, toward, rise, reach_r in HIGH_GROUND:
            a = next(t for t in TOWNS if t[0] == town); b = next(t for t in TOWNS if t[0] == toward)
            ax, ay, bx, by = a[3], a[4], b[3], b[4]
            along = ((x - ax) * (bx - ax) + (y - ay) * (by - ay)) / ((bx - ax) ** 2 + (by - ay) ** 2)   # 0 at the town, 1 at the other
            near = max(0.0, 1 - math.hypot(x - ax, y - ay) / reach_r)
            fall = max(0.0, min(1.0, 1 - along))
            plat[k] += rise * fall * min(1.0, near * 2.5)
        for hx, hy, hr, rise in HEADLANDS:
            if math.hypot(x - hx, y - hy) < hr + (vnoise(x, y, 30, 71.0) - 0.5) * 16: plat[k] += rise
        # the agreed river courses run along valleys
        dv = min(seg_dist(x, y, p[a], p[a + 1]) for p in hint_paths for a in range(len(p) - 1))
        if dv < 60: h -= (1 - dv / 60) ** 2 * 6
        height[k] = max(0.3, h)
# the rolling ground between cliffs is kept gentle, so only the levels make cliffs: smooth it, then stand it on its level
for _ in range(4):
    nxt = height[:]
    for j in range(1, NY - 1):
        for i in range(1, NX - 1):
            k = idx(i, j)
            if land[k]:
                around = [height[n] for n in (k - 1, k + 1, k - NX, k + NX) if land[n]]
                nxt[k] = (height[k] * 2 + sum(around)) / (2 + len(around))
    height = nxt
for k in range(NX * NY):
    if land[k]: height[k] = max(0.3, height[k]) + plat[k]
# EACH TOWN ON FLAT GROUND, out past its farthest corner: level to TOWN_REACH + TOWN_CLEAR from its middle, then easing
# into the land around over TOWN_EASE, so no slope or cliff starts at a town's wall. TOWN_REACH is Lastline's
# half-diagonal (its ground is 49 x 62 units), the one town built; every town is given that room.
TOWN_REACH, TOWN_CLEAR, TOWN_EASE = 39.6, 8, 24
for tid, name, wpn, tx, ty, *_ in TOWNS:
    ci, cj = cell_of(tx, ty); flat = height[idx(ci, cj)]
    flat_r = TOWN_REACH + TOWN_CLEAR; out_r = flat_r + TOWN_EASE; reach = int(out_r / C) + 1
    for j in range(cj - reach, cj + reach + 1):
        for i in range(ci - reach, ci + reach + 1):
            if 0 <= i < NX and 0 <= j < NY and land[idx(i, j)]:
                d = math.hypot(i - ci, j - cj) * C
                if d < out_r:
                    k = max(0.0, d - flat_r) / TOWN_EASE; k = k * k * (3 - 2 * k)
                    height[idx(i, j)] += (flat - height[idx(i, j)]) * (1 - k)
for cx, cy, cr in CLEARINGS:                              # the same levelling as a town's, at the clearing's own radius
    ci, cj = cell_of(cx, cy); flat = height[idx(ci, cj)]
    out_r = cr + TOWN_EASE; reach = int(out_r / C) + 1
    for j in range(cj - reach, cj + reach + 1):
        for i in range(ci - reach, ci + reach + 1):
            if 0 <= i < NX and 0 <= j < NY and land[idx(i, j)]:
                d = math.hypot(i - ci, j - cj) * C
                if d < out_r:
                    k = max(0.0, d - cr) / TOWN_EASE; k = k * k * (3 - 2 * k)
                    height[idx(i, j)] += (flat - height[idx(i, j)]) * (1 - k)

# ---------------------------------------------------------------- rivers: the agreed course, its bed always falling to the sea
rivers = []; river_cell = set()
def lay_bed(c, bed):
    ci, cj = c % NX, c // NX
    # the valley: its sides rise gently from the bed until they meet the ground, so a river through high ground
    # runs in a wide valley, never between walls that leave a pit (sides 0.2 units up per unit out)
    for v in range(cj - 30, cj + 31):
        for u in range(ci - 30, ci + 31):
            if 0 <= u < NX and 0 <= v < NY and land[idx(u, v)]:
                d = math.hypot(u - ci, v - cj) * C
                if bed + d * 0.2 < height[idx(u, v)]: height[idx(u, v)] = bed + d * 0.2
    height[c] = bed
    for u, v in [(ci, cj), (ci + 1, cj), (ci - 1, cj), (ci, cj + 1), (ci, cj - 1)]:   # the water is three cells wide
        if 0 <= u < NX and 0 <= v < NY and land[idx(u, v)]: river_cell.add(idx(u, v))
for spring, hint in zip(SPRINGS, RIVER_HINTS):
    pts = [spring] + hint
    (ax, ay), (bx, by) = pts[-2], pts[-1]
    dx, dy = bx - ax, by - ay; Ln = math.hypot(dx, dy); dx, dy = dx / Ln, dy / Ln
    x, y = bx, by
    while land[idx(*cell_of(x, y))] and 0 < x < W and 0 < y < H: x += dx * 2; y += dy * 2
    pts.append((x + dx * 8, y + dy * 8))                   # out past the shore into the sea
    path = []
    for p0, p1 in zip(pts, pts[1:]):
        steps = max(1, int(math.hypot(p1[0] - p0[0], p1[1] - p0[1]) / 2))
        for n in range(steps):
            c = idx(*cell_of(p0[0] + (p1[0] - p0[0]) * n / steps, p0[1] + (p1[1] - p0[1]) * n / steps))
            if not path or path[-1] != c: path.append(c)
    mouth = next((n for n, c in enumerate(path) if not land[c]), len(path) - 1)
    top = height[path[0]]; last = top + 1
    for n, c in enumerate(path[:mouth]):                     # the bed falls from the spring to the shore, never rising
        bed = min(top * (1 - n / max(1, mouth)) + 0.2 * (n / max(1, mouth)), height[c], last - 0.001)
        last = bed
        lay_bed(c, bed)
    rivers.append(path[:mouth + 1])

# ---------------------------------------------------------------- river links: a channel joining two rivers, so the three are one water
# Its bed is the lowest ground met so far from where it leaves, cutting through anything higher and never raised over
# anything lower, so the water runs one way and the channel is a valley like the rivers', never a bank.
link_ends = []
for a, b, course in RIVER_LINKS:
    ends = []
    for r, near in ((a, course[0]), (b, course[-1])):
        c = min((c for c in rivers[r] if land[c]), key=lambda c: math.dist(centre(c % NX, c // NX), near))
        ends.append(c)
    pts = [centre(ends[0] % NX, ends[0] // NX)] + course + [centre(ends[1] % NX, ends[1] // NX)]
    path = []
    for p0, p1 in zip(pts, pts[1:]):
        steps = max(1, int(math.hypot(p1[0] - p0[0], p1[1] - p0[1]) / 2))
        for n in range(steps + 1):
            c = idx(*cell_of(p0[0] + (p1[0] - p0[0]) * n / steps, p0[1] + (p1[1] - p0[1]) * n / steps))
            if not path or path[-1] != c: path.append(c)
    last = height[path[0]]
    for n, c in enumerate(path):
        bed = height[c] if n in (0, len(path) - 1) else min(last, height[c])
        last = bed
        lay_bed(c, bed)
    rivers.append(path)
    link_ends.append((a, b))

# ---------------------------------------------------------------- themes: nearest town on the same landmass, wavy
town_cell = {t[0]: idx(*cell_of(t[3], t[4])) for t in TOWNS}
theme = [-1] * (NX * NY); theme2 = [-1] * (NX * NY); blend = [0.0] * (NX * NY)
MIX = 60.0
for j in range(NY):
    for i in range(NX):
        k = idx(i, j)
        if not land[k]: continue
        x, y = centre(i, j)
        wx = (vnoise(x, y, 130, 23.0) - 0.5) * 220 + (vnoise(x, y, 45, 5.0) - 0.5) * 50; wy = (vnoise(x, y, 130, 31.0) - 0.5) * 220 + (vnoise(x, y, 45, 9.0) - 0.5) * 50
        best = [(math.hypot(x + wx - t[3], y + wy - t[4]), n) for n, t in enumerate(TOWNS) if mass[town_cell[t[0]]] == mass[k]]
        if not best:   # an island with no town of its own takes the nearest town's theme
            best = [(math.hypot(x - t[3], y - t[4]), n) for n, t in enumerate(TOWNS)]
        best.sort()
        theme[k] = best[0][1]
        if len(best) > 1:
            theme2[k] = best[1][1]; blend[k] = max(0.0, 0.5 - (best[1][0] - best[0][0]) / (2 * MIX))

for n, t in enumerate(TOWNS):                             # a town always stands in its own theme, 60 units round
    ci, cj = cell_of(t[3], t[4]); r = int(60 // C)
    for j in range(cj - r, cj + r + 1):
        for i in range(ci - r, ci + r + 1):
            if 0 <= i < NX and 0 <= j < NY and land[idx(i, j)] and math.hypot(i - ci, j - cj) <= r:
                if theme[idx(i, j)] != n: theme2[idx(i, j)] = theme[idx(i, j)]; blend[idx(i, j)] = 0.0
                theme[idx(i, j)] = n

# ---------------------------------------------------------------- gorges: a rock ringed by a sheer drop, after the rivers so no valley wears it down
gorge_rock = set(); gorges = []
for cid, gx, gy, gr, gw, gd, (bx, by), lid in GORGES:
    top = height[idx(*cell_of(gx, gy))]
    rim = [height[idx(*cell_of(gx + math.cos(a / 36 * math.tau) * (gr + gw + 6), gy + math.sin(a / 36 * math.tau) * (gr + gw + 6)))] for a in range(36)]
    floor = min(rim) - gd
    ci, cj = cell_of(gx, gy); reach_c = int((gr + gw) / C) + 2
    for j in range(cj - reach_c, cj + reach_c + 1):
        for i in range(ci - reach_c, ci + reach_c + 1):
            if not (0 <= i < NX and 0 <= j < NY and land[idx(i, j)]): continue
            d = math.dist(centre(i, j), (gx, gy))
            if d < gr: height[idx(i, j)] = top; gorge_rock.add(idx(i, j))
            elif d < gr + gw: height[idx(i, j)] = floor
    # the one way back up from the floor: a cutting climbing out on the lowest side clear of towns and rivers, at most 0.2 up per unit,
    # so nobody who falls in is trapped and nobody walks from it onto the rock (the rock's wall stays sheer)
    def cutting(a):
        """The cells of a way out leaving at angle a, each with the height it is cut to, or None when it would run
        within reach of a town or a river."""
        ux, uy = math.cos(a / 36 * math.tau), math.sin(a / 36 * math.tau); r = gr + gw / 2; out = []
        while True:
            want = floor + max(0.0, r - (gr + gw)) * 0.2
            x0, y0 = gx + ux * r, gy + uy * r
            if r > gr + gw and want >= height[idx(*cell_of(x0, y0))]: return out
            if any(math.hypot(x0 - t[3], y0 - t[4]) < TOWN_REACH + TOWN_CLEAR + TOWN_EASE for t in TOWNS): return None
            for side in (-6, -3, 0, 3, 6):
                k = idx(*cell_of(x0 - uy * side, y0 + ux * side))
                ki, kj = k % NX, k // NX
                if any(idx(u, v) in river_cell for u in range(ki - 6, ki + 7) for v in range(kj - 6, kj + 7) if 0 <= u < NX and 0 <= v < NY): return None
                out.append((k, want))
            r += 2
    way_out = next(w for w in (cutting(a) for a in sorted(range(36), key=lambda a: rim[a])) if w)
    for k, want in way_out:
        if land[k] and k not in gorge_rock and height[k] > want: height[k] = want
    mouth = (gx + bx * (gr - 14), gy + by * (gr - 14))
    gorges.append({"id": lid, "cave": cid, "x": gx, "y": gy, "rock_radius": gr, "width": gw, "rock_top": round(top, 2), "top": top,
                   "floor": round(floor, 2), "lowest_rim": round(min(rim), 2),
                   "bridge": {"from": [gx + bx * (gr - 4), gy + by * (gr - 4)], "to": [gx + bx * (gr + gw + 4), gy + by * (gr + gw + 4)]},
                   "mouth": mouth, "into": [-bx, -by]})

# ---------------------------------------------------------------- ponds: the ground dipped and filled
ponds = []; pond_cell = set()
def walked_from_towns():
    seen = set(town_cell.values()); q = list(seen)
    while q:
        k = q.pop(); i, j = k % NX, k // NX
        for a, b in N8[:4]:
            u, v = i + a, j + b
            if 0 <= u < NX and 0 <= v < NY:
                n = idx(u, v)
                if n not in seen and land[n] and abs(height[n] - height[k]) <= 1.0: seen.add(n); q.append(n)
    return seen
on_foot = walked_from_towns()                  # a pond goes where players can walk, never on top of a mesa
keep_clear = [(t[3], t[4], 60) for t in TOWNS] + [(x, y, 30) for i2, n, x, y in LANDMARKS] + [(x, y, 30) for t, x, y in DUNGEON_DOORS]
for pid, near, px, py, pr in PONDS:
    best = None
    for dj in range(-50, 51, 2):
        for di in range(-50, 51, 2):
            x, y = px + di * C, py + dj * C
            if any(math.hypot(x - a, y - b) < r + pr for a, b, r in keep_clear): continue
            disc = [idx(*cell_of(x + u * C, y + v * C)) for v in range(-int(pr // C) - 3, int(pr // C) + 4)
                    for u in range(-int(pr // C) - 3, int(pr // C) + 4) if math.hypot(u, v) * C <= pr + 12]
            if any(not land[k] or k in river_cell or k not in on_foot for k in disc): continue
            if theme[idx(*cell_of(x, y))] != [t[0] for t in TOWNS].index(near): continue   # inside its own town's theme
            hs = [height[k] for k in disc]
            if max(hs) - min(hs) > 4.5: continue
            d = math.hypot(x - px, y - py)
            if best is None or d < best[0]: best = (d, x, y, min(hs))
    if best is None: raise SystemExit("no flat clear ground for %s" % pid)
    d, x, y, low = best
    water = low - 0.3
    ci, cj = cell_of(x, y); r = int((pr + 14) // C) + 1
    for v in range(cj - r, cj + r + 1):
        for u in range(ci - r, ci + r + 1):
            if not (0 <= u < NX and 0 <= v < NY) or not land[idx(u, v)]: continue
            dd = math.hypot(u * C + C / 2 - x, v * C + C / 2 - y); k = idx(u, v)
            if dd < pr:
                height[k] = min(height[k], water - 0.9 * (1 - dd / pr) - 0.15); pond_cell.add(k)
            elif dd < pr + 14:
                height[k] = min(height[k], water + (dd - pr) * 0.15)          # a gentle bank, nobody is stuck in a pond
    ponds.append({"id": pid, "near": near, "x": round(x, 1), "y": round(y, 1), "radius": pr, "water": round(water, 2)})
# any water inside the land that is not the sea is a pond too (the desert's oasis)
ocean = set(); q = [k for k in range(NX * NY) if not land[k] and (k % NX in (0, NX - 1) or k // NX in (0, NY - 1))]
ocean.update(q)
while q:
    k = q.pop(); i, j = k % NX, k // NX
    for a, b in N8[:4]:
        u, v = i + a, j + b
        if 0 <= u < NX and 0 <= v < NY and not land[idx(u, v)] and idx(u, v) not in ocean: ocean.add(idx(u, v)); q.append(idx(u, v))
lake = [k for k in range(NX * NY) if not land[k] and k not in ocean]
seen_l = set()
for k in lake:
    if k in seen_l: continue
    piece = [k]; seen_l.add(k); n = 0
    while n < len(piece):
        c = piece[n]; n += 1; i, j = c % NX, c // NX
        for a, b in N8[:4]:
            u, v = i + a, j + b
            if 0 <= u < NX and 0 <= v < NY and not land[idx(u, v)] and idx(u, v) not in ocean and idx(u, v) not in seen_l:
                seen_l.add(idx(u, v)); piece.append(idx(u, v))
    cx = sum(centre(c % NX, c // NX)[0] for c in piece) / len(piece); cy = sum(centre(c % NX, c // NX)[1] for c in piece) / len(piece)
    near = min(TOWNS, key=lambda t: math.hypot(t[3] - cx, t[4] - cy))[0]
    ponds.append({"id": "%s_oasis" % near, "near": near, "x": round(cx, 1), "y": round(cy, 1),
                  "radius": round(math.sqrt(len(piece) * C * C / math.pi), 1), "water": 0.0})

for p in ponds:                                           # a pond stands in its own town's theme too, 60 units round,
    n = [t[0] for t in TOWNS].index(p["near"])            # short of any town's own 60
    ci, cj = cell_of(p["x"], p["y"]); r = int(60 // C)
    for j in range(cj - r, cj + r + 1):
        for i in range(ci - r, ci + r + 1):
            if not (0 <= i < NX and 0 <= j < NY) or not land[idx(i, j)] or math.hypot(i - ci, j - cj) > r: continue
            x, y = centre(i, j)
            if any(math.hypot(x - t[3], y - t[4]) <= 60 for t in TOWNS): continue
            if theme[idx(i, j)] != n: theme2[idx(i, j)] = theme[idx(i, j)]; blend[idx(i, j)] = 0.0
            theme[idx(i, j)] = n

# ---------------------------------------------------------------- roads: cheapest walk over the ground
CLIMB = 1.0                                       # the most a road (or a player) rises from one cell to the next
def walk(a, b, steep_ok=False):
    start, goal = town_cell[a], town_cell[b]
    gi, gj = goal % NX, goal // NX
    best = {start: 0.0}; came = {}; pq = [(0.0, start)]
    while pq:
        f, k = heapq.heappop(pq)
        if k == goal: break
        i, j = k % NX, k // NX; g = best[k]
        for a2, b2 in N8:
            u, v = i + a2, j + b2
            if not (0 <= u < NX and 0 <= v < NY): continue
            n = idx(u, v)
            if not land[n] or n in pond_cell: continue
            step = math.hypot(a2, b2) * C
            rise = abs(height[n] - height[k])
            if rise > CLIMB and steep_ok is False and n not in river_cell and k not in river_cell: continue
            cost = step * (1 + rise / step * 40) + (60 if (n in river_cell and k not in river_cell) else 0) + (400 if rise > CLIMB else 0)
            if g + cost < best.get(n, 1e18):
                best[n] = g + cost; came[n] = k
                heapq.heappush(pq, (g + cost + math.hypot(u - gi, v - gj) * C, n))
    if goal not in came: return None
    cells = [goal]
    while cells[-1] != start: cells.append(came[cells[-1]])
    return cells[::-1]
def smooth(pts, rounds=3):
    for _ in range(rounds):
        out = [pts[0]]
        for p, q in zip(pts, pts[1:]):
            out += [(p[0] * 0.75 + q[0] * 0.25, p[1] * 0.75 + q[1] * 0.25), (p[0] * 0.25 + q[0] * 0.75, p[1] * 0.25 + q[1] * 0.75)]
        pts = out + [pts[-1]]
    return pts
def thin(cells, every=4):
    pts = [centre(c % NX, c // NX) for c in cells]
    return pts[::every] + ([pts[-1]] if (len(pts) - 1) % every else [])
passes = []; pass_cells = []                       # one pass marked per ramp
def ramp(cells):
    """Where the road meets a cliff, the cliff is cut down into a ramp along the road, 3 cells either side of it."""
    for _ in range(30):
        steep = [n for n in range(len(cells) - 1) if abs(height[cells[n + 1]] - height[cells[n]]) > CLIMB
                 and cells[n] not in river_cell and cells[n + 1] not in river_cell]
        if not steep: return
        n = steep[0]
        drop = abs(height[cells[n + 1]] - height[cells[n]])
        r = int(math.ceil(drop / (CLIMB * 0.6))) + 2
        a0, a1 = max(0, n - r), min(len(cells) - 1, n + 1 + r)
        h0, h1 = height[cells[a0]], height[cells[a1]]
        for m in range(a0, a1 + 1):
            target = h0 + (h1 - h0) * (m - a0) / max(1, a1 - a0)
            ci, cj = cells[m] % NX, cells[m] // NX
            for v in range(cj - 3, cj + 4):
                for u in range(ci - 3, ci + 4):
                    if 0 <= u < NX and 0 <= v < NY and land[idx(u, v)] and idx(u, v) not in river_cell and idx(u, v) not in gorge_rock:
                        height[idx(u, v)] = target
        if not passes or passes[-1] != ("ramp", id(cells)): passes.append(("ramp", id(cells))); pass_cells.append(cells[n])
roads = []; bridges = []; walk_cells = {}
for a, b in ROADS:
    cells = walk(a, b) or walk(a, b, steep_ok=True)     # only if the levels leave no gentle way: a ramp is cut
    if cells is None: raise SystemExit("no way over land from %s to %s" % (a, b))
    ramp(cells); walk_cells[(a, b)] = cells
    crossings = [c for n, c in enumerate(cells) if c in river_cell and (n == 0 or cells[n - 1] not in river_cell)]
    for c in crossings: bridges.append({"road": [a, b], "x": round(centre(c % NX, c // NX)[0], 1), "y": round(centre(c % NX, c // NX)[1], 1)})
    road_pts = smooth(thin(cells))
    roads.append({"from": a, "to": b, "xy": [round(v, 1) for x, y in road_pts for v in (x, y)], "cells": cells,
                  "length": round(sum(math.hypot(*(lambda p, q: (p[0] - q[0], p[1] - q[1]))(centre(p % NX, p // NX), centre(q % NX, q // NX))) for p, q in zip(cells, cells[1:])), 1)})

# ---------------------------------------------------------------- checks: say what is wrong, never write a wrong world
# NO TRAPS: every piece of ground a player can stand on must be walkable to from a town on its landmass (islands
# without a town count from their low-tide path's end). A piece is ground joined by walkable steps; one smaller than
# PLACE cells is a ledge on a cliff face, not a place. Each cut-off place gets a ramp cut along the shortest way to
# walkable ground, until none is left.
PLACE = 25                                            # 25 cells = 100 square units, about 30 x 30 people standing
def reach(starts):
    seen = set(starts); q = list(starts)
    while q:
        k = q.pop(); i, j = k % NX, k // NX
        for a, b in N8[:4]:
            u, v = i + a, j + b
            if 0 <= u < NX and 0 <= v < NY:
                n = idx(u, v)
                if n not in seen and land[n] and (abs(height[n] - height[k]) <= CLIMB or n in river_cell or k in river_cell): seen.add(n); q.append(n)
    return seen
starts = [town_cell[t[0]] for t in TOWNS] + [idx(*cell_of(*e)) for a, b, st, e in LOW_TIDE_PATHS]
def cut_off(walked):
    """The TRAPS, biggest first: pieces of ground nobody can walk out of, that a player can still fall into — they are
    lower than walked ground right beside them. A piece higher than all its surroundings is a mesa nobody can climb:
    scenery, not a trap."""
    seen = set(walked); out = []
    for k in range(NX * NY):
        if land[k] and k not in seen:
            piece = reach([k]); seen |= piece
            if len(piece) < PLACE: continue
            if any(0 <= c % NX + a < NX and 0 <= c // NX + b < NY and idx(c % NX + a, c // NX + b) in walked
                   and height[idx(c % NX + a, c // NX + b)] > height[c] for c in piece for a, b in N8[:4]):
                out.append(piece)
    return sorted(out, key=len, reverse=True)
def free_traps():
  global trap_ramps
  for _ in range(200):
      walked = reach(starts)
      places = cut_off(walked)
      if not places: break
      cut = next(iter(places[0]))
      # the shortest way over land from the cut-off piece to walked ground
      prev = {cut: None}; q = [cut]; h2 = 0; found = None
      while h2 < len(q) and found is None:
          k = q[h2]; h2 += 1; i, j = k % NX, k // NX
          for a, b in N8[:4]:
              u, v = i + a, j + b
              if 0 <= u < NX and 0 <= v < NY and land[idx(u, v)] and idx(u, v) not in prev and idx(u, v) not in gorge_rock:
                  prev[idx(u, v)] = k; q.append(idx(u, v))
                  if idx(u, v) in walked: found = idx(u, v); break
      if found is None: starts.append(cut); continue     # a landmass with no town and no path: nothing to join it to
      way = [found]
      while prev[way[-1]] is not None: way.append(prev[way[-1]])
      ramp(way); trap_ramps += 1
trap_ramps = 0
for _ in range(4):                                    # roads and trap ramps can disturb each other: settle both
    for _ in range(8):
        for cells in walk_cells.values(): ramp(cells)
    free_traps()
for _ in range(8):
    for cells in walk_cells.values(): ramp(cells)
print("ramps cut out of cut-off ground:", trap_ramps)
passes = list(dict.fromkeys(pass_cells))
# the caves: at the foot of the nearest cliff (walked ground with ground 2+ units higher right beside it), facing out
caves = []
walked = reach(starts)
for cid, near, cx, cy in CAVES:
    g = next((g for g in gorges if g["cave"] == cid), None)
    if g:                                                  # a gorge's cave stands on its rock, facing the bridge
        caves.append({"id": cid, "near": near, "x": round(g["mouth"][0], 1), "y": round(g["mouth"][1], 1), "into": g["into"],
                      "ground": g["rock_top"], "rock_size": [48, 44], "hall_size": [36, 32]})
        continue
    best = None
    for j in range(int(cy // C) - 60, int(cy // C) + 61):
        for i in range(int(cx // C) - 60, int(cx // C) + 61):
            if not (1 <= i < NX - 1 and 1 <= j < NY - 1): continue
            k = idx(i, j)
            if k not in walked or k in river_cell: continue
            if mass[k] != mass[town_cell[near]]: continue                  # on its town's own landmass
            if cid == "sea_cave" and coast_dist[k] > 5: continue           # a sea cave opens on the shore
            for a, b in N8[:4]:
                n = idx(i + a, j + b)
                if land[n] and height[n] - height[k] >= 2:
                    d = math.hypot(i * C - cx, j * C - cy)
                    if best is None or d < best[0]: best = (d, k, (a, b))
    if best is None: raise SystemExit("no cliff near the %s cave's spot" % cid)
    d, k, (a, b) = best
    x, y = centre(k % NX, k // NX)
    caves.append({"id": cid, "near": near, "x": round(x, 1), "y": round(y, 1), "into": [a, b], "ground": round(height[k], 2),
                  "rock_size": [48, 44], "hall_size": [36, 32]})
problems = []
left = cut_off(reach(starts))
walked_now = reach(starts)
for g in gorges:
    if any(k in walked_now for k in gorge_rock): problems.append("the gorge round %s can be walked across" % g["cave"])
    if g["lowest_rim"] - g["floor"] < 4: problems.append("the gorge round %s is not deep enough on every side" % g["cave"])
    if any(height[k] != g["top"] for k in gorge_rock): problems.append("the rock in the gorge round %s was reshaped" % g["cave"])
if left: problems.append("%d places (%d cells) nobody can walk to" % (len(left), sum(map(len, left))))
for a, b in ROADS:
    cells = walk_cells[(a, b)]
    worst = max(abs(height[q] - height[p]) for p, q in zip(cells, cells[1:]) if p not in river_cell and q not in river_cell)
    if worst > CLIMB + 1e-6: problems.append("the road %s - %s climbs %.1f units in one step" % (a, b, worst))
for n, path in enumerate(rivers):
    if n < len(SPRINGS) and (not path or land[path[-1]]): problems.append("river %d does not reach the sea" % n)
    beds = [height[c] for c in path if land[c]]
    if any(b2 > b1 + 1e-6 for b1, b2 in zip(beds, beds[1:])): problems.append("river %d climbs somewhere" % n)
for t in TOWNS:
    k = town_cell[t[0]]
    if not land[k]: problems.append("%s is in the sea" % t[1])
    if theme[k] != TOWNS.index(t): problems.append("%s stands in another town's theme" % t[1])
    if k in river_cell: problems.append("%s stands in a river" % t[1])
for i2, n, x, y in LANDMARKS:
    if not land[idx(*cell_of(x, y))]: problems.append("%s is in the sea" % n)
for tid, x, y in DUNGEON_DOORS:
    if not land[idx(*cell_of(x, y))]: problems.append("%s's dungeon door is in the sea" % tid)
print("rivers:", [len(p) * C for p in rivers], "units long (about)")
print("roads:", len(roads), "bridges:", len(bridges))
if problems:
    print("PROBLEMS:"); [print("  -", p) for p in problems]; raise SystemExit(1)

# ---------------------------------------------------------------- the file
def ground_at(x, y): return height[idx(*cell_of(x, y))]
def river_record(n, path):
    pts = smooth(thin(path, 3), 2)
    link = n >= len(SPRINGS)
    rec = {"spring": list(SPRINGS[n]) if not link else [round(v, 1) for v in centre(path[0] % NX, path[0] // NX)],
           "xy": [round(v, 1) for x, y in pts for v in (x, y)],
           "bed": [round(min(ground_at(x, y), height[path[min(len(path) - 1, int(i / max(1, len(pts) - 1) * (len(path) - 1)))]]), 2) for i, (x, y) in enumerate(pts)]}
    if link: rec["joins"] = list(link_ends[n - len(SPRINGS)])
    return rec
for r in roads:                                  # each road point's ground height, after every ramp has settled
    cells = r.pop("cells")
    pts = list(zip(r["xy"][0::2], r["xy"][1::2]))
    r["heights"] = [round(ground_at(x, y), 2) for x, y in pts]
def cell_points(path): return [[round(centre(c % NX, c // NX)[0], 1), round(centre(c % NX, c // NX)[1], 1)] for c in path]
layout = {
    "unity": "Unity x = layout x - 640, Unity z = 640 - layout y, Unity y = height - Lastline's own height (towns[lastline].height)",
    "about": "The world layout. Written by tools/world-layout/build_world_layout.py; never edit by hand — change the PLACES block and run it.",
    "seed": SEED, "size": [W, H], "cell": C, "cells": [NX, NY],
    "height": "heights row by row (y then x), in tenths of a world unit; below 0 is sea",
    "heights": [int(round(h * 10)) for h in height],
    "themes": [t[0] for t in TOWNS],
    "theme_of_cell": "row by row, the index into themes of each land cell's town, -1 for sea",
    "theme_cells": theme,
    "towns": [{"id": t[0], "name": t[1], "weapon": t[2], "x": t[3], "y": t[4], "theme": t[5],
               "height": round(height[town_cell[t[0]]], 2)} for t in TOWNS],
    "landmarks": [{"id": i2, "name": n, "x": x, "y": y} for i2, n, x, y in LANDMARKS],
    "dungeon_doors": [{"town": t, "x": x, "y": y} for t, x, y in DUNGEON_DOORS],
    "rivers": [river_record(n, path) for n, path in enumerate(rivers)],
    "roads": roads, "bridges": bridges,
    "boats": [{"from": a, "to": b, "start": list(s), "end": list(e)} for a, b, s, e in BOATS],
    "caves": caves,
    "gorges": [{k: v for k, v in g.items() if k not in ("mouth", "into", "top")} for g in gorges],
    "ponds": ponds,
    "clearings": [{"x": x, "y": y, "radius": r} for x, y, r in CLEARINGS],
    "passes": [{"x": round(centre(c % NX, c // NX)[0], 1), "y": round(centre(c % NX, c // NX)[1], 1)} for c in passes],
    "cliff": "a cliff is wherever the ground rises more than 1.5 units from one cell to the next",
    "low_tide_paths": [{"from": a, "to": b, "start": list(s), "end": list(e)} for a, b, s, e in LOW_TIDE_PATHS],
}
os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
with open(OUT_JSON, "w", encoding="utf-8") as f: json.dump(layout, f, separators=(",", ":"))
print("wrote", OUT_JSON, os.path.getsize(OUT_JSON) // 1024, "KB")

# ---------------------------------------------------------------- the named places, for the game to load by name
# Every place a GM's /tp can name, at a spot a body can stand on: a town, a landmark and a dungeon door where the layout
# puts them; a cave 4 units in front of its mouth; a pond on the first dry cell 3 units past its edge, looking from the
# south round. x, y and height are the layout's own; "origin" is Lastline, the point the world is built around. "kind" is
# what it is, and mark_x, mark_y where the game's map marks it: the spot itself, but a cave's mouth.
OUT_PLACES = os.path.join(ROOT, "unity", "Assets", "3_Data", "Resources", "WorldPlaces.json")
def ground_at(x, y): return round(height[idx(*cell_of(x, y))], 2)
places = [{"id": t[0], "name": t[1], "kind": "town", "x": t[3], "y": t[4], "height": round(height[town_cell[t[0]]], 2)} for t in TOWNS]
places += [{"id": i2, "name": n, "kind": "landmark", "x": x, "y": y, "height": ground_at(x, y)} for i2, n, x, y in LANDMARKS]
places += [{"id": t + "_dungeon_door", "name": "", "kind": "dungeon_door", "x": x, "y": y, "height": ground_at(x, y)} for t, x, y in DUNGEON_DOORS]
for c in caves:
    fx, fy = c["x"] - c["into"][0] * 4, c["y"] - c["into"][1] * 4
    places.append({"id": c["id"], "name": "", "kind": "cave", "x": round(fx, 1), "y": round(fy, 1), "height": ground_at(fx, fy),
                   "mark_x": c["x"], "mark_y": c["y"]})
for p in ponds:
    spot = None
    for k in range(8):
        a = math.pi / 2 + k * math.pi / 4
        fx, fy = p["x"] + math.cos(a) * (p["radius"] + 3), p["y"] + math.sin(a) * (p["radius"] + 3)
        if spot is None or ground_at(fx, fy) > p["water"]:
            spot = (fx, fy)
            if ground_at(fx, fy) > p["water"]: break
    places.append({"id": p["id"], "name": "", "kind": "pond", "x": round(spot[0], 1), "y": round(spot[1], 1), "height": ground_at(*spot)})
for pl in places: pl.setdefault("mark_x", pl["x"]); pl.setdefault("mark_y", pl["y"])
origin = next(t for t in TOWNS if t[0] == "lastline")
with open(OUT_PLACES, "w", encoding="utf-8") as f:
    json.dump({"about": "Written by tools/world-layout/build_world_layout.py from the world layout; never edit by hand.",
               "origin": {"x": origin[3], "y": origin[4], "height": round(height[town_cell["lastline"]], 2)},
               "places": places}, f, indent=1)
print("wrote", OUT_PLACES, len(places), "places")

# ---------------------------------------------------------------- the map, drawn from the file alone
L = json.load(open(OUT_JSON, encoding="utf-8"))
hts = [v / 10 for v in L["heights"]]; th = L["theme_cells"]
cols = {t[0]: t[6] for t in TOWNS}
def rgb(h): return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))
tcol = [rgb(cols[t]) for t in L["themes"]]
rows = []
for j in range(NY):
    row = bytearray([0])
    for i in range(NX):
        k = idx(i, j); h = hts[k]
        if k in pond_cell or (h < 0 and k not in ocean):
            r, g, b = 70, 150, 205
        elif h < 0:
            d = min(1, -h / 7); r, g, b = int(70 - 30 * d), int(140 - 50 * d), int(200 - 40 * d)
        else:
            r, g, b = tcol[th[k]]
            if theme2[k] >= 0 and blend[k] > 0:
                r2, g2, b2 = tcol[theme2[k]]; w = blend[k]
                r, g, b = r * (1 - w) + r2 * w, g * (1 - w) + g2 * w, b * (1 - w) + b2 * w
            if coast_dist[k] <= 3 and h < 1.5: r, g, b = 234, 217, 164          # beach (a high shore is a sea cliff)
            hx = hts[idx(min(NX - 1, i + 1), j)] - hts[idx(max(0, i - 1), j)]
            hy = hts[idx(i, min(NY - 1, j + 1))] - hts[idx(i, max(0, j - 1))]
            # lit from the north-west: ground facing it (rising toward the south-east) is brighter, so raised ground pops out
            shade = max(0.55, min(1.35, 1 + (hx + hy) * 0.09)) * (0.72 + 0.45 * min(1.0, h / 30))
            # a cliff throws its shadow to the south-east: ground below a higher cliff just north-west of it is darker
            if any(hts[idx(max(0, i - s2), max(0, j - s2))] - h > 1.5 for s2 in (1, 2, 3, 4)): shade *= 0.72
            rocky = max(0, min(1, (h - 32) / 10))                                # bare rock on the high peaks (no snow: dropped)
            r, g, b = [min(255, int(c * shade * (1 - rocky) + 140 * shade * rocky)) for c in (r, g, b)]
        row += bytes((r, g, b))
    rows.append(bytes(row))
def png(w, h, raw):
    chunk = lambda t, d: struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(b"".join(raw), 9)) + chunk(b"IEND", b"")
img = base64.b64encode(png(NX, NY, rows)).decode()
pl = lambda xy: "M" + " L".join("%.0f,%.0f" % (xy[n], xy[n + 1]) for n in range(0, len(xy), 2))
s = ['<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 %d %d" font-family="Segoe UI, Arial">' % (W + 640, H, W + 640, H)]
s.append('<image href="data:image/png;base64,%s" width="%d" height="%d" preserveAspectRatio="none"/>' % (img, W, H))
for r in L["rivers"]: s.append('<path d="%s" fill="none" stroke="#3d8fd6" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>' % pl(r["xy"]))
for r in L["roads"]: s.append('<path d="%s" fill="none" stroke="#7a5a32" stroke-width="4.5" stroke-dasharray="12 6" stroke-linejoin="round"/>' % pl(r["xy"]))
for g in L["gorges"]:
    s.append('<circle cx="%.0f" cy="%.0f" r="%.0f" fill="none" stroke="#1a1a1a" stroke-width="%.0f" stroke-opacity="0.8"/>' % (g["x"], g["y"], g["rock_radius"] + g["width"] / 2, g["width"]))
    (fx, fy), (tx, ty) = g["bridge"]["from"], g["bridge"]["to"]
    s.append('<line x1="%.0f" y1="%.0f" x2="%.0f" y2="%.0f" stroke="#9a9a9a" stroke-width="7"/>' % (fx, fy, tx, ty))
for b in L["bridges"]: s.append('<rect x="%.0f" y="%.0f" width="18" height="10" fill="#9a9a9a" stroke="#333" stroke-width="2"/>' % (b["x"] - 9, b["y"] - 5))
for b in L["boats"]: s.append('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="#fff" stroke-width="4" stroke-dasharray="4 10"/>' % (*b["start"], *b["end"]))
for b in L["low_tide_paths"]: s.append('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="#ead9a4" stroke-width="12" stroke-dasharray="6 5"/>' % (*b["start"], *b["end"]))
for m in L["landmarks"]:
    s.append('<text x="%d" y="%d" font-size="24" text-anchor="middle" fill="#ffd34d" stroke="#5a3d00" stroke-width="1">&#9733;</text>' % (m["x"], m["y"] + 8))
    s.append('<text x="%d" y="%d" font-size="13" text-anchor="middle" fill="#1d1d1d" stroke="#fff" stroke-width="3" paint-order="stroke">%s</text>' % (m["x"], m["y"] + 25, m["name"]))
for cv in L["caves"]:
    s.append('<path d="M%.0f,%.0f a11,11 0 0 1 22,0 v8 h-22 z" fill="#1d1712" stroke="#e8e0d0" stroke-width="2"/>' % (cv["x"] - 11, cv["y"] + 2))
    s.append('<text x="%.0f" y="%.0f" font-size="12" text-anchor="middle" fill="#fff" stroke="#000" stroke-width="2" paint-order="stroke">cave</text>' % (cv["x"], cv["y"] + 24))
for d in L["dungeon_doors"]: s.append('<text x="%d" y="%d" font-size="20" text-anchor="middle" fill="#2a1a0a" stroke="#fff" stroke-width="1">&#9660;</text>' % (d["x"], d["y"] + 7))
for t in L["towns"]:
    s.append('<circle cx="%d" cy="%d" r="12" fill="#fff" stroke="#222" stroke-width="3"/>' % (t["x"], t["y"]))
    s.append('<text x="%d" y="%d" font-size="17" font-weight="700" text-anchor="middle" fill="#111" stroke="#fff" stroke-width="3" paint-order="stroke">%s</text>' % (t["x"], t["y"] - 18, t["name"]))
lx = W + 20
s.append('<text x="%d" y="40" font-size="22" font-weight="700">World layout</text>' % lx)
cl = []
for j in range(NY):
    for i in range(NX):
        k = idx(i, j)
        if hts[k] < 0: continue
        nb = [hts[idx(u, v)] for u, v in ((i + 1, j), (i, j + 1)) if u < NX and v < NY]
        if any((abs(hb - hts[k]) > 1.5 and hb > -0.5) or (hb < 0 and hts[k] > 3) for hb in nb): cl.append('M%d,%dh%dv%dh-%dz' % (i * C, j * C, C, C, C))
s.append('<path d="%s" fill="#3b2f26" opacity="0.85"/>' % "".join(cl))
for p in L["passes"]: s.append('<circle cx="%.0f" cy="%.0f" r="6" fill="#fff" stroke="#7a5a32" stroke-width="3"/>' % (p["x"], p["y"]))
s.append('<text x="%d" y="64" font-size="14" fill="#444">Drawn from data/world-layout.json. %d x %d units. Shading = height, grey = bare rock on the peaks.</text>' % (lx, W, H))
y0 = 100
for t in L["towns"]:
    s.append('<rect x="%d" y="%d" width="18" height="18" fill="%s" stroke="#333"/><text x="%d" y="%d" font-size="14"><tspan font-weight="700">%s</tspan>%s — %s</text>'
             % (lx, y0 - 14, cols[t["id"]], lx + 26, y0, t["name"], (" (%s)" % t["weapon"]) if t["weapon"] else " (start)", t["theme"]))
    y0 += 28
y0 += 10
for sym, txt in [("&#9733;", "landmark"), ("&#9660;", "dungeon door"), ("&#9632;", "bridge"), ("&#9675;", "pass: a ramp up a cliff"), ("&#9608;", "cliff (dark line; solid = steep mountain face)"), ("&#8745;", "cave")]:
    s.append('<text x="%d" y="%d" font-size="18">%s</text><text x="%d" y="%d" font-size="14">%s</text>' % (lx, y0, sym, lx + 26, y0, txt)); y0 += 26
for color, dash, txt in [("#3d8fd6", "", "river"), ("#7a5a32", "10 6", "road"), ("#ffffff", "4 8", "boat"), ("#ead9a4", "6 5", "low-tide path")]:
    s.append('<rect x="%d" y="%d" width="22" height="14" fill="#2f6fa8"/><line x1="%d" y1="%d" x2="%d" y2="%d" stroke="%s" stroke-width="5" stroke-dasharray="%s"/><text x="%d" y="%d" font-size="14">%s</text>'
             % (lx, y0 - 12, lx, y0 - 5, lx + 22, y0 - 5, color, dash, lx + 30, y0, txt)); y0 += 26
s.append('<text x="%d" y="%d" font-size="14">%d roads, %d bridges, %d rivers</text>' % (lx, y0 + 10, len(L["roads"]), len(L["bridges"]), sum(1 for r in L["rivers"] if "joins" not in r)))
grid = "".join('<line x1="%d" y1="0" x2="%d" y2="%d"/>' % (x, x, H) for x in range(0, W + 1, CHUNK)) + \
       "".join('<line x1="0" y1="%d" x2="%d" y2="%d"/>' % (y, W, y) for y in range(0, H + 1, CHUNK))
s.append('<g id="squares" style="display:none" stroke="#ffffff" stroke-opacity="0.55" stroke-width="0.8">%s</g>' % grid)
s.append("</svg>")
toggle = ("document.getElementById('squares').style.display = this.checked ? '' : 'none'")
html = ('<!DOCTYPE html><html><head><meta charset="utf-8"><title>World layout</title><style>'
        'body{margin:0;font-family:Segoe UI,Arial;background:#fff}'
        'label{position:fixed;top:12px;right:16px;background:#fff;border:1px solid #999;border-radius:6px;padding:6px 10px;font-size:14px;cursor:pointer}'
        '</style></head><body><label><input type="checkbox" onchange="%s"> Show build squares (%d x %d units)</label>%s</body></html>'
        % (toggle, CHUNK, CHUNK, "\n".join(s)))
open(OUT_MAP, "w", encoding="utf-8").write(html)
print("drew", OUT_MAP)
# THE CREATURES FOLLOW THE LAND: the spawn tool works its patches out of the file just written and lays them over this map
if OUT_MAP == os.path.join(ROOT, "data", "world-layout.html"):
    import subprocess
    subprocess.run([sys.executable, os.path.join(ROOT, "tools", "world-spawns", "build_world_spawns.py")], check=True)
