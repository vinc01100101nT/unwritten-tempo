"""Draft: one continent (fields, cities, special places edge to edge), cities a Ragnarok gap apart, gaps between
roads filled with fields, an inland sea, a few boat islands, dungeons as entrance marks; every field and dungeon
renamed after its city (dungeon floors from 0)."""
import json, collections, math, sys, html, random

LAYOUT = r'D:\tempo\projects\roguelike-mmo\tools\world-creator\worlds\world-regions.json'
OUT = sys.argv[1]
SEEDS = int(sys.argv[2]) if len(sys.argv) > 2 else 100
PULL, BAY_R = 45.0, 90.0
NEED = int(sys.argv[3]) if len(sys.argv) > 3 else 3
CAP = int(sys.argv[4]) if len(sys.argv) > 4 else 120
ONE_DOOR = (sys.argv[5] == '1') if len(sys.argv) > 5 else True
ISLANDS = ['tide_temple', 'drowned_chapel', 'sky_cairn']
d = json.load(open(LAYOUT, encoding='utf-8'))
M = {m['id']: dict(m) for m in d['maps']}
M['lastline']['size'] = [64, 64]      # measured: its real ground is about 62 x 62, not the 80 the layout says
G, SHARE, SEA = 8, 16, 32
FIELD = [64, 56]
town = lambda i: M[i]['type'] == 'town'
field = lambda i: M[i]['type'] == 'field'
dungeon = lambda i: M[i]['type'] == 'dungeon'
island = lambda i: i in ISLANDS
land = lambda i: not dungeon(i) and not island(i)

def centre_now(i):
    m = M[i]; return (m['pos'][0] + m['size'][0] / 2, m['pos'][1] + m['size'][1] / 2)
def dist(a, b):
    (ax, ay), (bx, by) = centre_now(a), centre_now(b); return math.hypot(ax - bx, ay - by)

orig = collections.defaultdict(set)
for p in d['portals']:
    orig[p['a']].add(p['b']); orig[p['b']].add(p['a'])
def field_path(a, b, graph):
    prev = {a: None}; q = collections.deque([a])
    while q:
        x = q.popleft()
        for y in sorted(graph[x]):
            if y in prev: continue
            if y == b: prev[y] = x; q.clear(); break
            if not field(y): continue
            prev[y] = x; q.append(y)
    if b not in prev: return None
    path, x = [], prev[b]
    while x != a: path.append(x); x = prev[x]
    return path[::-1]
towns = sorted(i for i in M if town(i))
routes = {}
for i, a in enumerate(towns):
    for b in towns[i + 1:]:
        p = field_path(a, b, orig)
        if p is not None: routes[(a, b)] = p
order = sorted(routes, key=lambda k: (dist(*k), k))
parent = {t: t for t in towns}
def root(x):
    while parent[x] != x: x = parent[x]
    return x
kept, deg = [], collections.Counter()
for a, b in order:
    if root(a) != root(b):
        parent[root(a)] = root(b); kept.append((a, b)); deg[a] += 1; deg[b] += 1
for a, b in order:
    if (a, b) in kept: continue
    if deg[a] < 2 and deg[b] < 3 or deg[b] < 2 and deg[a] < 3:
        kept.append((a, b)); deg[a] += 1; deg[b] += 1
kept.sort(key=lambda k: (dist(*k), k))
gap = {k: 2 + round(4 * r / max(1, len(kept) - 1)) for r, k in enumerate(kept)}

edge_doors, inner_doors = set(), set()
def door(a, b):
    k = tuple(sorted((a, b)))
    (edge_doors if land(a) and land(b) else inner_doors).add(k)
on_route = set()
for p in routes.values(): on_route.update(p)
for p in d['portals']:
    a, b = p['a'], p['b']
    if town(a) and town(b): continue
    if (town(a) or a in on_route) and (town(b) or b in on_route): continue
    door(a, b)
used, new_count = set(), 0
def new_field(cx, cy):
    global new_count
    new_count += 1
    nid = f'new_{new_count}'
    M[nid] = {'id': nid, 'name': '', 'type': 'field', 'size': FIELD, 'pos': [cx - FIELD[0] / 2, cy - FIELD[1] / 2], 'new': True}
    return nid
for a, b in kept:
    chain = [f for f in routes[(a, b)] if f not in used][:gap[(a, b)]]
    while len(chain) < gap[(a, b)]:
        k = len(chain) + 1; n = gap[(a, b)] + 1
        (ax, ay), (bx, by) = centre_now(a), centre_now(b)
        chain.append(new_field(ax + (bx - ax) * k / n, ay + (by - ay) * k / n))
    (ax, ay), (bx, by) = centre_now(a), centre_now(b)
    vx, vy = bx - ax, by - ay
    chain.sort(key=lambda f: ((centre_now(f)[0] - ax) * vx + (centre_now(f)[1] - ay) * vy, f))
    used.update(chain)
    seq = [a] + chain + [b]
    for x, y in zip(seq, seq[1:]): door(x, y)
for f in sorted(on_route):
    if f in used or any(f in dr for dr in edge_doors | inner_doors): continue
    nbrs = sorted(orig[f], key=lambda n: (town(n), dist(f, n), n))
    door(f, nbrs[0])

def land_adj():
    adj = collections.defaultdict(list)
    for a, b in sorted(edge_doors): adj[a].append(b); adj[b].append(a)
    return adj
adj = land_adj()
LAND = sorted(i for i in M if land(i))

pos = {}
def rect(i):
    x, y = pos[i]; w, h = M[i]['size']; return x, y, w, h
BAY = (-150.0, 140.0)
def in_bay(x, y, w, h):
    cx = min(max(BAY[0], x), x + w); cy = min(max(BAY[1], y), y + h)
    return math.hypot(cx - BAY[0], cy - BAY[1]) < BAY_R
def overlaps(i, x, y):
    w, h = M[i]['size']
    if in_bay(x, y, w, h): return True
    for j in pos:
        jx, jy, jw, jh = rect(j)
        if x < jx + jw - 0.5 and jx < x + w - 0.5 and y < jy + jh - 0.5 and jy < y + h - 0.5: return True
        if town(i) and town(j) and x <= jx + jw + 0.5 and jx <= x + w + 0.5 and y <= jy + jh + 0.5 and jy <= y + h + 0.5:
            return True
    return False
def candidates(a, b):
    ax, ay, aw, ah = rect(a); bw, bh = M[b]['size']; out = []
    for side in 'NESW':
        if side in 'EW':
            x = ax + aw if side == 'E' else ax - bw; lo, hi = ay - bh + SHARE, ay + ah - SHARE
            y = math.floor(lo / G) * G
            while y <= hi:
                if y >= lo: out.append((x, y))
                y += G
        else:
            y = ay - bh if side == 'N' else ay + ah; lo, hi = ax - bw + SHARE, ax + aw - SHARE
            x = math.floor(lo / G) * G
            while x <= hi:
                if x >= lo: out.append((x, y))
                x += G
    return out
def touch(i, j):
    ix, iy, iw, ih = rect(i); jx, jy, jw, jh = rect(j)
    if abs(ix + iw - jx) < 0.6 or abs(jx + jw - ix) < 0.6:
        if min(iy + ih, jy + jh) - max(iy, jy) >= SHARE - 0.5:
            return (ix + iw if abs(ix + iw - jx) < 0.6 else ix, (max(iy, jy) + min(iy + ih, jy + jh)) / 2)
    if abs(iy + ih - jy) < 0.6 or abs(jy + jh - iy) < 0.6:
        if min(ix + iw, jx + jw) - max(ix, jx) >= SHARE - 0.5:
            return ((max(ix, jx) + min(ix + iw, jx + jw)) / 2, iy + ih if abs(iy + ih - jy) < 0.6 else iy)
    return None
def build(seed):
    rnd = random.Random(seed); pos.clear(); pos['lastline'] = (0, 0)
    while len(pos) < len(LAND):
        rest = [i for i in LAND if i not in pos and any(n in pos for n in adj[i])] or [i for i in LAND if i not in pos]
        b = max(rest, key=lambda i: (sum(1 for n in adj[i] if n in pos), len(adj[i]), rnd.random()))
        nbrs = [n for n in adj[b] if n in pos]; best = None
        cog = (sum(v[0] for v in pos.values()) / len(pos), sum(v[1] for v in pos.values()) / len(pos))
        for nb in nbrs:
            (nx, ny), (bx, by) = centre_now(nb), centre_now(b); dx, dy = bx - nx, by - ny
            for x, y in candidates(nb, b):
                if overlaps(b, x, y): continue
                pos[b] = (x, y); touching = sum(1 for n in nbrs if touch(b, n)); del pos[b]
                vx = x + M[b]['size'][0] / 2 - (pos[nb][0] + M[nb]['size'][0] / 2)
                vy = y + M[b]['size'][1] / 2 - (pos[nb][1] + M[nb]['size'][1] / 2)
                facing = (dx * vx + dy * vy) / ((math.hypot(dx, dy) or 1) * (math.hypot(vx, vy) or 1))
                pull = math.hypot(x - cog[0], y - cog[1]) / PULL
                score = touching * 10 + facing * 3 - pull + rnd.random() * 2
                if best is None or score > best[0]: best = (score, x, y)
        if best is None:
            anchor = pos[nbrs[0]] if nbrs else (0, 0); found = None
            for r in range(0, 300):
                for gx in range(-r, r + 1):
                    for gy in ((-r, r) if abs(gx) != r else range(-r, r + 1)):
                        if not overlaps(b, anchor[0] + gx * G, anchor[1] + gy * G):
                            found = (anchor[0] + gx * G, anchor[1] + gy * G); break
                    if found: break
                if found: break
            pos[b] = found
        else:
            pos[b] = (best[1], best[2])
    return sum(1 for a, b in edge_doors if not touch(a, b)), dict(pos)
best = None
for s in range(SEEDS):
    bad, layout = build(s)
    if best is None or bad < best[0]: best = (bad, layout, s)
pos.clear(); pos.update(best[1])

def fewest_between(a):
    best_ = 99; dd = {a: 0}; q = collections.deque([a]); a_ = land_adj()
    while q:
        x = q.popleft()
        for y in a_[x]:
            if y in dd: continue
            dd[y] = dd[x] + 1
            if town(y): best_ = min(best_, dd[x]); continue
            if field(y): q.append(y)
    return best_
def gaps_ok(): return min(fewest_between(t) for t in towns) >= 2
def land_reach():
    a_ = land_adj(); seen = {'lastline'}; q = collections.deque(['lastline'])
    while q:
        x = q.popleft()
        for y in a_[x]:
            if y not in seen: seen.add(y); q.append(y)
    return seen
for k in [(a, b) for a, b in sorted(edge_doors) if not touch(a, b)]: edge_doors.discard(k)
for _ in range(len(M)):
    reach = land_reach()
    cut = [i for i in LAND if i in M and i not in reach]
    if not cut: break
    options = sorted((town(i) or town(j), dist(i, j), i, j) for i in cut for j in reach if touch(i, j))
    joined = False
    for _, _, i, j in options:
        edge_doors.add(tuple(sorted((i, j))))
        if gaps_ok(): joined = True; break
        edge_doors.discard(tuple(sorted((i, j))))
    if not joined:
        moved = False
        for i in cut:
            keep = pos.pop(i)
            for j in sorted(reach, key=lambda j: (dist(i, j), j)):
                if town(j): continue
                for x, y in candidates(j, i):
                    if overlaps(i, x, y): continue
                    pos[i] = (x, y)
                    for k in [k for k in edge_doors if i in k and not touch(*k)]: edge_doors.discard(k)
                    edge_doors.add(tuple(sorted((i, j))))
                    if gaps_ok(): moved = True; break
                    edge_doors.discard(tuple(sorted((i, j)))); del pos[i]
                if moved: break
            if moved: break
            pos[i] = keep
        if not moved: break
while True:
    a_ = land_adj()
    dead = [i for i in sorted(M) if M[i].get('new') and len(a_[i]) <= 1]
    if not dead: break
    for i in dead:
        for k in [k for k in edge_doors if i in k]: edge_doors.discard(k)
        del M[i]; pos.pop(i, None)
LAND = sorted(i for i in M if land(i))
road_new = sum(1 for i in M if M[i].get('new'))

# ---- FILL THE GAPS: a field in every free spot that touches 3 or more land maps, then 2 or more, until none ------
# Each filler gets doors to every FIELD it touches (never a city), kept only while no two cities come closer than 2.
filled = 0
for need in ((3, 2) if NEED == 2 else (NEED,)):
    while True:
        best_spot = None
        for i in sorted(pos):
            if i in ISLANDS: continue
            nid_size = FIELD
            ax, ay, aw, ah = rect(i)
            for x, y in [(ax + aw, ay), (ax - FIELD[0], ay), (ax, ay - FIELD[1]), (ax, ay + ah),
                         (ax + aw, ay + ah - FIELD[1]), (ax - FIELD[0], ay + ah - FIELD[1]),
                         (ax + aw - FIELD[0], ay - FIELD[1]), (ax + aw - FIELD[0], ay + ah)]:
                x, y = round(x / G) * G, round(y / G) * G
                M['_probe'] = {'type': 'field', 'size': FIELD}
                if overlaps('_probe', x, y): continue
                pos['_probe'] = (x, y)
                t = [j for j in pos if j != '_probe' and j not in ISLANDS and touch('_probe', j)]
                del pos['_probe']
                if len(t) >= need and any(field(j) for j in t):
                    score = (len(t), -abs(x) - abs(y))
                    if best_spot is None or score > best_spot[0]: best_spot = (score, x, y, t)
        M.pop('_probe', None)
        if best_spot is None: break
        _, x, y, t = best_spot
        nid = new_field(x + FIELD[0] / 2, y + FIELD[1] / 2); M[nid]['filler'] = True
        pos[nid] = (x, y)
        joined = 0
        for j in sorted(t, key=lambda j: (town(j), j)):
            if not field(j): continue
            edge_doors.add(tuple(sorted((nid, j))))
            if not gaps_ok(): edge_doors.discard(tuple(sorted((nid, j)))); continue
            joined += 1
            if ONE_DOOR: break
        if joined == 0:
            del M[nid]; del pos[nid]; break
        filled += 1
        if filled >= CAP: break
LAND = sorted(i for i in M if land(i))

# ---- NAMES: only the cities keep theirs. Every field is named after the city nearest it by doors (ties: nearer on
# the map), numbered from the city outward; every dungeon after its entrance field's city, numbered per city, floors
# from 0. Special places keep their names. -------------------------------------------------------------------------
a_ = land_adj()
owner, hops = {}, {}
q = collections.deque()
for t in towns: owner[t] = t; hops[t] = 0; q.append(t)
while q:
    x = q.popleft()
    for y in sorted(a_[x]):
        if y in owner or not field(y): continue
        owner[y] = owner[x]; hops[y] = hops[x] + 1; q.append(y)
label = {}
by_city = collections.defaultdict(list)
for f in sorted(i for i in M if field(i)):
    c = owner.get(f) or min(towns, key=lambda t: math.hypot(pos[f][0] - pos[t][0], pos[f][1] - pos[t][1]))
    by_city[c].append(f)
for c, fs in by_city.items():
    fs.sort(key=lambda f: (hops.get(f, 99), math.hypot(pos[f][0] - pos[c][0], pos[f][1] - pos[c][1]), f))
    for n_, f in enumerate(fs, 1):
        label[f] = f'{M[c]["name"]} Field {n_}'
for t in towns: label[t] = M[t]['name']
for i in M:
    if M[i]['type'] == 'special': label[i] = M[i]['name']
nm = lambda i: label.get(i, i)

dun_adj = collections.defaultdict(set)
for a, b in inner_doors:
    if dungeon(a) and dungeon(b): dun_adj[a].add(b); dun_adj[b].add(a)
dungeon_marks, seen = [], set()
for i in sorted(i for i in M if dungeon(i)):
    if i in seen: continue
    g, q = [], collections.deque([i]); seen.add(i)
    while q:
        x = q.popleft(); g.append(x)
        for y in sorted(dun_adj[x]):
            if y not in seen: seen.add(y); q.append(y)
    gate = next(((b if a in g else a) for a, b in sorted(inner_doors) if (a in g) != (b in g)), None)
    if gate is None: continue
    dungeon_marks.append([gate, len(g)])
dn = collections.Counter()
for m_ in sorted(dungeon_marks, key=lambda m_: nm(m_[0])):
    city = nm(m_[0]).split(' Field')[0]
    dn[city] += 1
    m_.append(f'{city} Dungeon {dn[city]}')

lx0 = min(pos[i][0] for i in LAND); lx1 = max(pos[i][0] + M[i]['size'][0] for i in LAND)
ly0 = min(pos[i][1] for i in LAND); ly1 = max(pos[i][1] + M[i]['size'][1] for i in LAND)
rnd = random.Random(3)
boats = []
for i in ISLANDS:
    gate = next(((b if a == i else a) for a, b in sorted(inner_doors) if i in (a, b)), None)
    w, h = M[i]['size']
    for _ in range(50000):
        x0 = round(rnd.uniform(lx0 - 140, lx1 + 140 - w) / G) * G
        y0 = round(rnd.uniform(ly0 - 140, ly1 + 140 - h) / G) * G
        if in_bay(x0, y0, w, h): continue
        if all(not (x0 < jx + jw + SEA and jx < x0 + w + SEA and y0 < jy + jh + SEA and jy < y0 + h + SEA)
               for jx, jy, jw, jh in (rect(j) for j in pos)):
            break
    pos[i] = (x0, y0); boats.append((gate, i))

edge_ok = [(a, b, touch(a, b)) for a, b in sorted(edge_doors) if touch(a, b)]
edge_bad = [(a, b) for a, b in edge_doors if not touch(a, b)]
closest = {t: fewest_between(t) for t in towns}
roads = set()
for t in towns:
    s3 = {t}; q = collections.deque([t])
    while q:
        x = q.popleft()
        for y in a_[x]:
            if y in s3: continue
            s3.add(y)
            if town(y): roads.add(tuple(sorted((t, y)))); continue
            if field(y): q.append(y)
n_fields = sum(1 for i in M if field(i))
new_now = sum(1 for i in M if M[i].get('new'))

xs = [pos[i][0] for i in pos] + [pos[i][0] + M[i]['size'][0] for i in pos] + [BAY[0] - BAY_R, BAY[0] + BAY_R]
ys = [pos[i][1] for i in pos] + [pos[i][1] + M[i]['size'][1] for i in pos] + [BAY[1] - BAY_R, BAY[1] + BAY_R]
minx, maxx, miny, maxy = min(xs), max(xs), min(ys), max(ys)
pad, S = 50, 1.3
W = (maxx - minx + pad * 2) * S; H = (maxy - miny + pad * 2) * S + 140
X = lambda v: (v - minx + pad) * S
Y = lambda v: (v - miny + pad) * S + 120
fill = {'town': '#e8c548', 'field': '#7fb069', 'special': '#b9a3d6'}
o = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W:.0f}" height="{H:.0f}" viewBox="0 0 {W:.0f} {H:.0f}" font-family="Segoe UI, Arial, sans-serif">',
     '<rect width="100%" height="100%" fill="#16202b"/>',
     f'<rect x="0" y="120" width="{W:.0f}" height="{H - 120:.0f}" fill="#1f4e74"/>',
     '<text x="24" y="40" fill="#fff" font-size="26" font-weight="700">World map draft 5 — one continent, every zone renamed after its city</text>',
     f'<text x="24" y="68" fill="#cfd8e3" font-size="16">{len(roads)} city-to-city roads, 2 to 6 fields between cities as Ragnarok has (fewest: {min(closest.values())}); no city touches another. '
     f'{n_fields} fields in all ({n_fields - new_now} of today\'s, {new_now} new — dashed).</text>',
     f'<text x="24" y="92" fill="#cfd8e3" font-size="16">Blue square = a door on the edge two maps share ({len(edge_ok)}). Purple mark = a dungeon entrance in that field, floors from 0 ({len(dungeon_marks)}). '
     f'Orange diamond = a boat to the island with the same number ({len(boats)}). Lilac = special place (name kept).</text>',
     '<text x="24" y="112" fill="#8796a8" font-size="13">Positions, doors and names only — no game file changed. Each box is its map at its real size; blue is sea.</text>']
for i in pos:
    x, y, w, h = rect(i); pad_ = 8 if i in ISLANDS else 6
    o.append(f'<rect x="{X(x - pad_):.1f}" y="{Y(y - pad_):.1f}" width="{(w + 2*pad_)*S:.1f}" height="{(h + 2*pad_)*S:.1f}" rx="10" fill="#c9b27c"/>')
for i in pos:
    x, y, w, h = rect(i); t = M[i]['type']; new = M[i].get('new')
    dash = ' stroke-dasharray="6 4"' if new else ''
    o.append(f'<rect x="{X(x):.1f}" y="{Y(y):.1f}" width="{w*S:.1f}" height="{h*S:.1f}" fill="{fill.get(t, "#777")}" '
             f'fill-opacity="{0.55 if new else 0.9}" stroke="{"#eaf6e0" if new else "#0d141b"}" stroke-width="2"{dash}/>')
    name = html.escape(nm(i)); fs = 10 if w >= 60 else 7
    words = name.split(' ')
    lines = words if w < 60 else ([' '.join(words[:-2]), ' '.join(words[-2:])] if len(words) > 2 else [name])
    for k, ln in enumerate(lines):
        o.append(f'<text x="{X(x + w/2):.1f}" y="{Y(y + h/2) + (k - (len(lines)-1)/2) * (fs+2) + 4:.1f}" text-anchor="middle" '
                 f'fill="#10161d" font-size="{fs}" font-weight="700">{ln}</text>')
for a, b, t in edge_ok:
    o.append(f'<rect x="{X(t[0]) - 4:.1f}" y="{Y(t[1]) - 4:.1f}" width="8" height="8" fill="#2b6cf2" stroke="#cfe0ff" stroke-width="1.5"/>')
marks = collections.Counter()
for gate, floors, dname in dungeon_marks:
    x, y, w, h = rect(gate); k_ = marks[gate]; marks[gate] += 1
    cx, cy = x + w * 0.12, y + h * (0.16 + 0.3 * k_)
    o.append(f'<circle cx="{X(cx):.1f}" cy="{Y(cy):.1f}" r="6" fill="#6a3fb5" stroke="#efe6ff" stroke-width="1.5"/>')
    o.append(f'<text x="{X(cx) + 9:.1f}" y="{Y(cy) + 3:.1f}" fill="#2a1450" font-size="7.5" font-weight="800">{html.escape(dname)} · floors 0–{floors - 1}</text>')
def diamond(ex, ey, num):
    o.append(f'<path d="M{X(ex):.1f},{Y(ey) - 9:.1f} L{X(ex) + 9:.1f},{Y(ey):.1f} L{X(ex):.1f},{Y(ey) + 9:.1f} L{X(ex) - 9:.1f},{Y(ey):.1f} Z" fill="#ff9a3c" stroke="#fff1e0" stroke-width="1.5"/>')
    o.append(f'<text x="{X(ex):.1f}" y="{Y(ey) + 3.5:.1f}" text-anchor="middle" fill="#1a1a1a" font-size="9" font-weight="800">{num}</text>')
for n_, (gate, isl) in enumerate(boats, 1):
    x, y, w, h = rect(gate); diamond(x + w * 0.85, y + h * 0.8, n_)
    x, y, w, h = rect(isl); diamond(x + 9, y + 9, n_)
o.append('</svg>')
open(OUT, 'w', encoding='utf-8').write('\n'.join(o))
ov = sum(1 for a in pos for b in pos if a < b and (lambda r1, r2: r1[0] < r2[0] + r2[2] - 0.5 and r2[0] < r1[0] + r1[2] - 0.5 and r1[1] < r2[1] + r2[3] - 0.5 and r2[1] < r1[1] + r1[3] - 0.5)(rect(a), rect(b)))
names = [nm(i) for i in pos]
print('seed', best[2], 'doors', len(edge_doors), 'NOT on edge', len(edge_bad), 'overlaps', ov, 'unreached', [i for i in LAND if i not in land_reach()],
      'duplicate names', [n for n, c in collections.Counter(names).items() if c > 1])
print('roads', len(roads), 'of', len(kept), 'fewest', min(closest.values()), 'fields', n_fields, 'new', new_now, '(road', road_new, 'filler', filled, ')',
      'dungeons', len(dungeon_marks), 'islands', len(boats))
print('fields per city', {M[c]['name']: len(fs) for c, fs in sorted(by_city.items())})
print('dungeons', [m_[2] + ' floors ' + str(m_[1]) for m_ in dungeon_marks])
