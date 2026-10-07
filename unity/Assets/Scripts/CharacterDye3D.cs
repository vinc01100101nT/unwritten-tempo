using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-cloth-part dye for a live-3D Quaternius person — your own avatar, and the body this machine draws for every
/// other player. The colours are swapped while the body is drawn: each renderer is handed its model's parts and the
/// chosen colour for each through a <see cref="MaterialPropertyBlock"/>, and the character shader
/// (<c>Unwritten/CharacterLit</c>, URP Lit plus the swap) does the rest. No texture is made per person, so a crowd of
/// differently dressed people costs no more memory than one. Skin and eyes are never listed as parts, so they pass
/// through.
///
/// <para>SINGLE SOURCE OF TRUTH: the persisted dye (source→target hex pairs) lives on
/// <see cref="Character"/>; on your own avatar this component is a computed VIEW that reads it (on
/// <see cref="Character.OnAppearanceChanged"/>) and writes back through <see cref="Character.SetDye"/>. On a body
/// drawn for somebody else it reads the look the server sent for them instead (<see cref="AddShowing"/>). Which parts
/// a model has is read from <see cref="CharacterDyeParts"/>, worked out in the editor.</para>
///
/// <para>Not a hot path: the colours are handed over only when the dye changes or a save loads — never per frame.</para>
/// </summary>
[DisallowMultipleComponent]
public class CharacterDye3D : MonoBehaviour
{
    // Must match the eight colour pairs in CharacterLitInput.hlsl.
    const int MaxParts = 8;

    static readonly int CountId = Shader.PropertyToID("_DyeCount");
    static readonly int[] SrcIds = PropertyIds("_DyeSrc");
    static readonly int[] DstIds = PropertyIds("_DyeDst");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");   // URP Lit
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");   // Standard / legacy fallback

    static int[] PropertyIds(string prefix)
    {
        var ids = new int[MaxParts];
        for (int i = 0; i < MaxParts; i++) ids[i] = Shader.PropertyToID(prefix + i);
        return ids;
    }

    /// <summary>The texture a material actually draws with.</summary>
    public static Texture TextureOf(Material m)
    {
        if (m == null) return null;
        return (m.HasProperty(BaseMapId) ? m.GetTexture(BaseMapId) : m.GetTexture(MainTexId)) ?? m.mainTexture;
    }

    /// <summary>A stable fingerprint of a worn model — the sorted, distinct names of the TEXTURES its renderers draw
    /// with. Keyed on texture, NOT mesh: Quaternius bodies often share one mesh across characters (BlueSoldier &amp;
    /// Soldier are the same geometry) but each has its own texture, so a mesh-name key would collide and let dye bleed
    /// between them. It is both the stamp a dye carries and the key its parts are saved under.</summary>
    public static string SignatureOf(SkinnedMeshRenderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0) return null;
        var names = new List<string>();
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var tex = TextureOf(r.sharedMaterial);
            if (tex == null) continue;
            if (!names.Contains(tex.name)) names.Add(tex.name);
        }
        if (names.Count == 0) return null;
        names.Sort(System.StringComparer.Ordinal);
        return string.Join("|", names);
    }

    SkinnedMeshRenderer[] renderers;
    MaterialPropertyBlock mpb;

    // The current recolor map (authored sRGB). Parallel lists; index-matched.
    readonly List<Color> sources = new List<Color>();
    readonly List<Color> targets = new List<Color>();

    Character boundCharacter;

    // This model's recolourable parts, read from the saved table once per body.
    IReadOnlyList<Color> parts;

    // A BODY DRAWN FOR SOMEBODY ELSE shows the colours the server sent for that person and never binds to this
    // machine's own character — binding would paint your colours onto them. Set before OnEnable can bind (see AddShowing).
    bool showsOther;
    static bool addingForOther;

    /// <summary>Recolour a body this machine draws for ANOTHER person, with the look the server sent for them
    /// (<see cref="PackLook"/>). It never touches this machine's own character.</summary>
    public static CharacterDye3D AddShowing(GameObject body, string look)
    {
        addingForOther = true;
        CharacterDye3D dye;
        try { dye = body.AddComponent<CharacterDye3D>(); }
        finally { addingForOther = false; }
        dye.showsOther = true;
        // Nothing to poll for: the look only ever changes by being handed a new one.
        dye.enabled = false;
        dye.ShowLook(look);
        return dye;
    }

    /// <summary>One string carrying a whole look — the model the colours were chosen on, then each part's
    /// original→chosen colour — so it travels as a single value and lands all at once.</summary>
    public static string PackLook(string model, string[] sourceHex, string[] targetHex)
    {
        if (string.IsNullOrEmpty(model) || sourceHex == null || targetHex == null) return "";
        int n = Mathf.Min(sourceHex.Length, targetHex.Length);
        if (n == 0) return "";
        var sb = new System.Text.StringBuilder(model.Length + n * 16);
        sb.Append(model).Append('\n');
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(sourceHex[i]).Append('>').Append(targetHex[i]);
        }
        return sb.ToString();
    }

    /// <summary>Show a look made by <see cref="PackLook"/>. An empty one shows the model's own colours.</summary>
    public void ShowLook(string look)
    {
        int cut = string.IsNullOrEmpty(look) ? -1 : look.IndexOf('\n');
        if (cut < 0) { ShowDye(null, null, null); return; }
        var pairs = look.Substring(cut + 1).Split(',');
        var src = new string[pairs.Length];
        var dst = new string[pairs.Length];
        for (int i = 0; i < pairs.Length; i++)
        {
            int arrow = pairs[i].IndexOf('>');
            src[i] = arrow < 0 ? "" : pairs[i].Substring(0, arrow);
            dst[i] = arrow < 0 ? "" : pairs[i].Substring(arrow + 1);
        }
        ShowDye(src, dst, look.Substring(0, cut));
    }

    void Awake()
    {
        if (GameServer.RemovesPresentation(this)) return;
        if (addingForOther) showsOther = true;
        renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        mpb = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        // Bind to the persistent character (may not exist yet on frame one — poll cheaply in Update until it does).
        if (!showsOther) TryBind();
    }

    void Update()
    {
        if (!showsOther && boundCharacter == null) TryBind();
    }

    void TryBind()
    {
        var c = Character.Instance;
        if (c == null || c == boundCharacter) return;
        boundCharacter = c;
        boundCharacter.OnAppearanceChanged += PullFromCharacter;
        PullFromCharacter();
    }

    void OnDisable()
    {
        if (boundCharacter != null) boundCharacter.OnAppearanceChanged -= PullFromCharacter;
        boundCharacter = null;
    }

    // ── Public API (a dye UI calls these) ────────────────────────────────────────
    /// <summary>Recolor the part whose atlas color is <paramref name="source"/> to <paramref name="target"/>.
    /// Persists through <see cref="Character"/> and re-applies. Pass <paramref name="target"/> == source to reset.</summary>
    public void SetPartColor(Color source, Color target)
    {
        int i = IndexOf(source);
        if (i < 0) { sources.Add(source); targets.Add(target); }
        else targets[i] = target;
        PushToCharacter();   // writes the SSOT + fires OnAppearanceChanged → PullFromCharacter → Apply
    }

    /// <summary>The chosen color for a part, or the part's own color if it hasn't been dyed.</summary>
    public Color TargetFor(Color source) => ChosenFor(source, sources, targets);

    /// <summary>The model's distinct dyeable cloth colors (skin/eyes filtered out), most-worn first, as saved in
    /// <see cref="CharacterDyeParts"/>. Drives character creation's part swatches.</summary>
    public IReadOnlyList<Color> DetectableParts()
    {
        EnsureRenderers();
        if (parts == null) parts = CharacterDyeParts.For(SignatureOf(renderers));
        return parts;
    }

    // ── Character binding (SSOT) ─────────────────────────────────────────────────
    void PullFromCharacter()
        => ShowDye(boundCharacter != null ? boundCharacter.DyeSources : null,
                   boundCharacter != null ? boundCharacter.DyeTargets : null,
                   boundCharacter != null ? boundCharacter.DyeModel : null);

    // The one way a look reaches the body, whether it came from this machine's own character or from the server
    // for somebody else — so the two can never come to show the same colours differently.
    void ShowDye(string[] sourceHex, string[] targetHex, string stampedModel)
    {
        // A dye belongs to the specific character it was made on — it must NOT ride onto a different character
        // just because they share a base color (both navy, etc.). The dye is stamped with a signature of the
        // worn TEXTURE (each Quaternius character has its own, even when the mesh is shared); if the stamp doesn't
        // match the worn texture, THIS body shows its own plain colours.
        //
        // IT NEVER ERASES THE CHARACTER'S DYE. Two bodies are often bound to one character at once — the creation
        // preview beside the world body, or the old body still standing while the chosen one loads at sign-in —
        // and the wrong one used to wipe the colours the right one was about to show. The first recolour on a
        // body replaces the whole map anyway, so a foreign dye left in the save never survives being edited.
        if (sourceHex != null && sourceHex.Length > 0)
        {
            EnsureRenderers();
            string sig = SignatureOf(renderers);
            if (!string.IsNullOrEmpty(sig) && stampedModel != sig)
            {
                sources.Clear();
                targets.Clear();
                Apply();
                return;
            }
        }

        DecodeInto(sourceHex, targetHex);
        Apply();
    }

    void PushToCharacter()
    {
        if (boundCharacter == null) { Apply(); return; }
        EnsureRenderers();
        boundCharacter.SetDye(Encode(sources), Encode(targets), SignatureOf(renderers));   // fires OnAppearanceChanged → PullFromCharacter → Apply
    }

    int IndexOf(Color source)
    {
        for (int i = 0; i < sources.Count; i++)
            if (Approximately(sources[i], source)) return i;
        return -1;
    }

    static bool Approximately(Color a, Color b)
        => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

    static string[] Encode(List<Color> cols)
    {
        var arr = new string[cols.Count];
        for (int i = 0; i < cols.Count; i++) arr[i] = "#" + ColorUtility.ToHtmlStringRGB(cols[i]);
        return arr;
    }

    void DecodeInto(string[] src, string[] dst) => DecodePairs(src, dst, sources, targets);

    // ── Hand the colours to the renderers ────────────────────────────────────────
    // Re-fetch the mesh renderers if the cache is empty or went stale (e.g. this component was reused across a
    // model swap, or Awake ran before the mesh existed). Cheap; only re-queries when actually stale.
    void EnsureRenderers()
    {
        if (renderers != null && renderers.Length > 0)
        {
            bool stale = false;
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] == null) { stale = true; break; }
            if (!stale) return;
        }
        renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        // The mesh changed → the parts belong to the old body; read them again for the new one.
        parts = null;
    }

    void Apply()
    {
        if (mpb == null) return;
        EnsureRenderers();
        if (renderers == null) return;
        Paint(renderers, sources.Count > 0 ? DetectableParts() : null, sources, targets, mpb);
    }

    /// <summary>THE ONE COLOURING ROUTINE — hand <paramref name="renderers"/> their model's
    /// <paramref name="parts"/> and the chosen colour for each. Every body the game draws comes through here, and so
    /// does the Mob Table's preview in the editor, so the two can never show one look two ways.
    /// <paramref name="sources"/> and <paramref name="targets"/> are index-matched original→chosen colours; a part
    /// missing from them keeps its own colour. Null or empty parts hands over a count of zero, which shows the
    /// model's own colours.</summary>
    public static void Paint(SkinnedMeshRenderer[] renderers, IReadOnlyList<Color> parts, IReadOnlyList<Color> sources,
                             IReadOnlyList<Color> targets, MaterialPropertyBlock block)
    {
        if (renderers == null || block == null) return;

        // Hand over EVERY part (dyed or not), so the shader's nearest-part match can give each part its own pixels
        // and no dyed part bleeds into a neighbor. Undyed parts map to themselves and pass through. Nothing dyed →
        // a count of zero, and the shader skips the swap entirely.
        int n = parts != null && sources != null && sources.Count > 0 ? Mathf.Min(parts.Count, MaxParts) : 0;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            // Read first and write back: the fade and other effects keep their own values in this same block.
            r.GetPropertyBlock(block);
            block.SetFloat(CountId, n);
            for (int i = 0; i < n; i++)
            {
                block.SetVector(SrcIds[i], (Vector4)parts[i]);
                block.SetVector(DstIds[i], (Vector4)ChosenFor(parts[i], sources, targets));
            }
            r.SetPropertyBlock(block);
        }
    }

    // The chosen colour for one part, or the part's own colour when it was not changed.
    static Color ChosenFor(Color part, IReadOnlyList<Color> sources, IReadOnlyList<Color> targets)
    {
        int n = targets != null ? Mathf.Min(sources.Count, targets.Count) : 0;
        for (int i = 0; i < n; i++)
            if (Approximately(sources[i], part)) return targets[i];
        return part;
    }

    /// <summary>Read "#RRGGBB" colours into index-matched lists, skipping any pair that does not parse — the same
    /// reading every look gets, so a look saved anywhere is read one way.</summary>
    public static void DecodePairs(string[] src, string[] dst, List<Color> sources, List<Color> targets)
    {
        sources.Clear();
        targets.Clear();
        if (src == null || dst == null) return;
        int n = Mathf.Min(src.Length, dst.Length);
        for (int i = 0; i < n; i++)
            if (ColorUtility.TryParseHtmlString(src[i], out var s) && ColorUtility.TryParseHtmlString(dst[i], out var t))
            { sources.Add(s); targets.Add(t); }
    }
}
