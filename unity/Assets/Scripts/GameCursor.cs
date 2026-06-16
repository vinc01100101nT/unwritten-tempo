using UnityEngine;

/// <summary>
/// Swaps the hardware mouse cursor (Kenney Cursor Pack) to match intent, Dota-style:
///   • default     — the normal pointer.
///   • attack      — shown while hovering an attackable monster.
///   • attack-move — shown while the player is "attack-armed" (pressed A, awaiting a click).
///
/// Textures and per-cursor hotspots are assigned by the editor builder (Setup Mouse
/// Combat). It uses <see cref="Cursor.SetCursor"/>, so the chosen PNGs must be imported
/// as readable Cursor textures — see the KenneyCursorPack special-case in
/// <c>PixelArtImportPostprocessor</c>.
/// </summary>
public class GameCursor : MonoBehaviour
{
    [Header("Cursor textures (assigned by Setup Global Systems)")]
    public Texture2D defaultCursor;
    [Tooltip("Pixel offset of the 'active point' from the texture's top-left.")]
    public Vector2 defaultHotspot = new Vector2(3, 3);     // tip of pointer_b

    public Texture2D attackCursor;
    public Vector2 attackHotspot = Vector2.zero;           // (0,0) => auto-centered at runtime

    public Texture2D attackMoveCursor;
    public Vector2 attackMoveHotspot = Vector2.zero;       // (0,0) => auto-centered at runtime

    enum Kind { None, Default, Attack, AttackMove }
    Kind current = Kind.None;

    PlayerCommander commander;
    Camera cam;

    void Awake()
    {
        commander = FindFirstObjectByType<PlayerCommander>();
        cam = Camera.main;
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (commander == null) commander = FindFirstObjectByType<PlayerCommander>();

        Kind want = Kind.Default;
        if (commander != null && commander.AttackArmed)
            want = Kind.AttackMove;
        else if (!PlayerAttacker.PointerOverUI() && PlayerAttacker.MonsterUnderCursor(cam) != null)
            want = Kind.Attack;

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
        if (hotspot == Vector2.zero) hotspot = new Vector2(tex.width, tex.height) * 0.5f;   // center
        Cursor.SetCursor(tex, hotspot, CursorMode.Auto);
    }

    void OnDisable() => Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);   // restore the OS cursor
}
