using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The single player avatar for the WHOLE game. It lives in the persistent <c>Systems</c>
/// scene (never unloaded), so everything bolted to it — movement
/// (<see cref="PlayerController2D"/>), the input brain (<see cref="PlayerCommander"/>),
/// <see cref="PlayerAttacker"/> and pathing — keeps working across map swaps with no
/// <c>DontDestroyOnLoad</c>. The singleton check is a guard: any stray copy a map carries
/// destroys itself.
///
/// On every map load it does the arrival routing that used to live in <see cref="Bootstrap"/>:
/// cancel any carried order, teleport to the <see cref="SpawnPoint"/> a <see cref="Portal"/>
/// asked for (via <see cref="SceneTravel.Target"/>), re-aim the persistent camera, and clear
/// out any stray per-map Player.
/// </summary>
[RequireComponent(typeof(PlayerController2D))]
public class PersistentPlayer : MonoBehaviour
{
    public static PersistentPlayer Instance { get; private set; }

    Rigidbody2D rb;
    PlayerCommander commander;
    bool placed;   // guards the initial placement so it never doubles with sceneLoaded

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);   // a map loaded its own copy — drop it, keep the Systems one
            return;
        }
        Instance = this;

        rb = GetComponent<Rigidbody2D>();
        commander = GetComponent<PlayerCommander>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // sceneLoaded does NOT fire for the scene we were spawned into, so Start covers the
    // very first map; the flag stops it from running twice if both ever happen.
    void Start() { if (!placed) PlaceInScene(); }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => PlaceInScene();

    void PlaceInScene()
    {
        placed = true;

        // Any leftover per-scene Player (an old hand-placed avatar) yields to the
        // persistent one, so maps authored before this change just work.
        foreach (var pc in FindObjectsByType<PlayerController2D>(FindObjectsSortMode.None))
            if (pc.gameObject != gameObject) Destroy(pc.gameObject);

        // Don't carry a half-finished move/attack order from the previous map into this one.
        if (commander != null) commander.Stop();

        var target = ResolveSpawn();
        if (target != null)
        {
            Vector3 p = target.transform.position;
            p.z = transform.position.z;
            transform.position = p;
            if (rb != null) { rb.position = p; rb.linearVelocity = Vector2.zero; }   // sync the body so interpolation can't snap us back
        }

        AimCamera();
    }

    // Where to arrive: the SpawnPoint a Portal requested (matched by id), else one named
    // "Start", else the first SpawnPoint in the scene, else stay put.
    SpawnPoint ResolveSpawn()
    {
        var points = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);

        if (!string.IsNullOrEmpty(SceneTravel.Target))
        {
            foreach (var sp in points)
                if (sp.id == SceneTravel.Target) { SceneTravel.Target = null; return sp; }
            SceneTravel.Target = null;   // id not in this scene — ignore it
        }

        SpawnPoint start = null;
        foreach (var sp in points) if (sp.id == "Start") { start = sp; break; }
        return start != null ? start : (points.Length > 0 ? points[0] : null);
    }

    // The persistent camera lives in the Systems scene and is the only follow rig (MapManager
    // strips any a map carries), so find it by scene — robust against a map's leftover camera.
    void AimCamera()
    {
        foreach (var follow in FindObjectsByType<CameraFollow2D>(FindObjectsSortMode.None))
            if (follow.gameObject.scene.name == MapManager.SystemsScene) { follow.target = transform; return; }
    }
}
