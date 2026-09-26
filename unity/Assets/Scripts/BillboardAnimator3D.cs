using UnityEngine;

/// <summary>
/// 4-direction billboard animator for mobs in the 2.5D world. Slices a Ninja Adventure character
/// sheet at RUNTIME (via <see cref="Sprite.Create"/>, like CharacterAnimator2D's attack poses — so
/// the sheet doesn't need to be pre-sliced in the project) and plays the walk frames based on a
/// <see cref="PathAgent3D"/>'s motion. Columns are Down/Up/Left/Right; the top rows are walk frames.
///
/// <para>Art that comes as one picture per animation instead (the bosses: Idle.png, Walk.png…, each a single row of
/// front-facing frames) sets <see cref="idleStrip"/> and <see cref="walkStrip"/>, which replace the grid sheet: the
/// Walk row plays while moving, the Idle row while standing.</para>
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class BillboardAnimator3D : MonoBehaviour
{
    [Tooltip("The character SpriteSheet (e.g. NinjaAdventure/Actor/Monster/<Name>/SpriteSheet.png).")]
    public Texture2D sheet;
    [Tooltip("Cell size in px (Ninja Adventure = 16).")]
    public int cell = 16;
    [Tooltip("Walk frames per direction (top rows of each column).")]
    public int walkFrames = 4;
    public float framesPerSecond = 6f;

    [System.Serializable]
    public struct StripClip
    {
        [Tooltip("One animation as a single row of equal frames (e.g. Boss/<Name>/Idle.png). Empty = not used.")]
        public Texture2D sheet;
        [Tooltip("How many frames the row holds.")]
        public int frames;
        [Tooltip("Pixels from the bottom of a frame up to the creature's feet.")]
        public int footPx;
    }

    [Header("Strip art (bosses) — replaces the grid sheet when the idle strip is set")]
    [Tooltip("Played while standing still.")]
    public StripClip idleStrip;
    [Tooltip("Played while moving. Empty = the idle strip plays while moving too.")]
    public StripClip walkStrip;
    [Tooltip("Pixels per world unit for both strips, so the two animations draw the creature at the same size.")]
    public int stripPixelsPerUnit;

    [Header("Measured art (written by the mob baker from the frames this plays)")]
    [Tooltip("How far the tallest frame's crown sits above its feet line, in world units at scale one. The creature's " +
             "size is fitted against THIS, so every creature of one size stands the same height whatever padding its " +
             "art carries. Zero = never measured: the body keeps scale one.")]
    public float crownAtScaleOne;
    [Tooltip("How wide the widest frame's painted part is, in world units at scale one. Zero = never measured.")]
    public float paintedWidthAtScaleOne;

    [Header("Idle breathing (procedural — the sheets have no idle frames)")]
    [Tooltip("Gentle squash-stretch loop while standing still, so a stopped mob still feels alive.")]
    public bool breathe = true;
    [Tooltip("Peak breath amount (0.03 = ±3% wider/flatter).")]
    public float breatheAmount = 0.03f;
    [Tooltip("Breaths per ~6 seconds (higher = faster).")]
    public float breatheSpeed = 2.5f;

    SpriteRenderer sr;
    PathAgent3D agent;
    AttackLunge lunge;
    Sprite[] down, up, left, right;
    Sprite[] idleClip, walkClip;
    Sprite[] playing;
    Vector2 facing = Vector2.down;
    float timer; int frame;
    Vector3 baseScale;

    /// <summary>THE rest scale for this mob's body: the scale its size was fitted to. Everything that
    /// animates the scale (the idle breathe here, the attack squash in <see cref="AttackLunge"/>) squashes around
    /// THIS one value. <see cref="AttackLunge"/> used to recompute the same product from its own captured build
    /// scale, with a comment saying it "matches BillboardAnimator3D" — a hand-sync note, which is always the tell.</summary>
    public Vector3 BaseScale => baseScale;
    float breathePhase;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        agent = GetComponent<PathAgent3D>();
        lunge = GetComponent<AttackLunge>();
        baseScale = transform.localScale;
        breathePhase = Random.value * Mathf.PI * 2f;   // desync the herd
        BuildFrames();
        if (down != null && down.Length > 0) sr.sprite = down[0];   // show before moving

        // Light-facing cast shadow that survives the sun being off to the camera's side (see BillboardShadow).
        // Self-bootstrapped so existing baked mobs get it at Play without a re-bake.
        if (GetComponent<BillboardShadow>() == null) gameObject.AddComponent<BillboardShadow>();
    }

    /// <summary>Scale this body so its art stands <paramref name="height"/> tall, feet to crown, and state that height
    /// and the matching width as the unit's real body (<see cref="UnitBody"/>), so its nameplate, health bar, aim point
    /// and click area use the measured body rather than a guess from the scale. Returns the scale applied. Called by
    /// <see cref="MobBody.Fit"/> only — once when the prefab is baked and once as each creature is made.</summary>
    public float FitToHeight(float height)
    {
        float s = MobSizes.BodyScale(height, crownAtScaleOne);
        baseScale = Vector3.one * s;
        transform.localScale = baseScale;
        if (crownAtScaleOne > 0f) UnitBody.Set(gameObject, height, paintedWidthAtScaleOne * s);
        return s;
    }

    void BuildFrames()
    {
        if (idleStrip.sheet != null)
        {
            idleClip  = SpriteSheetCache.Row(idleStrip.sheet, idleStrip.frames, idleStrip.footPx, stripPixelsPerUnit);
            walkClip = walkStrip.sheet != null
                ? SpriteSheetCache.Row(walkStrip.sheet, walkStrip.frames, walkStrip.footPx, stripPixelsPerUnit)
                : idleClip;
            down = idleClip;
            return;
        }
        if (sheet == null) { GameLog.Warn(LogCat.Unwritten, "BillboardAnimator3D has no sheet on " + name); return; }
        // Shared slice cache: all mobs that use the same sheet+cell share ONE set of walk frames instead
        // of each instance allocating its own identical copies in Awake.
        var dirs = SpriteSheetCache.Walk4(sheet, cell, walkFrames);
        down = dirs[0]; up = dirs[1]; left = dirs[2]; right = dirs[3];
    }

    void Update()
    {
        bool moving = agent != null && agent.Moving;
        if (moving) facing = agent.Facing;

        var clip = idleClip != null ? (moving ? walkClip : idleClip) : ClipFor(facing);
        if (clip == null || clip.Length == 0) return;
        if (idleClip != null && clip != playing) { playing = clip; frame = 0; timer = 0f; }
        if (moving || idleClip != null)
        {
            timer += Time.deltaTime;
            float ft = 1f / Mathf.Max(framesPerSecond, 0.01f);
            while (timer >= ft) { timer -= ft; frame = (frame + 1) % clip.Length; }
        }
        else { frame = 0; timer = 0f; }
        sr.sprite = clip[Mathf.Min(frame, clip.Length - 1)];

        ApplyIdleScale(moving);
    }

    // Procedural idle: a soft squash-stretch loop while standing still. Yields to AttackLunge (the
    // chomp owns the scale during a bite); resets to base while moving.
    void ApplyIdleScale(bool moving)
    {
        if (lunge != null && lunge.Punching) return;
        if (breathe && !moving && idleClip == null)
        {
            float e = Mathf.Sin(Time.time * breatheSpeed + breathePhase);
            transform.localScale = new Vector3(
                baseScale.x * (1f + breatheAmount * e),
                baseScale.y * (1f - breatheAmount * e),
                baseScale.z);
        }
        else transform.localScale = baseScale;
    }

    Sprite[] ClipFor(Vector2 f)
    {
        if (f.y > 0f) return up;
        if (f.y < 0f) return down;
        if (f.x < 0f) return left;
        return right;
    }
}
