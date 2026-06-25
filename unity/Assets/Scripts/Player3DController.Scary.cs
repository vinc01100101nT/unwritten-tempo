using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Sscary 48×64 billboard player — a THIRD animation path for <see cref="Player3DController"/> alongside the
/// legacy single-pose and the baked NinjaGreen sheet. When the Sscary strips are wired (by
/// Tools ▸ unwritten ▸ Player ▸ Use Sscary Player) <see cref="Scary"/> is true and this path drives the
/// sprite + a Dust child layer, superseding the other two. Shadows are the engine's real URP cast shadow.
///
/// <para>The art ships 6 visual angles (front, back, and the four diagonals); the two LEFT diagonals are
/// exact horizontal mirrors of the RIGHT ones, so we only slice 4 drawn clips per action
/// (down / up / right-down / right-up) and resolve full 8-direction facing (<see cref="Facing8"/>) onto them
/// with a <see cref="SpriteRenderer.flipX"/> for the western half. Pure E/W collapse onto the front-diagonal
/// (there is no side-profile pose) — if a side pose is ever added it can slot straight into the octant map.</para>
///
/// <para>Animations: idle &amp; walk loop; jump, dash and death play once and hold the last frame. Space = a
/// REAL, free-directional jump arc (steered live by WASD, even mid-air); after the arc it keeps falling under
/// gravity and holds the last frame until it actually lands, so walk/idle can't play in the air. E = a
/// facing-direction dash burst on a cooldown (dash kicks up dust; the jump has none). The free pack has no
/// attack frames, so a swing falls back to the walk/idle pose (the
/// slash FX still plays).</para>
/// </summary>
public partial class Player3DController
{
    // ===== authored clips (one PNG strip per drawn direction; 8 frames each) =====
    [System.Serializable]
    public struct ScaryClipSet
    {
        public Texture2D down, up, rightDown, rightUp;
        public bool Any => down != null || up != null || rightDown != null || rightUp != null;
    }

    [Header("Sscary player — wired by Tools ▸ unwritten ▸ Player ▸ Use Sscary Player")]
    public ScaryClipSet scaryIdle, scaryWalk, scaryJump, scaryDash, scaryDeath;
    [Tooltip("Directional dust puff for the dash. (Jump no longer uses dust — it followed the body up the arc " +
             "and looked wrong.) No sprite shadows — the engine renders the character's real cast shadow.")]
    public ScaryClipSet scaryDashDust;
    public int scaryCellW = 48, scaryCellH = 64, scaryFrames = 8, scaryPpu = 48;
    [Tooltip("Sprite feet below the cell CENTRE, in local units (pre-scale). Grounds the CharacterController so " +
             "the feet rest on the floor. Auto-set by Use Sscary Player; tune live via P ▸ DevSettings ▸ Player ▸ Foot offset.")]
    public float scaryFeetBelowCenter = 0.13f;

    [Header("Sscary frame rates")]
    public float scaryIdleFps = 6f, scaryWalkFps = 10f, scaryDeathFps = 12f;

    [Header("Sscary jump (Space) — real arc")]
    [Tooltip("World units the hop travels horizontally in the current move/facing direction.")]
    public float jumpDistance = 2.4f;
    [Tooltip("Peak jump height in world units. A REAL vertical arc — the body rises (the engine's cast shadow " +
             "stays on the ground). Air time is derived from this + gravity (t = 2·√(2h/g)) so it stays realistic, " +
             "and the 8-frame clip auto-stretches to fill it. Scales with UnitScale.ForPlayer.")]
    public float jumpHeight = 0.8f;

    [Header("Sscary dash (E) — facing-direction burst")]
    public float eDashSpeed = 4f;
    public float eDashDuration = 0.50f;
    public float eDashCooldown = 0.7f;

    // ===== runtime =====
    public enum ScaryState { Idle, Walk, Jump, Dash, Death, Fall }

    bool scaryBuilt;
    /// <summary>True once the Sscary strips are sliced — this path then owns the sprite (see class doc).</summary>
    public bool Scary => scaryBuilt;

    // Sliced clips: [drawnDir][frame], drawnDir 0=down 1=up 2=rightDown 3=rightUp.
    Sprite[][] sIdle, sWalk, sJump, sDash, sDeath, sDashDust;
    SpriteRenderer scaryDustSR;

    // Frame clocks: one looping clock (idle/walk) + one "play once, hold last" clock (jump/dash/death).
    float scaryLoopT; int scaryLoopFrame;
    float scaryOnceStart; ScaryState scaryOnceState = (ScaryState)(-1);

    // Ability state.
    bool jumping; float jumpStart; Vector3 jumpDir;

    /// <summary>True while a hop arc is in progress (takeoff → land). Read by FX like wet-ground footstep
    /// ripples so they pause mid-air and splash on landing.</summary>
    public bool IsAirborne => jumping;
    float jumpBaseY;       // ground Y captured at takeoff — the arc returns here, then gravity falls to land
    bool eDashing; float eDashStart; Vector3 eDashDir; float eDashReadyAt;

    /// <summary>True during the Sscary E-key dash burst. Exposed so the baked visual can play Roll for it
    /// (the charge gap-closer uses <see cref="Dashing"/>; this is the free traversal dash).</summary>
    public bool EDashing => eDashing;

    // ---- build (called from Awake, after BuildAnimClips) ----------------------------------------------
    void BuildScary()
    {
        if (!scaryIdle.Any) { scaryBuilt = false; return; }   // not a Sscary player — leave the other paths in charge
        int w = scaryCellW, h = scaryCellH, n = scaryFrames, p = scaryPpu > 0 ? scaryPpu : scaryCellH;

        sIdle  = SliceSet(scaryIdle,  w, h, n, p);
        sWalk  = SliceSet(scaryWalk,  w, h, n, p);
        sJump  = SliceSet(scaryJump,  w, h, n, p);
        sDash  = SliceSet(scaryDash,  w, h, n, p);
        sDeath = SliceSet(scaryDeath, w, h, n, p);
        sDashDust = SliceSet(scaryDashDust, w, h, n, p);

        EnsureScaryChildren();
        breathe = false;   // the idle clip conveys life — skip the procedural squash so they don't fight
        scaryBuilt = true;
        ApplyScaryGrounding();   // self-correct the capsule bottom to the feet, even on a stale/persistent player

        var first = ResolveClip(sIdle, 4 /*S*/, out bool flip0);   // sensible frame before the first Update
        if (first != null && first.Length > 0) { sr.sprite = first[0]; sr.flipX = flip0; }
    }

    // Place the CharacterController bottom at the sprite's feet (cc.center.y - height/2 == -feetBelowCenter), so
    // gravity / PersistentPlayer3D rest the FEET on the terrain instead of a stale (taller) capsule's bottom.
    //
    // The capsule SHAPE (radius/height/step/slope/skin) stays the single source of truth tuned in
    // Player3DController (ApplyCapsule) — this used to hardcode h1/r0.3 here and silently stomp those tuned
    // defaults. We now only re-seat the center's Y for grounding, preserving the tuned x/z depth nudge.
    void ApplyScaryGrounding()
    {
        if (!scaryBuilt || controller == null) return;
        ApplyCapsule();   // tuned radius/height/step/slope/skin — the one source of truth
        var c = capsuleCenter;
        c.y = controller.height * 0.5f - scaryFeetBelowCenter;   // bottom of the (tuned-height) capsule on the feet
        controller.center = c;
    }

    /// <summary>Live foot-offset tuning (DevSettings): re-ground so the feet sit on the floor.</summary>
    public void SetScaryFootOffset(float v) { scaryFeetBelowCenter = Mathf.Clamp(v, 0f, 0.6f); ApplyScaryGrounding(); }

    Sprite[][] SliceSet(in ScaryClipSet set, int w, int h, int n, int p) => new[]
    {
        set.down      != null ? SpriteSheetCache.Row(set.down,      w, h, n, p) : System.Array.Empty<Sprite>(),
        set.up        != null ? SpriteSheetCache.Row(set.up,        w, h, n, p) : System.Array.Empty<Sprite>(),
        set.rightDown != null ? SpriteSheetCache.Row(set.rightDown, w, h, n, p) : System.Array.Empty<Sprite>(),
        set.rightUp   != null ? SpriteSheetCache.Row(set.rightUp,   w, h, n, p) : System.Array.Empty<Sprite>(),
    };

    // The dust renders as a cheap transparent billboard layer parented to the player (inherits the Billboard
    // rotation + scale). A small local-Z nudge toward the camera draws it just in front of the body. Shadows
    // are the engine's real URP cast shadow now — there is no sprite-shadow layer.
    void EnsureScaryChildren()
    {
        scaryDustSR = MakeScaryLayer("ScaryDust", -0.05f);
        if (scaryDustSR != null) scaryDustSR.enabled = false;   // hidden until a hop/dash
    }

    SpriteRenderer MakeScaryLayer(string childName, float zLocal)
    {
        var existing = transform.Find(childName);
        var go = existing != null ? existing.gameObject : new GameObject(childName);
        if (existing == null) go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, zLocal);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        var r = go.GetComponent<SpriteRenderer>();
        if (r == null) r = go.AddComponent<SpriteRenderer>();
        r.sharedMaterial = ScaryLayerMaterial();
        return r;
    }

    // One shared transparent (alpha-blended, ZWrite-off) material so the soft dust keeps its gradients —
    // the body's BillboardLit alpha-CLIPS, which would chunk a translucent puff into hard edges.
    static Material _scaryLayerMat;
    static Material ScaryLayerMaterial()
    {
        if (_scaryLayerMat != null) return _scaryLayerMat;
        var sh = Shader.Find("Sprites/Default");
        if (sh != null) _scaryLayerMat = new Material(sh) { name = "ScaryLayer (transparent)" };
        return _scaryLayerMat;
    }

    // ---- per-frame animation (called via Animate() for idle/walk/stun, the death branch, and abilities) ----
    void AnimateScary(ScaryState s)
    {
        int oct = Facing8.OctantFromWorldDir(worldFacing);

        var clip = ResolveClip(SetFor(s), oct, out bool flip);
        int frame = AdvanceScary(s, clip != null ? clip.Length : 0);
        if (clip != null && clip.Length > 0) { sr.sprite = clip[Mathf.Min(frame, clip.Length - 1)]; sr.flipX = flip; }

        DriveScaryLayers(s, oct, frame);
    }

    Sprite[][] SetFor(ScaryState s)
    {
        switch (s)
        {
            case ScaryState.Walk:  return sWalk;
            case ScaryState.Jump:  return sJump;
            case ScaryState.Fall:  return sJump;   // free-fall reuses the jump clip (we loop its descent half)
            case ScaryState.Dash:  return sDash;
            case ScaryState.Death: return sDeath;
            default:               return sIdle;
        }
    }

    // Octant (Facing8 order) → one of the 4 drawn clips + a horizontal flip for the western half.
    static void OctToClip(int oct, out int idx, out bool flip)
    {
        switch (oct)
        {
            case 0: idx = 1; flip = false; return;   // N  → up
            case 1: idx = 3; flip = false; return;   // NE → right-up
            case 2: idx = 2; flip = false; return;   // E  → right-down (no side pose; nearest front-diagonal)
            case 3: idx = 2; flip = false; return;   // SE → right-down
            case 5: idx = 2; flip = true;  return;   // SW → right-down, mirrored
            case 6: idx = 2; flip = true;  return;   // W  → right-down, mirrored
            case 7: idx = 3; flip = true;  return;   // NW → right-up, mirrored
            default: idx = 0; flip = false; return;  // 4 = S → down
        }
    }

    Sprite[] ResolveClip(Sprite[][] set, int oct, out bool flip)
    {
        OctToClip(oct, out int idx, out flip);
        if (set == null) return null;
        var clip = set[idx];
        if ((clip == null || clip.Length == 0) && set[0] != null && set[0].Length > 0) { clip = set[0]; flip = false; }
        return clip;
    }

    // Looping clock for idle/walk; a "play once, hold last frame" clock for jump/dash/death whose duration is
    // matched to the hop/dash length (so the 8 frames span the move) or driven by scaryDeathFps for death.
    int AdvanceScary(ScaryState s, int len)
    {
        if (len <= 0) return 0;

        // Free-fall: loop the jump clip's DESCENT half (2nd half of the frames) for as long as we're airborne,
        // so a ledge walk-off shows the same falling pose the hop ends on — without a takeoff/landing frame.
        if (s == ScaryState.Fall)
        {
            scaryOnceState = (ScaryState)(-1);
            int half = len >= 2 ? len / 2 : 0;
            int span = Mathf.Max(1, len - half);
            scaryLoopT += Time.deltaTime;
            float ftf = 1f / Mathf.Max(scaryWalkFps, 0.01f);
            while (scaryLoopT >= ftf) { scaryLoopT -= ftf; scaryLoopFrame++; }
            return half + (scaryLoopFrame % span);
        }

        bool loop = s == ScaryState.Idle || s == ScaryState.Walk;
        if (loop)
        {
            scaryOnceState = (ScaryState)(-1);
            float fps = s == ScaryState.Walk ? scaryWalkFps : scaryIdleFps;
            scaryLoopT += Time.deltaTime;
            float ft = 1f / Mathf.Max(fps, 0.01f);
            while (scaryLoopT >= ft) { scaryLoopT -= ft; scaryLoopFrame++; }
            return scaryLoopFrame % len;
        }
        if (scaryOnceState != s) { scaryOnceState = s; scaryOnceStart = Time.time; scaryLoopT = 0f; scaryLoopFrame = 0; }
        float t = (Time.time - scaryOnceStart) / Mathf.Max(OnceDuration(s), 0.01f);
        return Mathf.Clamp(Mathf.FloorToInt(t * len), 0, len - 1);
    }

    float OnceDuration(ScaryState s)
    {
        switch (s)
        {
            case ScaryState.Jump: return JumpAirTime;   // anim spans the (height-derived) air time → slows as height grows
            case ScaryState.Dash: return eDashDuration;
            default:              return scaryFrames / Mathf.Max(scaryDeathFps, 0.01f);   // Death
        }
    }

    void DriveScaryLayers(ScaryState s, int oct, int frame)
    {
        if (scaryDustSR == null) return;
        scaryDustSR.sortingOrder = sr.sortingOrder + 1;   // just in front of the body

        // Dust is DASH-only now — the jump dust was removed (it followed the body up the arc and looked wrong).
        Sprite dustSprite = null; bool dustFlip = false;
        if (s == ScaryState.Dash)
            dustSprite = FrameAt(ResolveClip(sDashDust, oct, out dustFlip), frame);

        scaryDustSR.enabled = dustSprite != null;
        if (dustSprite != null) { scaryDustSR.sprite = dustSprite; scaryDustSR.flipX = dustFlip; }
    }

    static Sprite FrameAt(Sprite[] clip, int frame)
        => clip != null && clip.Length > 0 ? clip[Mathf.Clamp(frame, 0, clip.Length - 1)] : null;

    // ---- traversal abilities (called early in Update; returns true when it owns the frame) ----
    bool TickScaryAbilities()
    {
        if (eDashing) { StepEDash(); AnimateScary(ScaryState.Dash); transform.localScale = baseScale; return true; }
        if (jumping)  { StepJump();  AnimateScary(ScaryState.Jump); transform.localScale = baseScale; return true; }

        // Same input guards as ReadInput so a panel/skill-aim click doesn't also hop/dash.
        if (DevSettings.Blocking || GroundTargeter.Active || UnitTargeter.Active) return false;

        if (KeyDash() && Time.time >= eDashReadyAt)
        {
            eDashDir = AbilityDir(); eDashing = true; eDashStart = Time.time;
            SetDashInvuln(true);   // i-frames for the whole dash burst — roll THROUGH incoming hits
            StepEDash(); AnimateScary(ScaryState.Dash); transform.localScale = baseScale; return true;
        }
        if (KeyJump())
        {
            // Direction is steered LIVE in StepJump (re-reads WASD each frame), so we only latch the takeoff
            // height/time here. Fully free-directional in the air; no key held = static up-and-down hop.
            jumping = true; jumpStart = Time.time; jumpBaseY = transform.position.y;
            StepJump(); AnimateScary(ScaryState.Jump); transform.localScale = baseScale; return true;
        }
        return false;
    }

    // Current WASD direction, or the last move/aim facing when standing still.
    Vector3 AbilityDir()
    {
        Vector3 wasd = ReadWasd();
        if (wasd.sqrMagnitude > 1e-4f) return wasd.normalized;
        Vector3 f = worldFacing; f.y = 0f;
        return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
    }

    // Realistic air time for the current peak height under the player's gravity: t = 2·√(2h/g).
    float JumpAirTime => 2f * Mathf.Sqrt(2f * Mathf.Max(0.01f, jumpHeight * UnitScale.ForPlayer) / Mathf.Max(0.1f, gravity));

    void StepJump()
    {
        float air = JumpAirTime;
        float elapsed = Time.time - jumpStart;

        // FULLY free-directional, even mid-air: re-read WASD EVERY frame so releasing or switching keys steers
        // the hop live (release W mid-air -> stops forward; tap A mid-air -> moves and faces left). No key held
        // -> jumpDir zero -> no horizontal travel, the body just rises/falls in place.
        Vector3 wasd = ReadWasd();
        jumpDir = wasd.sqrMagnitude > 1e-4f ? wasd.normalized : Vector3.zero;
        float fwd = jumpDistance * UnitScale.ForPlayer / air;
        Vector3 delta = jumpDir * fwd * Time.deltaTime;

        if (elapsed < air)
        {
            // Phase 1 — scripted arc: the vertical lift is driven DIRECTLY (no gravity) as a symmetric parabola
            // back to takeoff height. The engine's cast shadow tracks the lifted body for a natural jump shadow.
            float u = Mathf.Clamp01(elapsed / air);
            float h = jumpHeight * UnitScale.ForPlayer * 4f * u * (1f - u);   // 0 -> peak (u=0.5) -> 0
            delta.y = (jumpBaseY + h) - transform.position.y;
            controller.Move(delta);
        }
        else
        {
            // Phase 2 — the arc is done but we may still be high in the air (hopped off a tall tree/ledge).
            // Keep FALLING under gravity, holding the last jump frame, until we actually touch the ground —
            // so the walk/idle pose can't play mid-fall. Horizontal steering stays live.
            delta.y = -gravity * Time.deltaTime;
            controller.Move(delta);
            if (controller.isGrounded) { jumping = false; OnJumpLand(); }   // landed -> hand control back to normal movement
        }

        if (jumpDir.sqrMagnitude > 1e-4f) worldFacing = jumpDir;   // re-face toward live input; no input keeps facing
    }

    // On touchdown: if we landed in a visible rain puddle, splash. Same puddle test as the walking water step,
    // so the landing sound only fires where you'd see (and hear) water — dry-ground landings stay silent.
    void OnJumpLand()
    {
        if (waterJumpSfx != null && !waterJumpSfx.Empty
            && WetGround.Instance != null && WetGround.Instance.IsPuddleAt(transform.position))
            Sfx.Play(waterJumpSfx);
    }

    void StepEDash()
    {
        float spd = eDashSpeed * UnitScale.ForPlayer * (status != null ? status.MoveMult : 1f);   // dash distance scales with size
        controller.Move((eDashDir * spd + Vector3.down * gravity) * Time.deltaTime);
        worldFacing = eDashDir;
        if (Time.time - eDashStart >= eDashDuration)
        {
            eDashing = false; eDashReadyAt = Time.time + eDashCooldown;
            SetDashInvuln(false);   // dash over → vulnerable again
        }
    }

    /// <summary>Tie the player's i-frames to the dash burst. The dash is the dodge: while it runs the player is
    /// <see cref="Character.Invulnerable"/>, so incoming hits whiff with a "Dodge" cue. Cleared whenever the dash
    /// ends — including a death-cancel (see the dead branch in the main controller).</summary>
    void SetDashInvuln(bool on) { if (Character.Instance != null) Character.Instance.Invulnerable = on; }

    static bool KeyJump()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.Space);
    }

    static bool KeyDash()
    {
        // Alt+E is the Inventory-window hotkey (ProgressionWindows) — don't also dash on it.
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.E);
    }
}
