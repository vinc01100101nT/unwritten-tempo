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
    SkillTargets mask;                      // which unit KINDS this cast may legally land on (from the skill's bindings)
    System.Action<Transform> onConfirm;     // fires with the chosen unit's transform (a mob, or the player)
    Camera cam;
    LineRenderer ring;
    Transform hoverUnit;                     // unit currently under the cursor (mob or player), else null
    bool hoverIsPlayer;                      // is hoverUnit the player? (else a mob)
    bool hoverAllowed;                       // is hoverUnit's KIND in the mask?
    bool hoverInRange;                       // within castRange (player/self is always in range)
    float ringRadius = 0.5f;

    static readonly Color ValidColor   = new Color(0.40f, 1f, 0.55f, 0.95f);   // green: a legal target
    static readonly Color InvalidColor = new Color(1f, 0.30f, 0.25f, 0.90f);   // red: out of range / illegal

    /// <summary>Enter aim mode for a single-target skill. <paramref name="mask"/> is the set of unit KINDS the
    /// skill may legally hit (from its Single-scope bindings): the reticle rings a hovered unit GREEN only when its
    /// kind is in the mask AND it's in range, else RED. On a valid left-click it calls <paramref name="onConfirm"/>
    /// with the chosen unit's transform (a mob, or the player for a self-cast); clicking an illegal target instead
    /// pops an in-game warning. Re-call to retarget a fresh cast.</summary>
    public static void Begin(Transform player, float castRange, SkillTargets mask, System.Action<Transform> onConfirm)
    {
        // Quick cast (Settings ▸ Gameplay): skip the reticle entirely and fire at the target the player is
        // already engaged with. Every caster inherits this because it sits in the aim step itself.
        if (GameSettings.QuickCastSingleTarget && QuickTarget(player, castRange, mask, out var quick))
        { active = false; onConfirm?.Invoke(quick); return; }

        if (inst == null)
        {
            var go = new GameObject("UnitTargeter") { hideFlags = HideFlags.DontSaveInEditor };
            inst = go.AddComponent<UnitTargeter>();
            inst.Build();
        }
        inst.player = player;
        inst.castRange = Mathf.Max(0f, castRange);
        inst.mask = mask;
        inst.onConfirm = onConfirm;
        inst.hoverUnit = null;
        active = true;
        inst.ring.enabled = false;   // the ring appears only once a unit is hovered
    }

    // The current target, ONLY when this cast could legally have landed on it through the reticle: the skill must
    // accept mobs and the target must be alive and inside castRange. Anything else returns false, so the reticle
    // opens exactly as it would with quick cast off (no target yet, a self-only skill, a target out of reach).
    static bool QuickTarget(Transform player, float castRange, SkillTargets mask, out Transform target)
    {
        target = null;
        if (player == null || (mask & SkillTargets.Mobs) == 0) return false;
        var pc = player.GetComponentInParent<Player3DController>();
        var mob = pc != null ? pc.AttackTarget : null;
        if (mob == null) return false;
        if (castRange > 0f && PlanarDist(player.position, mob.transform.position) > castRange) return false;
        target = mob.transform;
        return true;
    }

    public static void Cancel()
    {
        active = false;
        if (inst == null) return;
        inst.onConfirm = null;
        inst.hoverUnit = null;
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

        if (EscDown()) { Cancel(); Hotkeys.MarkCancelHandled(); return; }   // Esc bails out of aim mode
        if (RightDown()) { Cancel(); return; }                             // right-click does too

        // Hover pick: a unit (mob or the player) under/near the cursor. Classify its KIND against the mask so the
        // ring reads GREEN only for a legal, in-range target — RED for an illegal kind or out of range.
        hoverUnit = PickUnit(out hoverIsPlayer);
        hoverAllowed = hoverUnit != null &&
                       (hoverIsPlayer ? (mask & SkillTargets.Player) != 0 : (mask & SkillTargets.Mobs) != 0);
        Vector3 me = player != null ? player.position : Vector3.zero;
        hoverInRange = hoverUnit != null && (hoverIsPlayer || castRange <= 0f ||
                       PlanarDist(me, hoverUnit.position) <= castRange);

        if (hoverUnit != null)
        {
            ring.enabled = true;
            ring.startColor = ring.endColor = (hoverAllowed && hoverInRange) ? ValidColor : InvalidColor;
            DrawRing(hoverUnit);
        }
        else if (ring.enabled) ring.enabled = false;

        if (LeftDown() && !PointerOverUI() && hoverUnit != null)
        {
            // Illegal kind → the exact warning the design asks for; out of range → a range warning. No spend either way.
            if (!hoverAllowed)
            {
                BattleNotice.Warn(hoverIsPlayer ? "Can't target yourself" : "Can't target mobs");
                return;
            }
            if (!hoverInRange) { BattleNotice.Warn("Out of range"); return; }

            var cb = onConfirm; var target = hoverUnit;
            Cancel();              // leave aim mode BEFORE firing so the skill's cooldown sweep starts cleanly
            cb?.Invoke(target);
        }
    }

    // Ring at the unit's FEET, so it reads as a ground marker under the billboard rather than a halo through
    // its chest. Both mobs and the player are feet-pivoted now (UnitAnchor invariant), so the feet ARE the
    // transform; the player still prefers its real sprite bounds when available (exact rendered bottom).
    void DrawRing(Transform unit)
    {
        Vector3 p = unit.position;   // pivot = feet (UnitAnchor invariant): just lay the ring at the pivot level
        for (int i = 0; i < Segments; i++)
        {
            float a = (i / (float)Segments) * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(p.x + Mathf.Cos(a) * ringRadius, p.y, p.z + Mathf.Sin(a) * ringRadius));
        }
    }

    // The unit under the cursor — a mob or the player — else null (no ring). The MOB pick goes through the
    // canonical, snap-magnetised Player3DController.MonsterUnderCursor (the SAME pick the sword cursor / hover
    // panel / click-to-attack use), NOT a bespoke first-hit raycast. That matters with a crowd: a plain ray
    // returns the FIRST collider it meets, so a mob standing behind the player (or behind another mob) on screen
    // gets shadowed by that nearer capsule and can't be targeted — the exact "can't target with 2+ mobs" bug.
    // Magnetism also means clustered small billboards don't demand a pixel-perfect hit on their feet-capsule.
    // The player (self-cast) is still found by a direct ray, since MonsterUnderCursor only reports mobs. Mask
    // priority decides which kind wins when both are under the cursor; kind is reported via isPlayer.
    Transform PickUnit(out bool isPlayer)
    {
        isPlayer = false;
        if (cam == null || PointerOverUI()) return null;

        bool wantMobs   = (mask & SkillTargets.Mobs) != 0;
        bool wantPlayer = (mask & SkillTargets.Player) != 0;

        var mob = Player3DController.MonsterUnderCursor(cam);   // magnetised, dead-filtered, frame-memoized

        Transform self = null;
        if (player != null)
        {
            var ray = cam.ScreenPointToRay(MousePos());
            if (Physics.Raycast(ray, out var hit, 500f) &&
                hit.collider.GetComponentInParent<Player3DController>() != null)
                self = player;
        }

        // Prefer the kind THIS skill can actually land on; fall back to the other only so an illegal hover still
        // shows the red ring + the right warning ("Can't target mobs" / "Can't target yourself").
        if (wantMobs && mob != null) return mob.transform;
        if (wantPlayer && self != null) { isPlayer = true; return self; }
        if (mob != null) return mob.transform;
        if (self != null) { isPlayer = true; return self; }
        return null;
    }

    static float PlanarDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    static bool PointerOverUI() => UiBlock.PointerOverUI();

    // The one pointer gate (mouse on the desktop, a finger on a phone). Aiming confirms on RELEASE there, so a
    // drag can slide the pick before it commits; the right-button bail-out simply never fires on a phone, where
    // the hardware Back gesture reaches the same Cancel through Escape.
    static Vector2 MousePos() => PointerInput.Position;

    static bool LeftDown() => PointerInput.PrimaryDown;

    static bool RightDown() => PointerInput.SecondaryDown;

    static bool EscDown() => Hotkeys.CancelDown();
}
