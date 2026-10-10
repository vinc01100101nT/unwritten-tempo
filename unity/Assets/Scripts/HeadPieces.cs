using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// THE list of hairstyles, and what hair each character model was made with. Baked by <c>HeadPieceBaker</c> from the
/// character models; read by every machine. Headgear is not here: a piece of headgear is the model of a Head item
/// (<see cref="Item.equipModel"/>), so the item is its one owner.
///
/// <para><b>What a character's hair IS.</b> A hairstyle id and a colour, saved with the character. Both empty means
/// the character was made before hairstyles existed: they wear the hair their body model was made with, in the
/// colour that body is drawn in — its own, or the colour they chose for that part at creation. <see cref="NoHair"/>
/// is a real choice, never written as empty, so "chose no hair" can never be mistaken for "never chose".</para>
/// </summary>
public class HeadPieces : ScriptableObject
{
    public const string ResourcePath = "HeadPieces";

    /// <summary>The hairstyle id of no hair at all.</summary>
    public const string NoHair = "none";

    [Serializable]
    public class Hairstyle
    {
        public string id;
        public GameObject piece;
    }

    [Serializable]
    public class Body
    {
        [Tooltip("The character model's name — also the name of the texture it is drawn with.")]
        public string model;
        [Tooltip("The hairstyle this model was made with, or 'none'.")]
        public string hair = NoHair;
        [Tooltip("The colour that hair was drawn in on this model, \"#RRGGBB\".")]
        public string hairColour = "";
        [Tooltip("False for a model whose head takes no hair at all (an animal's head). Such a body is offered no hair.")]
        public bool takesHair = true;
    }

    [Tooltip("Every hairstyle, in the order character creation offers them.")]
    public List<Hairstyle> hairstyles = new List<Hairstyle>();

    [Tooltip("Every character model, with the hair it was made with.")]
    public List<Body> bodies = new List<Body>();

    static HeadPieces table;
    static bool looked;

    public static HeadPieces Table
    {
        get
        {
            if (!looked) { table = Resources.Load<HeadPieces>(ResourcePath); looked = true; }
            return table;
        }
    }

    public static void Forget() { table = null; looked = false; }

    /// <summary>The piece prefab of a hairstyle, or null for no hair or an id nobody knows.</summary>
    public static GameObject HairPiece(string id)
    {
        var t = Table;
        if (t == null || string.IsNullOrEmpty(id) || id == NoHair) return null;
        foreach (var h in t.hairstyles) if (h != null && h.id == id) return h.piece;
        return null;
    }

    public static Body BodyOf(string model)
    {
        var t = Table;
        if (t == null || string.IsNullOrEmpty(model)) return null;
        foreach (var b in t.bodies) if (b != null && b.model == model) return b;
        return null;
    }

    /// <summary>The hairstyles a body can be given at creation, "none" first. A body whose head takes no hair is
    /// offered "none" alone. The character server checks a new character against this same list.</summary>
    public static List<string> ChoicesFor(string model)
    {
        var list = new List<string> { NoHair };
        var body = BodyOf(model);
        var t = Table;
        if (t == null || (body != null && !body.takesHair)) return list;
        foreach (var h in t.hairstyles) if (h != null && !string.IsNullOrEmpty(h.id)) list.Add(h.id);
        return list;
    }

    /// <summary>The own colour of a hairstyle — what it is drawn in when nobody chose another.</summary>
    public static Color OwnColourOf(string id)
    {
        var piece = HairPiece(id);
        var hp = piece != null ? piece.GetComponent<HeadPiece>() : null;
        return hp != null && hp.colours != null && hp.colours.Length > 0 ? hp.colours[0] : Color.black;
    }

    /// <summary>The hair a character wears: their saved choice, or — when they never chose — the hair their body
    /// model was made with, in the colour that body is drawn in for them (<paramref name="dyeSources"/> →
    /// <paramref name="dyeTargets"/>). An empty colour on a chosen style means the style's own colour.</summary>
    public static void Resolve(string model, string style, string colour, string[] dyeSources, string[] dyeTargets,
                               out string id, out string colourHex)
    {
        if (!string.IsNullOrEmpty(style))
        {
            id = style;
            colourHex = !string.IsNullOrEmpty(colour) || style == NoHair ? colour ?? ""
                      : "#" + ColorUtility.ToHtmlStringRGB(OwnColourOf(style));
            return;
        }

        var body = BodyOf(model);
        if (body == null || string.IsNullOrEmpty(body.hair) || body.hair == NoHair) { id = NoHair; colourHex = ""; return; }
        id = body.hair;
        colourHex = body.hairColour;
        // THE COLOUR THEY CHOSE FOR THAT PART AT CREATION, back when the hair was part of the body.
        int n = dyeSources != null && dyeTargets != null ? Mathf.Min(dyeSources.Length, dyeTargets.Length) : 0;
        for (int i = 0; i < n; i++)
            if (string.Equals(dyeSources[i], body.hairColour, StringComparison.OrdinalIgnoreCase)) { colourHex = dyeTargets[i]; break; }
    }

    /// <summary>Another shade of a hair, moved with the hair's own colour: <paramref name="shade"/> is to
    /// <paramref name="own"/> what the result is to <paramref name="chosen"/>, by brightness. A colour, never a game
    /// number.</summary>
    public static Color Shade(Color shade, Color own, Color chosen)
    {
        float ownLight = own.r * 0.299f + own.g * 0.587f + own.b * 0.114f;
        float shadeLight = shade.r * 0.299f + shade.g * 0.587f + shade.b * 0.114f;
        float k = ownLight > 1e-4f ? shadeLight / ownLight : 1f;
        return new Color(Mathf.Clamp01(chosen.r * k), Mathf.Clamp01(chosen.g * k), Mathf.Clamp01(chosen.b * k), 1f);
    }

    /// <summary>One string carrying a hair, so it travels as a single value: "id|#RRGGBB".</summary>
    public static string Pack(string id, string colourHex) =>
        string.IsNullOrEmpty(id) ? "" : id + "|" + (colourHex ?? "");

    public static void Unpack(string packed, out string id, out string colourHex)
    {
        id = ""; colourHex = "";
        if (string.IsNullOrEmpty(packed)) return;
        int cut = packed.IndexOf('|');
        if (cut < 0) { id = packed; return; }
        id = packed.Substring(0, cut);
        colourHex = packed.Substring(cut + 1);
    }
}
