using UnityEngine;

/// <summary>
/// Lightweight, code-driven 4-direction sprite animator for top-down characters.
/// It derives movement from the transform's OWN position change each frame, so it
/// needs no reference to the controller and works for the player AND monsters,
/// however they move. Assign the sliced walk frames per direction in the
/// Inspector; idle shows the first frame of the last-faced direction.
///
/// Optionally assign an <see cref="attackSheet"/> (a 4-column Down/Up/Left/Right
/// attack-pose strip, e.g. SeparateAnim/Attack.png). When <see cref="PlayAttack"/>
/// is called the character snaps to face the target and holds the attack pose for
/// a moment, then returns to walking — so swings don't look static.
///
/// Deliberately NOT an AnimatorController/blend tree: for plain 2D walk cycles
/// this is far less wiring and trivially reused. Swap to an Animator later if you
/// need animation events or complex transitions.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CharacterAnimator2D : MonoBehaviour
{
    [Header("Walk frames (sliced from the character SpriteSheet)")]
    public Sprite[] down;
    public Sprite[] up;
    public Sprite[] left;
    public Sprite[] right;

    [Tooltip("Animation playback speed, in frames per second.")]
    public float framesPerSecond = 8f;

    [Tooltip("Below this speed (world units/sec) the character is treated as idle.")]
    public float moveThreshold = 0.05f;

    [Header("Attack pose (optional)")]
    [Tooltip("4-column Down/Up/Left/Right attack-pose strip (e.g. SeparateAnim/Attack.png). Leave empty for none.")]
    public Texture2D attackSheet;
    [Tooltip("Cell size (px) of the attack strip — Ninja Adventure characters are 16.")]
    public int attackCell = 16;

    SpriteRenderer sr;
    Vector3 lastPos;
    Vector2 facing = Vector2.down;   // start facing the camera
    float timer;
    int frame;

    Sprite[] attackPoses;            // [0]=down [1]=up [2]=left [3]=right
    float attackUntil;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        // Give this character per-object Y-depth so it occludes / is occluded by others purely by
        // screen position — covers monsters spawned at runtime (their prefab has no sorter yet).
        DepthSortRuntime.EnsureUnitSorter(sr);
        lastPos = transform.position;
        BuildAttackPoses();
    }

    /// <summary>Snap to face <paramref name="dir"/> and hold the attack pose briefly.</summary>
    public void PlayAttack(Vector2 dir, float holdSeconds)
    {
        if (attackPoses == null) return;
        if (dir.sqrMagnitude > 1e-4f) facing = SnapDir(dir);
        attackUntil = Time.time + holdSeconds;
    }

    void Update()
    {
        // Velocity straight from the position delta — works for any kind of mover.
        Vector2 vel = (transform.position - lastPos) / Mathf.Max(Time.deltaTime, 1e-5f);
        lastPos = transform.position;

        // Holding an attack pose: show it for the faced direction and skip walking.
        if (attackPoses != null && Time.time < attackUntil)
        {
            sr.sprite = attackPoses[DirIndex(facing)];
            return;
        }

        bool moving = vel.sqrMagnitude > moveThreshold * moveThreshold;
        if (moving) facing = SnapDir(vel);

        var clip = ClipFor(facing);
        if (clip == null || clip.Length == 0) return;

        if (moving)
        {
            timer += Time.deltaTime;
            float frameTime = 1f / Mathf.Max(framesPerSecond, 0.01f);
            while (timer >= frameTime)
            {
                timer -= frameTime;
                frame = (frame + 1) % clip.Length;
            }
        }
        else
        {
            frame = 0;     // idle = first frame of the current facing
            timer = 0f;
        }

        sr.sprite = clip[Mathf.Min(frame, clip.Length - 1)];
    }

    void BuildAttackPoses()
    {
        if (attackSheet == null || attackCell <= 0) return;
        int cols = Mathf.Max(1, attackSheet.width / attackCell);
        attackPoses = new Sprite[4];
        for (int i = 0; i < 4 && i < cols; i++)
            attackPoses[i] = Sprite.Create(attackSheet,
                new Rect(i * attackCell, 0, attackCell, attackCell),
                new Vector2(0.5f, 0.5f), attackCell);   // pivot centre, ppu = cell → 1 unit, matches walk
    }

    Sprite[] ClipFor(Vector2 dir)
    {
        if (dir.y > 0f) return up;
        if (dir.y < 0f) return down;
        if (dir.x < 0f) return left;
        return right;
    }

    // Column order in the sheets: 0 = down, 1 = up, 2 = left, 3 = right.
    static int DirIndex(Vector2 d)
    {
        if (d.y > 0f) return 1;
        if (d.y < 0f) return 0;
        if (d.x < 0f) return 2;
        return 3;
    }

    static Vector2 SnapDir(Vector2 v)
        => Mathf.Abs(v.x) > Mathf.Abs(v.y) ? new Vector2(Mathf.Sign(v.x), 0f) : new Vector2(0f, Mathf.Sign(v.y));
}
