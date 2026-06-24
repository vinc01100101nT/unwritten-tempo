using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fixes the "billboard has no side" shadow bug: a flat sprite quad yaws to face the CAMERA, so when the
/// light is off to the camera's side the quad is edge-on to the light and its cast shadow collapses to a
/// sliver (the body shadow "disappears"). This component adds a hidden <see cref="ShadowCastingMode.ShadowsOnly"/>
/// twin of the sprite that instead turns BROADSIDE to the light (via <see cref="SunRig"/>), so the light always
/// sees the full silhouette and the ground shadow keeps the character's shape from any sun/camera angle. The
/// visible quad stops casting (its casting is switched to <see cref="ShadowCastingMode.Off"/>) so we never
/// double-cast.
///
/// <para>That cast proxy still vanishes when the light is straight DOWN (top-down): a vertical paper-thin quad
/// can't occlude a vertical light. So we ALSO draw an always-on ground "blob" — a flat, soft, fake-shadow decal
/// (rendered to the camera, not a real occluder) anchored at the sprite's feet, exactly where the proxy streak
/// meets the ground, so the two read as one continuous shadow with no gap. The blob carries the shadow at steep
/// angles; the proxy stretches it out at low ones.</para>
///
/// <para>Self-bootstrapped onto every mob (<see cref="BillboardAnimator3D"/>) and the baked player
/// (<see cref="BakedBillboard8"/>), so no re-bake is needed. The proxy is only rendered into shadow maps,
/// never to the camera; both proxy and blob are DISTANCE-CULLED (disabled past <see cref="MaxDistance"/> from the
/// camera) so a screen full of mobs doesn't pay for far-away shadows. Allocation-free per frame (two GameObjects
/// made in Awake, then only reference/transform writes).</para>
/// </summary>
[DisallowMultipleComponent]
public class BillboardShadow : MonoBehaviour
{
    /// <summary>Global on/off (DevSettings ▸ Shadows). When off, every proxy hides — the sprites stop casting entirely.</summary>
    public static bool Enabled = true;
    /// <summary>Past this distance from the camera a proxy is disabled (no billboard shadow). Shared by all proxies
    /// so one DevSettings knob tunes the whole world.</summary>
    public static float MaxDistance = 32f;

    /// <summary>Always-on ground "blob" decal under the feet (DevSettings ▸ Shadows). A FAKE shadow rendered to
    /// the camera — survives a straight-down/top-down light where the thin quad can't occlude, and anchors at the
    /// feet so it joins the proxy's cast streak with no gap.</summary>
    public static bool GroundBlob = true;
    /// <summary>Blob darkness (alpha, 0..1).</summary>
    public static float BlobStrength = 0.5f;
    /// <summary>Blob size multiplier relative to the sprite's width.</summary>
    public static float BlobSize = 1f;

    [Tooltip("The visible billboard sprite this shadow twins. Auto-found (self, then children) if left empty.")]
    public SpriteRenderer source;

    SpriteRenderer proxy;
    Transform proxyTf;
    SpriteRenderer blob;
    Transform blobTf;

    // Shared soft-circle sprite + unlit transparent material for every blob (built once).
    static Sprite _blobSprite;
    static Material _blobMat;

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

        // Always-on ground blob: a flat, soft, fake-shadow decal under the feet. Drawn to the camera (not a real
        // occluder) so it survives a straight-down light, and anchored at the feet so it merges with the proxy streak.
        var bgo = new GameObject(source.name + " (shadow blob)") { hideFlags = HideFlags.DontSave };
        blobTf = bgo.transform;
        blob = bgo.AddComponent<SpriteRenderer>();
        blob.sprite = BlobSprite();
        blob.sharedMaterial = BlobMaterial();
        blob.shadowCastingMode = ShadowCastingMode.Off;
        blob.receiveShadows = false;
        blob.sortingOrder = -50;                                      // behind the unit billboards (depth test still occludes it)
        blob.enabled = false;
    }

    void OnDisable() { if (proxy != null) proxy.enabled = false; if (blob != null) blob.enabled = false; }
    void OnDestroy()
    {
        if (proxyTf != null) Destroy(proxyTf.gameObject);
        if (blobTf != null) Destroy(blobTf.gameObject);
    }

    void LateUpdate()
    {
        if (source == null) return;

        // Shared cull: off globally, source hidden/frameless, or beyond the cull distance from the camera.
        bool culled = !Enabled || !source.enabled || source.sprite == null;
        if (!culled)
        {
            var cam = CameraRig.Cam;
            if (cam != null && (source.transform.position - cam.transform.position).sqrMagnitude > MaxDistance * MaxDistance)
                culled = true;
        }

        UpdateProxy(culled);
        UpdateBlob(culled);
    }

    // The light-facing cast shadow (needs a sun; vanishes when the light is straight down — that's what the blob covers).
    void UpdateProxy(bool culled)
    {
        if (proxy == null) return;
        if (culled || !SunRig.HasSun) { proxy.enabled = false; return; }

        var st = source.transform;
        proxy.enabled = true;
        proxy.sprite = source.sprite;
        proxy.flipX = source.flipX;
        proxyTf.position = st.position;            // rotation doesn't move the pivot, so this is order-independent
        proxyTf.rotation = SunRig.ShadowFacing;    // broadside to the light → full silhouette from any angle
        proxyTf.localScale = st.lossyScale;        // unparented → local == world scale (tracks unit size + breathing)
    }

    // The always-on ground blob: flat ellipse at the sprite's feet, sized to its width. No sun needed.
    void UpdateBlob(bool culled)
    {
        if (blob == null) return;
        if (culled || !GroundBlob) { blob.enabled = false; return; }

        var b = source.bounds;                                       // world AABB: min.y = feet, center = body axis
        blobTf.position = new Vector3(b.center.x, b.min.y + 0.03f, b.center.z);   // sit on the ground at the feet
        blobTf.rotation = Quaternion.Euler(90f, 0f, 0f);             // lay the quad flat on the ground
        float w = Mathf.Max(0.05f, b.size.x) * BlobSize;
        blobTf.localScale = new Vector3(w, w * 0.55f, 1f);           // ellipse — shorter front-to-back than wide
        blob.color = new Color(0f, 0f, 0f, BlobStrength);            // per-renderer tint (no material instance)
        blob.enabled = true;
    }

    // Soft radial-falloff circle, built once and shared by every blob (white; tinted black per-renderer via color).
    static Sprite BlobSprite()
    {
        if (_blobSprite != null) return _blobSprite;
        const int size = 64; const float half = size * 0.5f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ShadowBlob" };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * size + x] = new Color(1f, 1f, 1f, a * a);     // squared → soft edge, denser core
            }
        tex.SetPixels(px); tex.Apply();
        _blobSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);  // ppu=size → 1 world unit
        return _blobSprite;
    }

    static Material BlobMaterial()
    {
        if (_blobMat != null) return _blobMat;
        var sh = Shader.Find("Sprites/Default");                     // unlit, alpha-blended, Cull Off, ZTest LEqual
        if (sh != null) _blobMat = new Material(sh) { name = "ShadowBlob (transparent)" };
        return _blobMat;
    }
}
