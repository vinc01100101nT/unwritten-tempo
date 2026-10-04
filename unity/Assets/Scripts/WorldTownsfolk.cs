using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// EVERY NAMED TOWNSPERSON IN THE WORLD — the one list of them, kept outside the maps, the townsfolk's twin of
/// <see cref="WorldSpawns"/>. Each row is one person: who they are, what they look like, which map they live in and
/// where their home is. When the server loads a map it puts that map's people into it (<see cref="Populate"/>), so
/// nothing about townsfolk is saved inside a map and moving one never means opening it.
///
/// <para>Lives at <c>Resources/WorldTownsfolk</c>. A home is written in the MAP'S OWN coordinates — the numbers
/// <c>/where</c> prints — so a map keeps its people wherever the server happens to set it down.</para>
/// </summary>
[CreateAssetMenu(fileName = "WorldTownsfolk", menuName = "unwritten/World Townsfolk", order = 3)]
public class WorldTownsfolk : ScriptableObject
{
    public const string ResourcePath = "WorldTownsfolk";

    [System.Serializable]
    public class Row
    {
        [Tooltip("Stable id a quest names this person by, e.g. 'gunnar'. Never change it once a quest uses it — the " +
                 "name above may be renamed freely.")]
        public string id = "";
        [Tooltip("This person's name, shown over their head and when you talk to them.")]
        public string name = "";
        [Tooltip("A short line under their name saying what they are — the service they offer (Smith, Tool Dealer), " +
                 "or who they are when they offer none.")]
        public string description = "";
        [Tooltip("The body they wear — one of the CharacterSpec's civilian models, e.g. 'Chef_Female'.")]
        public string model = "";
        [Tooltip("The map's scene name, e.g. 'lastline'.")]
        public string map = "";
        [Tooltip("Where they live, in the map's own coordinates — what /where prints: X west to east, Y south to " +
                 "north, Z height. They stand on the ground under it.")]
        [ShownCoords] public Vector3 home;
        [Tooltip("How far they stroll from their home. 0 = they stand at their home and never stroll — a shopkeeper " +
                 "at their door.")]
        [Min(0f)] public float wanderRadius = 6f;
        [Tooltip("Only for somebody who stands (Wander Radius 0): the way they face, in degrees clockwise from north.")]
        public float facing;
        [Tooltip("The craft station they work: pressing F on them opens it. None = they offer no crafting.")]
        public CraftStation station = CraftStation.None;
        [Tooltip("The shop they keep, by its id in the shops list (WorldShops): pressing F on them opens it. Empty = " +
                 "they keep no shop.")]
        public string shop = "";
        [Tooltip("Their level. At 99 or above they wear the same smoke every unit at level 99 or above wears.")]
        [Min(1)] public int level = 1;
        [Tooltip("Whether they call out the odd line when a player walks past.")]
        public bool barks = true;
    }

    public List<Row> rows = new List<Row>();

    static WorldTownsfolk loaded;
    static bool looked;

    /// <summary>The list, loaded once. Null when it has not been made — every map is then empty of townsfolk, and the
    /// server says so once.</summary>
    public static WorldTownsfolk Instance
    {
        get
        {
            if (!looked) { looked = true; loaded = Resources.Load<WorldTownsfolk>(ResourcePath); }
            return loaded;
        }
    }

    // Maps already given their people, by scene handle — a map activated twice is populated once. A map that is let
    // go takes its people with it, and its handle is forgotten, so loading it again populates it again.
    static readonly HashSet<int> populated = new HashSet<int>();
    static bool warnedNoList;

    /// <summary>Put every person who lives in <paramref name="map"/> into it, and announce each so every player near
    /// enough receives the same person. Does nothing off the server, for the persistent scene, or for a map already
    /// done.
    ///
    /// <para>NO MODEL IS FETCHED HERE. The server has no screen; each watching machine fetches its own copy of the
    /// body from the model name that crosses the wire.</para></summary>
    public static void Populate(Scene map)
    {
        if (!GameServer.IsServer || !map.IsValid() || map.name == MapManager.SystemsScene) return;
        if (!populated.Add(map.handle)) return;

        var list = Instance;
        if (list == null)
        {
            if (!warnedNoList)
            {
                warnedNoList = true;
                GameLog.Warn(LogCat.Spawn, "no '" + ResourcePath + "' list in Resources — every map is empty of townsfolk.");
            }
            return;
        }

        // The map was just set down in its place; the ground has to be where it now stands before it is aimed at.
        Physics.SyncTransforms();

        int people = 0;
        for (int i = 0; i < list.rows.Count; i++)
        {
            var row = list.rows[i];
            if (row == null || row.map != map.name) continue;
            if (string.IsNullOrWhiteSpace(row.model))
            {
                GameLog.Warn(LogCat.Spawn, "townsperson '" + row.name + "' in '" + map.name + "' has no model — left out.");
                continue;
            }

            Vector3 at = GroundUnder(MapManager.InWorld(row.home, map.name), map);
            // The ONE recipe for a townsperson, shared with the machines that only watch one.
            var root = Townsperson.Build(row.model, row.name, at, map, null);
            // Turned before anybody is told about them, so a shopkeeper is never first seen facing their own door.
            if (row.wanderRadius <= 0f) root.transform.rotation = Quaternion.Euler(0f, row.facing, 0f);
            var npc = root.GetComponent<TownNPC>();
            npc.Init(at, row.wanderRadius, row.barks, row.level, row.name, row.description, row.station, row.shop);
            // Who they are to a quest — "talk to Gunnar" is decided against this, never against a name on screen.
            npc.PersonId = row.id;
            GameNetwork.PublishNpc(root, row.model);
            people++;
        }
        if (people > 0)
            GameLog.Info(LogCat.Spawn, "map '" + map.name + "' given " + people + " townsperson(s).");
    }

    /// <summary>Forget a map that has gone, so it is populated again if it comes back.</summary>
    public static void Forget(Scene map) => populated.Remove(map.handle);

    // The highest Ground-layer surface of THIS map under the spot — not a roof, not a prop, not another map that
    // happens to share the coordinates. Falls back to the spot's own height when nothing is hit.
    static Vector3 GroundUnder(Vector3 p, Scene map)
    {
        int ground = LayerMask.NameToLayer("Ground");
        int mask = ground >= 0 ? 1 << ground : Physics.DefaultRaycastLayers;
        // perf-ok: once per townsperson per map load.
        var hits = Physics.RaycastAll(p + Vector3.up * 50f, Vector3.down, 200f, mask, QueryTriggerInteraction.Ignore);
        float best = float.NaN;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.gameObject.scene != map) continue;
            if (float.IsNaN(best) || hits[i].point.y > best) best = hits[i].point.y;
        }
        if (!float.IsNaN(best)) p.y = best;
        return p;
    }
}
