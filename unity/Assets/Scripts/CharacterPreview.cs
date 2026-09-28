using UnityEngine;
using UnityEngine.EventSystems;
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
    Transform bodyPivot;
    GameObject body;
    CharacterDye3D dye;
    string shownModel;

    // How the body is being held right now. The camera and the lights never move sideways — turning the BODY
    // is what a hand expects when it drags a doll, and it also keeps the lighting steady while it turns.
    float turn, tilt, lean;
    float zoom = 1f;

    const float TiltLimit = 60f;     // past this the body is being looked at from above or below, not turned
    const float ZoomIn = 0.45f;
    const float ZoomOut = 1.9f;

    /// <summary>Raised once a new body is standing and its recolourable parts are known, so a colour picker can
    /// rebuild for the body actually on show rather than the one before it.</summary>
    public event System.Action OnBodyChanged;

    /// <summary>The parts of the body on show that can be recoloured, or an empty list before one is up.</summary>
    public System.Collections.Generic.IReadOnlyList<Color> Parts =>
        dye != null ? dye.DetectableParts() : System.Array.Empty<Color>();

    /// <summary>Recolour one part of the body on show. Nothing is saved: the colours are held by the persistent
    /// character this screen sits on, stamped for this body, and only written down when the character is created.</summary>
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

        // The body hangs from a pivot at half its height, so tilting turns it about its middle. Hung from the
        // feet instead, a tilt swings the head right out of the frame.
        bodyPivot = new GameObject("BodyPivot") { hideFlags = HideFlags.DontSaveInEditor }.transform;
        bodyPivot.SetParent(stageRoot, false);
        bodyPivot.localPosition = new Vector3(0f, Character3DBody.BodyHeight * 0.5f, 0f);

        picture = new RenderTexture(Size, Size, 16) { name = "CharacterPreview" };

        var camGo = new GameObject("Camera") { hideFlags = HideFlags.DontSaveInEditor };
        camGo.transform.SetParent(stageRoot, false);
        cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.white;   // a plain white studio, so a body reads as itself and not as a silhouette
        cam.orthographic = false;
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.01f;
        // Only just past the furthest the camera can be pulled back to, so nothing but the staged body can be
        // in shot. Measured against the zoom limit rather than guessed, or a body vanishes when zoomed out.
        cam.farClipPlane = Character3DBody.BodyHeight * (2.6f * ZoomOut + 2f);
        cam.targetTexture = picture;

        // Slightly above eye level looking down a little, which reads as a portrait rather than a floor plan.
        camGo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
        ApplyView();

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
            into.raycastTarget = true;   // the picture IS the handle: it has to be able to catch a drag
            var hand = into.gameObject.AddComponent<PreviewHandle>();
            hand.preview = this;
        }
    }

    // ---- holding the body ------------------------------------------------------------------------------

    /// <summary>Turn the body left/right and tilt it forwards/backwards, in degrees.</summary>
    public void Turn(float turnDegrees, float tiltDegrees)
    {
        turn += turnDegrees;
        tilt = Mathf.Clamp(tilt + tiltDegrees, -TiltLimit, TiltLimit);
        ApplyView();
    }

    /// <summary>Lean the body over sideways, in degrees — the third axis a drag cannot reach on its own.</summary>
    public void Lean(float degrees)
    {
        lean += degrees;
        ApplyView();
    }

    /// <summary>Move the camera closer or further away. Positive comes closer.</summary>
    public void Zoom(float amount)
    {
        zoom = Mathf.Clamp(zoom - amount, ZoomIn, ZoomOut);
        ApplyView();
    }

    /// <summary>Back to the straight-on portrait the preview opens with.</summary>
    public void ResetView()
    {
        turn = tilt = lean = 0f;
        zoom = 1f;
        ApplyView();
    }

    void ApplyView()
    {
        if (bodyPivot != null) bodyPivot.localRotation = Quaternion.Euler(tilt, turn, lean);
        if (cam == null) return;
        float height = Character3DBody.BodyHeight;
        cam.transform.localPosition = new Vector3(0f, height * 0.62f * zoom, -height * 2.6f * zoom);
    }

    /// <summary>The picture's own hands. Left drag turns and tilts the body, right drag leans it and pushes the
    /// camera in and out, the wheel zooms, and a double-click puts it back the way it opened.</summary>
    class PreviewHandle : MonoBehaviour, IDragHandler, IScrollHandler, IPointerClickHandler
    {
        public CharacterPreview preview;

        const float TurnPerPixel = 0.55f;
        const float TiltPerPixel = 0.35f;
        const float LeanPerPixel = 0.4f;
        const float ZoomPerPixel = 0.005f;
        const float ZoomPerNotch = 0.09f;

        public void OnDrag(PointerEventData e)
        {
            if (preview == null) return;
            if (e.button == PointerEventData.InputButton.Left)
                preview.Turn(-e.delta.x * TurnPerPixel, -e.delta.y * TiltPerPixel);
            else
            {
                preview.Lean(e.delta.x * LeanPerPixel);
                preview.Zoom(e.delta.y * ZoomPerPixel);
            }
        }

        public void OnScroll(PointerEventData e)
        {
            if (preview != null) preview.Zoom(e.scrollDelta.y * ZoomPerNotch);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (preview != null && e.clickCount >= 2) preview.ResetView();
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

            body = Instantiate(model, bodyPivot);
            body.hideFlags = HideFlags.DontSaveInEditor;
            // Hung from the pivot's middle, so its feet still land on the stage floor.
            body.transform.localPosition = new Vector3(0f, -Character3DBody.BodyHeight * 0.5f, 0f);
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

            // The SAME recolouring the character wears in game. It binds to the persistent character that already
            // exists under this screen, so the world body is bound to the same colours — which is why a body only
            // SHOWS colours stamped for it and never erases them. Nothing is saved until the character is created.
            dye = body.AddComponent<CharacterDye3D>();
            ResetView();   // a body just paged to is shown as itself, not still held at the last one's angle
            OnBodyChanged?.Invoke();
        });
    }

    void OnDestroy()
    {
        if (cam != null) cam.targetTexture = null;
        if (picture != null) { picture.Release(); Destroy(picture); }
    }
}
