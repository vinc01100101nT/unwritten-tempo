using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// A reusable ground-target reticle for placed AoE skills (e.g. the Mage's Meteor). <see cref="Begin"/>
/// puts the player into AIM mode: a circle (drawn with a LineRenderer) follows the cursor on the ground,
/// clamped to <c>castRange</c> from the player and sized to the skill's AoE <c>radius</c>. <b>Left-click</b>
/// confirms (fires the skill's callback with the point); <b>Esc</b> (or re-pressing the skill) cancels.
///
/// While aiming, <see cref="Active"/> is true and <see cref="Player3DController"/> skips its click/move
/// input that frame (same pattern as <see cref="DevSettings.Blocking"/>) so the confirm click doesn't also
/// order a move. Self-boots a single hidden instance on first use.
/// </summary>
public class GroundTargeter : MonoBehaviour
{
    /// <summary>True while the player is aiming a placed skill — gameplay click input is suppressed.
    /// Self-heals: if the instance was destroyed (scene unload) this reads false, so input can't get stuck.</summary>
    public static bool Active => active && inst != null;

    static bool active;
    static GroundTargeter inst;
    const int Segments = 56;

    Transform player;
    float radius, castRange;
    System.Action<Vector3> onConfirm;
    Camera cam;
    LineRenderer ring;
    Vector3 point;
    bool hasPoint;

    /// <summary>Enter aim mode for an AoE of <paramref name="radius"/>, placeable within
    /// <paramref name="castRange"/> of <paramref name="player"/>. On left-click confirm, calls
    /// <paramref name="onConfirm"/> with the chosen ground point.</summary>
    public static void Begin(Transform player, float radius, float castRange, System.Action<Vector3> onConfirm)
    {
        if (inst == null)
        {
            var go = new GameObject("GroundTargeter");
            inst = go.AddComponent<GroundTargeter>();
            inst.Build();
        }
        inst.player = player;
        inst.radius = Mathf.Max(0.1f, radius);
        inst.castRange = Mathf.Max(0f, castRange);
        inst.onConfirm = onConfirm;
        inst.hasPoint = false;
        active = true;
        inst.ring.enabled = true;
    }

    public static void Cancel()
    {
        active = false;
        if (inst == null) return;
        inst.onConfirm = null;
        if (inst.ring != null) inst.ring.enabled = false;
    }

    void Build()
    {
        ring = gameObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = Segments;
        ring.widthMultiplier = 0.09f;
        ring.numCornerVertices = 0;
        ring.numCapVertices = 0;
        ring.textureMode = LineTextureMode.Stretch;
        ring.alignment = LineAlignment.View;
        ring.material = new Material(Shader.Find("Sprites/Default"));   // always-included, URP-friendly
        ring.startColor = ring.endColor = new Color(1f, 0.45f, 0.15f, 0.95f);
        ring.sortingOrder = DepthSortRuntime.FxSlash;                   // draw over the ground
        ring.enabled = false;
    }

    void Update()
    {
        if (!Active) return;
        if (cam == null) cam = Camera.main;

        if (EscDown()) { Cancel(); return; }

        if (Player3DController.GroundUnderCursor(cam, out Vector3 p))
        {
            // Clamp the reticle to castRange (planar) from the player.
            Vector3 me = player != null ? player.position : Vector3.zero;
            Vector3 off = p - me; off.y = 0f;
            if (castRange > 0f && off.magnitude > castRange)
                p = me + off.normalized * castRange + Vector3.up * (p.y - me.y);
            point = p; hasPoint = true;
            DrawRing(p);
        }

        if (LeftDown() && !PointerOverUI() && hasPoint)
        {
            var cb = onConfirm; var pt = point;
            Cancel();          // leave aim mode BEFORE firing so the skill's cooldown sweep starts cleanly
            cb?.Invoke(pt);
        }
    }

    void DrawRing(Vector3 center)
    {
        float y = center.y + 0.06f;   // hover just above the ground
        for (int i = 0; i < Segments; i++)
        {
            float a = (i / (float)Segments) * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * radius, y, center.z + Mathf.Sin(a) * radius));
        }
    }

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    static bool LeftDown()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.leftButton.wasPressedThisFrame;
#endif
        return Input.GetMouseButtonDown(0);
    }

    static bool EscDown()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.Escape);
    }
}
