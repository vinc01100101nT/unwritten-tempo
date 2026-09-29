---
name: mob-summon-addressables
description: "Runtime mob spawning from catalog data goes through MobSpawner via Addressables (async), fed by baked mob prefabs."
metadata: 
  node_type: memory
  type: project
  originSessionId: 6a9c1bd1-4e5a-44c8-bf7c-ebfabab6396f
---

Mobs can be spawned from catalog data at runtime (e.g. the `/summon <count> <normal|boss>` GM command) — but the mob BUILDER (`RigBuilder25D.BuildMonster`) is **editor-only**, so a build can't stamp a `MonsterDef` into a live mob directly. The pipeline that bridges this:

1. **Author-time bake** — `Tools ▸ unwritten ▸ Mobs & NPCs ▸ Bake Mob Prefabs` (`Editor/MobPrefabBaker.cs`) builds every `MonsterDef` via `RigBuilder25D.BuildMonster(def.ToArchetype())` (+ `BossBanner` for elites and shadows — it only ensures the name plate; the name itself is written at spawn from `def.displayName`), saves each to `Assets/2_Generated/MobPrefabs/Mobs/<def name>.prefab`, and marks it **Addressable**: group `Mobs`, address `Mobs/<def id>`. The art comes from the folder the def links to by GUID (`MonsterDef.artFolder`), so renaming a folder or a def file breaks nothing. Output is regeneratable (2_Generated bucket) — safe to delete + re-bake.
2. **Runtime spawn** — `MobSpawner.Spawn(def, foot, onSpawned)` (`Scripts/MobSpawner.cs`) is the ONE choke point. It `Addressables.LoadAssetAsync<GameObject>("Mobs/" + def.id)`, **caches the loaded prefab handle per key for the session** (never released; a failed load is cached too so a missing address isn't retried), then plain-`Instantiate`s a clone tagged `HideFlags.DontSaveInEditor`. Instances are NOT Addressables-managed, so mob death/recycle (`MobRespawn`, `DeathDissolve`) is unchanged — only where the prefab comes from changed.

**Async ripple:** because Addressables loads async, `Spawn` takes an `Action<GameObject>` callback (fires this frame if cached, else a few frames later). `/summon` (`GmCommands.CmdSummon`) fires N async spawns, counts completions, and replies to chat only when all N land (captures `c.reply` since the `GmContext` is transient).

**Gotchas:**
- The mob DEFS still live in `Resources/Monsters` (loaded by `MonsterCatalog`); only the baked PREFABS are Addressable. Don't put the prefabs under a `Resources/` folder — Addressables + Resources would double-bundle.
- Package: `com.unity.addressables` added to `Packages/manifest.json` (2.x for Unity 6). Scripts are in the default `Assembly-CSharp`/`-Editor` (no asmdefs), which auto-reference Addressables — no asmdef wiring needed.
- In-editor Play uses Addressables' "Use Asset Database" mode, so `/summon` works right after baking with no bundle build. A real player build needs Addressables content built (or "Build Addressables on Player Build" enabled).
- **PINK MOB scar (2026-07-19):** first bake produced magenta mobs. `RigBuilder25D.BillboardMaterial()` assigns a runtime `new Material(Unwritten/BillboardLit)` (in-memory, not a .mat asset). A SCENE can embed that material so baked field mobs look fine, but `PrefabUtility.SaveAsPrefabAsset` can't reference a non-asset material → it serializes fileID:0 → null → Unity's pink "missing material". The sprite mesh still shows (BillboardAnimator3D sets the sprite at runtime); only the material is null. Fix: `MobPrefabBaker.EnsureBillboardMaterial()` creates a PERSISTED `Assets/2_Generated/MobPrefabs/BillboardMob.mat` and repoints each prefab's `SpriteRenderer.sharedMaterial` at it before saving. This is the [[runtime-material-on-prefab-null-pink]] rule again — any material baked into a prefab must be a .mat asset, never a `new Material`.

## RO-style pooled field spawner (`FieldMobSpawner`)

`FieldMobSpawner.cs` (+ editor `FieldMobSpawnerTool.cs`, menu `Add Field Mob Spawner`) is the runtime, data-driven, **object-pooled** alternative to scene-BAKED field mobs. It keeps a `population` of random catalog mobs alive within `radius`, respawning on a timer (`respawnDelay` + jitter) after each death — RO's map mob-table model. Config: `population`, `radius`, `respawnDelay/respawnJitter`, `biome`, `minTier/maxTier`, `includeCommons` (feeds `MonsterCatalog.WeightedPick(biome,tier)`). No `Update` — event-driven; zero-alloc respawn timer coroutine.

**Pool mechanics:** per-type `Dictionary<string, Stack<GameObject>>` keyed by `MonsterDef.name`. Death → `PooledMob.OnFadedOut` → `ReturnToPool` → `SetActive(false)` + push. Next same-type spawn pops, `SetActive(true)`, revives. Fresh types load via `MobSpawner.Spawn` (Addressables) then get a `PooledMob` added. So after warmup steady-state respawns are ~0 alloc (no Destroy/Instantiate) — same cost class as baked in-place recycle; the only extra is one-time Instantiate + first Addressables load per type.

**Key wiring (measured-safe):**
- `MobRespawn.Reactivate(GameObject, Vector3)` is now a **public static** = THE single source of truth for "revive a mob object" (re-enable order + `ClampStepOffset`); used by both in-place `MobRespawn` and the pool. Don't fork this logic.
- `Health.Die` handoff order: `PooledMob` → else `MobRespawn` → else Destroy (additive; baked mobs unchanged).
- `MonsterAI3D.SetHome(pos)` resets the private wander anchor `home` — the pool calls it so a reused mob wanders around where it reappeared, not its first spawn.
- Pooling via `SetActive(false)` is safe: `MonsterAI3D` add/removes from `Active` on OnEnable/OnDisable (registry stays correct), and `UnitCrowd.Register` keeps NO object registry (only sets `go.layer`), so a banked mob leaves nothing stale. `MonsterAI3D.OnEnable` only re-fires (re-adds to Active) once `Reactivate` sets `.enabled = true` — so `SetActive(true)` must be followed by `Reactivate`.

## Generator + all maps now use spawners (2026-07-19)

Field maps no longer bake mobs into the scene — every map runs `FieldMobSpawner`(s):
- **Future generations:** `RecipeGenerator` MOBS block calls `RecipeGenerator.PlaceMobSpawner(mobGroup, mobsRoot, center=0, sizeX, sizeZ, biome)` per `RecipeMobGroup` instead of `BuildMonster`-baking. The old `PlaceMobs`/`ResolveArchetype` were REMOVED (dead). `PlaceMobSpawner` is `public static` (shared with migration). Spawner radius = `0.5·max(sizeX,sizeZ)`, population = the group's count/density·area, `fixedMobId = mobGroup.archetypeName` (empty=weighted mix, a mob id, or "Boss").
- **Existing maps:** `Convert Baked Mobs → Spawners (all maps)` (`Editor/MobSpawnerMigration.cs`) is **SCENE-driven, NOT recipe-driven** — MEASURED: the World Creator emits ~42 scenes (`field_0…16`, `dungeon_0…13`, `lastline…9`) from only ~4 template recipes whose `mapName` doesn't match the scene files (FieldRecipe.mapName="StarterTown" etc.), so a recipe→scene migration converted 0. The fix iterates `Assets/Scenes/*.unity`, and for each with a baked `Mobs` root: population = baked mob count; biome/tier recovered from the baked mob TYPES (each GameObject is named `Monster_<def>`, so `MonsterCatalog.Get(suffix)` → biome/tier; modal biome, observed tier band); spawn radius from how far baked mobs spread from origin. Then deletes the `Mobs` root and adds one `FieldMobSpawner`. Hand-placed bosses live OUTSIDE the `Mobs` root (`RegionBuilder`) so they survive; towns (0 mobs) are skipped.
- `RecipeMobGroup` gained `respawnDelay`/`respawnJitter` (append-only, safe). `FieldMobSpawner` gained `fixedMobId` + `PickDef()` (empty→WeightedPick, "Boss"→RandomBoss, else `MonsterCatalog.Get`).
- **Hard dependency now:** maps are empty of mobs until **Bake Mob Prefabs** is run (spawners draw from Addressable mobs), and a real player BUILD needs Addressables content built. `RegionBuilder`'s Spawn Field Boss still bakes a single boss (unchanged).

**Baked vs pooled (perf):** baked field mobs (`RecipeGenerator` → scene) recycle IN PLACE via `MobRespawn` (object stays active, SR hidden). Pooled spawner fully deactivates banked mobs. Both are ~0-GC at steady state; pooled adds data-driven variety + runtime population control at the cost of first-per-type Instantiate/load and a bigger startup burst. Measure with PerfHud F3 (`GC/s` ~0 during respawns) or Profiler ▸ Hierarchy GC Alloc.

Related: [[mob-item-map-data-tables]], [[changes-must-persist-in-repo]], [[performance-first-prebake]], [[perf-runtime-hotpath-patterns]].
