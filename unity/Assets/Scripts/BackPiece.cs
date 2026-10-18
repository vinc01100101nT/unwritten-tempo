using UnityEngine;

/// <summary>
/// A piece worn on a character's upper back — the model of a Garment item, such as a cape — made by
/// <c>BackPieceBaker</c>. It sits on the root of the piece's prefab; <see cref="GarmentSocket"/> puts it on, fixed to
/// the body's upper-back bone at that body's resting offset, the way a hat is fixed to the head.
///
/// <para><b>How it sways.</b> The piece carries its own short chains of bones — down the left side, the middle of the
/// back and the right side. Every frame each bone's tip is pulled by a damped spring toward where the body's animation
/// puts it, so the cloth trails when the wearer runs, swings back when they stop and follows when they turn. It is then
/// kept behind the wearer's back, and pushed out of both upper arms, so an arm opens the cloth instead of passing
/// through it. The sums are <see cref="BackPieceSway"/>.</para>
///
/// <para><b>What it costs.</b> One spring step and two arm checks per bone per frame — twelve bones for the cape — and
/// nothing at all while
/// the piece is off the screen: it then holds its resting shape and picks the swing up from rest when it comes back
/// into view. Nothing here runs on the server.</para>
/// </summary>
public class BackPiece : MonoBehaviour
{
    [Tooltip("The piece's swinging bones, column by column, each column from the top of the cloth to the last one above " +
             "the hem. Each is the child of the one before it in its column; a column's first is a child of this object.")]
    public Transform[] chain = System.Array.Empty<Transform>();

    [Tooltip("Where each chain bone's tip rests, in that bone's own space: the next bone's pivot, and for the last one " +
             "the middle of the hem.")]
    public Vector3[] tips = System.Array.Empty<Vector3>();

    [Tooltip("How far behind the wearer's middle, in this object's space, the cloth's swinging tips must stay, so the " +
             "cape never folds forward into the body. Measured by the bake from the body's back.")]
    public float backClearance;

    [Tooltip("Which way is the wearer's back in this object's space, as baked: the cloth is kept on this side.")]
    public Vector3 backward = Vector3.up;

    [Tooltip("How thick the wearer's upper arm is, in this object's space, measured by the bake from the plain body: " +
             "the cloth is pushed this far out from the line between shoulder and elbow.")]
    public float armRadius;

    [Tooltip("The cape's own colours, measured by the bake from its picture: what a wearer who chose none sees.")]
    public Color clothColour = Color.red, trimColour = Color.yellow;

    static readonly int CapeCloth = Shader.PropertyToID("_CapeCloth");
    static readonly int CapeClothHem = Shader.PropertyToID("_CapeClothHem");
    static readonly int CapeTrim = Shader.PropertyToID("_CapeTrim");
    static readonly int CapeGradient = Shader.PropertyToID("_CapeGradient");
    MaterialPropertyBlock colourBlock;

    Transform leftArm, leftElbow, rightArm, rightElbow;

    static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    static Texture2D nothing;
    MaterialPropertyBlock emblemBlock;

    Quaternion[] rest;
    Vector3[] point, speed;
    Renderer shown;
    bool live;

    void Awake()
    {
        if (GameServer.RemovesPresentation(this)) return;
        int n = chain.Length;
        rest = new Quaternion[n];
        point = new Vector3[n];
        speed = new Vector3[n];
        for (int i = 0; i < n; i++) rest[i] = chain[i].localRotation;
        shown = GetComponentInChildren<Renderer>();
        ShowEmblem(null);
    }

    /// <summary>The cape's own colours, as the colours a wearer starts from.</summary>
    public CapeColours OwnColours => new CapeColours { cloth = clothColour, trim = trimColour, hem = clothColour };

    /// <summary>Draw the cloth and trim in <paramref name="colours"/>, or in the cape's own colours when null. One set of
    /// values for this wearer's drawing; the material and its picture are shared.</summary>
    public void ShowColours(CapeColours? colours)
    {
        if (shown == null) return;
        var c = colours ?? OwnColours;
        if (colourBlock == null) colourBlock = new MaterialPropertyBlock();
        colourBlock.SetColor(CapeCloth, c.cloth);
        colourBlock.SetColor(CapeClothHem, c.gradient ? c.hem : c.cloth);
        colourBlock.SetColor(CapeTrim, c.trim);
        colourBlock.SetFloat(CapeGradient, c.gradient ? 1f : 0f);
        shown.SetPropertyBlock(colourBlock, 0);
    }

    /// <summary>Draw <paramref name="emblem"/> on the patch on the cape's back (its second material), or nothing there
    /// when null. The picture is one shared texture per emblem; only this wearer's drawing points at it.</summary>
    public void ShowEmblem(Texture2D emblem)
    {
        if (shown == null) return;
        var materials = shown.sharedMaterials;
        if (materials.Length < 2) return;
        if (nothing == null)
        {
            nothing = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "no emblem" };
            nothing.SetPixel(0, 0, Color.clear);
            nothing.Apply(false, true);
        }
        if (emblemBlock == null) emblemBlock = new MaterialPropertyBlock();
        emblemBlock.SetTexture(BaseMap, emblem != null ? emblem : nothing);
        shown.SetPropertyBlock(emblemBlock, 1);
    }

    /// <summary>The wearer's upper arms, shoulder joint and elbow on each side, which the cloth is pushed out of.</summary>
    public void PushedBy(Transform leftShoulder, Transform leftElbowJoint, Transform rightShoulder, Transform rightElbowJoint)
    {
        leftArm = leftShoulder;
        leftElbow = leftElbowJoint;
        rightArm = rightShoulder;
        rightElbow = rightElbowJoint;
    }

    void LateUpdate()
    {
        int n = chain.Length;
        if (n == 0 || tips.Length != n || rest == null) return;
        if (shown != null && !shown.isVisible)
        {
            if (live)
            {
                for (int i = 0; i < n; i++) chain[i].localRotation = rest[i];
                live = false;
            }
            return;
        }

        float dt = Mathf.Min(Time.deltaTime, BackPieceSway.LongestStep);
        var self = transform;

        float arm = armRadius * self.lossyScale.x;
        bool arms = leftArm != null && leftElbow != null && rightArm != null && rightElbow != null && arm > 0f;
        for (int i = 0; i < n; i++)
        {
            var bone = chain[i];
            bone.localRotation = rest[i];
            Vector3 pivot = bone.position;
            Vector3 goal = bone.TransformPoint(tips[i]);
            float length = (goal - pivot).magnitude;
            if (!live || (point[i] - goal).sqrMagnitude > length * length * BackPieceSway.SnapAfterLengths)
            {
                point[i] = goal;
                speed[i] = Vector3.zero;
            }

            BackPieceSway.Step(ref point[i], ref speed[i], goal, dt);
            point[i] = BackPieceSway.KeepLength(pivot, point[i], length);

            // Never in front of the wearer's back, measured in this piece's own space, where the bake wrote it.
            Vector3 local = self.InverseTransformPoint(point[i]);
            float behind = Vector3.Dot(local, backward);
            if (behind < backClearance)
            {
                local += backward * (backClearance - behind);
                point[i] = BackPieceSway.KeepLength(pivot, self.TransformPoint(local), length);
            }

            if (arms)
            {
                Vector3 clear = BackPieceSway.OutOfCapsule(point[i], leftArm.position, leftElbow.position, arm);
                clear = BackPieceSway.OutOfCapsule(clear, rightArm.position, rightElbow.position, arm);
                point[i] = BackPieceSway.KeepLength(pivot, BackPieceSway.EaseToward(point[i], clear, dt), length);
            }

            Vector3 from = goal - pivot, to = point[i] - pivot;
            if (from.sqrMagnitude > 1e-12f && to.sqrMagnitude > 1e-12f)
                bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
        }
        live = true;
    }

    /// <summary>True for a renderer that belongs to a worn back piece rather than to the body under it.</summary>
    public static bool IsPiece(Renderer r) => r != null && r.GetComponentInParent<BackPiece>(true) != null;
}

/// <summary>The sums that swing a worn back piece, kept apart from the component so they can be checked by calling
/// them with numbers. All in world units and seconds.</summary>
public static class BackPieceSway
{
    /// <summary>How hard a tip is pulled back toward where the animation puts it, per second squared per unit away.</summary>
    public const float Stiffness = 1250f;
    /// <summary>How fast a tip's swing dies away, per second. A tip trails a wearer moving at a steady pace by pace ×
    /// Damping ÷ Stiffness — 0.034 s of movement here, a third of the first cape's 0.1 s (140 and 14) — while the
    /// two keep the first cape's settling (Damping ÷ 2√Stiffness ≈ 0.59), which never swung side to side.</summary>
    public const float Damping = 42f;
    /// <summary>The longest frame the spring is stepped over, so a hitch cannot fling the cloth.</summary>
    public const float LongestStep = 1f / 30f;
    /// <summary>How quickly an arm pushes the cloth aside, per second. Moved out in one frame, a swinging arm knocked the
    /// cloth by up to 7.3 degrees a frame at a walk and 16.6 at a run; eased at this rate, 2.0 and 9.8 — a run's own swing
    /// with no arm checks at all is 9.8 (measured 2026-10-16 by replaying the walk and run over three seconds).</summary>
    public const float ArmPushRate = 8f;

    /// <summary><paramref name="point"/> moved part of the way to <paramref name="clear"/> over one step of
    /// <paramref name="dt"/> seconds, at <see cref="ArmPushRate"/>.</summary>
    public static Vector3 EaseToward(Vector3 point, Vector3 clear, float dt)
        => Vector3.Lerp(point, clear, 1f - Mathf.Exp(-ArmPushRate * dt));

    /// <summary>A tip further than this many bone lengths (squared) from where it belongs — after a teleport — snaps
    /// straight back instead of swinging across the map.</summary>
    public const float SnapAfterLengths = 9f;

    /// <summary>The longest single step of the spring. One step only holds while Stiffness × step² + 2 × Damping × step
    /// stays under 4 — a step of 1/31 s or longer breaks it, every step overshooting the last, and the cloth whips back
    /// and forth each frame (measured 2026-10-17: a nudge settles at 35 frames a second and grows a millionfold in ten
    /// seconds at 30). A longer frame is walked in equal steps no longer than this.</summary>
    public const float LongestSpringStep = 1f / 120f;

    /// <summary>The damped spring pulling <paramref name="point"/> toward <paramref name="goal"/> over <paramref name="dt"/>
    /// seconds, in as many equal steps of at most <see cref="LongestSpringStep"/> as that takes.</summary>
    public static void Step(ref Vector3 point, ref Vector3 speed, Vector3 goal, float dt)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / LongestSpringStep - 1e-4f));
        // the 1e-4 keeps a 1/60 s frame at two steps when float rounding lands it a hair over 2.0
        float h = dt / steps;
        for (int s = 0; s < steps; s++)
        {
            speed += ((goal - point) * Stiffness - speed * Damping) * h;
            point += speed * h;
        }
    }

    /// <summary><paramref name="point"/> pushed straight out of the capsule round the segment <paramref name="a"/>–
    /// <paramref name="b"/> with radius <paramref name="radius"/>; unchanged when it is already outside.</summary>
    public static Vector3 OutOfCapsule(Vector3 point, Vector3 a, Vector3 b, float radius)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        float t = len2 > 1e-12f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / len2) : 0f;
        Vector3 near = a + ab * t;
        Vector3 out_ = point - near;
        float d2 = out_.sqrMagnitude;
        if (d2 >= radius * radius || d2 < 1e-14f) return point;
        return near + out_ * (radius / Mathf.Sqrt(d2));
    }

    /// <summary><paramref name="point"/> moved along the line from <paramref name="pivot"/> to sit exactly
    /// <paramref name="length"/> from it, so the cloth swings but never stretches.</summary>
    public static Vector3 KeepLength(Vector3 pivot, Vector3 point, float length)
    {
        Vector3 d = point - pivot;
        float m = d.magnitude;
        return m > 1e-9f ? pivot + d * (length / m) : point;
    }
}
