using UnityEngine;

/// <summary>
/// Procedural 2D "hello world": builds a camera, a walled arena with a
/// checkerboard floor and scattered crates, and a player you can drive with
/// WASD / arrow keys. Every sprite is generated at runtime, so NO art assets
/// are required.
///
/// Setup: in an otherwise-empty scene, create one empty GameObject, add this
/// component, and press Play.
/// </summary>
public class Bootstrap : MonoBehaviour
{
    [Tooltip("Build the procedural box arena (floor/walls/crates). Turn this OFF " +
             "once you have a real Tilemap field so the two don't overlap.")]
    public bool buildArena = true;

    [Header("Arena size (in tiles)")]
    public int arenaWidth = 24;
    public int arenaHeight = 16;

    [Header("Obstacles")]
    public int crateCount = 14;

    void Start()
    {
        // When the persistent Systems scene is present it owns the camera + player, so the
        // per-map Bootstrap stands down entirely (no second camera / placeholder avatar).
        if (GameSystems.Instance != null) return;

        SetupCamera();
        if (buildArena) BuildArena();

        // The player avatar and camera-follow now live in the persistent systems layer:
        // GameSystems spawns Resources/Player.prefab and PersistentPlayer positions it at a
        // SpawnPoint + aims the camera, surviving portals. Bootstrap no longer owns the player.
        //
        // Fallback for the bare prototype (no Player prefab set up yet): spawn the placeholder
        // box so the arena is still playable. It's made persistent too, so it behaves like the
        // real prefab would — one avatar that survives scene loads.
        if (PersistentPlayer.Instance == null && FindFirstObjectByType<PlayerController2D>() == null)
        {
            var player = SpawnPlaceholderPlayer();
            AttachCameraFollow(player.transform);
        }
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
        }
        cam.orthographic = true;
        if (buildArena) cam.orthographicSize = arenaHeight * 0.6f;  // only drive zoom for the procedural arena
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
        cam.transform.position = new Vector3(0f, 0f, -10f);

        // Dynamic top-down depth: sort transparent renderers by Y (lower on screen = in front).
        // Set here too so it's live from the very first frame, even before CameraFollow2D attaches.
        cam.transparencySortMode = TransparencySortMode.CustomAxis;
        cam.transparencySortAxis = new Vector3(0f, 1f, 0f);
    }

    void BuildArena()
    {
        var floorA = new Color(0.13f, 0.15f, 0.20f);
        var floorB = new Color(0.16f, 0.18f, 0.24f);
        var wallColor = new Color(0.30f, 0.33f, 0.42f);
        var crateColor = new Color(0.55f, 0.40f, 0.24f);

        int halfW = arenaWidth / 2;
        int halfH = arenaHeight / 2;

        var floorRoot = new GameObject("Floor").transform;

        // Checkerboard floor so movement is easy to read.
        for (int x = -halfW; x < halfW; x++)
        {
            for (int y = -halfH; y < halfH; y++)
            {
                var c = ((x + y) & 1) == 0 ? floorA : floorB;
                // Floor sits behind everyone (-100); walls/crates/player share order 0 and
                // Y-sort against each other via the camera's transparency axis.
                var tile = MakeSprite($"Floor_{x}_{y}", c, new Vector3(x + 0.5f, y + 0.5f, 0f), -100);
                tile.transform.SetParent(floorRoot);
            }
        }

        // Border walls (with colliders) so the player is boxed in.
        var wallRoot = new GameObject("Walls").transform;
        for (int x = -halfW - 1; x <= halfW; x++)
        {
            SpawnWall(new Vector3(x + 0.5f, halfH + 0.5f, 0f), wallColor, wallRoot);
            SpawnWall(new Vector3(x + 0.5f, -halfH - 0.5f, 0f), wallColor, wallRoot);
        }
        for (int y = -halfH; y <= halfH; y++)
        {
            SpawnWall(new Vector3(-halfW - 0.5f, y + 0.5f, 0f), wallColor, wallRoot);
            SpawnWall(new Vector3(halfW + 0.5f, y + 0.5f, 0f), wallColor, wallRoot);
        }

        // Crates: static obstacles to bump into. Keep the spawn point clear.
        var crateRoot = new GameObject("Crates").transform;
        for (int i = 0; i < crateCount; i++)
        {
            var pos = new Vector3(
                Random.Range(-halfW + 1, halfW - 1) + 0.5f,
                Random.Range(-halfH + 1, halfH - 1) + 0.5f,
                0f);
            if (pos.magnitude < 2f) continue;

            var crate = MakeSprite($"Crate_{i}", crateColor, pos, 0);
            crate.AddComponent<BoxCollider2D>();
            crate.transform.SetParent(crateRoot);
        }
    }

    void SpawnWall(Vector3 pos, Color color, Transform parent)
    {
        var go = MakeSprite("Wall", color, pos, 0);
        go.AddComponent<BoxCollider2D>();
        go.transform.SetParent(parent);
    }

    // Prototype fallback only: a generated box avatar for the procedural arena when no real
    // Player prefab has been set up. PersistentPlayer makes it the single persistent avatar,
    // and PersistentPlayer.Start moves it to a SpawnPoint if the scene has one.
    GameObject SpawnPlaceholderPlayer()
    {
        var go = MakeSprite("Player", new Color(1f, 0.80f, 0.30f), Vector3.zero, 0);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        go.AddComponent<BoxCollider2D>();
        go.AddComponent<PlayerController2D>();
        go.AddComponent<PathAgent>();          // smart routing around walls/crates
        // Click-to-move for the placeholder box too (pulls in PlayerAttacker via RequireComponent).
        // The hardware cursor now lives on GameSystems (Setup Global Systems), not on the player.
        go.AddComponent<PlayerCommander>();
        go.AddComponent<PersistentPlayer>();   // one avatar, survives portals
        return go;
    }

    void AttachCameraFollow(Transform target)
    {
        var cam = Camera.main;
        var follow = cam.GetComponent<CameraFollow2D>();
        if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow2D>();
        follow.target = target;
    }

    static GameObject MakeSprite(string name, Color color, Vector3 pos, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.Solid(color);
        sr.sortingOrder = sortingOrder;
        return go;
    }
}
