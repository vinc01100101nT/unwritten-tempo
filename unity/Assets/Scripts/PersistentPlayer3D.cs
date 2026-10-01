using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The single billboard player avatar for the WHOLE 2.5D game.
/// It lives in the persistent <c>Systems</c> scene (never unloaded),
/// so its movement (<see cref="Player3DController"/>), combat (<see cref="Player3DAttacker"/>) and the
/// follow camera survive every map swap with no <c>DontDestroyOnLoad</c>.
///
/// On each map load it: drops any stray per-map player, cancels a carried order, teleports to the
/// <see cref="SpawnPoint"/> a <see cref="Portal3D"/> asked for (via <see cref="SceneTravel.Target"/>),
/// raycast-snaps onto that map's terrain (so it lands on the hills, not in the air), and re-aims the
/// persistent <see cref="CameraFollow3D"/>.
/// </summary>
public class PersistentPlayer3D : MonoBehaviour
{
    public static PersistentPlayer3D Instance { get; private set; }

    [Tooltip("Layer(s) treated as walkable ground for the arrival snap. Auto-set to 'Ground' if empty.")]
    public LayerMask groundMask;

    Player3DController controller;
    bool placed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        controller = GetComponent<Player3DController>();
        if (groundMask.value == 0)
        {
            int g = LayerMask.NameToLayer("Ground");
            if (g >= 0) groundMask = 1 << g;
        }
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy() { if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded; }

    void Start() { if (!placed) PlaceInScene(); }                 // first map (no sceneLoaded for it)
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => PlaceInScene();

    /// <summary>Place the player again in the map they are already in, by whatever <see cref="SceneTravel"/> now
    /// holds — where their save says they were (<see cref="SaveManager"/>, once the save arrives, which is always later
    /// than the map coming up), or a save point / arrival point the server sent them to in this same map.</summary>
    public void ReturnToSavedPlace() => PlaceInScene();

    void PlaceInScene()
    {
        placed = true;

        // Any leftover hand-placed avatar in the map yields to the persistent one.
        foreach (var pc in FindObjectsByType<Player3DController>())
            if (pc.gameObject != gameObject) Destroy(pc.gameObject);

        if (controller != null) controller.Halt();   // don't carry a half-finished order across maps

        // An exact spot — where the player logged out, or the save point the server sent them to — beats where the
        // map would put them, but only in the map it names, and only once. Anything else (a door, a fresh
        // character, a death with no save point) falls through to an arrival point.
        if (SceneTravel.HasResume && SceneTravel.ResumeScene == MapManager.CurrentMap)
        {
            MoveTo(SceneTravel.ResumePosition);
            SceneTravel.ForgetResume();
            AimCamera();
            return;
        }

        var spawn = ResolveSpawn();
        if (spawn != null) MoveTo(spawn.transform.position);

        // WHERE THEY WERE PUT, said once per arrival. "The player cannot move after a teleport" was investigated
        // twice by hand before this line existed; the answer was that they had been put at the WRONG map's spot.
        if (GameLog.On(LogCat.Client))
            GameLog.Info(LogCat.Client, "placed in '" + MapManager.CurrentMap + "' at "
                             + (spawn != null ? "spawn '" + spawn.id + "' " : "no spawn point — left ")
                             + Coords.Text(transform.position, "0.00"));   // perf-ok: once per map arrival

        AimCamera();
    }

    void MoveTo(Vector3 pos)
    {
        var cc = GetComponent<CharacterController>();

        // Snap so the sprite's FEET rest exactly on the terrain — place the capsule bottom on the
        // surface (computed from the controller, scaled), instead of lifting and hoping gravity settles.
        // THIS MAP'S GROUND, not the one being left: the old map is still there for the frame this runs in (its
        // unload is asynchronous) and sits on the same coordinates, so a single nearest-hit raycast could seat
        // the feet on ground that is about to vanish — and leave them sunk into this map's.
        var arrivedIn = MapManager.CurrentScene;
        var hits = Physics.RaycastAll(new Vector3(pos.x, pos.y + 50f, pos.z), Vector3.down, 200f,
                                      groundMask.value != 0 ? groundMask : ~0, QueryTriggerInteraction.Ignore);   // perf-ok: once per map arrival / respawn
        float groundY = float.NaN;
        for (int i = 0; i < hits.Length; i++)
        {
            if (arrivedIn.IsValid() && hits[i].collider.gameObject.scene != arrivedIn) continue;
            if (float.IsNaN(groundY) || hits[i].point.y > groundY) groundY = hits[i].point.y;
        }
        if (!float.IsNaN(groundY))
        {
            float capsuleBottom = cc != null ? (cc.center.y - cc.height * 0.5f) * transform.lossyScale.y : 0f;
            pos.y = groundY - capsuleBottom;
        }

        // CharacterController fights direct transform writes — disable it for the teleport frame. try/finally so
        // it's ALWAYS re-enabled even if the position write triggers something that throws (a disabled controller
        // left behind would spam "Move called on inactive controller" forever).
        if (cc != null) cc.enabled = false;
        try { transform.position = pos; }
        finally { if (cc != null) cc.enabled = true; }
    }

    // The spawn point a Portal asked for by id, else the first one this map has, else null — and null means the
    // arriving body is left exactly where it is.
    // ONLY THE MAP BEING ARRIVED IN. This runs the moment the new map has loaded, and the map being left is
    // still loaded for that frame (its unload is asynchronous) — so a search across everything finds BOTH maps'
    // spawn points. Whichever came first won: MEASURED, a teleport from a field into the town put the player on
    // the FIELD's arrival point (0,0) inside the town, sunk 0.14 below the town's ground and unable to move, on
    // some arrivals and not others.
    //
    // THERE IS NO CENTRE ARRIVAL POINT ANY MORE (removed 2026-09-18 at the user's instruction). Every map used to
    // carry one called "Start" at its exact middle, planted there by the generator with nothing checking what was
    // already standing on that spot — which is why arriving in a field dropped you through the floor and out of
    // the world. Doors name their own arrival points and are unaffected; anything that arrives without naming one
    // (a teleport, a new character) falls to the lines below.
    SpawnPoint ResolveSpawn()
    {
        var all = FindObjectsByType<SpawnPoint>();
        var here = MapManager.CurrentScene;
        var points = new System.Collections.Generic.List<SpawnPoint>(all.Length);   // perf-ok: once per map arrival / respawn
        foreach (var sp in all) if (!here.IsValid() || sp.gameObject.scene == here) points.Add(sp);

        if (!string.IsNullOrEmpty(SceneTravel.Target))
        {
            foreach (var sp in points)
                if (sp.id == SceneTravel.Target) { SceneTravel.Target = null; return sp; }
            SceneTravel.Target = null;   // id not in this map — ignore
        }

        // NOBODY NAMED ONE, in the map the defaultless stand back up in: its default arrival point, so a new
        // character and a death with no save point both come out at the same door (decided 2026-09-30).
        if (MapManager.CurrentMap == SavePoint.DefaultMap)
            foreach (var sp in points)
                if (sp.id == SavePoint.DefaultSpawnId) return sp;
        return points.Count > 0 ? points[0] : null;
    }

    // The persistent follow camera lives in Systems and is the only rig — point it at us.
    void AimCamera()
    {
        var follow = FindAnyObjectByType<CameraFollow3D>();
        if (follow != null) follow.target = transform;
    }
}
