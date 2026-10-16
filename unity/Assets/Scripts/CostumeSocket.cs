using UnityEngine;

/// <summary>
/// Dresses a character in the Costume Body item it wears: the body's own model is swapped for that body wearing the
/// costume (<see cref="CostumePiece"/>), and put back when the costume comes off. The costume's sibling of
/// <see cref="GarmentSocket"/>, worn the same way: your own avatar follows your own worn costume, and another person's
/// body is told their worn set (<see cref="ShowCostumeOf"/>), which the server already sends.
///
/// <para><b>What it costs.</b> A model and material swap when the worn costume changes, and nothing at all per frame.
/// Nothing here runs on the server: a worn costume is a picture.</para>
/// </summary>
[DisallowMultipleComponent]
public class CostumeSocket : MonoBehaviour
{
    SkinnedMeshRenderer body;
    Mesh plainMesh;
    Material[] plainMaterials;
    // The box the body is drawn within, in the bones' space, kept across a swap so a new model never resets it.
    Bounds plainBounds;
    CostumePiece shown;
    // The costume whose joined model this body has taken, given back when it changes or the body goes.
    CostumePiece wearing;
    bool told;
    UnitEquipment gear;
    Character subscribed;

    void Awake()
    {
        GameServer.RemovesPresentation(this);
    }

    /// <summary>Wear the Costume Body item <paramref name="worn"/> holds, followed whenever that worn set changes.</summary>
    public void ShowCostumeOf(UnitEquipment worn)
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
        if (wearing != null) CostumeModels.TakeOff(wearing, plainMesh);
    }

    void Refresh()
    {
        if (!Application.isPlaying || this == null) return;
        if (!FindBody()) return;

        Item costume;
        if (told)
        {
            var worn = gear != null ? gear.Get(EquipSlotType.CostumeBody) : null;
            costume = worn != null ? worn.def : null;
        }
        else
        {
            var c = subscribed != null ? subscribed : Character.Instance;
            if (c == null) return;
            costume = c.GetEquipped(EquipSlotType.CostumeBody);
        }

        var piece = costume != null && costume.equipModel != null ? costume.equipModel.GetComponent<CostumePiece>() : null;
        if (piece == shown) return;
        shown = piece;

        if (wearing != null) CostumeModels.TakeOff(wearing, plainMesh);
        var fitted = piece != null ? CostumeModels.Wear(piece, plainMesh, body) : null;
        wearing = fitted != null ? piece : null;
        if (piece != null && fitted == null)
            GameLog.Warn(LogCat.Client, "the costume '" + costume.displayName + "' was never fitted to the body '" +
                                        plainMesh.name + "' — the body is drawn without it");
        if (fitted != null)
        {
            var materials = new Material[plainMaterials.Length + 1];
            System.Array.Copy(plainMaterials, materials, plainMaterials.Length);
            materials[plainMaterials.Length] = piece.material;
            body.sharedMesh = fitted;
            body.localBounds = plainBounds;
            CharacterFade.SetMaterials(body, materials);
        }
        else
        {
            body.sharedMesh = plainMesh;
            body.localBounds = plainBounds;
            CharacterFade.SetMaterials(body, plainMaterials);
        }

        // The outline that shows a person through a wall takes the new shape.
        var silhouette = GetComponentInParent<OccludedSilhouetteTarget>();
        if (silhouette != null) silhouette.Refresh();
    }

    // The plain body and what it is drawn with, read once, before anything is swapped.
    bool FindBody()
    {
        if (body != null) return true;
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (!HeadPiece.IsPiece(r)) { body = r; break; }
        if (body == null || body.sharedMesh == null) { body = null; return false; }
        plainMesh = body.sharedMesh;
        plainMaterials = body.sharedMaterials;
        plainBounds = body.localBounds;
        return true;
    }
}
