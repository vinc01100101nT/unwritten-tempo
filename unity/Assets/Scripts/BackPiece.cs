using UnityEngine;

/// <summary>
/// A piece worn on a character's upper back — the model of a Garment item, such as a cape — made by
/// <c>BackPieceBaker</c>. It sits on the root of the piece's prefab; <see cref="GarmentSocket"/> puts it on, fixed to
/// the body's upper-back bone at that body's resting offset, the way a hat is fixed to the head.
///
/// <para><b>How it flies.</b> The cloth is lifted behind the wearer by how fast they are going — a little at a walk,
/// nearly level at a sprint — and kicked by starting and stopping (<see cref="BackPieceSway.LiftDegrees"/>).</para>
///
/// <para><b>How it sways.</b> The piece carries its own short chains of bones — down the left side, the middle of the
/// back and the right side. Every frame each bone's tip is pulled by a damped spring toward where the body's animation
/// puts it, so the cloth trails when the wearer runs, swings back when they stop and follows when they turn. It is then
/// kept behind the wearer's back, and pushed out of both upper arms and the worn weapon, so an arm or a blade bulges
/// the cloth instead of passing through it. The sums are <see cref="BackPieceSway"/>.</para>
///
/// <para><b>What it costs.</b> One spring step, two arm checks and up to two weapon checks per bone per frame — twelve
/// bones for the cape — and
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

    [Tooltip("How many bones each column has, top to bottom — the chain is stored column by column.")]
    public int rows = 4;

    Transform leftArm, leftElbow, rightArm, rightElbow;
    Vector3 lastRoot, lastStep, travel;
    WeaponSocket weapons;

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

    /// <summary>What the cloth is pushed out of: the wearer's upper arms (shoulder joint and elbow on each side), and the
    /// weapon they wear, in the hand or put away.</summary>
    public void PushedBy(Transform leftShoulder, Transform leftElbowJoint, Transform rightShoulder, Transform rightElbowJoint,
                         WeaponSocket worn)
    {
        weapons = worn;
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

        // HOW FAST THE WEARER IS GOING, measured from where the cape itself moved, so it works the same for every
        // body: your own, another player's, anyone's. The cloth is carried along with that movement (so a steady pace
        // drags nothing), kicked back only by a change of pace, and lifted behind by the pace itself.
        Vector3 root = self.position;
        Vector3 step = live ? root - lastRoot : Vector3.zero;
        if (step.sqrMagnitude > BackPieceSway.TeleportStep * BackPieceSway.TeleportStep) { step = Vector3.zero; live = false; }
        Vector3 kick = (step - lastStep) * BackPieceSway.StartStopKick;
        lastRoot = root;
        lastStep = step;
        Vector3 flat = dt > 0f ? new Vector3(step.x, 0f, step.z) / Mathf.Max(Time.deltaTime, 1e-4f) : Vector3.zero;
        travel = Vector3.Lerp(travel, flat, 1f - Mathf.Exp(-BackPieceSway.PaceSmoothing * dt));
        float pace = travel.magnitude;
        float lift = BackPieceSway.LiftDegrees(pace, CharacterSpec.Active.FullSpeed);
        Vector3 behindMe = pace > 1e-4f ? -travel / pace : Vector3.zero;
        float arm = armRadius * self.lossyScale.x;
        bool arms = leftArm != null && leftElbow != null && rightArm != null && rightElbow != null && arm > 0f;
        bool blade0 = false, blade1 = false;
        Vector3 b0From = default, b0To = default, b1From = default, b1To = default;
        float b0Radius = 0f, b1Radius = 0f;
        if (weapons != null)
        {
            blade0 = weapons.WeaponShape(0, out b0From, out b0To, out b0Radius);
            blade1 = weapons.WeaponShape(1, out b1From, out b1To, out b1Radius);
        }
        for (int i = 0; i < n; i++)
        {
            var bone = chain[i];
            bone.localRotation = rest[i];
            Vector3 pivot = bone.position;
            Vector3 goal = bone.TransformPoint(tips[i]);
            float length = (goal - pivot).magnitude;
            if (lift > 0f)
                goal = pivot + Vector3.RotateTowards(goal - pivot, behindMe * length,
                                                     lift * BackPieceSway.RowShare(i % rows, rows) * Mathf.Deg2Rad, 0f);
            point[i] += step - kick;
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
                point[i] = BackPieceSway.OutOfCapsule(point[i], leftArm.position, leftElbow.position, arm);
                point[i] = BackPieceSway.OutOfCapsule(point[i], rightArm.position, rightElbow.position, arm);
                point[i] = BackPieceSway.KeepLength(pivot, point[i], length);
            }
            if (blade0 || blade1)
            {
                if (blade0) point[i] = BackPieceSway.OutOfCapsule(point[i], b0From, b0To, b0Radius);
                if (blade1) point[i] = BackPieceSway.OutOfCapsule(point[i], b1From, b1To, b1Radius);
                point[i] = BackPieceSway.KeepLength(pivot, point[i], length);
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
    public const float Stiffness = 140f;
    /// <summary>How fast a tip's swing dies away, per second.</summary>
    public const float Damping = 14f;
    /// <summary>The longest frame the spring is stepped over, so a hitch cannot fling the cloth.</summary>
    public const float LongestStep = 1f / 30f;
    /// <summary>A tip further than this many bone lengths (squared) from where it belongs — after a teleport — snaps
    /// straight back instead of swinging across the map.</summary>
    public const float SnapAfterLengths = 9f;

    /// <summary>How far, in degrees, the cloth is lifted behind a wearer going at <paramref name="pace"/>: a little at a
    /// walk, rising faster than the pace, up to <see cref="SprintLift"/> at <paramref name="sprintPace"/> and above.</summary>
    public static float LiftDegrees(float pace, float sprintPace)
    {
        if (sprintPace <= 0f) return 0f;
        return SprintLift * Mathf.Pow(Mathf.Clamp01(pace / sprintPace), LiftCurve);
    }

    /// <summary>The share of the lift row <paramref name="row"/> of <paramref name="rows"/> takes: the top bends most, so
    /// the cloth rises from the shoulders and the rest follows. The shares add up to one.</summary>
    public static float RowShare(int row, int rows)
    {
        if (rows <= 1) return 1f;
        float total = rows * (rows + 1) * 0.5f;
        return (rows - row) / total;
    }

    /// <summary>The lift at a sprint, in degrees from hanging straight down.</summary>
    public const float SprintLift = 75f;
    /// <summary>How sharply the lift rises with pace: 1.5 gives about 7 degrees at a walk (0.4), 26 at a run (1) and the
    /// full lift at a sprint (2), with the walk, run and sprint speeds measured from CharacterSpec on 2026-10-16.</summary>
    public const float LiftCurve = 1.5f;
    /// <summary>How quickly the measured pace follows the real one, per second — smooths out the body's bobbing.</summary>
    public const float PaceSmoothing = 8f;
    /// <summary>How hard a change of pace kicks the cloth back (starting) or forward (stopping): this many times the
    /// change in one frame's movement.</summary>
    public const float StartStopKick = 2f;
    /// <summary>A move longer than this in one frame, in world units, is a teleport: the cloth starts again from rest.</summary>
    public const float TeleportStep = 1f;

    /// <summary>One step of the damped spring pulling <paramref name="point"/> toward <paramref name="goal"/>.</summary>
    public static void Step(ref Vector3 point, ref Vector3 speed, Vector3 goal, float dt)
    {
        speed += ((goal - point) * Stiffness - speed * Damping) * dt;
        point += speed * dt;
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
