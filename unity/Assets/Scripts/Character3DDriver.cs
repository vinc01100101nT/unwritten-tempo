using UnityEngine;

/// <summary>
/// SPIKE (experiment/3d-characters): drives a LIVE 3D skinned Quaternius character with a real
/// <see cref="Animator"/> + AnimatorController, instead of the shipped baked-billboard path
/// (<see cref="BakedPlayerVisual"/>). Camera-relative WASD moves a CharacterController; the model
/// yaws to face travel; the Animator gets a <c>Speed</c> float (Idle→Walk→Run blend) plus
/// <c>Attack</c>/<c>Jump</c> triggers. Left-click swings; Space hops; Shift walks.
///
/// This is a THROWAWAY comparison rig — it does NOT touch Player3DController or the billboard
/// pipeline. Input routes through <see cref="Hotkeys"/> (the typing-guarded choke point) and the
/// camera basis through <see cref="CameraRig"/>, matching Player3DController so the feel matches.
/// If we commit to 3D, its movement/attack logic folds into the real controller.
///
/// Perf: no heap allocation in Update — only value-type Vector3/Quaternion math, static-cached
/// param hashes, and sibling refs cached in Awake (CameraRig self-caches the camera basis).
/// </summary>
[RequireComponent(typeof(CharacterController), typeof(Animator))]
public class Character3DDriver : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Run speed in world units/sec (Shift downshifts to walk).")]
    public float runSpeed = 3.5f;
    [Tooltip("Multiplier applied to runSpeed while holding Shift (walk).")]
    public float walkMult = 0.5f;
    [Tooltip("How fast the model yaws to face its travel direction (deg/sec).")]
    public float turnSpeed = 900f;
    public float gravity = 20f;
    public float jumpSpeed = 6f;

    [Header("Attack")]
    [Tooltip("Seconds movement is held while a left-click swing plays.")]
    public float attackLock = 0.55f;

    CharacterController cc;
    Animator anim;
    float vy;
    float attackUntil;

    // Hashed once — these names MUST match the AnimatorController that Build3DCharacterSpike generates.
    static readonly int PSpeed  = Animator.StringToHash("Speed");
    static readonly int PAttack = Animator.StringToHash("Attack");
    static readonly int PJump   = Animator.StringToHash("Jump");

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        anim = GetComponent<Animator>();
        // Motion comes from the CharacterController, not the clip's root curve.
        anim.applyRootMotion = false;
    }

    void Update()
    {
        Vector3 wasd = ReadWasd();
        bool walking = wasd.sqrMagnitude > 0.0001f && HeldShift();
        bool attacking = Time.time < attackUntil;

        // Left-click = swing (SwordSlash); hold position for the swing window so it reads as a planted attack.
        if (LeftDown() && !attacking) { anim.SetTrigger(PAttack); attackUntil = Time.time + attackLock; }

        // Space = hop (only from the ground).
        if (Hotkeys.Down(KeyCode.Space) && cc.isGrounded) { anim.SetTrigger(PJump); vy = jumpSpeed; }

        float speedMul = walking ? walkMult : 1f;
        bool moving = wasd.sqrMagnitude > 0.0001f && !attacking;
        Vector3 horiz = moving ? wasd * (runSpeed * speedMul) : Vector3.zero;

        // Face travel (real 3D yaw — the whole model turns, not a billboard frame swap).
        if (moving)
        {
            Quaternion look = Quaternion.LookRotation(new Vector3(horiz.x, 0f, horiz.z));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        // Accumulating gravity (mirrors Player3DController: stick to the floor when grounded, free-fall otherwise).
        if (cc.isGrounded && vy < 0f) vy = -2f;
        else vy -= gravity * Time.deltaTime;
        cc.Move((horiz + Vector3.up * vy) * Time.deltaTime);

        // Speed 0..1 drives the Idle(0)→Walk(0.5)→Run(1) blend tree; damped so it eases between gaits.
        float target = moving ? speedMul : 0f;
        anim.SetFloat(PSpeed, target, 0.1f, Time.deltaTime);
    }

    // WASD → camera-relative XZ direction (normalized), or zero. Same CameraRig basis as Player3DController,
    // so "W" is always away-from-camera even as the follow cam orbits, and diagonals aren't faster.
    Vector3 ReadWasd()
    {
        float x = 0f, z = 0f;
        if (Hotkeys.Held(KeyCode.W)) z += 1f;
        if (Hotkeys.Held(KeyCode.S)) z -= 1f;
        if (Hotkeys.Held(KeyCode.D)) x += 1f;
        if (Hotkeys.Held(KeyCode.A)) x -= 1f;
        if (x == 0f && z == 0f) return Vector3.zero;
        Vector3 dir = CameraRig.ForwardXZ * z + CameraRig.RightXZ * x; dir.y = 0f;
        return dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero;
    }

    static bool HeldShift() =>
        Hotkeys.Held(KeyCode.LeftShift) || Hotkeys.Held(KeyCode.RightShift);

    // Legacy Input is enabled (Hotkeys uses it), so a plain mouse read is safe here too.
    static bool LeftDown() => Input.GetMouseButtonDown(0);
}
