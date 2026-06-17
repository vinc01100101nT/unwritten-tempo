using UnityEngine;

/// <summary>
/// 4-direction billboard animator for mobs in the 2.5D world. Slices a Ninja Adventure character
/// sheet at RUNTIME (via <see cref="Sprite.Create"/>, like CharacterAnimator2D's attack poses — so
/// the sheet doesn't need to be pre-sliced in the project) and plays the walk frames based on a
/// <see cref="PathAgent3D"/>'s motion. Columns are Down/Up/Left/Right; the top rows are walk frames.
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
    Vector2 facing = Vector2.down;
    float timer; int frame;
    Vector3 baseScale;
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
    }

    void BuildFrames()
    {
        if (sheet == null) { Debug.LogWarning("[unwritten] BillboardAnimator3D has no sheet on " + name); return; }
        int cols = Mathf.Max(1, sheet.width / cell);
        int rows = Mathf.Max(1, sheet.height / cell);
        int wf = Mathf.Clamp(walkFrames, 1, rows);

        Sprite[] Column(int c)
        {
            c = Mathf.Min(c, cols - 1);
            var arr = new Sprite[wf];
            for (int r = 0; r < wf; r++)
            {
                // Unity texture origin is bottom-left; row 0 is the TOP of the sheet.
                var rect = new Rect(c * cell, sheet.height - (r + 1) * cell, cell, cell);
                arr[r] = Sprite.Create(sheet, rect, new Vector2(0.5f, 0.5f), cell);
            }
            return arr;
        }
        down = Column(0); up = Column(1); left = Column(2); right = Column(3);
    }

    void Update()
    {
        bool moving = agent != null && agent.Moving;
        if (moving) facing = agent.Facing;

        var clip = ClipFor(facing);
        if (clip == null || clip.Length == 0) return;
        if (moving)
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
        if (breathe && !moving)
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
