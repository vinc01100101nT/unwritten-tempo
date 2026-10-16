using UnityEngine;

/// <summary>
/// Puts the model of the Garment item a character wears — a cape — on its upper back. The back's sibling of
/// <see cref="HeadSocket"/>, worn the same way: your own avatar follows your own worn Garment, and another person's
/// body is told their worn set (<see cref="ShowGarmentOf"/>), which the server already sends.
///
/// <para><b>How it sits.</b> The piece is a child of the body's upper-back bone at that body's resting offset for the
/// bone (its bind pose), exactly as a hat sits on the head bone, so it follows the body for nothing. Its own sway is
/// <see cref="BackPiece"/>.</para>
///
/// <para>Nothing here runs on the server: a worn piece is a picture.</para>
/// </summary>
[DisallowMultipleComponent]
public class GarmentSocket : MonoBehaviour
{
    /// <summary>The upper-back bone a back piece hangs from. Every body in the cast shares this skeleton.</summary>
    public const string BackBone = "Spine2";

    SkinnedMeshRenderer body;
    GameObject shown, prefabShown;
    Texture2D emblem;
    CapeColours? colours;

    // THIS MACHINE'S OWN PERSON'S EMBLEM: set by their player shell, read by whichever avatar body is theirs now (a
    // model swap builds a new one), so a rebuilt avatar puts it straight back on.
    static Texture2D myEmblem;
    static CapeColours? myColours;
    static event System.Action MyEmblemChanged;

    /// <summary>The cape colours this machine's own person chose, or null for the cape's own — from their player shell.</summary>
    public static void ShowMyColours(CapeColours? chosen)
    {
        myColours = chosen;
        MyEmblemChanged?.Invoke();
    }

    /// <summary>The colours this machine's own person's cape shows now — theirs if chosen, else the worn cape's own;
    /// null when no cape is worn. What the colour boxes in the Emblem window start from.</summary>
    public static CapeColours? MyCapeColours()
    {
        if (myColours.HasValue) return myColours;
        var piece = mine != null && mine.shown != null ? mine.shown.GetComponent<BackPiece>() : null;
        return piece != null ? piece.OwnColours : (CapeColours?)null;
    }

    static GarmentSocket mine;

    /// <summary>Draw the cloth and trim in <paramref name="chosen"/> (null = the cape's own), now and on every cape worn
    /// after it.</summary>
    public void ShowColours(CapeColours? chosen)
    {
        colours = chosen;
        var piece = shown != null ? shown.GetComponent<BackPiece>() : null;
        if (piece != null) piece.ShowColours(colours);
    }

    /// <summary>The emblem this machine's own person wears, or null — from their player shell.</summary>
    public static void ShowMyEmblem(Texture2D texture)
    {
        myEmblem = texture;
        MyEmblemChanged?.Invoke();
    }

    /// <summary>Draw <paramref name="texture"/> on the worn cape's back (or nothing when null), now and on every cape
    /// worn after it.</summary>
    public void ShowEmblem(Texture2D texture)
    {
        emblem = texture;
        var piece = shown != null ? shown.GetComponent<BackPiece>() : null;
        if (piece != null) piece.ShowEmblem(emblem);
    }

    void MineChanged()
    {
        ShowEmblem(myEmblem);
        ShowColours(myColours);
    }

    bool told;
    UnitEquipment gear;
    Character subscribed;

    void Awake()
    {
        GameServer.RemovesPresentation(this);
    }

    /// <summary>Wear the Garment item <paramref name="worn"/> holds, followed whenever that worn set changes.</summary>
    public void ShowGarmentOf(UnitEquipment worn)
    {
        told = true;
        if (gear != null) gear.OnChanged -= Refresh;
        gear = worn;
        if (gear != null) gear.OnChanged += Refresh;
        Refresh();
    }

    void Start()
    {
        if (told) return;
        MyEmblemChanged += MineChanged;
        mine = this;
        emblem = myEmblem;
        colours = myColours;
        subscribed = Character.Instance;
        if (subscribed != null)
        {
            subscribed.OnAppearanceChanged += Refresh;
            subscribed.OnStatsChanged += Refresh;
        }
        Refresh();
    }

    void OnDestroy()
    {
        if (subscribed != null)
        {
            subscribed.OnAppearanceChanged -= Refresh;
            subscribed.OnStatsChanged -= Refresh;
        }
        if (gear != null) gear.OnChanged -= Refresh;
        MyEmblemChanged -= MineChanged;
        if (mine == this) mine = null;
    }

    void Refresh()
    {
        if (!Application.isPlaying || this == null) return;
        if (!FindBody()) return;

        Item garment;
        if (told)
        {
            var worn = gear != null ? gear.Get(EquipSlotType.Garment) : null;
            garment = worn != null ? worn.def : null;
        }
        else
        {
            var c = subscribed != null ? subscribed : Character.Instance;
            if (c == null) return;
            garment = c.GetEquipped(EquipSlotType.Garment);
        }

        var prefab = garment != null && garment.equipModel != null && garment.equipModel.GetComponent<BackPiece>() != null
                     ? garment.equipModel : null;
        if (prefab == prefabShown) return;
        if (shown != null) Destroy(shown);
        shown = prefab != null ? Wear(body, prefab) : null;
        prefabShown = prefab;
        if (shown != null)
        {
            var piece = shown.GetComponent<BackPiece>();
            if (piece != null)
            {
                piece.ShowEmblem(emblem);
                piece.ShowColours(colours);
            }
        }

        // The outline that shows a person through a wall takes the new piece into their shape.
        var silhouette = GetComponentInParent<OccludedSilhouetteTarget>();
        if (silhouette != null) silhouette.Refresh();
    }

    bool FindBody()
    {
        if (body != null) return true;
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (!HeadPiece.IsPiece(r)) { body = r; break; }
        return body != null && body.sharedMesh != null;
    }

    /// <summary>Put the back piece <paramref name="prefab"/> on <paramref name="body"/> (the body's own skinned
    /// renderer) and return it — the one way a back piece goes on. Null when the body has no upper-back bone.</summary>
    public static GameObject Wear(SkinnedMeshRenderer body, GameObject prefab)
    {
        if (body == null || body.sharedMesh == null || prefab == null) return null;
        var all = body.bones;
        int back = -1;
        for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].name.EndsWith(BackBone)) { back = i; break; }
        if (back < 0) return null;
        var bind = body.sharedMesh.bindposes[back];
        var go = Instantiate(prefab, all[back], false);
        go.transform.localPosition = bind.GetColumn(3);
        go.transform.localRotation = bind.rotation;
        go.transform.localScale = bind.lossyScale;
        go.hideFlags = HideFlags.DontSaveInEditor;
        var piece = go.GetComponent<BackPiece>();
        if (piece != null)
            piece.PushedBy(Bone(all, "LeftArm"), Bone(all, "LeftForeArm"), Bone(all, "RightArm"), Bone(all, "RightForeArm"));
        return go;
    }

    static Transform Bone(Transform[] bones, string name)
    {
        for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name.EndsWith(name)) return bones[i];
        return null;
    }
}
