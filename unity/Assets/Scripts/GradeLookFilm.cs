using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE GRADE LOOKS ON A PICTURE in a window. Windows are drawn flat over the world and a particle effect cannot draw
/// into them, so every look (<see cref="GradeGlow"/>) — the 15 weapon enchants (5 themes × 3 levels) and the armour
/// aura at its 6 grades — is built once on a stage far from every map and filmed by one camera into one shared
/// picture, a cell per look. Every item picture that needs a look shows its cell of that picture
/// (<see cref="GradeLookWatcher"/>).
///
/// <para>The looks stand <see cref="Pitch"/> cells apart, so the widest one (Water Lvl 3's orb, 3 times its weapon)
/// never reaches into a neighbour's cell. One camera for all of them: measured 2026-10-18 in the editor, 21 cameras
/// (one per look) took 10.6 ms a frame against 0.6 ms for one.</para>
///
/// <para><b>Cost.</b> One 864×576 picture (2 MB, plus 1 MB of depth), fixed. Per frame, only while at least one
/// picture showing a look is on screen: the camera renders once, and only the looks currently shown animate — a look
/// shown by a hundred pictures animates once. Measured 2026-10-18 in the editor with all 21 looks on: 0.23 ms of
/// animation and 0.6 ms for the camera's render a frame. Nothing shown: the camera is off and every look is switched off.</para>
/// </summary>
public static class GradeLookFilm
{
    public const int Looks = 21;
    const int Cols = 6, Rows = 4, CellPixels = 96;
    // How far apart the looks stand, in cells; the shown cell is the one in the middle of each.
    const float Pitch = 1.5f;
    // The first aura cell; the enchants fill the cells before it.
    const int FirstAura = 15;
    // The stage: far from every map and from the character preview's stage, and drawn by nothing but this camera.
    static readonly Vector3 StagePosition = new Vector3(5000f, 5000f, 5000f);
    const int StageLayer = 31;
    // How much of a cell a look's weapon or body takes, so what streams off it stays inside its own cell.
    const float LookShare = 0.55f;

    static RenderTexture film;
    static Camera cam;
    static Transform stage;
    static readonly Transform[] cells = new Transform[Looks];
    static readonly GameObject[] built = new GameObject[Looks];
    static readonly int[] watchers = new int[Looks];
    static int watching;

    // A play session started without reloading scripts keeps the last session's counts; the stage they counted is gone.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ForgetLastSession()
    {
        film = null; cam = null; stage = null; watching = 0;
        System.Array.Clear(cells, 0, Looks);
        System.Array.Clear(built, 0, Looks);
        System.Array.Clear(watchers, 0, Looks);
    }

    /// <summary>Which look <paramref name="copy"/> wears: its weapon's enchant, or a Body, Garment or Shoes piece's
    /// aura, at its grade. -1 for none — Raw, no theme, or anything else.</summary>
    public static int LookOf(EquipmentInstance copy) => copy == null ? -1 : LookOf(copy.def, copy.quality);

    /// <summary>The same, for a piece known only by its item and its <paramref name="grade"/> — what a machine that
    /// never holds the copy is told.</summary>
    public static int LookOf(Item def, Quality grade)
    {
        if (def == null || !GradeGlow.Glows(grade)) return -1;
        if (GradeGlow.InSet(def.slot)) return FirstAura + (int)grade - 1;
        var theme = GradeGlow.EnchantTheme(def.weaponType);
        int level = GradeGlow.EnchantLevel(grade);
        if (theme == GradeGlow.Enchant.None || level == 0) return -1;
        return ((int)theme - 1) * 3 + level - 1;
    }

    /// <summary>How opaque <paramref name="look"/> is drawn over a picture: its enchant theme's
    /// (<see cref="GradeGlow.PictureOpacity"/>), and full for the armour aura.</summary>
    public static float Opacity(int look) =>
        look < 0 || look >= FirstAura ? 1f : GradeGlow.PictureOpacity((GradeGlow.Enchant)(look / 3 + 1));

    /// <summary>The shared picture, built on first use. Null on a server, which draws nothing.</summary>
    public static Texture Film => Ensure() ? film : null;

    /// <summary>Where <paramref name="look"/>'s cell lies in <see cref="Film"/>, as the part of it a picture shows.</summary>
    public static Rect Cell(int look)
    {
        float wide = Cols * Pitch, tall = Rows * Pitch;
        return new Rect(((look % Cols + 0.5f) * Pitch - 0.5f) / wide, (tall - (look / Cols + 0.5f) * Pitch - 0.5f) / tall,
                        1f / wide, 1f / tall);
    }

    /// <summary>One more picture is showing <paramref name="look"/>.</summary>
    public static void Watch(int look)
    {
        if (look < 0 || look >= Looks || !Ensure()) return;
        if (watchers[look]++ == 0) Light(look, true);
        if (watching++ == 0) cam.enabled = true;
    }

    /// <summary>One picture fewer is showing <paramref name="look"/>.</summary>
    public static void Unwatch(int look)
    {
        if (look < 0 || look >= Looks || watchers[look] <= 0) return;
        if (--watchers[look] == 0) Light(look, false);
        if (--watching == 0 && cam != null) cam.enabled = false;
    }

    static bool Ensure()
    {
        if (cam != null) return true;
        if (!Application.isPlaying || Application.isBatchMode) return false;

        var root = new GameObject("GradeLookStage") { hideFlags = HideFlags.DontSaveInEditor };
        Object.DontDestroyOnLoad(root);
        stage = root.transform;
        stage.position = StagePosition;
        for (int i = 0; i < Looks; i++)
        {
            var cell = new GameObject("Look " + i) { hideFlags = HideFlags.DontSaveInEditor, layer = StageLayer };
            cells[i] = cell.transform;
            cells[i].SetParent(stage, false);
            cells[i].localPosition = new Vector3((i % Cols + 0.5f - Cols * 0.5f) * Pitch, (Rows * 0.5f - i / Cols - 0.5f) * Pitch, 0f);
            cell.SetActive(false);
        }

        film = new RenderTexture(Mathf.RoundToInt(Cols * Pitch * CellPixels), Mathf.RoundToInt(Rows * Pitch * CellPixels), 16,
                                 RenderTextureFormat.ARGB32) { name = "GradeLookFilm" };
        var camGo = new GameObject("Camera") { hideFlags = HideFlags.DontSaveInEditor };
        camGo.transform.SetParent(stage, false);
        camGo.transform.localPosition = new Vector3(0f, 0f, -10f);
        cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = Rows * Pitch * 0.5f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 20f;
        cam.cullingMask = 1 << StageLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.targetTexture = film;
        cam.enabled = false;
        return true;
    }

    // Switch a look on (built the first time) or off, so a look nobody shows costs nothing.
    static void Light(int look, bool on)
    {
        var cell = cells[look];
        if (cell == null) return;
        if (on && built[look] == null)
        {
            built[look] = look >= FirstAura
                ? GradeGlow.MakeSetAura(cell, new Vector3(0f, -LookShare * 0.5f, 0f), LookShare, (Quality)(look - FirstAura + 1))
                : GradeGlow.MakeThemeEnchant(cell, new Bounds(Vector3.zero, Vector3.one * LookShare), LookShare,
                                             (GradeGlow.Enchant)(look / 3 + 1), look % 3 + 1);
            if (built[look] != null)
                foreach (var t in built[look].GetComponentsInChildren<Transform>(true))   // perf-ok: once per look, ever
                    t.gameObject.layer = StageLayer;
        }
        if (cell.gameObject.activeSelf != on) cell.gameObject.SetActive(on);
    }
}

/// <summary>A picture showing one look's cell of <see cref="GradeLookFilm.Film"/>. It counts as watching that look
/// only while it is switched on, so a closed window stops costing anything.</summary>
[RequireComponent(typeof(RawImage))]
public class GradeLookWatcher : MonoBehaviour
{
    int look = -1;
    bool counted;

    /// <summary>Show <paramref name="next"/> (-1 for none, which switches the picture off).</summary>
    public void Set(int next)
    {
        if (next != look)
        {
            if (counted) { GradeLookFilm.Unwatch(look); counted = false; }
            look = next;
            if (look >= 0)
            {
                var img = GetComponent<RawImage>();
                if (img.texture == null) img.texture = GradeLookFilm.Film;
                img.uvRect = GradeLookFilm.Cell(look);
                img.color = new Color(1f, 1f, 1f, GradeLookFilm.Opacity(look));
            }
        }
        bool on = look >= 0;
        if (gameObject.activeSelf != on) gameObject.SetActive(on);
        if (on && isActiveAndEnabled && !counted) { GradeLookFilm.Watch(look); counted = true; }
    }

    void OnEnable()
    {
        if (look >= 0 && !counted) { GradeLookFilm.Watch(look); counted = true; }
    }

    void OnDisable()
    {
        if (counted) { GradeLookFilm.Unwatch(look); counted = false; }
    }
}
