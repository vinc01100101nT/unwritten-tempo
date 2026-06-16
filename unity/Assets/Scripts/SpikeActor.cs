using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Self-contained driver for the 2.5D spike's player billboard: moves around the XZ ground
/// plane with WASD and plays the same 4-direction Ninja Adventure walk frames the real game
/// uses. It's separate from <see cref="CharacterAnimator2D"/> on purpose — that one reads
/// facing from XY transform deltas (2D play), but in a 3D scene movement is on XZ, so the
/// spike needs its own little mover. The 4 frame arrays are filled by Build25DSpike.
///
/// Pair it with <see cref="Billboard"/> (faces the camera) on the same SpriteRenderer object.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpikeActor : MonoBehaviour
{
    [Header("Walk frames (column = facing), filled by Build 2.5D Spike")]
    public Sprite[] down, up, left, right;

    public float moveSpeed = 4f;
    public float framesPerSecond = 8f;

    SpriteRenderer sr;
    Vector2 facing = Vector2.down;   // start facing the camera
    float timer;
    int frame;

    void Awake() => sr = GetComponent<SpriteRenderer>();

    void Update()
    {
        float h = 0f, v = 0f;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  h -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    v += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  v -= 1f;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        h = Input.GetAxisRaw("Horizontal");
        v = Input.GetAxisRaw("Vertical");
#endif
        var move = new Vector3(h, 0f, v);
        bool moving = move.sqrMagnitude > 0.001f;
        if (moving)
        {
            move.Normalize();
            transform.position += move * (moveSpeed * Time.deltaTime);
            // Frame facing: horizontal wins ties. +Z (forward, up the screen) = "up" frames.
            facing = Mathf.Abs(h) > Mathf.Abs(v) ? new Vector2(Mathf.Sign(h), 0f)
                                                 : new Vector2(0f, Mathf.Sign(v));
        }

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
    }

    Sprite[] ClipFor(Vector2 f)
    {
        if (f.y > 0f) return up;     // +Z away from camera
        if (f.y < 0f) return down;   // -Z toward camera
        if (f.x < 0f) return left;
        return right;
    }
}
