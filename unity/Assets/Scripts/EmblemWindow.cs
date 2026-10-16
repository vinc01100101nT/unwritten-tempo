using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The <b>Emblem</b> window, opened from <see cref="SystemWindow"/>: pick a picture from the emblem folder, see it as
/// it will look, and upload it to be shown on the back of your cape (<see cref="NetPlayer.UploadEmblem"/>).
///
/// <para><b>It only shows and sends.</b> The preview uses the same reader the server does (<see cref="EmblemImage"/>), so
/// a picture that breaks a rule says so before it is sent; but the server reads the file again and its answer is the
/// one that counts (<see cref="Answered"/>).</para>
///
/// <para><b>The cape's colours.</b> Above the picture, three boxes — Cloth, Trim and the cloth's colour at the hem — and
/// the Gradient switch, edited with the same colour picker character creation uses. A change is sent a moment after the
/// player stops picking, and the server's copy is what every screen draws (<see cref="NetPlayer.SetCapeColours"/>).</para>
///
/// <para><b>Where the pictures come from.</b> A folder named "Emblem" inside the game's own save folder, as in
/// Ragnarok — the same on Windows and Mac, with no file dialog. The window lists it on opening and every two seconds
/// while open, so a picture dropped in shows up without reopening.</para>
/// </summary>
public class EmblemWindow : MonoBehaviour
{
    static EmblemWindow instance;
    const float SecondsBetweenFolderLooks = 2f;
    const int PreviewPixels = 96;

    GameObject root;
    RectTransform list;
    RawImage preview;
    TMPro.TMP_Text status;
    Button upload;
    readonly List<GameObject> fileButtons = new List<GameObject>();
    readonly List<string> filesShown = new List<string>();
    string picked;
    byte[] pickedBytes;
    Texture2D previewTexture;
    float nextLook;

    // ---- the cape's colours ----
    // Long enough that dragging across the picker is one request, short enough that it feels instant.
    const float QuietSecondsBeforeSending = 0.4f;
    static readonly string[] BoxNames = { "Cloth", "Trim", "Cloth at hem" };
    CapeColours draft;
    bool draftLoaded, colourDirty, settingPicker;
    float sendColoursAt;
    int editing;
    ColourPicker picker;
    readonly Image[] swatches = new Image[3];
    readonly Button[] boxes = new Button[3];
    TMPro.TMP_Text gradientLabel;

    /// <summary>The folder the window lists: "Emblem" inside the game's own save folder, made when missing.</summary>
    public static string Folder => Path.Combine(Application.persistentDataPath, "Emblem");

    public static void Open()
    {
        if (instance == null || instance.root == null) return;
        instance.nextLook = 0f;
        instance.draftLoaded = false;
        UiAnim.Open(instance.root);
        UISounds.PlayOpen();
    }

    /// <summary>The server's answer to an upload.</summary>
    public static void Answered(bool accepted, string reason)
    {
        if (accepted) OverheadNotice.Success("Emblem uploaded");
        else OverheadNotice.Fail("Emblem refused");
        if (instance == null || instance.status == null) return;
        instance.status.text = accepted ? "Uploaded. It shows on the back of a worn cape." : "Refused: " + reason;
        instance.status.color = accepted ? UiKit.NoticeSuccess : UiKit.NoticeFailure;
    }

    void Start()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { enabled = false; return; }
        instance = this;

        list = UiKit.ListWindow(canvas.transform, "Emblem", new Vector2(320, 520), out root);

        for (int i = 0; i < BoxNames.Length; i++)
        {
            int which = i;
            boxes[i] = UiKit.ListButton(list, BoxNames[i], () => Edit(which), 26);
            swatches[i] = UiKit.Img("Swatch", boxes[i].transform, Color.white);
            var srt = swatches[i].rectTransform;
            srt.anchorMin = new Vector2(1f, 0.5f);
            srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(1f, 0.5f);
            srt.sizeDelta = new Vector2(40f, 16f);
            srt.anchoredPosition = new Vector2(-6f, 0f);
            swatches[i].raycastTarget = false;
        }
        var gradient = UiKit.ListButton(list, "Gradient: Off", ToggleGradient, 26);
        gradientLabel = gradient.GetComponentInChildren<TMPro.TMP_Text>();
        picker = ColourPicker.Build(list);
        picker.Root.gameObject.AddComponent<LayoutElement>().preferredHeight = ColourPicker.Height;
        picker.OnChanged += Picked;
        foreach (var rule in EmblemImage.Rules) UiKit.ListLine(list, "• " + rule, 13, UiKit.TextDim);
        UiKit.ListLine(list, "Put pictures in: " + Folder, 11, UiKit.TextDim, 3);
        UiKit.ListButton(list, "Open folder", OpenFolder, 26);

        var holder = new GameObject("Preview", typeof(RectTransform));
        holder.transform.SetParent(list, false);
        holder.AddComponent<LayoutElement>().preferredHeight = PreviewPixels + 8;
        preview = new GameObject("Picture", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        preview.transform.SetParent(holder.transform, false);
        var prt = preview.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(PreviewPixels, PreviewPixels);
        preview.raycastTarget = false;
        preview.enabled = false;

        upload = UiKit.PrimaryBtn("Upload", list, Upload);
        upload.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
        upload.interactable = false;
        status = UiKit.ListLine(list, "Pick a picture below.", 12, UiKit.TextDim, 2);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (previewTexture != null) Destroy(previewTexture);
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;
        if (!draftLoaded) LoadColours();
        if (colourDirty && Time.unscaledTime >= sendColoursAt && NetPlayer.Mine != null)
        {
            colourDirty = false;
            NetPlayer.Mine.SetCapeColours(draft.Pack());
        }
        if (Time.unscaledTime < nextLook) return;
        nextLook = Time.unscaledTime + SecondsBetweenFolderLooks;
        LookInFolder();
    }

    // The boxes start from what the cape shows now: the colours chosen before, or the worn cape's own.
    void LoadColours()
    {
        var mine = NetPlayer.Mine;
        if (mine != null && CapeColours.TryParse(mine.ChosenCapeColours, out var chosen)) draft = chosen;
        else
        {
            var shown = GarmentSocket.MyCapeColours();
            draft = shown ?? new CapeColours { cloth = Color.white, trim = Color.white, hem = Color.white };
        }
        draftLoaded = true;
        colourDirty = false;
        DrawColours();
        Edit(editing);
    }

    void Edit(int which)
    {
        editing = which;
        for (int i = 0; i < boxes.Length; i++)
        {
            var label = boxes[i].GetComponentInChildren<TMPro.TMP_Text>();
            if (label != null) label.fontStyle = i == which ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
        }
        settingPicker = true;
        picker.Set(ColourOf(which));
        settingPicker = false;
    }

    Color ColourOf(int which) => which == 0 ? draft.cloth : which == 1 ? draft.trim : draft.hem;

    void Picked(Color c)
    {
        if (!draftLoaded || settingPicker) return;
        if (editing == 0) draft.cloth = c;
        else if (editing == 1) draft.trim = c;
        else draft.hem = c;
        Changed();
    }

    void ToggleGradient()
    {
        draft.gradient = !draft.gradient;
        if (!draft.gradient && editing == 2) Edit(0);
        Changed();
    }

    void Changed()
    {
        DrawColours();
        colourDirty = true;
        sendColoursAt = Time.unscaledTime + QuietSecondsBeforeSending;
    }

    void DrawColours()
    {
        for (int i = 0; i < swatches.Length; i++) swatches[i].color = ColourOf(i);
        boxes[2].gameObject.SetActive(draft.gradient);
        gradientLabel.text = draft.gradient ? "Gradient: On" : "Gradient: Off";
    }

    // The folder's BMP and PNG files, as one button each — rebuilt only when the list of names changed.
    void LookInFolder()
    {
        string[] found;
        try
        {
            Directory.CreateDirectory(Folder);
            found = Directory.GetFiles(Folder);
        }
        catch (IOException) { return; }
        catch (System.UnauthorizedAccessException) { return; }
        var names = new List<string>();
        foreach (var f in found)
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext == ".bmp" || ext == ".png") names.Add(f);
        }
        names.Sort(System.StringComparer.OrdinalIgnoreCase);
        if (Same(names, filesShown)) return;
        filesShown.Clear();
        filesShown.AddRange(names);
        foreach (var b in fileButtons) Destroy(b);
        fileButtons.Clear();
        if (names.Count == 0)
        {
            var none = UiKit.ListLine(list, "No BMP or PNG pictures in the folder yet.", 12, UiKit.TextDim);
            fileButtons.Add(none.gameObject);
            return;
        }
        foreach (var path in names)
        {
            string p = path;
            var b = UiKit.ListButton(list, Path.GetFileName(p), () => Pick(p), 24);
            fileButtons.Add(b.gameObject);
        }
    }

    static bool Same(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    void Pick(string path)
    {
        picked = path;
        pickedBytes = ReadAtMost(path, EmblemImage.MaxFileBytes + 1);
        var rgba = EmblemImage.Decode(pickedBytes, out string refusal);
        if (previewTexture != null) Destroy(previewTexture);
        previewTexture = rgba != null ? EmblemCache.TextureOf(rgba) : null;
        preview.texture = previewTexture;
        preview.enabled = previewTexture != null;
        upload.interactable = rgba != null && NetPlayer.Mine != null;
        status.text = rgba != null ? Path.GetFileName(path) + " — ready to upload." : Path.GetFileName(path) + ": " + refusal;
        status.color = rgba != null ? UiKit.TextDim : UiKit.NoticeFailure;
    }

    // Never more than one byte past the limit is read: that is enough for the server to say "too big".
    static byte[] ReadAtMost(string path, int count)
    {
        try
        {
            using (var f = File.OpenRead(path))
            {
                int n = (int)System.Math.Min(f.Length, count);
                var bytes = new byte[n];
                int read = 0;
                while (read < n)
                {
                    int got = f.Read(bytes, read, n - read);
                    if (got <= 0) break;
                    read += got;
                }
                return bytes;
            }
        }
        catch (IOException) { return null; }
        catch (System.UnauthorizedAccessException) { return null; }
    }

    void Upload()
    {
        if (pickedBytes == null || NetPlayer.Mine == null) return;
        NetPlayer.Mine.UploadEmblem(pickedBytes);
        status.text = "Sending " + Path.GetFileName(picked) + "…";
        status.color = UiKit.TextDim;
    }

    static void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Application.OpenURL("file://" + Folder.Replace('\\', '/'));
    }
}
