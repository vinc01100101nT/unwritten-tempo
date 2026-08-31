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

    /// <summary>Teleport the avatar back to the current map's SpawnPoint (used by the death screen
    /// on respawn). Reuses the same snap-to-terrain placement as a fresh map arrival.</summary>
    public void Respawn() => PlaceInScene();

    /// <summary>Put the player back where their save says they were. Called once, by <see cref="SaveManager"/>,
    /// after the save arrives — which is always later than the map coming up, because it crosses a network.</summary>
    public void ReturnToSavedPlace() => PlaceInScene();

    void PlaceInScene()
    {
        placed = true;

        // Any leftover hand-placed avatar in the map yields to the persistent one.
        foreach (var pc in FindObjectsByType<Player3DController>())
            if (pc.gameObject != gameObject) Destroy(pc.gameObject);

        if (controller != null) controller.Halt();   // don't carry a half-finished order across maps

        // Where the player logged out beats where the map would put them — but only in the map they logged
        // out of, and only once. Anything else (a door, a death, a fresh character) falls through to the
        // spawn point exactly as before.
        if (SceneTravel.HasResume && SceneTravel.ResumeScene == MapManager.CurrentMap)
        {
            MoveTo(SceneTravel.ResumePosition);
            SceneTravel.ForgetResume();
            AimCamera();
            return;
        }

        var spawn = ResolveSpawn();
        if (spawn != null) MoveTo(spawn.transform.position);

        AimCamera();
    }

    void MoveTo(Vector3 pos)
    {
        var cc = GetComponent<CharacterController>();

        // Snap so the sprite's FEET rest exactly on the terrain — place the capsule bottom on the
        // surface (computed from the controller, scaled), instead of lifting and hoping gravity settles.
        if (Physics.Raycast(new Vector3(pos.x, pos.y + 50f, pos.z), Vector3.down, out var hit, 200f,
                            groundMask.value != 0 ? groundMask : ~0, QueryTriggerInteraction.Ignore))
        {
            float capsuleBottom = cc != null ? (cc.center.y - cc.height * 0.5f) * transform.lossyScale.y : 0f;
            pos.y = hit.point.y - capsuleBottom;
        }

        // CharacterController fights direct transform writes — disable it for the teleport frame. try/finally so
        // it's ALWAYS re-enabled even if the position write triggers something that throws (a disabled controller
        // left behind would spam "Move called on inactive controller" forever).
        if (cc != null) cc.enabled = false;
        try { transform.position = pos; }
        finally { if (cc != null) cc.enabled = true; }
    }

    // SpawnPoint a Portal requested (by id), else one named "Start", else the first, else null.
    SpawnPoint ResolveSpawn()
    {
        var points = FindObjectsByType<SpawnPoint>();

        if (!string.IsNullOrEmpty(SceneTravel.Target))
        {
            foreach (var sp in points)
                if (sp.id == SceneTravel.Target) { SceneTravel.Target = null; return sp; }
            SceneTravel.Target = null;   // id not in this map — ignore
        }
        foreach (var sp in points) if (sp.id == "Start") return sp;
        return points.Length > 0 ? points[0] : null;
    }

    // The persistent follow camera lives in Systems and is the only rig — point it at us.
    void AimCamera()
    {
        var follow = FindAnyObjectByType<CameraFollow3D>();
        if (follow != null) follow.target = transform;
    }
}
