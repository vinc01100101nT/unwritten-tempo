using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A live 3D look at a character body, rendered into a UI panel — what character creation shows so a player
/// picks a body by SEEING it rather than by reading its name.
///
/// <para>The body is a real model from the shared roster (<see cref="CharacterModels"/>), so the preview can
/// never show something the game would not draw. It is built far above the world and filmed by its own camera,
/// which is why nothing from the map leaks into the shot and nothing in the shot touches the map.</para>
///
/// <para>Everything it makes is marked never-save and destroyed with it: the staging rig, the camera, its light
/// and the picture it renders into. Leaving any of them behind is how a runtime-only object ends up baked into
/// a saved scene.</para>
/// </summary>
public class CharacterPreview : MonoBehaviour
{
    // Far enough above the map that no world geometry, weather or light can wander into frame.
    static readonly Vector3 Stage = new Vector3(0f, 5000f, 0f);

    const int Size = 512;

    Camera cam;
    RenderTexture picture;
    Transform stageRoot;
    GameObject body;
    CharacterDye3D dye;
    string shownModel;

    /// <summary>Raised once a new body is standing and its recolourable parts are known, so a colour picker can
    /// rebuild for the body actually on show rather than the one before it.</summary>
    public event System.Action OnBodyChanged;

    /// <summary>The parts of the body on show that can be recoloured, or an empty list before one is up.</summary>
    public System.Collections.Generic.IReadOnlyList<Color> Parts =>
        dye != null ? dye.DetectableParts() : System.Array.Empty<Color>();

    /// <summary>Recolour one part of the body on show. Nothing is persisted — this preview has no character
    /// behind it, so the dye lives only on the model in the frame until the character is created.</summary>
    public void SetPartColor(Color source, Color target)
    {
        if (dye != null) dye.SetPartColor(source, target);
    }

    /// <summary>What a part is currently showing as.</summary>
    public Color TargetFor(Color source) => dye != null ? dye.TargetFor(source) : source;

    /// <summary>Build a preview and put its picture into <paramref name="into"/>. It lives under
    /// <paramref name="owner"/>, so closing the screen that opened it takes the camera, the light and the
    /// picture with it — a preview that outlived its panel would keep rendering, unseen, forever.</summary>
    public static CharacterPreview Create(RawImage into, Transform owner)
    {
        var go = new GameObject("CharacterPreview") { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.SetParent(owner, false);
        var preview = go.AddComponent<CharacterPreview>();
        preview.Build(into);
        return preview;
    }

    void Build(RawImage into)
    {
        stageRoot = new GameObject("Stage") { hideFlags = HideFlags.DontSaveInEditor }.transform;
        stageRoot.SetParent(transform, false);
        stageRoot.position = Stage;

        picture = new RenderTexture(Size, Size, 16) { name = "CharacterPreview" };

        var camGo = new GameObject("Camera") { hideFlags = HideFlags.DontSaveInEditor };
        camGo.transform.SetParent(stageRoot, false);
        cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.white;   // a plain white studio, so a body reads as itself and not as a silhouette
        cam.orthographic = false;
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 5f;      // tight, so only the staged body can possibly be in shot
        cam.targetTexture = picture;

        // Slightly above eye level looking down a little, which reads as a portrait rather than a floor plan.
        float height = Character3DBody.BodyHeight;
        camGo.transform.localPosition = new Vector3(0f, height * 0.62f, -height * 2.6f);
        camGo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);

        var lightGo = new GameObject("Light") { hideFlags = HideFlags.DontSaveInEditor };
        lightGo.transform.SetParent(stageRoot, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.color = new Color(1f, 0.97f, 0.9f);
        lightGo.transform.localRotation = Quaternion.Euler(35f, 160f, 0f);
        light.cullingMask = ~0;

        // A second, softer light from the other side. One light leaves half of a body in darkness, which reads
        // as "this model is broken" rather than "this side is unlit".
        var fillGo = new GameObject("Fill") { hideFlags = HideFlags.DontSaveInEditor };
        fillGo.transform.SetParent(stageRoot, false);
        var fillLight = fillGo.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.intensity = 0.6f;
        fillLight.color = new Color(0.85f, 0.9f, 1f);
        fillGo.transform.localRotation = Quaternion.Euler(20f, -40f, 0f);

        if (into != null)
        {
            into.texture = picture;
            into.color = Color.white;
        }
    }

    /// <summary>Show <paramref name="modelName"/>. Asking for the body already on stage does nothing, so paging
    /// back and forth through the roster does not rebuild the same body over and over.</summary>
    public void Show(string modelName)
    {
        if (string.IsNullOrEmpty(modelName) || modelName == shownModel) return;
        shownModel = modelName;

        CharacterModels.Load(modelName, model =>
        {
            if (this == null || model == null) return;
            if (modelName != shownModel) return;   // paged on while this one was loading — it is no longer wanted

            if (body != null) Destroy(body);
            dye = null;

            body = Instantiate(model, stageRoot);
            body.hideFlags = HideFlags.DontSaveInEditor;
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // face the camera

            // Fit to the same height every character in the game stands at, so paging bodies doesn't make them
            // jump in size — measured at scale one, exactly as the shared body builder does it.
            body.transform.localScale = Vector3.one;
            float native = Character3DBody.MeasureHeight(body);
            if (native > 1e-3f) body.transform.localScale = Vector3.one * (Character3DBody.BodyHeight / native);

            // The shared Humanoid controller, so the body stands in its authored idle instead of a T-pose.
            var animator = body.GetComponent<Animator>() ?? body.AddComponent<Animator>();
            animator.runtimeAnimatorController = CharacterSpec.Active.animatorController;
            animator.applyRootMotion = false;

            // The SAME recolouring the wardrobe uses in game. With no live character to bind to it applies the
            // colours straight to the model in front of us and persists nothing, which is exactly what a preview
            // wants — the choice is only written down when the character is actually created.
            dye = body.AddComponent<CharacterDye3D>();
            OnBodyChanged?.Invoke();
        });
    }

    void OnDestroy()
    {
        if (cam != null) cam.targetTexture = null;
        if (picture != null) { picture.Release(); Destroy(picture); }
    }
}
