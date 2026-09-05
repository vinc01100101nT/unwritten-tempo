using UnityEngine;

/// <summary>
/// Swaps the hardware cursor (Kenney pack) to match intent,
/// Dota-style: default pointer, sword while hovering a mob, crosshair while attack-armed (A).
/// Reads <see cref="Player3DController"/> (AttackArmed + the static MonsterUnderCursor). Textures
/// are assigned by Build 2.5D Spike. Uses Cursor.SetCursor, so the PNGs must import as readable
/// Cursor textures (the KenneyCursorPack special-case in PixelArtImportPostprocessor handles that).
/// </summary>
public class GameCursor3D : MonoBehaviour
{
    // Hotspot = the click pixel INSIDE the art, and the single source of truth for it (RigBuilder25D
    // reads this default rather than re-typing a value). pointer_b.png is 32×32 with the arrow tip at
    // (9,6), measured from its alpha bbox. Must be the tip, not the top-left padding, or the visible
    // arrow and the real click point diverge (~6px) → HUD clicks land on the wrong pixel.
    public Texture2D defaultCursor;    public Vector2 defaultHotspot = new Vector2(9, 6);
    public Texture2D attackCursor;     public Vector2 attackHotspot = Vector2.zero;       // (0,0) => centered
    public Texture2D attackMoveCursor; public Vector2 attackMoveHotspot = Vector2.zero;   // (0,0) => centered
    public Texture2D targetCursor;     public Vector2 targetHotspot = Vector2.zero;       // (0,0) => centered; shown while aiming a skill

    enum Kind { None, Default, Attack, AttackMove, Target }
    Kind current = Kind.None;

    Player3DController player;
    Camera cam;

    void Awake()
    {
        player = FindAnyObjectByType<Player3DController>();
        cam = Camera.main;
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (player == null) player = FindAnyObjectByType<Player3DController>();

        // Aiming a skill (single-target UnitTargeter or AoE GroundTargeter) takes priority → reticle cursor.
        // Then: A-armed → crosshair; hovering a living mob → SWORD (so the player sees it's clickable to attack);
        // otherwise the default pointer. Skip the mob check while a panel is blocking (cursor is over the UI).
        bool freeHover = player != null && !DevSettings.Blocking;
        Kind want = UnitTargeter.Active || GroundTargeter.Active ? Kind.Target
                  : player != null && player.AttackArmed       ? Kind.AttackMove
                  : freeHover && Player3DController.MonsterUnderCursor(cam) != null ? Kind.Attack
                  :                                               Kind.Default;

        if (want != current) { Apply(want); current = want; }
    }

    void Apply(Kind k)
    {
        switch (k)
        {
            case Kind.Attack:     Set(attackCursor, attackHotspot); break;
            case Kind.AttackMove: Set(attackMoveCursor, attackMoveHotspot); break;
            // Reticle is the large 64×64 texture; ForceSoftware so the OS doesn't clamp it to ~32px (hardware
            // cursors are size-limited). Software cursor renders the texture at its full native resolution.
            case Kind.Target:     Set(targetCursor != null ? targetCursor : attackMoveCursor, targetHotspot, CursorMode.ForceSoftware); break;
            default:              Set(defaultCursor, defaultHotspot); break;
        }
    }

    void Set(Texture2D tex, Vector2 hotspot, CursorMode mode = CursorMode.Auto)
    {
        if (tex == null) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }
        if (hotspot == Vector2.zero) hotspot = new Vector2(tex.width, tex.height) * 0.5f;   // centre on the cursor point
        Cursor.SetCursor(tex, hotspot, mode);
    }

    void OnDisable() => Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);   // restore the OS cursor
}
