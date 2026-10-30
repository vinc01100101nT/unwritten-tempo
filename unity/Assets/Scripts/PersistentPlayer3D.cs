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

    /// <summary>True once this body stands where its arrival put it: in a map, with no other map on its way and its
    /// saved spot already applied. Until then its position is a leftover — the sign-in screen's origin, or the map
    /// being left — and must not be reported to the server as where this person is. A map's arrival places the body
    /// in the same moment the map becomes current, so "in a map, nothing on its way" already means "placed".</summary>
    public bool HasArrived =>
        !string.IsNullOrEmpty(MapManager.CurrentMap)
        && MapManager.ArrivingAt == null
        && !SceneTravel.HasResume
        && !SeamlessWorld.PlacementPending
        && (SaveManager.Instance == null || SaveManager.Instance.Initialized);

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
            var resumeAt = SceneTravel.ResumePosition;
            SceneTravel.ForgetResume();
            Land(resumeAt, null, false);
            return;
        }

        var spawn = ResolveSpawn();
        if (spawn != null) { Land(spawn.transform.position, spawn, true); return; }
        SayWherePlaced(null);
        AimCamera();
    }

    // EVERY ARRIVAL LANDS THROUGH HERE. In a seamless world the ground at the spot may not have loaded yet — a respawn, a
    // wing or a sign-in can be hundreds of metres from where this machine was — so the body is held back behind the
    // loading screen until it has (SeamlessWorld.PlaceWhenReady), instead of being put down over nothing and falling.
    void Land(Vector3 at, SpawnPoint spawn, bool say)
    {
        var world = SeamlessWorld.ForMap(MapManager.CurrentMap);
        // A server's own idle body is nobody's to hold back: no one sees it and no ground is loaded for it.
        if (world == null || GameServer.IsServer) { LandNow(at, spawn, say); return; }
        world.PlaceWhenReady(MapManager.InMap(at, MapManager.CurrentMap), () => LandNow(at, spawn, say));
    }

    void LandNow(Vector3 at, SpawnPoint spawn, bool say)
    {
        MoveTo(at);
        if (say) SayWherePlaced(spawn);
        AimCamera();
    }

    // WHERE THEY WERE PUT, said once per arrival. "The player cannot move after a teleport" was investigated
    // twice by hand before this line existed; the answer was that they had been put at the WRONG map's spot.
    void SayWherePlaced(SpawnPoint spawn)
    {
        if (GameLog.On(LogCat.Client))
            GameLog.Info(LogCat.Client, "placed in '" + MapManager.CurrentMap + "' at "
                             + (spawn != null ? "spawn '" + spawn.id + "' " : "no spawn point — left ")
                             + Coords.Text(transform.position, "0.00"));   // perf-ok: once per map arrival
    }

    // How far an arrival may be stepped out of something solid — under the server's 1.5 m reach for an arrival report.
    const float ArrivalNudge = 1.25f;
    static readonly Collider[] blockScratch = new Collider[16];

    // Something solid in this map — not the ground, not a trigger, not this body — inside the body's capsule at `at`.
    bool Blocked(Vector3 at, CharacterController cc, UnityEngine.SceneManagement.Scene arrivedIn)
    {
        float s = transform.lossyScale.y, radius = cc.radius * s;
        Vector3 c = at + cc.center * s;
        float half = Mathf.Max(0f, cc.height * 0.5f * s - radius);
        // The bottom sphere is lifted off the ground by a hair, so the ground under the feet is never counted.
        var bottom = c - Vector3.up * (half - 0.02f);
        var top = c + Vector3.up * half;
        int n = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, blockScratch, ~(groundMask.value), QueryTriggerInteraction.Ignore);   // perf-ok: once per map arrival / respawn
        for (int i = 0; i < n; i++)
        {
            var col = blockScratch[i];
            if (col.transform.IsChildOf(transform)) continue;
            if (arrivedIn.IsValid() && col.gameObject.scene != arrivedIn) continue;
            return true;
        }
        return false;
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
        float capsuleBottom = cc != null ? (cc.center.y - cc.height * 0.5f) * transform.lossyScale.y : 0f;
        Vector3 Grounded(Vector3 p)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, p.y + 50f, p.z), Vector3.down, 200f,
                                          groundMask.value != 0 ? groundMask : ~0, QueryTriggerInteraction.Ignore);   // perf-ok: once per map arrival / respawn
            float groundY = float.NaN;
            for (int i = 0; i < hits.Length; i++)
            {
                if (arrivedIn.IsValid() && hits[i].collider.gameObject.scene != arrivedIn) continue;
                if (float.IsNaN(groundY) || hits[i].point.y > groundY) groundY = hits[i].point.y;
            }
            if (!float.IsNaN(groundY)) p.y = groundY - capsuleBottom;
            return p;
        }
        Vector3 want = pos;
        pos = Grounded(want);

        // NEVER PUT DOWN INSIDE SOMETHING SOLID. An arrival spot is a point on the ground, and a rock, a tree or a wall can
        // stand on it (measured 2026-10-29: Gemmara's middle is 0.39 from a rock a metre wide). Stepped out in rings of a
        // quarter metre, up to ArrivalNudge — inside the server's own reach for an arrival, so the server still takes it.
        if (cc != null && Blocked(pos, cc, arrivedIn))
        {
            bool found = false;
            for (float r = 0.25f; r <= ArrivalNudge && !found; r += 0.25f)
                for (int k = 0; k < 8 && !found; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    var tryAt = Grounded(want + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                    if (!Blocked(tryAt, cc, arrivedIn)) { pos = tryAt; found = true; }
                }
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
        // character and a death with no save point both come out at the same spot (SavePoint).
        if (MapManager.CurrentMap == SavePoint.DefaultMap)
            foreach (var sp in points)
                if (sp.id == SavePoint.DefaultSpawnId) return sp;
        return points.Count > 0 ? points[0] : null;
    }

    // The persistent follow camera lives in Systems and is the only rig — point it at us.
    // AND ON US, NOW. Every placement is a jump — a door, a sign-in, a save point — and with smooth follow on the
    // camera otherwise eased the whole way from the map being left, thousands of units, to where we landed.
    void AimCamera()
    {
        var follow = FindAnyObjectByType<CameraFollow3D>();
        if (follow == null) return;
        follow.target = transform;
        follow.SnapToTarget();
    }
}
