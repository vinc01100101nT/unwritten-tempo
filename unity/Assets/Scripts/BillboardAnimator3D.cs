using UnityEngine;

/// <summary>
/// 4-direction billboard animator for mobs in the 2.5D world. Slices a Ninja Adventure character
/// sheet at RUNTIME (via <see cref="SpriteSheetCache"/>, so the sheet doesn't need to be pre-sliced in the
/// project) and plays the walk frames based on a <see cref="PathAgent3D"/>'s motion. Columns are
/// Down/Up/Left/Right; the top rows are walk frames, and a character's sheet has pose rows under them.
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
    // The way it last went on the ground (x, z); until it has gone anywhere, it faces the camera.
    Vector2 groundHeading;
    bool headingKnown;
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
        BuildPoses();
        if (down != null && down.Length > 0) sr.sprite = down[0];   // show before moving

        // Light-facing cast shadow that survives the sun being off to the camera's side (see BillboardShadow).
        // Self-bootstrapped so existing baked mobs get it at Play without a re-bake.
        if (GetComponent<BillboardShadow>() == null) gameObject.AddComponent<BillboardShadow>();
    }

    /// <summary>Take the body's fitted <paramref name="scale"/> as this picture's rest scale — what breathing and a bite
    /// squash around — and draw it <paramref name="worldLift"/> above its feet. Called by <see cref="MobBody.Fit"/> only,
    /// which has already put that scale on the body.</summary>
    public void TakeFit(Vector3 scale, float worldLift)
    {
        baseScale = scale;
        FlyAt(worldLift);
    }

    // How wide this body is drawn against its height — 1 for every creature, narrower for a saved character (see
    // UseSheet). Not saved: it belongs to the life, and a body only becomes a character while the game runs.
    [System.NonSerialized] float widthScale = 1f;

    /// <summary>How wide this body is drawn against its height (see <see cref="MobBody.FlatArt"/>).</summary>
    public float DrawnWidth => widthScale;

    // Where the feet stand in a grid cell, as a fraction of it up from the bottom — the mob default, or a saved
    // character's own measured feet line (see UseSheet). Not saved, for the same reason as the width.
    [System.NonSerialized] float footPivot = SpriteSheetCache.MobFootPivotY;

    // How far above its feet a FLYING creature's picture is drawn, in world units (see MobBody.Fit) — zero on the
    // ground. Not saved: it is put on as each creature is made. Raised by moving the picture's pivot, so the body
    // that walks, collides and routes never leaves the ground.
    [System.NonSerialized] float lift;
    // The scale the current pictures were cut at — a pivot is in the art's units, so a new scale needs a new cut.
    [System.NonSerialized] float liftCutAtScale;

    /// <summary>Draw this body <paramref name="worldLift"/> world units above its feet from now on — a flying
    /// creature's (zero puts it back on the ground). Reached through <see cref="TakeFit"/>: the lift is turned into
    /// the art's own units at the scale that fit set. Rebuilds the pictures only when the lift, or the scale a
    /// non-zero lift was cut at, changed.</summary>
    void FlyAt(float worldLift)
    {
        worldLift = Mathf.Max(0f, worldLift);
        if (Mathf.Approximately(worldLift, lift) && (lift <= 0f || Mathf.Approximately(baseScale.y, liftCutAtScale))) return;
        lift = worldLift;
        liftCutAtScale = baseScale.y;
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        idleClip = walkClip = playing = null;
        frame = 0; timer = 0f;
        BuildFrames();
        BuildPoses();
        if (down != null && down.Length > 0) sr.sprite = down[0];
    }

    // The lift in the art's own units at scale one, which is what a pivot is measured in.
    float LiftAtScaleOne => MobSizes.LiftAtScaleOne(lift, baseScale.y);

    // ---- the poses under a character sheet's walk --------------------------------------------------------
    // A character's sheet carries three more rows under its four walk rows: an attack, one picture per direction;
    // a jump; and a row of single pictures — dead, casting, releasing a cast, releasing with no cast. A sheet without
    // them (every mob's) simply has none of these, and every call below does nothing on it.
    const int AttackRow = 4;
    const int PoseRow = 6;
    const float AttackPoseSeconds = 0.3f;
    const float ReleasePoseSeconds = 0.4f;
    // A release this soon after a wind-up ended is that wind-up's release; later than this, it had none.
    const float ReleaseFollowsCastSeconds = 0.5f;

    Sprite[] attackPoses;
    Sprite deadPose, castPose, releasePose, instantReleasePose;
    Sprite posing;
    // Whether the picture held is a swing's, which follows the camera like the walk; a release does not turn.
    bool posingAttack;
    float poseUntil;
    bool holdingCast;
    float castEndedAt = -1f;

    void BuildPoses()
    {
        attackPoses = null;
        deadPose = castPose = releasePose = instantReleasePose = posing = null;
        posingAttack = false;
        holdingCast = false;
        if (sheet == null || cell <= 0 || idleStrip.sheet != null) return;
        var attack = SpriteSheetCache.FootRow(sheet, cell, AttackRow, DrawnPivot);
        var poses = SpriteSheetCache.FootRow(sheet, cell, PoseRow, DrawnPivot);
        if (attack.Length < 4 || poses.Length < 4) return;
        attackPoses = attack;
        deadPose = poses[0];
        castPose = poses[1];
        releasePose = poses[2];
        instantReleasePose = poses[3];
    }

    /// <summary>A swing: the attack picture for the way it faces, briefly. A sheet without one — every ordinary
    /// creature's — has no picture to show, so it bites instead (<see cref="AttackLunge"/>), toward
    /// <paramref name="aimedAt"/> when this machine draws them, slamming in <paramref name="connectsIn"/> seconds from
    /// now — the moment its blow lands (0 = no blow lands with it, so it bites at once).</summary>
    public void ShowAttack(Transform aimedAt, float connectsIn)
    {
        if (attackPoses == null)
        {
            if (lunge != null) lunge.Bite(aimedAt, connectsIn);
            return;
        }
        posing = attackPoses[DirectionColumn(facing)];
        posingAttack = true;
        poseUntil = Time.time + AttackPoseSeconds;   // server-math: how long a picture stays on screen, not a game number
    }

    /// <summary>Hold, or stop holding, the casting picture — a skill's wind-up.</summary>
    public void HoldCast(bool on)
    {
        if (castPose == null || on == holdingCast) return;
        holdingCast = on;
        if (!on) castEndedAt = Time.time;
    }

    /// <summary>A skill let go: the releasing picture after a wind-up, or the one for a skill with no wind-up.</summary>
    public void ShowRelease()
    {
        if (releasePose == null) return;
        bool afterCast = holdingCast || (castEndedAt >= 0f && Time.time - castEndedAt <= ReleaseFollowsCastSeconds);
        holdingCast = false;
        castEndedAt = -1f;
        posing = afterCast ? releasePose : instantReleasePose;
        posingAttack = false;
        poseUntil = Time.time + ReleasePoseSeconds;   // server-math: how long a picture stays on screen, not a game number
    }

    /// <summary>Show the dead picture — called as the body starts its death fade, which then stops this animator.</summary>
    public void ShowDeathPose()
    {
        if (deadPose == null) return;
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        posing = null;
        posingAttack = false;
        holdingCast = false;
        sr.sprite = deadPose;
    }

    // Columns are down, up, left, right — the same order the walk is read in.
    static int DirectionColumn(Vector2 f)
    {
        if (f.y > 0f) return 1;
        if (f.y < 0f) return 0;
        if (f.x < 0f) return 2;
        return 3;
    }

    // The pivot a grid cell's pictures are cut with: the feet line, moved down by the lift.
    float DrawnPivot => SpriteSheetCache.LiftedPivot(footPivot, LiftAtScaleOne);

    void BuildFrames()
    {
        if (idleStrip.sheet != null)
        {
            float liftUnits = LiftAtScaleOne;
            idleClip  = SpriteSheetCache.Row(idleStrip.sheet, idleStrip.frames,
                SpriteSheetCache.LiftedFootPx(idleStrip.footPx, liftUnits, stripPixelsPerUnit), stripPixelsPerUnit);
            walkClip = walkStrip.sheet != null
                ? SpriteSheetCache.Row(walkStrip.sheet, walkStrip.frames,
                    SpriteSheetCache.LiftedFootPx(walkStrip.footPx, liftUnits, stripPixelsPerUnit), stripPixelsPerUnit)
                : idleClip;
            down = idleClip;
            return;
        }
        if (sheet == null) { GameLog.Warn(LogCat.Unwritten, "BillboardAnimator3D has no sheet on " + name); return; }
        // Shared slice cache: all mobs that use the same sheet+cell share ONE set of walk frames instead
        // of each instance allocating its own identical copies in Awake.
        var dirs = SpriteSheetCache.Walk4(sheet, cell, walkFrames, DrawnPivot);
        down = dirs[0]; up = dirs[1]; left = dirs[2]; right = dirs[3];
    }

    /// <summary>Draw this body from a different grid <paramref name="grid"/> from now on — a creature that has turned back
    /// into the character it was drawn as. Any strip art is dropped, and the art's measured height and width come with the
    /// picture, because the body's size is fitted against them (<see cref="MobBody.Fit"/>, which the caller runs next).</summary>
    public void UseSheet(Texture2D grid, float crown, float paintedWidth, float drawnWidth = 1f,
                         float feetPivot = SpriteSheetCache.MobFootPivotY)
    {
        if (grid == null) return;
        sheet = grid;
        footPivot = Mathf.Clamp01(feetPivot);
        widthScale = Mathf.Max(0.05f, drawnWidth);
        idleStrip = default;
        walkStrip = default;
        idleClip = walkClip = playing = null;
        crownAtScaleOne = crown;
        paintedWidthAtScaleOne = paintedWidth;
        frame = 0; timer = 0f;
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        BuildFrames();
        BuildPoses();
        if (down != null && down.Length > 0) sr.sprite = down[0];
    }

    // WATCHING MACHINES ONLY: whether the server says this creature is travelling, and which way on the ground. Its
    // mover is switched off on a machine that only watches it, and a switched-off mover answers "standing, facing
    // down" forever — so without these every watched creature showed one picture and never stepped.
    bool travelIsTold;
    bool toldMoving;
    Vector2 toldHeading;

    /// <summary>The server's word on whether this creature is travelling and which way, in world terms (x, z). Safe
    /// to call every frame. Once called, this body stops asking its own mover.</summary>
    public void TellTravel(bool moving, Vector2 worldHeading)
    {
        travelIsTold = true;
        toldMoving = moving;
        toldHeading = worldHeading;
    }

    void Update()
    {
        bool moving = travelIsTold ? toldMoving : agent != null && agent.Moving;
        if (moving)
        {
            groundHeading = travelIsTold ? toldHeading : agent.Heading;
            headingKnown = true;
        }
        // THE SIDE SHOWN IS ASKED OF THE CAMERA EVERY FRAME, from the way it last went on the ground — so turning the
        // camera turns a creature standing still or swinging too, not only one that is walking.
        if (headingKnown) facing = Facing3D.FromWorldDir(new Vector3(groundHeading.x, 0f, groundHeading.y));
        if (posingAttack && posing != null) posing = attackPoses[DirectionColumn(facing)];

        // A POSE OVERRIDES THE WALK: the casting picture for as long as the wind-up lasts, a swing or a release for a
        // moment. Only a character's sheet has any.
        if (holdingCast && castPose != null) { sr.sprite = castPose; ApplyIdleScale(true); return; }
        if (posing != null)
        {
            if (Time.time < poseUntil) { sr.sprite = posing; ApplyIdleScale(true); return; }
            posing = null;
        }

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
    // bite owns the scale while it plays); resets to base while moving.
    void ApplyIdleScale(bool moving)
    {
        if (lunge != null && lunge.Biting) return;
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
