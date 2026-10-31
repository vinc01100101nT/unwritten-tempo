---
name: world-authoring
description: Use for ANY work on the seamless world "world" or a town in it — planning or changing the world layout, planning a new town, building or rebuilding a town piece or the world's squares, bridges, curbs, rivers, cliffs, roads, nature props, a town's theme, desert or other biome ground, extending the world and choosing its next block, or a bug in the built ground (a hole, a gap, an empty field). Covers the plan → picture → go flow (a town is never built before the user has seen its picture), the user's layout and town decisions, how the builders work, the exact rebuild commands through the live editor, the checks after a build, and what each change means for the game server.
---

# World authoring — the seamless world and its towns

**This skill is the ONE home of world and town knowledge.** A new decision, a bug's real cause in the built ground, a
measured fact about a builder: write it HERE, in the section it belongs to, rewriting the old sentence in place — never
a memory file, never a separate doc, never an appended "update" section.

The game world is ONE seamless map, id `world`, built square by square from one layout file. Towns are hand-composed
pieces set into it. Two project rules always apply and live in the project's instructions: **ONEGROUND** (every ground
shape is the one ground body, reshaped — never a piece laid on it) and **WORLDSCALE** (everything sized against our
person, `CharacterSpec.Active.bodyHeight` = 0.327). Where the world stands and what to build next is section 6; older
open items (the look, the game server, Keepers) are in `handoffs/seamless-world.md` (written 2026-10-28) and
`handoffs/parley-town.md`.

## 1. The flow — plan, picture, go, build

**Never build a town piece or rebuild the world until the user has seen the plan picture and said go.** A changed
plan replaces the picture: say so plainly — the game keeps the last built version until a build. *(Why: a build froze
Unity and left Parley broken while the user play-tested; a redesigned Parley read as a Lastline copy, "bullshit".)*

- **The world layout** — `data/world-layout.json`, made ONLY by `tools/world-layout/build_world_layout.py` (places
  decided by hand in its `PLACES` block; coast, height, rivers, themes and roads worked out; the same output every run).
  It writes its map beside it on every run, `data/world-layout.html` (a "Show build squares" toggle draws the 32-unit
  square grid). The end-of-turn check re-runs the tool whenever the tool is edited, so file and picture never drift.
  It also writes `unity/Assets/3_Data/Resources/WorldPlaces.json`: every town, landmark, dungeon door, cave (4 units in
  front of its mouth) and pond (first dry cell 3 units past its edge) with its layout x, y and ground height, plus
  Lastline as the origin — what the GM's `/tp <place>` resolves names to (`/tp <x> <z>` takes a spot directly; a spot
  no built square holds is refused). Nothing is typed into code and the world needs no rebuild for it.
- **A town** — `tools/town-plan/build_town_plan.py` writes a NEW town's first draft, `data/towns/<id>.json` and its
  picture `data/towns/<id>.html`, and never writes over a plan file that exists. From then on the plan file is the
  town, edited by hand in the town editor (`python tools/town-editor/serve.py`, the user starts it, then
  http://127.0.0.1:8765): streets, canals, areas (rectangle, circle or outline), bridges, mounds, gates, every piece, and
  the ground raised or lowered by a brush (`lift`, a 0.5-unit grid of heights over the whole piece, eased to nothing at
  the water's edge). Its Rebuild button runs `TownPieceBuilder.Build(id)` then `SeamlessWorldBuilder.BuildCore()` in the
  open editor; Build replaces `1_Authored/Prefabs/Towns/<Name>.prefab` and its folder every time. Tree soil beds are
  worked out at build from where each tree stands — a plan never holds them.
- **Then area by area**, each walked by the user before the next.

## 2. The user's decisions — binding

**Land and borders**
- **The world is a sandbox, not a ladder** (committed game-wide, 2026-09-26): no town, road or stretch of land is a
  step a player must take in order, and from the first day they can walk anywhere the ground leads. A stretch of land
  is known for the level most of its creatures sit at, never locked by it (how the levels are laid out is section 3,
  Creatures). The continent is laid out FRESH — the old 90-map layout and the old continent draft are not the starting point.
- Area borders are invisible, slightly wavy lines with NOTHING built along them (no river, no tree line, no wall);
  themes blend across them, props and paths run over them. Land is designed for itself, never shaped by borders. The
  user hates square-border looks ("rivers everywhere… square rivers and treelines"); the book's world is "peace and lively".
- Cliffs: the land is cut into plateau levels (five steps of 3–5 units); MOST of each level edge is a sheer cliff
  ("cliffs everywhere"), the rest a walkable slope, so no area is shut in; any fall-in pit gets a ramp cut; the build
  refuses to write a world with a fall-in pit nobody can walk out of. Rivers run in wide valleys, not walled gorges.
  Raised mesas with cliffs all round are scenery. Halcyra is HIGH GROUND (about 29 units) with sea cliffs and one long
  walkable slope down to Tornel (about 17). Four caves: under the northern mountains (Gemmara), a sea cave in Hilt's
  headland, a forest ridge west of Pennaryl, the dark isle near Kharnel.
- Rivers are one water: the west river feeds the middle one past Tornel's north (bridge on the Tornel–Halcyra road),
  the middle one feeds the east one from Gemmara past Velmora's south (bridge on the Old Oak–Velmora road). A channel's
  bed only ever falls (lowest ground met so far, cutting, never raised), so it is a valley, never a bank.
- Ponds: by Lastline, by Pluvenne, in Pennaryl's forest, a desert oasis for Hazemere — each on flat walkable ground in
  its own town's theme, and the land 60 units round it is that town's theme too (as round a town), short of any
  town's own 60 — the wavy borders had put half of Lastline's pond's banks in Hazemere's desert. Coasts are freehand noise shapes (seed 7).
- The dark towns, Kharnel and Cindrel, are on their own dark isle off the south-west coast, reached by boat.
- A shadow waits only at its own place, away from where players level: bringing one down takes a guild, and meeting
  one while levelling would spoil the levelling. Each place has one way in, and none works yet (no quest leads to a
  place, and the game has no keys that open a way, no guides that move a party and no world clock) — a **quest**
  (Hill of the Flame, a pilgrimage carrying the flame between shrines; Thousand Gates, each gate a trial; Aurora Steps, the monastery's trials; the Mountain's Root, whose front is shut by the Iron Gate, so the
  bridge-keeper's chain opens the Last Bridge), a **key** (Masked Ballroom: the Mask, a rare and costly item), a
  **guide** (Burning Keel: a lava-walker; Everburning Plain: the fire temple's keeper; Sky Cairn: a steppe rider;
  Narrowbend: a boatman), the **world clock** (Tide Temple: low tide; Dusk Tower: only at dusk; Walking Road: only in
  fog) or **hidden** (Drowned Chapel, Unmarked Path: found by the player). Open: whether one key lets a whole party in.
- Each shadow's place (a landmark) stands in its own weapon town's land, on the far side from the town, with the ground
  shaped to its description (the user, 2026-10-29): Hill of the Flame on Halcyra's high ground; Thousand Gates on a
  hill in Parley's land; Narrowbend on a hairpin of the river past Pluvenne; Masked Ballroom on Velmora's east shore;
  Burning Keel on the lip of a volcano in Harrowen's land; Sky Cairn in a levelled clearing at Pennaryl's forest edge;
  Dusk Tower on a rise above Old Oak; Walking Road at the south end of Hazemere's desert; Drowned Chapel at the cove
  south-west of Kharnel; Aurora Steps on a mountain at the north end of Luminhal's island; Unmarked Path on the
  mountain side north of Tornel; Everburning Plain by Cindrel; Tide Temple on Hilt's islet; Tharos waits in the
  Mountain's Root cave, on a round rock ringed by a sheer gorge at the mountain's foot north-west of Gemmara; the
  Last Bridge (layout `gorges`, not a road bridge) crosses from the west and is the only way onto the rock. A cutting
  climbs out of the gorge floor on its lowest side clear of towns and rivers, so a fall never traps anyone; the layout
  tool refuses a world where the rock can be walked to or reshaped. The coast is grown round fixed land anchors, so moving a landmark never moves it.
  Luminhal and the dark isle are reached by BOAT, far enough out that a boat makes sense; the Tide Temple sits on its own islet, reached on foot
  by a low-tide path. The game has no tide, so the path is a strip you can always wade (the user, 2026-10-30, "for now").
  **DEFERRED, not to be forgotten: a real tide** — the sea rising and falling, so the path is only walkable at low tide.
- The sea's and a pond's wading strip ends in a sheer 0.75 step down to deep water, which a body cannot climb back up
  (most a character climbs: 0.075), and the game has no swimming (the user, 2026-10-30: "it is okay for now").
  **DEFERRED, not to be forgotten: swimming, or drop-offs a body can walk back up** — until then a fall into deep water
  strands whoever falls.

**Look and props**
- **Desert land wears desert ground, never grass** (the user, 2026-10-30: "the deserts should have desert ground, not
  grass ofcourse"). The builder lays sand on every desert and coast land (`SandBiomes`: Hazemere, Hilt), a beach
  staying sand to the water. Cindrel's volcanic and Kharnel's haunted land still wear the world's green ground; the user
  has not ruled on them — ask when their land is built.
- Only NATURE props are replaced by the Idyllic Fantasy Nature pack; buildings, houses and town structures stay ours.
  Never swap buildings for pack models; towns keep their buildings, only their nature surroundings take the theme.
- The lively pack look is the main look; a FEW dark biomes stay as rare places.
- **The Idyllic Fantasy Nature pack is searched FIRST, always, for anything the world needs** — a ground texture, a
  prop, a plant, a rock, a material (the user, 2026-10-30: "that pack must be searched first before looking for other
  package"). Another pack is looked at only when this one has no such thing, and the report says what was searched for
  in it and not found. The desert's and the beach's sand is the pack's own Sand.
- Nature props come ONLY from the Idyllic Fantasy Nature pack, nothing else (the user, 2026-10-29) — the town
  editor's plant brush offers only that pack's plants, and only on green ground (grass and meadow areas).
- EVERY prop of the nature pack is used somewhere — except its Water (river, pond and sea use the Genshin water). Demo
  tools (Controller, WindControl, VegetationBendControl) are not props. A check is to fail the world build when a pack
  prop is unused.
- Plant WIND uses the pack's own values, NOT scaled to the world: gusts ~0.67 units wide, 1.4 units/s, strength 0.25.
  Scaling it by 0.172 made gusts a third of a person wide and the user lost the rolling waves. Only the player BEND is
  scaled.

**Towns**
- One weapon per town, 1:1: Lastline (start town, no weapon) + 14 weapon towns. Brimstone is removed; the Sniper moves
  to Old Oak (open plains, walls, long sight lines), placed on the Finesse–Arcane border.
- Themes follow each weapon's style: Lastline green trees + green willow, mixed meadows; Old Oak (Sniper, Dark Moon)
  open grass, lone oaks; Parley (Sword, Blossom Wind) pink blossom + pink willow, pink meadows; Harrowen (Axe, Wildfire)
  green woods, orange + red-orange meadows; Pennaryl (Bow, Passing Cloud) thick forest + firs; Gemmara (Warhammer,
  Earthrend) firs, rocks, cliffs; Tornel (Shuriken, Red Autumn) red trees + red willow; Velmora (Dagger, Nightshade
  Bloom) purple trees + purple willow, purple meadows; Hazemere (Katar, Veiled Mirage) DESERT (old props kept); Hilt
  (Greatsword, Raging Tide) BEACH near the Tide Temple (sand, rocks, cliffs; beach plants need another pack); Pluvenne
  (Spear, Bending River) blue willows, reeds, lilies; Luminhal (Fist, Northern Lights) blue broadleaf, blue/blue-purple
  meadows; Halcyra (Wand, Morning Mist) blue willows, white meadows. Snow is not a town theme (weather can snow anywhere).
- Every town is its own saved piece like the hand-made Lastline: walls, gates at its roads, paved streets, houses, a
  square — never a flat clearing — HEAVILY inspired by its weapon's theme.
- **Every town's layout is wholly unique** ("billion percent unique"): no grid-cross-and-ring copy of Lastline (straight
  cross, rectangular ring). Use slanted roads, water with bridges, irregular squares, landmarks shaped by the theme,
  real-town composition (houses in rows along streets, doors on the street, gardens behind). Check a new layout against
  Lastline and every planned town before showing it.
- **No real-country culture** anywhere in the world, and no made-up language. A new place's name is either a real
  English word or phrase (one syllable like Hilt, up to three words) — about a third of them — or a made-up name that
  sounds like a Ragnarok place; each ties to its shadow's style, its weapon and its setting.
- Castles, the seats guild wars will be fought over, are not placed yet, and castle gameplay is deferred; one rule
  already holds: **a castle never gives power**.
- A planned town's piece is never edited by hand: the plan file is the source and every build replaces the piece
  (the user's choice, 2026-10-29: a town editor with every control, the ground brush included). Lastline, which has no
  plan file, stays hand-made.

## 3. How the world is built

- `SeamlessWorldBuilder.BuildCore()` (Editor), output `Assets/2_Generated/World`. Squares are the layout's own 32-unit
  grid, id (I, J) = layout x/32, y/32. `Blocks` in `SeamlessWorldBuilder.cs` lists every built area, each with a
  comment saying what it holds and when it was decided — read it there, never copy it here; a town with a plan file
  adds its own 7×7. `python3 tools/project-status.py` prints what is built. Never leave a column of land unbuilt between two built blocks: the world map
  shows it as a gap and the edge walls fence each block off. A square not in `Blocks` is
  deleted with its address on the next build. **Build every square a fall can lead to**: a gorge's or pit's way out
  must be inside the built squares, or the world's edge wall traps whoever falls in (found 2026-10-29: the gorge's
  cutting ran into unbuilt squares 14–15). A full build is slow (measured 2026-10-30: 203 squares in 539 s) and the editor shows no progress while it runs. Start it only once a recompile has
  finished loading: a request sent while the editor is still loading code is dropped and nothing is built.
- Caves (layout `caves`): a block of rock (`rock_size`, along the way in × across) raised 8 units with sheer walls on
  the cave's own ground, a hall (`hall_size`) inside at the mouth's ground, an 8-wide passage through the front wall,
  open to the sky so the camera sees in. The rock never rises out of lower ground; no props inside it; cliff-face rocks
  line the hall but not the passage. A gorge's bridge (layout `gorges[].bridge`) is fitted between the layout's two ends
  with no road, the ground at each end easing up across the bridge's whole width.
- The sea: every layout cell no town's land holds, its surface at the layout's height 0, its shore worked out from the
  whole layout so it comes out the same whichever squares are built. A low-tide path (layout `low_tide_paths`) is a
  strip across the sea at wading depth, cut down each shore. Boats are not built.
- Scene coords: x = layout x − 640, z = 640 − layout y (north +z), y = layout height − Lastline's height (13.12).
- Towns: every layout town with a piece at `Towns/<Name>.prefab` whose ground lies in the built box is set in (the
  builder's TownSite list; address `world/<id>`). The world builder reads Lastline from `1_Authored/Prefabs/Towns/Lastline.prefab`.
  A square wholly under a town is still built (no ground child), or the world's edge walls appear inside the town.
- Edge walls follow the outline of the built squares (`MapEdgeMath.OutlineWalls`).
- Cliffs: a layout step > 1.5 between cells becomes a near-vertical jump in the one ground. The pack's cliff rocks line
  every wall and keep their own colliders (the user's call, 2026-10-27 — they stand up to 2.7 units in front of the wall).
- Roads are graded to at most 0.25 rise per unit; ground eases up to bridge ends; a short straight road runs onto each
  bridge end. Walk limit 45°.
- **Themes.** Every land cell belongs to the nearest town on its landmass, measured from a point pushed by noise up to
  about ±135 units, so a border wanders far and one town's theme can reach close to another town (Hazemere's desert came
  to within ~140 units of Lastline, ~40 of Lastline's pond). A town always holds its own theme 60 units round its middle,
  and a pond 60 round its middle short of any town's 60.
- Kept-biome themes (Hazemere desert, Hilt coast, Cindrel volcanic, Kharnel haunted) take trees and flowers from their
  Biome Table row and NOTHING from the pack: no field trees, firs, bushes, rocks or plants, no field grass, no meadow
  carpets (all gated on the theme being a pack theme), so a kept-biome square holds far fewer props: ~200–380 against
  ~800–1,100 in a pack-themed square (measured 2026-10-30). Desert and coast land wear sand; volcanic and haunted land
  wear the world's green ground and look like empty grass.
- **Grass and meadow flowers are not props.** They are carpet patches on each square's root (`FlowerCarpet`), drawn
  instanced; counting a square's prefab instances counts trees, bushes, rocks, plants and reeds only.
- Independence (proven 2026-10-27/28): shared square edges identical, a wider build changes no existing square, a
  rebuild gives byte-identical ground meshes and prefabs identical apart from Unity's own object ids; props stand by
  neighbour rolls only (no placement-order chain). A change to a theme, a pond, a bridge or a road still changes the
  props of the squares round it, since props roll against their neighbours'.
- Trees stay SOLID (the user's call).

**Bridges are ground** (the user, 2026-10-28: "convert that entire bridge as a ground, and merge it into the ground").
`BridgeDecks` (shared by both builders) fits the bridge model to a crossing; `BridgeDecks.AddModel` copies the model's
near-detail triangles into the ground mesh, drawn with the model's own material as extra submeshes. There is no separate
bridge object and no hidden floor. The model is a shell OPEN UNDERNEATH, so every triangle is laid twice, once facing
the other way — drawn and solid from below. (A hand-built per-slice underside left holes: the piers and end blocks
drag a slice's lowest point below the walkway.) The fit still samples the walkway's middle for the ramps at each end,
the mounds and the footprint lift. The model's ends are rounded, so the fit also measures how far in from each end the
walkway is first whole across its width (`insetFrom`, `insetTo`; 0–4 m), and the world's ground stays at the deck's
height under that stretch, easing back over a metre — without it a slot two metres deep opened between the raised
approach and the first stones (the Last Bridge's west end, found 2026-10-30). A town ground holding a bridge casts shadows.

**Town ground** — `TownPieceBuilder` lays it with `GroundBlendBuilder.LayPlotCurbed`: smooth curbs traced along the zero
line of the plan's signed-distance field, a curb face plus a one-row stone band, curb 0.05 up. Each curb face turns
toward the low side by the field's slope across ITS OWN triangle (sampling 0.02 off the face's middle put 118 faces on
the wrong side = blue sky holes). Where low paving meets the stone band along water, a face closes the step.

**On the game server** — the server loads squares only around players and awake spawn areas, so anything built when
the map loads stands on no ground. Anything placed at map load calls `SeamlessWorld.HoldGround(rect, then)` first.
The old zone world (77 maps) was deleted on 2026-10-28; the build ships only Systems + world. Still open: Keepers are
looked up per MAP (breaks with a second town); teleport price is by doors, which the world does not have.

**Creatures** — `tools/world-spawns/build_world_spawns.py` (rules in its header, decided 2026-10-29) writes the world's
rows and shaped areas into `WorldSpawns.asset`, moves the 18 summoners in `WorldTownsfolk.asset`, and lays the patches
over the layout's own map, `data/world-layout.html`, behind a "Show creature patches" toggle (one map, never a second
page). The layout tool runs it at the end of every run; run it alone after a roster change. Never hand-edit the world's
rows. A row with `area` ≥ 0 spawns on that area's squares (`FieldMobSpawner.SetShape`); an area reaches at most ~100
units from its middle, because the server holds an awake area's whole ground loaded. In game, the map (M) has a "Show
respawn patches" button. **A creature's home biome, element, level and size are written on the creature alone**; a row
names which creatures live in a patch and never changes them, so a creature is the same in every patch it lives in. The
tool reads a home biome as a kind of ground (`ground_of_creature`), so one creature can live from one end of the world
to the other. A patch's level climbs with its distance from the nearest town, among the patches of its own ground, so
the land round every town — Lastline's first steps included — is the gentlest, and nothing stops a player walking on.
What a creature drops and what its kill pays follow its own level, wherever it lives. The normals keep to the element
tiers by level: 1–33 only Neutral, Earth or Water; 34–66 add Wind and Fire; 67–99 add Poison, Holy, Dark, Ghost and
Undead — the elements whose counter (skill buffs and cards) is still to come.

- **Preview first, data only on go:** `--preview` writes nothing and draws `~/Downloads/world-spawn-preview.html`, with
  hover boxes for every patch and dungeon. The user sees it before any write.
- **Before showing a preview, every check the tool prints must pass, and the picture is read as the user will read
  it.** The prints: the whole roster (66 originals as themselves, 66 Tainted, 66 Blighted, 14 field elites, 4
  summon-only elites and 14 shadows with a summoner); no elite and nothing Blighted within 150 of a town; no patch
  spread over 12 levels; no dungeon with a Blighted floor above a Tainted one or a group dropping in level; every
  deepest floor Blighted; 0 stars, numbers or names covering something else. Then read the key, the hover boxes and
  the elite list for stale words. *(2026-10-29: the preview went out three times with faults the user's "is anything
  still wrong?" found every time — originals only present as versions, floors spanning 1–34 levels, Blighted beside
  towns, elites inside the ring, Blighted floors above Tainted ones, a key saying ×2/×5 when the data says ×1 or ×2 and
  ×3 or ×5, a cut-off elite list, labels on top of each other. Each one is now a printed check.)*
- **Words on the map are read from the data**, never typed: the multipliers in the key, each floor's level range.
- **Every name finds a free spot**: the layout's town, landmark and cave names and every mark are boxes; each star,
  level number and name takes the nearest spot that hits none, with a thin line back to its mark when it moved out.
  The picture grows to hold the whole key.
- **Dungeon floors** (6 doors and 3 caves; the Mountain's Root is Tharos's place) are worked out every run and shown
  in the preview, but written only once the floor maps exist: a row needs a map and a spawn area on that map.
- `tools/check-creature-roster.py` checks the placement law on the written data (and its prover plants every fault);
  it can only pass after a write.

**Kit prefabs** — LOD1+ children must carry neither an extra scale nor the root's rotation. Tavern and Windmill had
their LOD1–3 children scaled 0.3 under a 0.3 root (3.3× too small at a distance); Tavern, Windmill and WoodenHouse had a
180° turn on them (the drawn house flipped from its LOD0 collider). Both fixed 2026-10-28.

## 4. Rebuilding — through the live editor

**Rebuild only when the ground or its props actually change**, and use the smallest mode that covers the change (new
squares → `BuildNewSquares`, one town → `BuildTownSquares`, anything else → `BuildCore`). Something that only reads the
world — a teleport list, a map label, a GM command — needs no rebuild at all: its data comes from the layout tool's own
output (the user, on a rebuild started for `/tp`: "what the fuck, why do you need to rebuild the world?").

Stop Play first. A build takes minutes, so the editor call times out at 5 s but keeps running: log a marker line and
wait for it in the editor's log. Write this to a scratchpad `.cs` file and run it with `eval_file` (param `file`):

```csharp
string t; try { t = TownPieceBuilder.Build("parley"); } catch (System.Exception e) { t = "FAIL " + e; }
UnityEngine.Debug.Log("TOWNBUILD " + t);
string w; try { w = SeamlessWorldBuilder.BuildCore(); } catch (System.Exception e) { w = "FAIL " + e; }
UnityEngine.Debug.Log("WORLDBUILD " + w);
return "done";
```

```bash
L=$(tools/platform.sh editor-log); for i in $(seq 1 110); do grep -q "WORLDBUILD" "$L" && break; sleep 5; done; grep -a "TOWNBUILD\|WORLDBUILD" "$L"
```

Use a fresh marker word each run, or an old line will match. The town editor's Rebuild button runs the same two calls.

**Extending the world** (new squares added to `Blocks`, nothing already built changed): call
`SeamlessWorldBuilder.BuildNewSquares()` instead of `BuildCore()`. It builds only the squares that have no prefab yet,
keeps every other square's prefab and meshes as they are, and still writes the whole scene list, the towns and the
minimap. Measured 2026-10-30: one square alone took 105 s, against 456 s for all 196; the rebuilt square's mesh came out
byte-identical and its prefab identical apart from Unity's own object ids, and no other square changed.

**A town set in or changed after the world around it was built**: `SeamlessWorldBuilder.BuildTownSquares("<id>")`
rebuilds only that town's built 7 × 7 (its rim and the props kept off it lie there) plus every built square its roads lie
on differently than with no town there, and the squares round those; every other square is kept. Its report says how far
the town's roads change the ground. Measured 2026-10-30 on Parley: roads changed up to 50.2 m from its middle, all inside
its 7 × 7; 43 squares in 240 s (about 50 s of it the minimap); all 43 identical to the full build's apart from Unity's
object ids and their order, meshes byte-identical, no other square touched. The minimap bake itself is not
pixel-exact: a second bake of the same world changed 29 tiles by up to 11 pixels each. A change to the builder's own
rules or to the layout (heights, roads, rivers) still needs `BuildCore()`.

## 5. After a build — the checks

- The build's own report (squares, the bridge fit line).
- Lastline's squares and piece unchanged (compare the chunk prefabs and ground meshes against before).
- Downward rays on the Ground layer land where people walk; for a bridge, upward rays from the river meet its
  underside. One cast reports one hit per collider and the ground is one collider — walk stacked layers one cast at a
  time (`GroundSnap.SurfaceNearestY` does this).
- **Probe the built ground without Play**: load the chunk prefabs into a preview scene and cast rays in its own physics:
  `var sc = EditorSceneManager.NewPreviewScene(); PrefabUtility.InstantiatePrefab(prefab, sc); Physics.SyncTransforms();
  sc.GetPhysicsScene().Raycast(...)`, then `ClosePreviewScene`. Chunk prefabs sit at the world's own coordinates (root
  at 0). A grid of heights printed as one character per half metre shows a hole at a glance. The ground is one mesh
  with submeshes — submesh 0 the ground, 1 the bridge stone — so `hit.triangleIndex` against `GetSubMesh` tells which
  was hit. Four prefabs fit in one 5-second editor call; for more, `Debug.Log` a marker and read the editor's log.
- **Which squares a build changed**: fingerprint `Assets/2_Generated/World` (md5 of every file) before and after.
  Ground meshes are byte-identical when unchanged; chunk prefabs are NOT a usable comparison (Unity's object ids are
  new every build), so compare their props by counting `PrefabInstance:` and the `m_Name` values per square.
- A render from the game camera with a person-tall figure beside the change (WORLDSCALE). A hidden-scene render of 15+
  squares at once draws houses and leaves as shadows only — render one square plus a town.
- Walk it in Play with the test account (Alpha) where the change is — onto it, under it, past its edges.
- `tools/check-map-ids.py`, `tools/check-biome-table.py`, `tools/check-scene-stays-clean.py`, `tools/check-asset-rules.py`,
  and the code checker after any C# edit; read the whole console.
- Report: **the game server must be rebuilt and restarted** whenever the ground changed. The character server and the
  account service need nothing for world changes.

## 6. Extending the world — where it stands, and choosing the next block

**A spot the user names in the game** is shown as X, Y, Z = east, north, height (the engine's x, z, y). Its layout spot is
x = X + 640, y = 640 − Y, and its square is (layout x ÷ 32, layout y ÷ 32), rounded down.

**What is built, and what is not** — run `python3 tools/project-status.py`. Its World section reads the layout and the
built squares, so it is never stale: it counts squares, towns, landmarks, caves, ponds, bridges, dungeon doors,
low-tide paths and boats, and names every one not yet on built ground. Never write what is built into this skill.

**Never built yet, each a new kind of thing the builder has not proven** — the reason to build them early:
- **Boats**: the layout has them (to the dark isle and to Luminhal), and the builder makes none.
- **Volcanic and haunted ground**: Cindrel's and Kharnel's land, on the dark isle, would wear the world's green ground
  today (section 2 — the user has not ruled).
- **The desert's heart**: the builder lays sand on desert land and part of Hazemere's land is built, but not Hazemere
  itself, its oasis or the Walking Road.

**Building the whole world before its towns** — possible, and Gemmara and Old Oak are already built that way. The layout
flattens every town's ground out to 47.6 units from its middle (`TOWN_REACH` 39.6 + `TOWN_CLEAR` 8), easing into the
land over 24 more. A town with no piece gets no gates, its roads stop at its middle, and the world's props grow on its
spot. When its piece comes, `BuildTownSquares("<id>")` (section 4) rebuilds only its squares. **Open, the user has not
answered:** should a waiting town's spot stay bare, or keep the world's trees and rocks as now?

**Before adding a block to `Blocks`:** run the status script; pick the block holding the most never-built kinds; check that
no gap is left between it and what is built (a one-square gap is a wall on both sides and a hole in the world map); keep
every square a fall can lead to inside it; then plan → picture → go (section 1). After the build, run section 5's checks
and tell the user the game server must be rebuilt and restarted.
