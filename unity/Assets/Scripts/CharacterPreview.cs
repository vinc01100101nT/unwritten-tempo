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
    string shownModel;

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
        cam.backgroundColor = new Color(0.07f, 0.075f, 0.09f, 1f);   // the same charcoal an empty slot uses
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
        light.intensity = 1.1f;
        light.color = new Color(1f, 0.97f, 0.9f);
        lightGo.transform.localRotation = Quaternion.Euler(35f, 160f, 0f);
        light.cullingMask = ~0;

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
        });
    }

    void OnDestroy()
    {
        if (cam != null) cam.targetTexture = null;
        if (picture != null) { picture.Release(); Destroy(picture); }
    }
}
