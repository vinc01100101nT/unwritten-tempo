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
// Renders at the default order 0 — AFTER a StanceController (−50) has set this frame's override, which is itself
// after the gameplay brain (BakedPlayerVisual, −100). Explicit so that ordering contract can't silently drift.
[DisallowMultipleComponent]
[DefaultExecutionOrder(0)]
public class BakedBillboard8 : MonoBehaviour, IStancePose
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

    [Header("Speed-synced gait (self-drive only)")]
    [Tooltip("When self-animating, scale the Walk/Run playback by the ACTUAL ground speed, so a faster mover " +
             "(a quicker NPC, or one under a move-speed buff) churns its legs faster and a slower one slower — " +
             "the NPC/mob twin of the player's GaitSpeedMult. Off = always play at the baked fps. Ignored while a " +
             "controller drives the action (e.g. the player, which syncs its own gait explicitly).")]
    public bool speedSyncGait = true;
    [Tooltip("Ground speed (units/sec) at which the baked WALK fps looks natural; the walk cycle scales around this.")]
    public float walkRefSpeed = 1.4f;
    [Tooltip("Ground speed (units/sec) at which the baked RUN fps looks natural; the run cycle scales around this.")]
    public float runRefSpeed = 3.5f;
    [Tooltip("Clamp on the gait speed multiplier so extremes never churn the legs absurdly fast/slow.")]
    public Vector2 gaitScaleClamp = new Vector2(0.4f, 3f);

    [Header("Grounding")]
    [Tooltip("Local units to lift the (centre-pivot) sprite so the feet sit on this transform. The baker " +
             "bottom-aligns the art, so ~0.45 × sprite height grounds it; tune if it floats/sinks.")]
    public float feetOffset = 0.17f;

    [Header("Lighting / shadows")]
    [Tooltip("Light the sprite like the 3D world: assign the Unwritten/BillboardLit material so the flat art " +
             "casts a cutout shadow and darkens in shade — the SAME treatment regular mobs get. Off keeps the " +
             "plain unlit Sprites/Default look (no shadow).")]
    public bool castReceiveShadows = true;

    // ---- sliced clips, keyed by action: [octant][frame] ----
    readonly Dictionary<CharacterAction, Sprite[][]> clips = new Dictionary<CharacterAction, Sprite[][]>();
    readonly Dictionary<CharacterAction, float> fpsByAction = new Dictionary<CharacterAction, float>();
    // Per-action playback-speed multiplier (1 = baked fps). Lets a controller sync e.g. Attack to attack-speed
    // without re-baking; multiplies the baked fps at play time. Default 1 for any action not set.
    readonly Dictionary<CharacterAction, float> speedScale = new Dictionary<CharacterAction, float>();
    SpriteRenderer sr;
    Transform spriteTf;

    /// <summary>The visible BODY sprite renderer (the "BakedSprite" child) — NOT the cast-shadow proxy. Read this
    /// for hover-picks / feet-Y so callers don't blindly grab the flat ground shadow via GetComponentInChildren.</summary>
    public SpriteRenderer Body => sr;

    // ---- drive state ----
    Vector3 lastPos, facingDir = Vector3.back;   // default: face the camera (S) when idle
    bool externallyDriven, moving;
    float extSpeed;
    bool hasOverride; CharacterAction overrideAction;
    // Jump/Fall clip-phase drive (no dedicated Fall art — reuse the Jump clip): play up to phaseFreezeSeconds and
    // HOLD that frame (airborne), or once phaseRelease play from it to the end (landing tail). Seconds→frame via fps.
    bool phaseMode; CharacterAction phaseSource; float phaseFreezeSeconds = 1.5f; bool phaseRelease;
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

        // Light-facing cast shadow (survives the sun being off to the camera's side — see BillboardShadow).
        // Only when this character casts shadows at all; source = the BakedSprite child found by the component.
        if (castReceiveShadows && GetComponent<BillboardShadow>() == null)
            gameObject.AddComponent<BillboardShadow>().source = sr;
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
        if (castReceiveShadows) ApplyBillboardLighting(r);
        return r;
    }

    // Runtime twin of RigBuilder25D.ApplyBillboardLighting (which lives in the editor assembly and can't be
    // called from Play): light the flat sprite like the rest of the 3D world so it throws a clean cutout
    // shadow (cast from both faces of the thin quad) and darkens in shade — exactly what regular mobs get.
    static Material _billboardMat;
    static void ApplyBillboardLighting(SpriteRenderer sr)
    {
        if (_billboardMat == null)
        {
            var shader = Shader.Find("Unwritten/BillboardLit");
            if (shader == null)
            {
                Debug.LogWarning("[unwritten] Shader 'Unwritten/BillboardLit' not found — baked character won't " +
                                 "cast/receive shadows. Add it to Project Settings ▸ Graphics ▸ Always Included Shaders.");
                return;
            }
            _billboardMat = new Material(shader) { name = "BillboardLit (baked)" };
        }
        sr.sharedMaterial = _billboardMat;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;  // thin quad: cast from both faces
        sr.receiveShadows = true;
    }

    /// <summary>Scale an action's PLAYBACK speed relative to its baked fps (1 = baked, 2 = twice as fast). Used to
    /// sync e.g. Attack to the live attack-speed so the swing animation keeps pace with the swing cadence.</summary>
    public void SetActionSpeed(CharacterAction a, float multiplier) => speedScale[a] = Mathf.Max(0.01f, multiplier);

    /// <summary>The action's baked playback fps (the value the baker authored / the inspector shows), or 0 if the
    /// action isn't baked. Lets one character read another's fine-tuned cadence to copy it.</summary>
    public float ActionFps(CharacterAction a) => fpsByAction.TryGetValue(a, out var f) ? f : 0f;

    /// <summary>Live-set an action's playback fps (updates both the runtime table and the serialized ActionSet so it
    /// sticks). Used to push the player's tuned Walk/Run cadence onto NPCs so they don't foot-slide.</summary>
    public void SetActionFps(CharacterAction a, float fps)
    {
        fps = Mathf.Max(1f, fps);
        if (fpsByAction.ContainsKey(a)) fpsByAction[a] = fps;       // only if the action exists (don't invent strips)
        foreach (var act in actions) if (act != null && act.action == a) act.fps = fps;
    }

    /// <summary>Make this self-driven character walk/run EXACTLY like the fine-tuned <paramref name="src"/> (the
    /// player's billboard): copy its locomotion thresholds, per-action fps, speed-sync flag and clamp, and anchor
    /// the speed-sync to the player's REAL Walk/Run ground speeds (<paramref name="walkRef"/>/<paramref name="runRef"/>),
    /// so an NPC moving at the player's speed looks identical and faster/slower ones scale from the same baseline —
    /// killing the foot-slide. Call once after spawn (cheap). No effect on the player itself (it overrides its own gait).</summary>
    public void MatchGaitTuning(BakedBillboard8 src, float walkRef, float runRef)
    {
        if (src == null) return;
        walkThreshold  = src.walkThreshold;
        speedSyncGait  = src.speedSyncGait;
        gaitScaleClamp = src.gaitScaleClamp;
        walkRefSpeed   = walkRef > 0.01f ? walkRef : src.walkRefSpeed;
        runRefSpeed    = runRef  > 0.01f ? runRef  : src.runRefSpeed;
        // Calibrate the Walk→Run crossover to the REAL reference speeds, NOT src.runThreshold: the player never
        // self-drives, so its runThreshold is an uncalibrated stale value (e.g. 3.5) that no self-driven NPC ever
        // reaches — leaving townsfolk who actually move at ~run speed stuck churning the Walk clip super-fast. Cross
        // to Run at the midpoint between the walk and run reference speeds (kept above the idle/walk line).
        if (runRefSpeed > walkRefSpeed)
            runThreshold = Mathf.Max(walkThreshold + 0.01f, Mathf.Lerp(walkRefSpeed, runRefSpeed, 0.5f));
        else
            runThreshold = src.runThreshold;   // refs unusable — fall back to the copied value
        // Inherit the player's hand-tuned leg cadence for the locomotion (and idle) clips.
        float wf = src.ActionFps(CharacterAction.Walk); if (wf > 0f) SetActionFps(CharacterAction.Walk, wf);
        float rf = src.ActionFps(CharacterAction.Run);  if (rf > 0f) SetActionFps(CharacterAction.Run,  rf);
        float idf = src.ActionFps(CharacterAction.Idle); if (idf > 0f) SetActionFps(CharacterAction.Idle, idf);
    }

    /// <summary>The action's natural play length in seconds at its baked fps (frame count / fps), ignoring any
    /// speed scale. 0 if the action isn't baked. Lets a caller compress a clip to fit a target window (e.g. a swing).</summary>
    public float NaturalDuration(CharacterAction a, string clip = null)
    {
        // 2D billboards key on the baked action; the 3D clip name is unused here.
        if (!clips.TryGetValue(a, out var dirs) || dirs == null) return 0f;
        var s = dirs[4];                                            // South strip
        int n = s != null ? s.Length : 0;
        if (n <= 0) return 0f;
        float fps = fpsByAction.TryGetValue(a, out var f) ? f : 12f;
        return n / Mathf.Max(1f, fps);
    }

    /// <summary>The action's baked FRAME COUNT (South strip length), 0 if not baked. Lets a caller map an authored
    /// frame index to a normalized point in the action.</summary>
    public int NaturalFrameCount(CharacterAction a, string clip = null)
    {
        if (!clips.TryGetValue(a, out var dirs) || dirs == null) return 0;
        var s = dirs[4];                                            // South strip
        return s != null ? s.Length : 0;
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

    // Actions that play ONCE and then freeze on their final frame instead of looping. Die is the clear case:
    // the corpse must stay down on the ground, not snap back to standing and re-play the fall (the "repeating
    // death" bug). The windowed one-shots the controller drives (Attack/Jump/Roll/Hurt/PickUp) are switched
    // away the instant they end, so looping within their short window is invisible — only the persistent Die
    // pose ever shows the wrap.
    static bool HoldsLastFrame(CharacterAction a) => a == CharacterAction.Die;

    bool Has(CharacterAction a) => clips.TryGetValue(a, out var c) && c != null;
    CharacterAction FirstAction() => actions.Count > 0 && actions[0] != null ? actions[0].action : CharacterAction.Idle;
    Sprite[][] ClipFor(CharacterAction a) => clips.TryGetValue(a, out var c) ? c : null;

    /// <summary>True if the given action has baked strips, so a caller can fall back when it's missing
    /// (e.g. use Dash when Roll wasn't baked, or Walk when there's no Run).</summary>
    public bool HasAction(CharacterAction a, string clip = null) => Has(a);   // 2D keys on action; clip unused

    /// <summary>The current sprite's world-space height (sprite bounds × lossy scale), or 0 before the first
    /// frame is set. Used to size gameplay thresholds to the VISIBLE character — e.g. the player's max
    /// auto-climb step (block elevated surfaces ≥50% of this).</summary>
    public float VisualWorldHeight
    {
        get
        {
            if (sr == null || sr.sprite == null) return 0f;
            float s = spriteTf != null ? Mathf.Abs(spriteTf.lossyScale.y) : 1f;
            return sr.sprite.bounds.size.y * s;
        }
    }

    // ---- AI / controller hooks ----
    /// <summary>Force a specific action (e.g. Attack) until <see cref="StopOverride"/>. Switches off self-drive.</summary>
    public void Play(CharacterAction a, string clip = null) { externallyDriven = true; hasOverride = true; overrideAction = a; phaseMode = false; }
    /// <summary>Drive <paramref name="src"/> as a jump/fall clip with a freeze point (in SECONDS): <paramref name="releaseToEnd"/>
    /// false = play up to <paramref name="freezeSeconds"/> then HOLD that frame (airborne); true = play from it to the
    /// end once (landing tail). Cleared by any other Play / StopOverride.</summary>
    public void PlayClipPhase(CharacterAction src, string clip, float freezeSeconds, bool releaseToEnd, float mult = 1f)
    {
        Play(src);                       // sets the override; Play cleared phaseMode, so re-arm it below
        phaseMode = true; phaseSource = src; phaseFreezeSeconds = Mathf.Max(0f, freezeSeconds); phaseRelease = releaseToEnd;
    }
    /// <summary>Return to movement-driven Idle/Walk/Run selection (still under explicit facing/moving if set).</summary>
    public void StopOverride() { hasOverride = false; phaseMode = false; }
    /// <summary>Hand control fully back to the built-in self-drive (movement-sampled facing + Idle/Walk/Run).
    /// Clears any override and re-bases the movement sampler so the first frame doesn't see a stale jump.</summary>
    public void ResumeSelfDrive() { externallyDriven = false; hasOverride = false; phaseMode = false; lastPos = transform.position; }
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

        // Self-driven locomotion: bind the leg cadence to the ACTUAL ground speed (the NPC/mob twin of the
        // player's GaitSpeedMult). Skipped while a controller overrides the action — the player syncs its own
        // gait explicitly, so we don't double-apply.
        if (speedSyncGait && !hasOverride && (current == CharacterAction.Walk || current == CharacterAction.Run))
        {
            float refSpd = current == CharacterAction.Run ? runRefSpeed : walkRefSpeed;
            if (refSpd > 0.01f) fps *= Mathf.Clamp(speed / refSpd, gaitScaleClamp.x, gaitScaleClamp.y);
        }

        clock += Time.deltaTime * Mathf.Max(1f, fps);

        // Jump/Fall clip phase: play up to the freeze frame and HOLD it (airborne), or once releasing play it to the
        // end (landing tail). freeze seconds → frame via the current fps; clock is clamped so the hold can resume cleanly.
        if (phaseMode && current == phaseSource)
        {
            int freezeFrame = Mathf.Clamp(Mathf.RoundToInt(phaseFreezeSeconds * fps), 0, clip.Length - 1);
            if (phaseRelease)
            {
                if (clock < freezeFrame) clock = freezeFrame;      // short hop: snap up to the freeze frame, then play out
                frame = Mathf.Min((int)clock, clip.Length - 1);    // play to the end, hold the last frame
            }
            else
            {
                if (clock > freezeFrame) clock = freezeFrame;       // hold on the freeze frame while airborne
                frame = (int)clock;
            }
            sr.sprite = clip[Mathf.Clamp(frame, 0, clip.Length - 1)];
            return;
        }

        if (HoldsLastFrame(current))
            frame = Mathf.Min((int)clock, clip.Length - 1);   // play ONCE, then hold the final frame (no loop)
        else
        {
            if (clock >= clip.Length) clock -= clip.Length * Mathf.Floor(clock / clip.Length);
            frame = Mathf.Min((int)clock, clip.Length - 1);
        }
        sr.sprite = clip[frame];
    }
}
