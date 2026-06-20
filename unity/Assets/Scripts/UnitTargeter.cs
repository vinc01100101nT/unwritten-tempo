using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// A reusable SINGLE-TARGET reticle for "aim-first" skills — the unit analog of <see cref="GroundTargeter"/>
/// (which places a ground AoE point). <see cref="Begin"/> puts the player into AIM mode: the enemy under the
/// cursor (via the shared, magnetised <see cref="Player3DController.MonsterUnderCursor"/> pick) is ringed at
/// its feet — GREEN when it's a valid target within <c>castRange</c>, RED when out of range. <b>Left-click</b>
/// confirms (fires the skill's callback with the chosen <see cref="Health"/>); <b>Esc</b> (or re-pressing the
/// skill) cancels.
///
/// While aiming, <see cref="Active"/> is true and <see cref="Player3DController"/> skips its click/move input
/// that frame (same pattern as <see cref="GroundTargeter"/> / <see cref="DevSettings.Blocking"/>), so the
/// confirm click doesn't also order a move. Self-boots a single hidden instance on first use; the ring follows
/// the (moving) target each frame with no per-frame allocation. Any future single-target skill (mage OR melee)
/// can reuse this — it only deals in a <c>castRange</c> and an <c>onConfirm(Health)</c> callback.
/// </summary>
public class UnitTargeter : MonoBehaviour
{
    /// <summary>True while the player is aiming a single-target skill — gameplay click input is suppressed.
    /// Self-heals: if the instance was destroyed (scene unload) this reads false, so input can't get stuck.</summary>
    public static bool Active => active && inst != null;

    static bool active;
    static UnitTargeter inst;
    const int Segments = 40;

    Transform player;
    float castRange;
    System.Action<Health> onConfirm;
    Camera cam;
    LineRenderer ring;
    Health hover;        // the current valid hovered target (within range), else null
    float ringRadius = 0.5f;

    static readonly Color ValidColor   = new Color(0.40f, 1f, 0.55f, 0.95f);   // green: a legal target
    static readonly Color InvalidColor = new Color(1f, 0.30f, 0.25f, 0.90f);   // red: out of range / illegal

    /// <summary>Enter aim mode: pick a single enemy within <paramref name="castRange"/> of
    /// <paramref name="player"/>. On left-click confirm, calls <paramref name="onConfirm"/> with the chosen
    /// living target. Re-call to retarget a fresh cast.</summary>
    public static void Begin(Transform player, float castRange, System.Action<Health> onConfirm)
    {
        if (inst == null)
        {
            var go = new GameObject("UnitTargeter");
            inst = go.AddComponent<UnitTargeter>();
            inst.Build();
        }
        inst.player = player;
        inst.castRange = Mathf.Max(0f, castRange);
        inst.onConfirm = onConfirm;
        inst.hover = null;
        active = true;
        inst.ring.enabled = false;   // the ring appears only once an enemy is hovered
    }

    public static void Cancel()
    {
        active = false;
        if (inst == null) return;
        inst.onConfirm = null;
        inst.hover = null;
        if (inst.ring != null) inst.ring.enabled = false;
    }

    void Build()
    {
        ring = gameObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = Segments;
        ring.widthMultiplier = 0.07f;
        ring.numCornerVertices = 0;
        ring.numCapVertices = 0;
        ring.textureMode = LineTextureMode.Stretch;
        ring.alignment = LineAlignment.View;
        ring.material = new Material(Shader.Find("Sprites/Default"));   // always-included, URP-friendly
        ring.sortingOrder = DepthSortRuntime.FxSlash;                   // draw over the ground
        ring.enabled = false;
    }

    void Update()
    {
        if (!Active) return;
        if (cam == null) cam = Camera.main;

        if (EscDown() || RightDown()) { Cancel(); return; }   // Esc or right-click bails out of aim mode

        // Hover pick: the magnetised cursor→mob resolve, and if the cursor is over bare GROUND, fall back to
        // the nearest mob to that ground point — so you can aim loosely and it auto-snaps to the closest enemy.
        var h = PickTarget();
        Vector3 me = player != null ? player.position : Vector3.zero;
        bool inRange = h != null && (castRange <= 0f || PlanarDist(me, h.transform.position) <= castRange);
        hover = inRange ? h : null;

        if (h != null)
        {
            ring.enabled = true;
            ring.startColor = ring.endColor = inRange ? ValidColor : InvalidColor;
            DrawRing(h);
        }
        else if (ring.enabled) ring.enabled = false;

        if (LeftDown() && !PointerOverUI() && hover != null && !hover.IsDead)
        {
            var cb = onConfirm; var target = hover;
            Cancel();              // leave aim mode BEFORE firing so the skill's cooldown sweep starts cleanly
            cb?.Invoke(target);
        }
    }

    // Ring at the target's FEET (centre − ~0.4×height, matching MobNameplate's feet math), so it reads as a
    // ground marker under the billboard rather than a halo through its chest.
    void DrawRing(Health target)
    {
        Vector3 p = target.transform.position;
        float footY = p.y - 0.4f * Mathf.Max(0.0001f, target.transform.lossyScale.y) + 0.06f;
        for (int i = 0; i < Segments; i++)
        {
            float a = (i / (float)Segments) * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(p.x + Mathf.Cos(a) * ringRadius, footY, p.z + Mathf.Sin(a) * ringRadius));
        }
    }

    // The enemy to highlight: 1) the mob directly under / magnetised to the cursor; else 2) when the cursor is
    // over bare ground, the LIVING mob nearest that ground point (registry scan, no per-frame GetComponent —
    // .Health is cached on each mob). Lets the player aim roughly at a cluster and auto-lock the closest one.
    Health PickTarget()
    {
        var h = Player3DController.MonsterUnderCursor(cam);
        if (h != null) return h;
        if (!Player3DController.GroundUnderCursor(cam, out Vector3 gp)) return null;

        Health best = null; float bestSqr = float.MaxValue;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null) continue;
            var hh = ai.Health;
            if (hh == null || hh.IsDead) continue;
            Vector3 d = hh.transform.position - gp; d.y = 0f;
            float sqr = d.sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = hh; }
        }
        return best;
    }

    static float PlanarDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    static bool LeftDown()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.leftButton.wasPressedThisFrame;
#endif
        return Input.GetMouseButtonDown(0);
    }

    static bool RightDown()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.rightButton.wasPressedThisFrame;
#endif
        return Input.GetMouseButtonDown(1);
    }

    static bool EscDown()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.Escape);
    }
}
