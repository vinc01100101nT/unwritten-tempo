using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// THE one recipe for the player's live-3D body: fit a rigged model to the shared body height, put it on the shared
/// Humanoid controller, and hang the player's rig on it (pose sink, stance choke point, weapon socket, outfit dye,
/// x-ray silhouette). Both callers use this and neither re-implements it — the author-time build
/// (<c>Player3DVisualBuilder</c>, which also builds the controller asset and saves the scene) and the in-game swap
/// (<see cref="Wear"/>, which loads the model by name from <see cref="CharacterModels"/>).
///
/// <para><b>Why this is not <see cref="Character3DBody"/>.</b> That one builds a body as a CHILD of a unit's root —
/// right for townsfolk and shadow mobs, whose root carries the mover. The player's visual is a SEPARATE scene object
/// that follows the player (<see cref="Character3DVisual"/> drives it), because the player's root carries a
/// CharacterController that must stay unscaled and unencumbered. Two structures, deliberately: what they share —
/// the height, the controller, the stance machinery — is read from the same places by both.</para>
///
/// <para>The caller creates the instance, because that genuinely differs: at author time it must be a PREFAB
/// instance (so the scene saves a link, not a copy), in Play it is a plain clone. Everything after that instant is
/// here.</para>
/// </summary>
public static class Player3DBody
{
    /// <summary>The model name the player is currently wearing, or null when nothing has swapped it this session
    /// (it wears whatever body its scene was built with).</summary>
    public static string Worn { get; private set; }

    /// <summary>Put the player in the roster model called <paramref name="modelName"/>, fetching it if this is the
    /// first time. Works in a build — this is the in-game path, and the only one that does not need the editor.
    /// <paramref name="onDone"/> reports the outcome: the model name on success, null when there is no such model
    /// (or no player to dress).</summary>
    public static void Wear(string modelName, System.Action<string> onDone = null)
    {
        // SAID OUT LOUD WHEN IT CANNOT. Asked while the player is switched off, this used to do nothing at all, and
        // every character entered the world in the default body with nothing anywhere saying why.
        var player = Object.FindAnyObjectByType<Player3DController>();
        if (player == null)
        {
            GameLog.Warn(LogCat.Client, "could not put on '" + modelName + "' — there is no player to dress yet.");
            onDone?.Invoke(null);
            return;
        }

        CharacterModels.Load(modelName, model =>
        {
            if (model == null)
            {
                GameLog.Warn(LogCat.Client, "could not put on '" + modelName + "' — no character model by that name.");
                onDone?.Invoke(null);
                return null;
            }
            if (player == null) { onDone?.Invoke(null); return null; }   // player went away while the model loaded

            var built = Configure(player, Object.Instantiate(model), modelName,
                                  CharacterSpec.Active.animatorController);
            if (built != null)
            {
                Worn = modelName;
                GameLog.Info(LogCat.Client, "wearing '" + modelName + "'");
            }
            onDone?.Invoke(built != null ? modelName : null);
            return built;
        });
    }

    /// <summary>Dress <paramref name="instance"/> (a freshly created copy of a rigged model) as the player's body and
    /// retire whatever body the player was wearing. Returns the new visual.
    ///
    /// <para>The new body is fully built BEFORE the old one is destroyed, so a failure part-way leaves the player
    /// wearing the body they already had rather than standing bare.</para></summary>
    /// <param name="avatar">The model's own Humanoid avatar. Null → taken from the instance's Animator, which is
    /// where an imported model carries it.</param>
    public static GameObject Configure(Player3DController player, GameObject instance, string charName,
                                       RuntimeAnimatorController controller, Avatar avatar = null)
    {
        if (player == null || instance == null) return null;

        // Snapshot the visuals to replace, but DON'T remove them yet — build the new one FIRST. Enumerated with
        // FindObjectsOfTypeAll, not FindObjectsByType: a body worn in Play is marked not-to-be-saved below, and
        // FindObjectsByType skips exactly those — so the body being replaced was never found and never removed.
        // It also lists prefab assets on disk, which are not in any scene; those are skipped.
        var toRemove = Resources.FindObjectsOfTypeAll<Character3DVisual>();

        instance.name = "Player3DVisual (" + charName + ")";
        if (Application.isPlaying)
        {
            instance.hideFlags = HideFlags.DontSaveInEditor;   // a Play-time swap must never bake into a saved scene
            QuitTeardown.Register(instance);
        }
        SceneManager.MoveGameObjectToScene(instance, player.gameObject.scene);
        instance.transform.position = player.transform.position;

        // Fit to the shared body height, so the player stands exactly as tall as the townsfolk and the shadow mobs.
        float nativeH = Character3DBody.MeasureHeight(instance);
        instance.transform.localScale = Vector3.one * (nativeH > 1e-3f ? Character3DBody.BodyHeight / nativeH : 1f);

        var anim = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
        if (controller != null) anim.runtimeAnimatorController = controller;
        var rigAvatar = avatar != null ? avatar : anim.avatar;
        if (rigAvatar != null) anim.avatar = rigAvatar;
        anim.applyRootMotion = false;

        if (instance.GetComponent<Character3DPose>() == null) instance.AddComponent<Character3DPose>();
        if (instance.GetComponent<StanceController>() == null) instance.AddComponent<StanceController>();
        var vis = instance.GetComponent<Character3DVisual>() ?? instance.AddComponent<Character3DVisual>();
        vis.player = player;
        var hand = instance.GetComponent<WeaponSocket>();
        if (hand == null) hand = instance.AddComponent<WeaponSocket>();
        // The model is not a child of the avatar, so a thrown weapon finds this hand by being told which one it is.
        WeaponSocket.MarkMine(hand);
        if (instance.GetComponent<CharacterDye3D>() == null) instance.AddComponent<CharacterDye3D>();   // per-part outfit dye
        // Hair and headgear — this machine's own character's, the same socket every body wears.
        var head = instance.GetComponent<HeadSocket>();
        if (head == null) head = instance.AddComponent<HeadSocket>();
        HeadSocket.MarkMine(head);
        // The Garment this machine's own character wears, on its back.
        if (instance.GetComponent<GarmentSocket>() == null) instance.AddComponent<GarmentSocket>();
        // The Costume Body this machine's own character wears, swapped in for the plain body.
        if (instance.GetComponent<CostumeSocket>() == null) instance.AddComponent<CostumeSocket>();
        // The x-ray occlusion silhouette flags THIS object's renderers (the visible model) — it lives here, not on
        // the player root, because the model is a SEPARATE object riding the controller (the root only holds the
        // hidden sprite). A model swap rebuilds this object, so the fresh renderers are re-tagged automatically.
        if (instance.GetComponent<OccludedSilhouetteTarget>() == null) instance.AddComponent<OccludedSilhouetteTarget>();

        // New visual is fully built — now retire the old one(s). (Skip the new instance in case it was already matched.)
        foreach (var v in toRemove)
            if (v != null && v.gameObject != instance && v.gameObject.scene.IsValid()) Object.DestroyImmediate(v.gameObject);

        return instance;
    }
}
