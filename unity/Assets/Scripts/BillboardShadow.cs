using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fixes the "billboard has no side" shadow bug: a flat sprite quad yaws to face the CAMERA, so when the light
/// is off to the side (or straight overhead) the quad is edge-on to the light and its cast shadow collapses to a
/// sliver — the body shadow "disappears". This adds a hidden <see cref="ShadowCastingMode.ShadowsOnly"/> twin of
/// the sprite that instead turns to FULLY FACE the light in 3D (via <see cref="SunRig.ShadowFacing"/>): the quad
/// stands up for a low sun (long cast streak) and tilts flat for a steep/overhead sun (the silhouette projects
/// straight down into a contact shadow under the feet). One caster that morphs with the sun, so the shadow is
/// always full and never breaks into pieces. The visible quad stops casting (<see cref="ShadowCastingMode.Off"/>)
/// so we never double-cast.
///
/// <para>Self-bootstrapped onto every mob (<see cref="BillboardAnimator3D"/>) and the baked player
/// (<see cref="BakedBillboard8"/>), so no re-bake is needed. The proxy is only rendered into shadow maps, never
/// to the camera, and is DISTANCE-CULLED (disabled past <see cref="MaxDistance"/> from the camera) so a screen
/// full of mobs doesn't pay for far-away shadow draws. Allocation-free per frame (one GameObject in Awake, then
/// only reference/transform writes).</para>
/// </summary>
[DisallowMultipleComponent]
public class BillboardShadow : MonoBehaviour
{
    /// <summary>Global on/off (DevSettings ▸ Shadows). When off, every proxy hides — the sprites stop casting entirely.</summary>
    public static bool Enabled = true;
    /// <summary>Past this distance from the camera a proxy is disabled (no billboard shadow). Shared by all proxies
    /// so one DevSettings knob tunes the whole world.</summary>
    public static float MaxDistance = 32f;
    /// <summary>Slides the cast shadow along its own length (metres) to line its root up with the feet. + pushes the
    /// shadow away from the unit (along the light→sprite ground direction), − pulls it back under the feet.</summary>
    public static float ContactOffset = 0f;
    /// <summary>Slides the cast shadow left/right (metres), perpendicular to its length on the ground, to centre it
    /// under the feet when the sprite pivot isn't horizontally centred.</summary>
    public static float SideOffset = 0f;

    [Tooltip("The visible billboard sprite this shadow twins. Auto-found (self, then children) if left empty.")]
    public SpriteRenderer source;

    SpriteRenderer proxy;
    Transform proxyTf;

    void Awake()
    {
        if (source == null) source = GetComponent<SpriteRenderer>();
        if (source == null) source = GetComponentInChildren<SpriteRenderer>();
        if (source == null) { enabled = false; return; }

        // The proxy carries the cast shadow now → stop the camera-facing quad casting its (buggy, thin) one.
        source.shadowCastingMode = ShadowCastingMode.Off;

        var go = new GameObject(source.name + " (shadow)") { hideFlags = HideFlags.DontSave };
        proxyTf = go.transform;
        proxy = go.AddComponent<SpriteRenderer>();
        proxy.sharedMaterial = source.sharedMaterial;                 // BillboardLit → real silhouette shadow-caster pass
        proxy.shadowCastingMode = ShadowCastingMode.ShadowsOnly;      // only feeds shadow maps; never drawn to the camera
        proxy.receiveShadows = false;
        proxy.enabled = false;                                        // shown by the first LateUpdate when in range
    }

    void OnDisable() { if (proxy != null) proxy.enabled = false; }    // hide while the unit is pooled/disabled
    void OnDestroy() { if (proxyTf != null) Destroy(proxyTf.gameObject); }

    void LateUpdate()
    {
        if (proxy == null || source == null) return;

        // Which light casts this frame? At night the sun is dark, so a nearby NightLight (the one closest to the
        // player — the only lamp allowed to cast) takes over; in daytime we fall back to the sun.
        bool hasLamp = NightLight.TryGetShadowLamp(out Vector3 lampPos);

        // Off globally, source hidden/frameless, or no light at all → nothing to cast.
        if (!Enabled || !source.enabled || source.sprite == null || (!hasLamp && !SunRig.HasSun))
        { proxy.enabled = false; return; }

        // Distance cull — far mobs cast no billboard shadow (saves the extra shadow-pass draw).
        var cam = CameraRig.Cam;
        if (cam != null && (source.transform.position - cam.transform.position).sqrMagnitude > MaxDistance * MaxDistance)
        { proxy.enabled = false; return; }

        // Twin the current frame, turned to FULLY FACE the light (3D) so the silhouette stays full at any angle.
        var st = source.transform;
        proxy.enabled = true;
        proxy.sprite = source.sprite;
        proxy.flipX = source.flipX;
        // Ground direction the shadow travels (away from the light); used to nudge the cast root onto the feet.
        Vector3 groundDir = hasLamp ? new Vector3(st.position.x - lampPos.x, 0f, st.position.z - lampPos.z)
                                    : SunRig.DirXZ;
        if (groundDir.sqrMagnitude > 1e-5f) groundDir.Normalize(); else groundDir = Vector3.zero;
        Vector3 sideDir = Vector3.Cross(Vector3.up, groundDir);      // horizontal, perpendicular to the shadow
        proxyTf.position = st.position + groundDir * ContactOffset + sideDir * SideOffset;   // rotation pivots here, so order-independent
        proxyTf.rotation = hasLamp ? LampFacing(st.position, lampPos) : SunRig.ShadowFacing;
        proxyTf.localScale = st.lossyScale;        // unparented → local == world scale (tracks unit size + breathing)
    }

    // Turn the proxy broadside to a POINT lamp: quad normal along the lamp→sprite axis, so the lamp sees the
    // full silhouette (same convention as SunRig.ShadowFacing, but per-sprite since a point light's direction
    // differs for each unit). Cheap; only the few proxies near the casting lamp ever reach here.
    static Quaternion LampFacing(Vector3 spritePos, Vector3 lampPos)
    {
        Vector3 dir = spritePos - lampPos;
        if (dir.sqrMagnitude < 1e-5f) return Quaternion.identity;
        dir.Normalize();
        Vector3 refUp = Mathf.Abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up;
        return Quaternion.LookRotation(dir, refUp);
    }
}
