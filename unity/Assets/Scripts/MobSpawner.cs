using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// THE runtime choke point for "make a live mob from catalog data" during Play. The mob BUILDER
/// (<c>RigBuilder25D</c>) is editor-only, so it can't stamp a <see cref="MonsterDef"/> into a live mob in a
/// build. Instead every mob is baked to a PREFAB marked Addressable under address <c>Mobs/&lt;asset name&gt;</c>
/// at author time (Tools ▸ unwritten ▸ Mobs &amp; NPCs ▸ Bake Mob Prefabs); this loads that prefab by def and
/// clones it. Route all data-driven spawning through here so the address key + caching + hideFlags rule live
/// in one place.
///
/// Loading is ASYNC and runs through <see cref="AddressablePrefabs"/> — the shared address→prefab cache the
/// character models use too, so the "load once, let go once unused for two maps, never retry a dead address" rule has one
/// owner rather than a copy per system. The first spawn of a given mob loads its prefab off the main path and
/// every later spawn is an immediate <see cref="UnityEngine.Object.Instantiate"/>. Instances are plain clones
/// (NOT Addressables-managed), so a mob's death/recycle lifecycle (<c>MobRespawn</c>, <c>DeathDissolve</c>) is
/// unchanged — only where the prefab comes from changed (Addressables instead of a Resources folder).
/// </summary>
public static class MobSpawner
{
    const string AddressPrefix = "Mobs/";

    /// <summary>Spawn <paramref name="def"/>'s baked prefab with its feet at <paramref name="foot"/>, belonging
    /// to <paramref name="map"/>. The prefab loads async on first use then is cached, so
    /// <paramref name="onSpawned"/> may fire this frame (cached) or a few frames later (first load). Delivers
    /// the new mob, or null if that def has no baked Addressable prefab (baker not run for it, or Addressables
    /// content not built).
    ///
    /// <para><b>The map is asked for, not guessed.</b> A creature's map is the scene it sits in, and Unity puts
    /// a new object in whichever scene was loaded LAST — which on a machine holding several is not the map that
    /// asked for it. Worse, the loading above means the creature is built some frames after the asking, so even
    /// the map that was current at the time may not be by then. Every caller therefore says which map its
    /// creature belongs to, and none of them can forget.</para></summary>
    public static void Spawn(MonsterDef def, Vector3 foot, Scene map, Action<GameObject> onSpawned = null)
    {
        if (def == null) { onSpawned?.Invoke(null); return; }

        // A CREATURE DRAWN AS A CHARACTER MUST NAME A BODY THE GAME CAN LOAD, checked by the server before it makes
        // one. Without this a bad name is only found by the watchers, each of which would draw an invisible creature
        // that still fights. Listing the roster loads no model, only the names, and it is fetched once per run.
        if (GameServer.IsServer && CreatureKinds.Is3D(def.kind))
        {
            CharacterModels.Names(roster =>
            {
                if (!HasBody(roster, def.modelName))
                {
                    if (refusedBody.Add(def.id))
                        GameLog.Warn(LogCat.Unwritten, "creature '" + def.id + "' names body '" + def.modelName +
                                         "', which is not in the character roster — it is not made. Pick its body " +
                                         "in the Mob Table.");
                    onSpawned?.Invoke(null);
                    return;
                }
                LoadAndClone(def, foot, map, onSpawned);
            });
            return;
        }

        LoadAndClone(def, foot, map, onSpawned);
    }

    // A TAINTED OR BLIGHTED VERSION WEARS ITS ORIGINAL'S BODY: one baked body per original, shared by its versions.
    static void LoadAndClone(MonsterDef def, Vector3 foot, Scene map, Action<GameObject> onSpawned)
        => AddressablePrefabs.Load(AddressPrefix + (def.versionOf != null ? def.versionOf.id : def.id), prefab =>
        {
            var go = prefab != null ? Clone(prefab, foot, map, def) : null;
            onSpawned?.Invoke(go);
            return go;
        });

    // Each creature refused for a body it cannot wear is written to the log once, not once per attempt: a field
    // spawner retries an empty slot, and the reason is the same every time.
    static readonly HashSet<string> refusedBody = new HashSet<string>();

    static bool HasBody(IReadOnlyList<string> roster, string body)
    {
        if (roster == null || string.IsNullOrWhiteSpace(body)) return false;
        for (int i = 0; i < roster.Count; i++)
            if (string.Equals(roster[i], body, StringComparison.Ordinal)) return true;
        return false;
    }

    static bool SamePieces(Item[] a, Item[] b)
    {
        int na = a != null ? a.Length : 0, nb = b != null ? b.Length : 0;
        if (na != nb) return false;
        for (int i = 0; i < na; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // SERVER: a switched-off parent each creature is made under, so none of its parts runs a line before its picture is
    // taken off — a part's first line (its Awake) runs the moment it is made on a live object, and on a flat creature
    // that first line cuts sprite frames and builds a cast shadow. Kept across map loads; remade if anything destroyed it.
    static Transform unborn;

    static Transform Unborn()
    {
        if (unborn != null) return unborn;
        var holder = new GameObject("MobSpawner_Unborn") { hideFlags = HideFlags.DontSaveInEditor };
        holder.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(holder);
        return unborn = holder.transform;
    }

    static GameObject Clone(GameObject prefab, Vector3 foot, Scene map, MonsterDef def)
    {
        GameObject go;
        MobBody.FlatArt art;
        if (GameServer.IsServer)
        {
            // THE SERVER DRAWS NOTHING, so a creature it makes never has a picture at all: made asleep, its picture's
            // measurements read for the body fit below, the picture parts removed, and only then woken.
            go = UnityEngine.Object.Instantiate(prefab, foot, Quaternion.identity, Unborn());
            art = MobBody.FlatArt.Of(go.GetComponent<BillboardAnimator3D>());   // perf-ok: once per creature made
            MobBody.DropFlatPicture(go, beforeItWakes: true);
            go.transform.SetParent(null, true);
        }
        else
        {
            go = UnityEngine.Object.Instantiate(prefab, foot, Quaternion.identity);
            art = MobBody.FlatArt.Of(go.GetComponent<BillboardAnimator3D>());   // perf-ok: once per creature made
        }
        go.hideFlags = HideFlags.DontSaveInEditor;   // a Play-only clone must never be baked into a saved scene
        MapManager.PutInMap(go, map);
        // ITS KIND, from its data, before anything on it asks — its skills, its gear and its loot all read this.
        var health = go.GetComponent<Health>();   // perf-ok: once per creature made
        if (health != null)
        {
            health.kind = def.kind;
            health.multiplier = CreaturePay.Clamp(def.multiplier);
            health.creatureId = def.id;
            // ITS ELEMENT AND LEVEL, from its data, on every machine — so the server and a watching copy can never
            // disagree about what a hit on it does. The prefab's baked element is only the baker's copy of this.
            health.element = def.element;
            health.elementLevel = def.elementLevel;
        }
        // ITS NAME, from its data. The plate otherwise falls back to the object's name, which Unity has just made
        // "<prefab>(Clone)" — and every readout that asks the plate for a name prints that suffix.
        var plate = go.GetComponent<MobNameplate>();   // perf-ok: once per creature made
        if (plate != null && !string.IsNullOrEmpty(def.displayName)) plate.displayName = def.displayName;
        // ITS GEAR'S GRADE, FOLDS AND INK come from its catalog entry, which OWNS them; the baked prefab only mirrors
        // what the baker saw. Its gear was already put on while it was being built, so a mirror that has gone stale —
        // the table edited without re-baking — is corrected here by putting it on again at the entry's settings.
        // Only on the machine that owns the world: a watching copy is told what the creature wears, and rolls nothing.
        if (GameServer.IsServer)
        {
            var gear = go.GetComponent<UnitEquipment>();   // perf-ok: once per creature made
            var block = go.GetComponent<UnitStatBlock>();   // perf-ok: once per creature made
            bool rederive = false;
            // ITS LEVEL AND STAT POINTS come from its catalog entry too, for the same reason: the entry owns them and
            // the prefab holds only what the baker saw, so stats edited without re-baking are corrected here. Whoever
            // made it may still replace them afterwards (a summon at a typed level, a saved character's own).
            var authored = def.StatInputs;
            // perf-ok: once per creature made
            if (block != null && !block.inputs.Equals(authored)) block.SetSpawnInputs(authored);
            // The pieces themselves too, not only their settings: the weapon picked in the table is worn without a re-bake.
            if (gear != null && (gear.gearQuality != def.WornGrade || gear.kind != def.kind || gear.rollsGearEachLife != def.rollsGearEachLife ||
                                 !SamePieces(gear.startingGear, def.startingGear)))
            {
                gear.startingGear = def.startingGear ?? Array.Empty<Item>();
                gear.gearQuality = def.WornGrade;
                gear.kind = def.kind;
                gear.rollsGearEachLife = def.rollsGearEachLife;
                gear.ReseatForNewLife();
                rederive = true;
            }
            // ITS WEAPON MASTERY, from its catalog entry, onto the one weapon it holds — in the same store a player's
            // mastery lives in, so it lifts that weapon's stats by the player's own rule. A creature never earns, so
            // this is the only thing that ever writes it.
            var held = def.Weapon;
            if (health != null && held != null && def.MasteryOnWeapon > 0)
            {
                health.Mastery.SetPractice(held.weaponType, MasteryMath.WeaponXpFor(def.MasteryOnWeapon));
                rederive = true;
            }
            // A creature that rolls its weapon each life has its mastery in every weapon, so whichever it rolls counts.
            if (health != null && def.rollsGearEachLife && def.weaponMastery > 0)
            {
                foreach (WeaponType w in System.Enum.GetValues(typeof(WeaponType)))
                    if (w != WeaponType.None) health.Mastery.SetPractice(w, MasteryMath.WeaponXpFor(def.weaponMastery));
                rederive = true;
            }
            // A SUMMONING KIND HOLDS THE SUMMON SKILL whatever weapon it wields, or none: it is the kind's, not a weapon's.
            if (health != null && CreatureKinds.Summons(def.kind))
                health.Mastery.GrantCreatureSkill(CreatureKinds.SummonSkillId, 1);
            // Its health and attack are multiplied by its multiplier (UnitStatMath.CreatureMaxHP,
            // CreatureKinds.AttackMultiplier), which was only set above.
            if (health != null && health.multiplier != CreaturePay.LeastMultiplier) rederive = true;
            if (rederive && block != null) block.Rederive(healToFull: true);
        }
        // ITS SIZE, from its data, on every machine — how tall it stands, its body, and the melee reach both read it, so
        // the server and a watching copy can never disagree about where a blow reaches. The prefab's baked size is only
        // the baker's copy of this.
        MobBody.Fit(go, def.kind, def.size, def.flies, art);
        // A SHADOW IS A 3D CHARACTER, decided by its kind, here — the one door every creature is made through — so no
        // caller can make one flat. Before it is announced, because what crosses the wire is read off the creature at
        // the moment it is published. It keeps the body for life, through every pooled respawn.
        // ITS LOOK comes from its entry, on every machine: the server takes the body from it (and sends that name to
        // every watcher), and each machine reads the outfit colours from its own copy of the same entry — exactly as
        // its size, element and name are read from it above.
        if (CreatureKinds.Is3D(def.kind)) go.AddComponent<MobShadow3DVisual>().WearLookOf(def);   // perf-ok: once per creature made
        // AND A KIND THAT SUMMONS gets the list its summons are kept in, knowing which creature it is so it can work out
        // whom it calls. Server only: a watching copy summons nothing, it is told about each summon.
        if (GameServer.IsServer && CreatureKinds.Summons(def.kind)) go.AddComponent<CreatureSummons>().def = def;   // perf-ok: once per creature made
        // AND A KIND THAT CAN TURN BACK into the character the artist drew rolls for it when it dies (SavedCharacters).
        // Server only: a watching copy is told about the character, it rolls nothing.
        if (GameServer.IsServer && SavedCharacterRules.CanTurnBack(def.kind)) go.AddComponent<TurnsBackOnDeath>().def = def;   // perf-ok: once per creature made
        return go;
    }
}
