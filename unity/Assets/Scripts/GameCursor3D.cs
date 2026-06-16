using UnityEngine;

/// <summary>
/// 3D port of <see cref="GameCursor"/> — swaps the hardware cursor (Kenney pack) to match intent,
/// Dota-style: default pointer, sword while hovering a mob, crosshair while attack-armed (A).
/// Reads <see cref="Player3DController"/> (AttackArmed + the static MonsterUnderCursor). Textures
/// are assigned by Build 2.5D Spike. Uses Cursor.SetCursor, so the PNGs must import as readable
/// Cursor textures (the KenneyCursorPack special-case in PixelArtImportPostprocessor handles that).
/// </summary>
public class GameCursor3D : MonoBehaviour
{
    public Texture2D defaultCursor;    public Vector2 defaultHotspot = new Vector2(3, 3);
    public Texture2D attackCursor;     public Vector2 attackHotspot = Vector2.zero;       // (0,0) => centered
    public Texture2D attackMoveCursor; public Vector2 attackMoveHotspot = Vector2.zero;   // (0,0) => centered

    enum Kind { None, Default, Attack, AttackMove }
    Kind current = Kind.None;

    Player3DController player;
    Camera cam;

    void Awake()
    {
        player = FindFirstObjectByType<Player3DController>();
        cam = Camera.main;
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (player == null) player = FindFirstObjectByType<Player3DController>();

        Kind want = Kind.Default;
        if (player != null && player.AttackArmed) want = Kind.AttackMove;
        else if (Player3DController.MonsterUnderCursor(cam) != null) want = Kind.Attack;

        if (want != current) { Apply(want); current = want; }
    }

    void Apply(Kind k)
    {
        switch (k)
        {
            case Kind.Attack:     Set(attackCursor, attackHotspot); break;
            case Kind.AttackMove: Set(attackMoveCursor, attackMoveHotspot); break;
            default:              Set(defaultCursor, defaultHotspot); break;
        }
    }

    void Set(Texture2D tex, Vector2 hotspot)
    {
        if (tex == null) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }
        if (hotspot == Vector2.zero) hotspot = new Vector2(tex.width, tex.height) * 0.5f;
        Cursor.SetCursor(tex, hotspot, CursorMode.Auto);
    }

    void OnDisable() => Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);   // restore the OS cursor
}
