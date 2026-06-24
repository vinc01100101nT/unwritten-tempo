#!/usr/bin/env python3
"""
Generate a Prontera-style map.json for the Map Painter importer
(Tools > unwritten > World > Import Painted Map...).

Replicates the iconic Ragnarok Online capital in the painter's vocabulary
(see tools/map-painter/prop-manifest.json):
  - square outer wall with 4 gates (N/S/E/W) + corner/gate towers
  - a wide cross-shaped main avenue joining the gates
  - a central stone plaza with a square fountain (water) in the middle
  - a grand hall (Tavern landmark) at the north end of the plaza
  - 4 residential quarters of houses facing the avenues
  - tree-lined streets and corner groves

Coordinate model (locked by MapPainterImporter.cs):
  - everything is authored in painter-world space 0..size; the importer
    re-centres on the origin at bake time.
  - a ground cell (cx,cz) covers painter-world [cx*cell,(cx+1)*cell].
  - grass is the ground base layer => we only emit path/water cells.
"""
import json, math

CELL = 4.0
COLS = ROWS = 40
SIZE = COLS * CELL          # 160 x 160
C = SIZE / 2.0              # 80,80 centre

ground, objects = [], []

# ---- helpers ---------------------------------------------------------------
def paint(cx, cz, brush):
    if 0 <= cx < COLS and 0 <= cz < ROWS:
        ground.append({"brush": brush, "cx": cx, "cz": cz})

def obj(brush, prefab, x, z, yaw=0):
    objects.append({"brush": brush, "prefab": prefab,
                    "x": round(x, 3), "z": round(z, 3), "yaw": int(yaw) % 360})

def face_centre(x, z):
    """Yaw so the prop's front points toward the central cross."""
    if abs(x - C) >= abs(z - C):
        return 270 if x < C else 90
    return 0 if z < C else 180

# ---- GROUND: cross avenue + plaza + fountain -------------------------------
AV = range(18, 22)          # 4 cells wide (x/z 72..88) -> the gate gap width
PLAZA = range(15, 25)       # 60..100 stone square at the crossing

for c in range(COLS):
    for a in AV:
        paint(c, a, "path")  # horizontal avenue
        paint(a, c, "path")  # vertical avenue

for cx in PLAZA:
    for cz in PLAZA:
        paint(cx, cz, "path")

# central fountain: 2x2 water pool in the dead centre (cells 19,20)
for cx in (19, 20):
    for cz in (19, 20):
        # overwrite: water wins over the plaza path beneath it
        ground[:] = [g for g in ground if not (g["cx"] == cx and g["cz"] == cz)]
        paint(cx, cz, "water")

# ---- WALLS: square ring with 4 gates ---------------------------------------
WALL_LO, WALL_HI = 10.0, 150.0
GATE_LO, GATE_HI = 72.0, 88.0      # gap = the avenue width
STEP = 2.3                          # ~Wall_part2 footprint -> continuous wall

def wall_run(fixed, axis, yaw):
    """Lay wall segments from WALL_LO..WALL_HI along `axis` ('x' or 'z'),
    leaving the central gate gap open."""
    p = WALL_LO
    while p <= WALL_HI + 0.01:
        if not (GATE_LO - 1 < p < GATE_HI + 1):
            if axis == "x":
                obj("wall", "Wall_part2", p, fixed, yaw)
            else:
                obj("wall", "Wall_part2", fixed, p, yaw)
        p += STEP

wall_run(WALL_LO, "x", 0)    # south
wall_run(WALL_HI, "x", 0)    # north
wall_run(WALL_LO, "z", 90)   # west
wall_run(WALL_HI, "z", 90)   # east

# corners
for (x, z, y) in [(WALL_LO, WALL_LO, 0), (WALL_HI, WALL_LO, 90),
                  (WALL_LO, WALL_HI, 270), (WALL_HI, WALL_HI, 180)]:
    obj("wall", "Wall_corner", x, z, y)

# towers flanking every gate + an entrance arch on the (main) south gate
for fixed, axis in [(WALL_LO, "x"), (WALL_HI, "x"), (WALL_LO, "z"), (WALL_HI, "z")]:
    for edge in (GATE_LO, GATE_HI):
        tw = "Wall_tower1" if edge == GATE_LO else "Wall_tower2"
        if axis == "x":
            obj("wall", tw, edge, fixed, 0)
        else:
            obj("wall", tw, fixed, edge, 90)
obj("wall", "Wall_entrance", C, WALL_LO, 0)   # grand south gate arch

# ---- LANDMARKS -------------------------------------------------------------
obj("landmark", "Tavern_LOD0", C, 118, 180)   # grand hall, north of the plaza
obj("landmark", "Windmill_LOD0", 28, 132, 200)

# ---- HOUSES: 4 residential quarters ---------------------------------------
HOUSES = ["House1", "House2", "House3",
          "WoodenHouse_1_LOD0", "WoodenHouse_2_LOD0", "WoodenHouse_3_LOD0"]
# quarter X/Z bands sit between the wall and the avenue/plaza (off the streets)
BANDS = [18, 27, 36, 45, 54, 104, 113, 122, 131, 140]
i = 0
for x in BANDS:
    for z in BANDS:
        # skip the Tavern's footprint at the north-plaza edge
        if abs(x - C) < 12 and z > 108:
            continue
        i += 1
        obj("houses", HOUSES[(i * 7) % len(HOUSES)], x, z, face_centre(x, z))

# ---- TREES: street-lining + corner groves ----------------------------------
TREES = ["Oak_tree1", "Oak_tree2", "Pine_tree1", "Pine_tree2",
         "Deciduous_tree1", "Birch_tree1", "Willow_tree1"]
t = 0
def tree(x, z):
    global t
    if WALL_LO + 2 < x < WALL_HI - 2 and WALL_LO + 2 < z < WALL_HI - 2:
        t += 1
        obj("trees", TREES[(t * 5) % len(TREES)], x, z, (t * 47) % 360)

# rows hugging the avenue edges (outside the plaza)
for p in range(16, 145, 9):
    if not (60 < p < 100):
        tree(68, p); tree(92, p)   # along the vertical avenue
        tree(p, 68); tree(p, 92)   # along the horizontal avenue
# four corner groves inside the walls
for (gx, gz) in [(22, 22), (138, 22), (22, 138), (138, 138)]:
    for dx in (0, 6, 12):
        for dz in (0, 6, 12):
            tree(gx + dx, gz + dz)

# ---- WRITE -----------------------------------------------------------------
doc = {
    "version": 1,
    "name": "Prontera",
    "cellSize": CELL,
    "size": {"x": SIZE, "z": SIZE},
    "cols": COLS,
    "rows": ROWS,
    "ground": ground,
    "objects": objects,
}
import os
out = os.path.join(os.path.dirname(__file__), "prontera.json")
with open(out, "w") as f:
    json.dump(doc, f, indent=2)
print(f"wrote {out}: {len(ground)} ground cells, {len(objects)} objects "
      f"({sum(1 for o in objects if o['brush']=='houses')} houses, "
      f"{sum(1 for o in objects if o['brush']=='trees')} trees, "
      f"{sum(1 for o in objects if o['brush']=='wall')} walls)")
