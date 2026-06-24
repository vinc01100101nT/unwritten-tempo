using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a sprite from <b>true 8-direction</b> baked strips — the runtime half of the model→sprite pipeline
/// (bake with <c>Tools ▸ unwritten ▸ Art ▸ Bake 8-Dir Sprites</c>, or build a whole character from a
/// <see cref="CharacterDef"/>). Each <see cref="CharacterAction"/> owns one set of 8 strips (one unique strip
/// per <see cref="Facing8"/> octant — no mirroring, so side profiles are correct).
///
/// <para>Actions are keyed by the <see cref="CharacterAction"/> enum (the pre-declared contract), so gameplay
/// can ask for them by name with no typo risk. Self-driving by default: it reads its own movement to pick
/// Idle / Walk / Run and a facing; an AI/controller can override with <see cref="Play"/> +
/// <see cref="SetFacing"/> / <see cref="SetMoving"/>.</para>
///
/// The visual lives on a CHILD sprite (carrying its own <see cref="Billboard"/>) lifted by
/// <see cref="feetOffset"/> to ground the feet. Strips are sliced ONCE in Awake via
/// <see cref="SpriteSheetCache.Row"/> (shared/read-only); the per-frame path allocates nothing.
/// </summary>
[DisallowMultipleComponent]
public class BakedBillboard8 : MonoBehaviour
{
    /// <summary>Eight directional strips in <see cref="Facing8"/> octant order:
    /// 0=N 1=NE 2=E 3=SE 4=S 5=SW 6=W 7=NW. A null/short entry falls back to S (index 4).</summary>
    [System.Serializable]
    public struct Dir8
    {
        [Tooltip("One horizontal strip PNG per octant, in Facing8 order: N, NE, E, SE, S, SW, W, NW.")]
        public Texture2D[] strips;   // length 8

        public bool Any
        {
            get
            {
                if (strips == null) return false;
                for (int i = 0; i < strips.Length; i++) if (strips[i] != null) return true;
                return false;
            }
        }
    }

    /// <summary>One bakeable action: which <see cref="CharacterAction"/>, its 8 strips, and its playback fps.</summary>
    [System.Serializable]
    public class ActionSet
    {
        public CharacterAction action = CharacterAction.Idle;
        public Dir8 dirs;
        [Min(1f)] public float fps = 12f;
    }

    [Header("Baked actions (each keyed by CharacterAction)")]
    public List<ActionSet> actions = new List<ActionSet>();

    [Header("Strip geometry (written by the baker)")]
    public int cellW = 96, cellH = 96, ppu = 96;
    [Tooltip("Slicing hint — set to the largest action's frame count. SpriteSheetCache.Row clamps per strip.")]
    public int maxFrames = 24;

    [Header("Self-drive thresholds (units/sec)")]
    public float walkThreshold = 0.15f;
    public float runThreshold = 3.5f;

    [Header("Grounding")]
    [Tooltip("Local units to lift the (centre-pivot) sprite so the feet sit on this transform. The baker " +
             "bottom-aligns the art, so ~0.45 × sprite height grounds it; tune if it floats/sinks.")]
    public float feetOffset = 0.45f;

    // ---- sliced clips, keyed by action: [octant][frame] ----
    readonly Dictionary<CharacterAction, Sprite[][]> clips = new Dictionary<CharacterAction, Sprite[][]>();
    readonly Dictionary<CharacterAction, float> fpsByAction = new Dictionary<CharacterAction, float>();
    // Per-action playback-speed multiplier (1 = baked fps). Lets a controller sync e.g. Attack to attack-speed
    // without re-baking; multiplies the baked fps at play time. Default 1 for any action not set.
    readonly Dictionary<CharacterAction, float> speedScale = new Dictionary<CharacterAction, float>();
    SpriteRenderer sr;
    Transform spriteTf;

    // ---- drive state ----
    Vector3 lastPos, facingDir = Vector3.back;   // default: face the camera (S) when idle
    bool externallyDriven, moving;
    float extSpeed;
    bool hasOverride; CharacterAction overrideAction;
    CharacterAction current;
    float clock; int frame;

    void Awake()
    {
        sr = EnsureChildSprite();

        int p = ppu > 0 ? ppu : cellH;
        clips.Clear(); fpsByAction.Clear();
        foreach (var a in actions)
        {
            if (a == null) continue;
            clips[a.action] = Slice(a.dirs, p);   // last one wins on a duplicate (the builder guards against dups)
            fpsByAction[a.action] = Mathf.Max(1f, a.fps);
        }

        current = Has(CharacterAction.Idle) ? CharacterAction.Idle : FirstAction();
        lastPos = transform.position;
        var c = ClipFor(current);
        if (c != null) { var s = c[4]; if (s != null && s.Length > 0) sr.sprite = s[0]; }
    }

    // The sprite lives on a CHILD lifted to ground the feet. Self-contained: we do the camera-facing here
    // (see LateUpdate) instead of adding the game's Billboard, so a baked character has no dependency on the
    // game's billboard/depth ecosystem.
    SpriteRenderer EnsureChildSprite()
    {
        var t = transform.Find("BakedSprite");
        var go = t != null ? t.gameObject : new GameObject("BakedSprite");
        if (t == null) go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, feetOffset, 0f);
        spriteTf = go.transform;
        var r = go.GetComponent<SpriteRenderer>();
        if (r == null) r = go.AddComponent<SpriteRenderer>();
        return r;
    }

    /// <summary>Scale an action's PLAYBACK speed relative to its baked fps (1 = baked, 2 = twice as fast). Used to
    /// sync e.g. Attack to the live attack-speed so the swing animation keeps pace with the swing cadence.</summary>
    public void SetActionSpeed(CharacterAction a, float multiplier) => speedScale[a] = Mathf.Max(0.01f, multiplier);

    /// <summary>The action's natural play length in seconds at its baked fps (frame count / fps), ignoring any
    /// speed scale. 0 if the action isn't baked. Lets a caller compress a clip to fit a target window (e.g. a swing).</summary>
    public float NaturalDuration(CharacterAction a)
    {
        if (!clips.TryGetValue(a, out var dirs) || dirs == null) return 0f;
        var s = dirs[4];                                            // South strip
        int n = s != null ? s.Length : 0;
        if (n <= 0) return 0f;
        float fps = fpsByAction.TryGetValue(a, out var f) ? f : 12f;
        return n / Mathf.Max(1f, fps);
    }

    /// <summary>Current grounding lift (local units). See <see cref="SetFeetOffset"/>.</summary>
    public float FeetOffset => feetOffset;

    /// <summary>Live-set the grounding lift and reposition the sprite child THIS frame (no Awake re-run needed).
    /// Used by the in-game DevSettings "Feet offset" knob so you can seat the feet on the ground while playing.</summary>
    public void SetFeetOffset(float v)
    {
        feetOffset = v;
        if (spriteTf == null) spriteTf = transform.Find("BakedSprite");
        if (spriteTf != null)
        {
            var p = spriteTf.localPosition;
            p.y = v;
            spriteTf.localPosition = p;
        }
    }

    // Upright billboard: yaw the sprite toward the camera but stay world-vertical (RO-style).
    void FaceCamera()
    {
        var cam = CameraRig.Cam;
        if (cam == null || spriteTf == null) return;
        Vector3 dir = spriteTf.position - cam.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 1e-6f) spriteTf.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    Sprite[][] Slice(in Dir8 set, int p)
    {
        var arr = new Sprite[8][];
        for (int i = 0; i < 8; i++)
        {
            Texture2D tex = (set.strips != null && i < set.strips.Length) ? set.strips[i] : null;
            arr[i] = tex != null ? SpriteSheetCache.Row(tex, cellW, cellH, maxFrames, p) : System.Array.Empty<Sprite>();
        }
        return arr;
    }

    bool Has(CharacterAction a) => clips.TryGetValue(a, out var c) && c != null;
    CharacterAction FirstAction() => actions.Count > 0 && actions[0] != null ? actions[0].action : CharacterAction.Idle;
    Sprite[][] ClipFor(CharacterAction a) => clips.TryGetValue(a, out var c) ? c : null;

    // ---- AI / controller hooks ----
    /// <summary>Force a specific action (e.g. Attack) until <see cref="StopOverride"/>. Switches off self-drive.</summary>
    public void Play(CharacterAction a) { externallyDriven = true; hasOverride = true; overrideAction = a; }
    /// <summary>Return to movement-driven Idle/Walk/Run selection (still under explicit facing/moving if set).</summary>
    public void StopOverride() => hasOverride = false;
    /// <summary>Hand control fully back to the built-in self-drive (movement-sampled facing + Idle/Walk/Run).
    /// Clears any override and re-bases the movement sampler so the first frame doesn't see a stale jump.</summary>
    public void ResumeSelfDrive() { externallyDriven = false; hasOverride = false; lastPos = transform.position; }
    /// <summary>Set the world-space facing/move direction (XZ). Switches off self-drive movement sampling.</summary>
    public void SetFacing(Vector3 worldDir) { externallyDriven = true; worldDir.y = 0f; if (worldDir.sqrMagnitude > 1e-6f) facingDir = worldDir; }
    /// <summary>Set whether the character is moving (drives Walk/Run vs Idle). Switches off self-drive.</summary>
    public void SetMoving(bool isMoving) { externallyDriven = true; moving = isMoving; }
    /// <summary>Explicit drive from a world velocity (XZ): sets facing + speed so Idle/Walk/Run pick correctly,
    /// and any moving cancels a one-shot action override. The precise way for a controller to drive this.</summary>
    public void SetVelocity(Vector3 worldVel)
    {
        externallyDriven = true;
        worldVel.y = 0f;
        extSpeed = worldVel.magnitude;
        moving = extSpeed >= walkThreshold;
        if (moving) hasOverride = false;                              // moving interrupts a played action
        if (worldVel.sqrMagnitude > 1e-6f) facingDir = worldVel;
    }

    // Pick the locomotion action for the current speed, falling back to whatever was actually baked.
    CharacterAction Locomotion(float speed)
    {
        if (speed >= runThreshold && Has(CharacterAction.Run)) return CharacterAction.Run;
        if (speed >= walkThreshold)
        {
            if (Has(CharacterAction.Walk)) return CharacterAction.Walk;
            if (Has(CharacterAction.Run)) return CharacterAction.Run;
        }
        return Has(CharacterAction.Idle) ? CharacterAction.Idle : FirstAction();
    }

    void LateUpdate()
    {
        if (sr == null) return;
        FaceCamera();

        float speed = 0f;
        if (!externallyDriven)
        {
            Vector3 delta = transform.position - lastPos;
            lastPos = transform.position;
            delta.y = 0f;
            speed = Time.deltaTime > 1e-5f ? delta.magnitude / Time.deltaTime : 0f;
            if (speed >= walkThreshold && delta.sqrMagnitude > 1e-8f) facingDir = delta;
        }
        else speed = extSpeed > 0f ? extSpeed                                        // explicit velocity drive
                   : (moving ? Mathf.Max(walkThreshold, runThreshold * 0.5f) : 0f);  // bare SetMoving fallback

        CharacterAction want = hasOverride ? overrideAction : Locomotion(speed);
        if (!Has(want)) want = Has(CharacterAction.Idle) ? CharacterAction.Idle : FirstAction();
        if (want != current) { current = want; clock = 0f; }

        var dirs = ClipFor(current);
        if (dirs == null) return;
        int oct = Facing8.OctantFromWorldDir(facingDir);
        Sprite[] clip = dirs[oct];
        if (clip == null || clip.Length == 0) clip = dirs[4];   // fall back to S
        if (clip == null || clip.Length == 0) return;

        float fps = fpsByAction.TryGetValue(current, out var f) ? f : 12f;
        if (speedScale.TryGetValue(current, out var sc)) fps *= sc;   // attack-speed sync etc.
        clock += Time.deltaTime * Mathf.Max(1f, fps);
        if (clock >= clip.Length) clock -= clip.Length * Mathf.Floor(clock / clip.Length);
        frame = Mathf.Min((int)clock, clip.Length - 1);
        sr.sprite = clip[frame];
    }
}
