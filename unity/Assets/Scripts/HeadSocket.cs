using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Puts a character's hair and headgear on its body — the head's sibling of <see cref="WeaponSocket"/>, and worn by
/// every live-3D body the same way: your own avatar, another person, a townsperson, a shadow creature and the
/// creation preview. Only WHERE the head comes from differs:
/// <list type="bullet">
/// <item>Told nothing: this machine's own character — its saved hair (<see cref="Character.HairStyle"/>) and the
/// Head item it wears, followed on every change.</item>
/// <item>Told by <see cref="ShowHair"/> and <see cref="ShowHeadgearOf"/> / <see cref="ShowHeadgear"/>: somebody else,
/// with the hair and the worn set the server (or the creature's own entry) gives.</item>
/// </list>
///
/// <para><b>How a piece sits.</b> Every body shares one mesh space, so a piece cut from any model sits right on any
/// other. A piece fixed to the head is a child of the head bone at that body's own resting offset for the head
/// (its bind pose), which costs nothing measurable; one that bends with the body (the hood) is skinned to the body's
/// own bones by name, with a copy of its mesh carrying this body's bind poses, made once per (piece, body) and shared.</para>
///
/// <para>Nothing here runs outside Play or on a machine with no screen: a piece is a picture.</para>
/// </summary>
[DisallowMultipleComponent]
public class HeadSocket : MonoBehaviour
{
    /// <summary>The head of the person at this keyboard. Their model is a SEPARATE object riding their avatar, so the
    /// body builder names it here, as it names their hand (<see cref="WeaponSocket.OnMyAvatar"/>).</summary>
    public static HeadSocket OnMyAvatar { get; private set; }

    public static void MarkMine(HeadSocket socket) => OnMyAvatar = socket;

    /// <summary>How tall the head is, head bone to the top of the bare body, in world units; 0 before the body is
    /// built. Read when something is sized to a head, never per frame.</summary>
    public float HeadSize() => TryHead(out var headBase, out float top) ? Mathf.Max(0f, top - headBase.y) : 0f;

    SkinnedMeshRenderer body;
    Transform headBone;
    string bodyModel;

    GameObject hairShown, gearShown;
    GameObject hairPrefabShown, gearPrefabShown;
    HeadPiece cutShownBy;
    Color hairColourShown;
    MaterialPropertyBlock block;

    // Told whose head this is. Nothing told means this machine's own character.
    bool told;
    string toldHair = "";
    UnitEquipment gear;
    Item toldHeadgear;
    Character subscribed;

    void Awake()
    {
        GameServer.RemovesPresentation(this);
    }

    /// <summary>Wear this hair — "id|#RRGGBB" (<see cref="HeadPieces.Pack"/>), already worked out by whoever gives it.</summary>
    public void ShowHair(string packed)
    {
        told = true;
        toldHair = packed ?? "";
        Refresh();
    }

    /// <summary>Wear the Head item <paramref name="worn"/> holds, followed whenever that worn set changes.</summary>
    public void ShowHeadgearOf(UnitEquipment worn)
    {
        told = true;
        if (gear != null) gear.OnChanged -= Refresh;
        gear = worn;
        if (gear != null) gear.OnChanged += Refresh;
        Refresh();
    }

    /// <summary>Wear this Head item (somebody with no worn set of their own — a townsperson).</summary>
    public void ShowHeadgear(Item item)
    {
        told = true;
        toldHeadgear = item;
        Refresh();
    }

    void Start()
    {
        if (told) return;   // told whose head to show; this machine's character is not this body's
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
    }

    void Refresh()
    {
        if (!Application.isPlaying || this == null) return;
        if (!FindBody()) return;

        string hairId, hairHex;
        Item hat;
        if (told)
        {
            HeadPieces.Unpack(toldHair, out hairId, out hairHex);
            var worn = gear != null ? gear.Get(EquipSlotType.Head) : null;
            hat = gear != null ? (worn != null ? worn.def : null) : toldHeadgear;
        }
        else
        {
            var c = subscribed != null ? subscribed : Character.Instance;
            if (c == null) return;
            HeadPieces.Resolve(bodyModel, c.HairStyle, c.HairColour, c.DyeSources, c.DyeTargets, out hairId, out hairHex);
            hat = c.GetEquipped(EquipSlotType.Head);
        }

        var gearPrefab = hat != null && hat.equipModel != null && hat.equipModel.GetComponent<HeadPiece>() != null
                         ? hat.equipModel : null;
        var gearPiece = gearPrefab != null ? gearPrefab.GetComponent<HeadPiece>() : null;
        var hairPrefab = gearPiece != null && gearPiece.hidesHair ? null : HeadPieces.HairPiece(hairId);
        var colour = ColorUtility.TryParseHtmlString(hairHex, out var parsed) ? parsed : HeadPieces.OwnColourOf(hairId);

        bool changed = false;
        if (gearPrefab != gearPrefabShown)
        {
            if (gearShown != null) Destroy(gearShown);
            gearShown = gearPrefab != null ? Mount(gearPrefab) : null;
            gearPrefabShown = gearPrefab;
            changed = true;
        }
        if (hairPrefab != hairPrefabShown)
        {
            if (hairShown != null) Destroy(hairShown);
            hairShown = hairPrefab != null ? Mount(hairPrefab) : null;
            hairPrefabShown = hairPrefab;
            changed = true;
            hairColourShown = new Color(-1f, 0f, 0f);
        }
        if (hairShown != null && colour != hairColourShown)
        {
            PaintHair(hairShown, colour);
            hairColourShown = colour;
        }

        // A HAT THAT COVERS THE HEAD CUTS THE HAIR AT ITS BRIM: only the hair hanging below it shows.
        var cutBy = gearPiece != null && gearPiece.coversHair ? gearPiece : null;
        if (hairShown != null && (changed || cutBy != cutShownBy))
        {
            CutHair(hairShown, hairPrefab, cutBy);
            cutShownBy = cutBy;
        }

        if (changed)
        {
            // The outline that shows a person through a wall takes the new pieces into their shape.
            var silhouette = GetComponentInParent<OccludedSilhouetteTarget>();
            if (silhouette != null) silhouette.Refresh();
        }
    }

    /// <summary>How far the top of whatever is worn on the head stands above <paramref name="feet"/>, in world
    /// units; 0 when nothing is worn. Read when something is about to float over the head, never per frame.</summary>
    public float TopAbove(Transform feet)
    {
        if (feet == null) return 0f;
        float top = Mathf.Max(TopOf(hairShown), TopOf(gearShown));
        return top == float.MinValue ? 0f : top - feet.position.y;
    }

    /// <summary>Where the head starts (its bone) and where the bare body ends at the top, in world space, for
    /// framing the head. Read when the framing changes, never per frame.</summary>
    public bool TryHead(out Vector3 headBase, out float bodyTop)
    {
        headBase = default;
        bodyTop = 0f;
        if (!FindBody()) return false;
        headBase = headBone.position;
        bodyTop = body.bounds.max.y;
        return true;
    }

    static float TopOf(GameObject piece)
    {
        var r = piece != null ? piece.GetComponent<Renderer>() : null;
        return r != null ? r.bounds.max.y : float.MinValue;
    }

    // The body this socket dresses: the body's skinned renderer (never a piece's) and its head bone. Looked up
    // once per body.
    bool FindBody()
    {
        if (body != null && headBone != null) return true;
        body = null;
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (!HeadPiece.IsPiece(r)) { body = r; break; }
        if (body == null || body.sharedMesh == null) return false;
        var bones = body.bones;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null && bones[i].name.EndsWith("Head"))
            {
                headBone = bones[i];
                break;
            }
        var tex = CharacterDye3D.TextureOf(body.sharedMaterial);
        bodyModel = tex != null ? tex.name : "";
        return headBone != null;
    }

    GameObject Mount(GameObject prefab) => Wear(body, prefab);

    /// <summary>Put the head piece <paramref name="prefab"/> on <paramref name="body"/> (the body's own skinned
    /// renderer) and return it — THE one way a piece goes on, shared with the editor's look previews, so a preview
    /// can never show a piece sitting differently from the game. Null when the body has no head bone.</summary>
    public static GameObject Wear(SkinnedMeshRenderer body, GameObject prefab)
    {
        if (body == null || body.sharedMesh == null || prefab == null) return null;
        var piece = prefab.GetComponent<HeadPiece>();
        if (piece == null) return null;
        GameObject go;
        if (piece.BendsWithBody)
        {
            go = Instantiate(prefab, body.transform.parent, false);
            go.transform.localPosition = body.transform.localPosition;
            go.transform.localRotation = body.transform.localRotation;
            go.transform.localScale = body.transform.localScale;
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            var names = piece.bones;
            var bones = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++) bones[i] = BoneNamed(body, names[i]);
            smr.sharedMesh = BoundTo(smr.sharedMesh, names, body);
            smr.bones = bones;
            smr.rootBone = body.rootBone;
            smr.localBounds = body.localBounds;
        }
        else
        {
            // At the body's own resting offset for its head, so the piece sits where it sat on the model it came from.
            int head = -1;
            var all = body.bones;
            for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].name.EndsWith("Head")) { head = i; break; }
            if (head < 0) return null;
            var bind = body.sharedMesh.bindposes[head];
            go = Instantiate(prefab, all[head], false);
            go.transform.localPosition = bind.GetColumn(3);
            go.transform.localRotation = bind.rotation;
            go.transform.localScale = bind.lossyScale;
        }
        go.hideFlags = HideFlags.DontSaveInEditor;
        return go;
    }

    static Transform BoneNamed(SkinnedMeshRenderer body, string boneName)
    {
        var bones = body.bones;
        for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == boneName) return bones[i];
        return null;
    }

    // One copy of a bending piece's mesh per body it is worn on, carrying THAT body's bind poses, shared by every
    // person wearing it on that body. Bounded by pieces x bodies, never by people.
    static readonly Dictionary<(Mesh, Mesh), Mesh> bound = new Dictionary<(Mesh, Mesh), Mesh>();

    static Mesh BoundTo(Mesh piece, string[] names, SkinnedMeshRenderer onto)
    {
        var key = (piece, onto.sharedMesh);
        if (bound.TryGetValue(key, out var mesh)) return mesh;
        mesh = Instantiate(piece);
        mesh.name = piece.name + " on " + onto.sharedMesh.name;
        var bodyBones = onto.bones;
        var bodyBind = onto.sharedMesh.bindposes;
        var bind = mesh.bindposes;
        for (int i = 0; i < names.Length && i < bind.Length; i++)
            for (int b = 0; b < bodyBones.Length; b++)
                if (bodyBones[b] != null && bodyBones[b].name == names[i]) { bind[i] = bodyBind[b]; break; }
        mesh.bindposes = bind;
        bound[key] = mesh;
        if (!dropsUnwornCopies)
        {
            dropsUnwornCopies = true;
            AddressablePrefabs.MapLeft += DropCopiesOfGoneBodies;
        }
        return mesh;
    }

    // A body leaves memory with its model once nobody has worn it for two maps (AddressablePrefabs); the copies of
    // pieces fitted to it would otherwise stay forever, and a fresh copy be made beside them on the next visit.
    static bool dropsUnwornCopies;
    static readonly List<(Mesh, Mesh)> goneCopies = new List<(Mesh, Mesh)>();

    // Once per map left, never per frame.
    static void DropCopiesOfGoneBodies()
    {
        goneCopies.Clear();
        foreach (var pair in bound)
            if (pair.Key.Item1 == null || pair.Key.Item2 == null) goneCopies.Add(pair.Key);
        foreach (var k in goneCopies)
        {
            if (bound[k] != null) Destroy(bound[k]);
            bound.Remove(k);
        }
    }

    /// <summary>Show <paramref name="hair"/> (worn from <paramref name="hairPrefab"/>) cut at the brim of
    /// <paramref name="by"/>, or whole when nothing covering is worn.</summary>
    public static void CutHair(GameObject hair, GameObject hairPrefab, HeadPiece by)
    {
        var shown = hair != null ? hair.GetComponent<MeshFilter>() : null;
        var own = hairPrefab != null ? hairPrefab.GetComponent<MeshFilter>() : null;
        if (shown == null || own == null) return;
        shown.sharedMesh = by != null ? HairCut.Below(own.sharedMesh, by) : own.sharedMesh;
    }

    // The chosen colour on the hair's own colour, and every other shade of the hair moved the same way, so the
    // strands keep their light and dark.
    void PaintHair(GameObject hair, Color chosen)
    {
        if (block == null) block = new MaterialPropertyBlock();
        PaintHair(hair, chosen, block);
    }

    /// <summary>Draw a worn hair in <paramref name="chosen"/> — shared with the editor's look previews.</summary>
    public static void PaintHair(GameObject hair, Color chosen, MaterialPropertyBlock block)
    {
        var piece = hair != null ? hair.GetComponent<HeadPiece>() : null;
        var r = hair != null ? hair.GetComponent<Renderer>() : null;
        if (piece == null || r == null || block == null || piece.colours == null || piece.colours.Length == 0) return;
        var own = piece.colours[0];
        var targets = new Color[piece.colours.Length];
        for (int i = 0; i < targets.Length; i++)
            targets[i] = i == 0 ? chosen : HeadPieces.Shade(piece.colours[i], own, chosen);
        CharacterDye3D.Paint(new[] { r }, piece.colours, piece.colours, targets, block);
    }
}
