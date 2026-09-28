using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Choose which of your characters to play, make a new one, or retire one. Opened by
/// <see cref="SignInScreen"/> once an account is signed in, and it removes itself the moment a character is
/// chosen — from then on <see cref="AccountSession"/> knows who is playing and the game loads them.
///
/// <para>The list is the SERVER's answer, asked for fresh every time this opens. It is never cached between
/// sessions: the account's characters are the account service's to know, and a stale local copy would be a
/// second version of the truth.</para>
/// </summary>
public class CharacterSelectScreen : MonoBehaviour
{
    // Selection wears the accent the rest of the UI already uses for "this one"; everything else the plain edge.
    static readonly Color RowIdle = UiKit.SlotFill;

    Transform panel;
    Transform listRoot;
    TextMeshProUGUI status;
    Button enterButton, deleteButton;

    readonly List<AccountClient.CharacterInfo> characters = new List<AccountClient.CharacterInfo>();
    readonly List<Image> rowBorders = new List<Image>();
    int selected = -1;

    // Character creation, shown in place of the list.
    GameObject createPanel;
    TMP_InputField nameField;
    TextMeshProUGUI modelLabel;
    CharacterPreview preview;
    string[] models;
    int modelIndex;

    // The recolour being chosen: which part is selected, and the swatches showing each part's current colour.
    Transform partsRow;
    readonly List<Image> partSwatches = new List<Image>();
    int selectedPart;

    public static void Open()
    {
        // NEVER A SECOND ONE — the same guard its sibling the sign-in screen has always had, and the reason it
        // needs one: both are full-screen overlays at the top draw order, so a duplicate is invisible, sits on
        // top of the real one, and swallows every click aimed at it.
        if (Object.FindAnyObjectByType<CharacterSelectScreen>() != null) return;

        var go = new GameObject("CharacterSelectScreen") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        go.AddComponent<CharacterSelectScreen>();
    }

    void Start()
    {
        var canvas = FrontScreen.Canvas(transform);
        panel = FrontScreen.Panel(canvas, new Vector2(780, 620));

        var title = UiKit.Label("Title", panel, "Your Characters", 28, FontStyle.Normal, TextAnchor.MiddleCenter);
        title.color = UiKit.TitleText;
        FrontScreen.Row(title.rectTransform, -28, 36);

        var listBox = UiKit.Img("List", panel, UiKit.PanelRaise);
        listBox.rectTransform.anchorMin = new Vector2(0, 1);
        listBox.rectTransform.anchorMax = new Vector2(1, 1);
        listBox.rectTransform.pivot = new Vector2(0.5f, 1);
        listBox.rectTransform.offsetMin = new Vector2(24, 0);
        listBox.rectTransform.offsetMax = new Vector2(-24, 0);
        listBox.rectTransform.sizeDelta = new Vector2(-48, 430);
        listBox.rectTransform.anchoredPosition = new Vector2(0, -76);
        listRoot = listBox.transform;

        status = UiKit.Label("Status", panel, "", 13, FontStyle.Normal, TextAnchor.MiddleCenter);
        status.color = UiKit.TitleText;
        FrontScreen.Row(status.rectTransform, -518, 18);

        enterButton = UiKit.Btn("Enter World", panel, OnEnter);
        FrontScreen.Place(enterButton.GetComponent<RectTransform>(), new Vector2(-267, -556), new Vector2(150, 34));

        var newButton = UiKit.Btn("New", panel, OpenCreate);
        FrontScreen.Place(newButton.GetComponent<RectTransform>(), new Vector2(-89, -556), new Vector2(150, 34));

        deleteButton = UiKit.Btn("Delete", panel, OnDelete);
        FrontScreen.Place(deleteButton.GetComponent<RectTransform>(), new Vector2(89, -556), new Vector2(150, 34));

        var signOut = UiKit.Btn("Sign Out", panel, OnSignOut);
        FrontScreen.Place(signOut.GetComponent<RectTransform>(), new Vector2(267, -556), new Vector2(150, 34));

        Refresh();
    }

    void Refresh()
    {
        Say("…");
        AccountClient.ListCharacters((list, error) =>
        {
            if (this == null) return;
            if (list == null) { Say(error ?? "Could not read your characters."); return; }

            characters.Clear();
            characters.AddRange(list);
            selected = characters.Count > 0 ? 0 : -1;
            BuildRows();
            Say(characters.Count == 0 ? "No characters yet — make one." : "");
        });
    }

    void BuildRows()
    {
        for (int i = listRoot.childCount - 1; i >= 0; i--) Destroy(listRoot.GetChild(i).gameObject);
        rowBorders.Clear();

        for (int i = 0; i < characters.Count; i++)
        {
            var info = characters[i];
            int index = i;

            var border = UiKit.Round(UiKit.Img("Row" + i, listRoot, UiKit.ButtonEdge), UiKit.ButtonRadius);
            border.rectTransform.anchorMin = new Vector2(0, 1);
            border.rectTransform.anchorMax = new Vector2(1, 1);
            border.rectTransform.pivot = new Vector2(0.5f, 1);
            border.rectTransform.offsetMin = new Vector2(8, 0);
            border.rectTransform.offsetMax = new Vector2(-8, 0);
            border.rectTransform.sizeDelta = new Vector2(-16, 60);
            border.rectTransform.anchoredPosition = new Vector2(0, -8 - i * 68);
            rowBorders.Add(border);

            var fill = UiKit.Round(UiKit.Img("Fill", border.transform, RowIdle), UiKit.ButtonRadius);
            UiKit.Fill(fill.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));

            var name = UiKit.Label("Name", fill.transform, info.name, 18, FontStyle.Normal, TextAnchor.MiddleLeft);
            name.color = UiKit.TextMain;
            name.raycastTarget = false;
            UiKit.Fill(name.rectTransform, new Vector2(14, 22), new Vector2(-14, -6));

            // A level of 0 is the service saying nobody has played this character yet — not a level-zero hero.
            string line = (info.level > 0 ? "Level " + info.level : "Unplayed") + "   ·   " + info.modelName;
            var detail = UiKit.Label("Detail", fill.transform, line, 13, FontStyle.Normal, TextAnchor.MiddleLeft);
            detail.color = UiKit.TextDim;
            detail.raycastTarget = false;
            UiKit.Fill(detail.rectTransform, new Vector2(14, 6), new Vector2(-14, -26));

            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            button.onClick.AddListener(() => Select(index));
        }

        PaintSelection();
    }

    void Select(int index)
    {
        selected = index;
        PaintSelection();
    }

    void PaintSelection()
    {
        for (int i = 0; i < rowBorders.Count; i++)
            if (rowBorders[i] != null) rowBorders[i].color = i == selected ? UiKit.Accent : UiKit.ButtonEdge;

        bool has = selected >= 0 && selected < characters.Count;
        if (enterButton != null) enterButton.interactable = has;
        if (deleteButton != null) deleteButton.interactable = has;
    }

    // WHAT HAPPENS WHEN A CHARACTER IS CHOSEN — answered ONCE, off the fact of the choice, not off the button.
    // Exactly the same fault the sign-in screen had, one screen further along: the dismissal lived inside this
    // button's handler, so anything that chose a character without pressing it — a test driving the game through
    // the same functions a player's clicks reach — entered the world with this full-screen overlay still on top,
    // covering the running game. The world showed in the editor's scene view and nothing but this in the game view.
    void OnCharacterChosen()
    {
        if (this == null) return;                                        // already gone; nothing to do
        if (string.IsNullOrEmpty(AccountSession.CharacterId)) return;    // signing out also raises this
        Destroy(gameObject);
    }

    void OnEnable()  { AccountSession.OnChanged += OnCharacterChosen; }
    void OnDisable() { AccountSession.OnChanged -= OnCharacterChosen; }

    void OnEnter()
    {
        if (selected < 0 || selected >= characters.Count) return;
        var info = characters[selected];

        // From here the game knows who it is playing: SaveManager sees the session go live and loads them.
        // Choosing raises the event above, which is what takes this screen down — by any route in, not just this one.
        AccountSession.PlayAs(info.id, info.name, info.modelName, info.dyeSources, info.dyeTargets);
    }

    void OnDelete()
    {
        if (selected < 0 || selected >= characters.Count) return;
        var info = characters[selected];

        Say("Deleting " + info.name + "…");
        AccountClient.DeleteCharacter(info.id, (ok, error) =>
        {
            if (this == null) return;
            if (!ok) { Say(error ?? "Could not delete that character."); return; }
            Refresh();
        });
    }

    void OnSignOut()
    {
        AccountSession.SignOut();
        // Through the one function that puts that screen up, not a hand-built copy of it. Its own guard refuses a
        // second instance, and a copy assembled here was invisible to that guard — two stacked full-screen
        // overlays, the top one silently eating every click meant for the real one.
        SignInScreen.Open();
        Destroy(gameObject);
    }

    // ---- making one ------------------------------------------------------------------------------------

    void OpenCreate()
    {
        if (createPanel != null) return;

        var box = UiKit.Round(UiKit.Img("Create", panel, UiKit.WindowEdge), UiKit.WindowRadius);
        box.rectTransform.anchorMin = new Vector2(0, 1);
        box.rectTransform.anchorMax = new Vector2(1, 1);
        box.rectTransform.pivot = new Vector2(0.5f, 1);
        box.rectTransform.offsetMin = new Vector2(24, 0);
        box.rectTransform.offsetMax = new Vector2(-24, 0);
        box.rectTransform.sizeDelta = new Vector2(-48, 430);
        box.rectTransform.anchoredPosition = new Vector2(0, -76);
        createPanel = box.gameObject;

        var fill = UiKit.Round(UiKit.Img("Fill", box.transform, UiKit.PanelRaise), UiKit.WindowRadius - 1);
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        var inner = fill.transform;

        var heading = UiKit.Label("Heading", inner, "A new character", 20, FontStyle.Normal, TextAnchor.MiddleCenter);
        heading.color = UiKit.TitleText;
        FrontScreen.Row(heading.rectTransform, -16, 28);

        // The body, shown as itself. It owns the left column outright; nothing else is allowed into that strip.
        var frame = UiKit.Img("PreviewFrame", inner, UiKit.ButtonEdge);
        FrontScreen.Place(frame.rectTransform, new Vector2(-232, -56), new Vector2(212, 300));
        var shot = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        shot.transform.SetParent(frame.transform, false);
        UiKit.Fill((RectTransform)shot.transform, new Vector2(2, 2), new Vector2(-2, -2));
        preview = CharacterPreview.Create(shot.GetComponent<RawImage>(), transform);
        preview.OnBodyChanged += RebuildParts;

        // Everything else starts past the preview's right edge PLUS a gutter, so no label can ever sit on the art.
        const float TextLeft = 262f;
        const float TextRight = 28f;
        const float Mid = 117f;            // centre of the right column, for the fixed-width controls

        nameField = FrontScreen.Field(inner, "Name", -56, false, TextLeft, TextRight);

        var bodyCaption = UiKit.Label("BodyLabel", inner, "Body", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        bodyCaption.color = UiKit.TextDim;
        FrontScreen.Row(bodyCaption.rectTransform, -120, 18, TextLeft, TextRight);

        modelLabel = UiKit.Label("Body", inner, "…", 16, FontStyle.Normal, TextAnchor.MiddleCenter);
        modelLabel.color = UiKit.TextMain;
        FrontScreen.Place(modelLabel.rectTransform, new Vector2(Mid, -142), new Vector2(180, 32));

        var prev = UiKit.Btn("<", inner, () => StepModel(-1));
        FrontScreen.Place(prev.GetComponent<RectTransform>(), new Vector2(Mid - 110, -142), new Vector2(40, 32));
        var next = UiKit.Btn(">", inner, () => StepModel(1));
        FrontScreen.Place(next.GetComponent<RectTransform>(), new Vector2(Mid + 110, -142), new Vector2(40, 32));

        var colourCaption = UiKit.Label("ColourLabel", inner, "Colours", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        colourCaption.color = UiKit.TextDim;
        FrontScreen.Row(colourCaption.rectTransform, -188, 18, TextLeft, TextRight);

        partsRow = MakeSwatchRow(inner, -210, TextLeft, TextRight).transform;
        BuildMixer(inner, TextLeft, TextRight, Mid);

        var create = UiKit.Btn("Create", inner, OnCreate);
        FrontScreen.Place(create.GetComponent<RectTransform>(), new Vector2(Mid - 76, -370), new Vector2(140, 34));
        var cancel = UiKit.Btn("Cancel", inner, CloseCreate);
        FrontScreen.Place(cancel.GetComponent<RectTransform>(), new Vector2(Mid + 76, -370), new Vector2(140, 34));

        nameField.ActivateInputField();

        // Ask the SERVER which bodies are allowed — it is what will refuse one it doesn't recognise, so it is
        // the only list worth offering.
        AccountClient.ListModels((list, error) =>
        {
            if (this == null || modelLabel == null) return;
            if (list == null || list.Length == 0) { Say(error ?? "Could not read the list of bodies."); return; }
            models = list;
            modelIndex = 0;
            ShowModel();
        });
    }

    const int SwatchSize = 26;

    UnityEngine.UI.Slider redSlider, greenSlider, blueSlider;
    Image mixedSwatch;

    // A left-aligned strip beside the preview that swatches are laid into.
    static GameObject MakeSwatchRow(Transform inner, float top, float left, float right)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(inner, false);
        var group = go.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        group.spacing = 6;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        group.childAlignment = TextAnchor.MiddleLeft;
        FrontScreen.Row((RectTransform)go.transform, top, SwatchSize, left, right);
        return go;
    }

    // ANY colour, not a menu of twenty. Three sliders mix it and the chosen part follows as they move, so the
    // body in the frame IS the preview of the colour — there is no separate "apply" step to forget.
    void BuildMixer(Transform inner, float left, float right, float mid)
    {
        redSlider   = MixerRow(inner, "R", -250, left, right, new Color(0.9f, 0.3f, 0.3f));
        greenSlider = MixerRow(inner, "G", -280, left, right, new Color(0.4f, 0.85f, 0.4f));
        blueSlider  = MixerRow(inner, "B", -310, left, right, new Color(0.45f, 0.6f, 1f));

        // Sits level with the middle slider, past their right end, so the mix reads as the sum of the three.
        var border = UiKit.Img("Mixed", inner, UiKit.ButtonEdge);
        FrontScreen.Place(border.rectTransform, new Vector2(mid + 138, -266), new Vector2(52, 52));
        mixedSwatch = UiKit.Img("Fill", border.transform, Color.white);
        UiKit.Fill(mixedSwatch.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        mixedSwatch.raycastTarget = false;
    }

    UnityEngine.UI.Slider MixerRow(Transform inner, string label, float top, float left, float right, Color tint)
    {
        var caption = UiKit.Label(label + "Cap", inner, label, 12, FontStyle.Bold, TextAnchor.MiddleLeft);
        caption.color = tint;
        FrontScreen.Row(caption.rectTransform, top, 22, left, right + 210f);

        var slider = UiKit.MakeSlider(inner, 1f);
        FrontScreen.Row(slider.GetComponent<RectTransform>(), top - 4, 14, left + 18f, right + 150f);
        slider.onValueChanged.AddListener(_ => OnMixed());
        return slider;
    }

    Color Mixed => new Color(
        redSlider   != null ? redSlider.value   : 1f,
        greenSlider != null ? greenSlider.value : 1f,
        blueSlider  != null ? blueSlider.value  : 1f);

    void OnMixed()
    {
        if (mixedSwatch != null) mixedSwatch.color = Mixed;
        PaintSelectedPart(Mixed);
    }

    // Moving to another part shows THAT part's colour on the sliders, so the mixer always describes what is
    // selected rather than whatever was mixed last.
    void PullMixerFrom(Color colour)
    {
        if (redSlider == null) return;
        redSlider.SetValueWithoutNotify(colour.r);
        greenSlider.SetValueWithoutNotify(colour.g);
        blueSlider.SetValueWithoutNotify(colour.b);
        if (mixedSwatch != null) mixedSwatch.color = colour;
    }

    // The body decides which parts exist, so the parts strip is rebuilt whenever the body changes.
    void RebuildParts()
    {
        if (partsRow == null || preview == null) return;
        for (int i = partsRow.childCount - 1; i >= 0; i--) Destroy(partsRow.GetChild(i).gameObject);
        partSwatches.Clear();
        selectedPart = 0;

        var parts = preview.Parts;
        for (int i = 0; i < parts.Count; i++)
        {
            int index = i;
            var border = UiKit.Img("Part", partsRow, i == 0 ? UiKit.Accent : UiKit.ButtonEdge);
            var le = border.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredWidth = le.preferredHeight = SwatchSize;

            var fill = UiKit.Img("Fill", border.transform, preview.TargetFor(parts[index]));
            UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
            fill.raycastTarget = true;
            partSwatches.Add(fill);

            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            button.onClick.AddListener(() => SelectPart(index));
        }

        if (parts.Count > 0) PullMixerFrom(preview.TargetFor(parts[0]));
    }

    void SelectPart(int index)
    {
        selectedPart = index;
        var parts = preview != null ? preview.Parts : null;
        if (parts != null && index >= 0 && index < parts.Count) PullMixerFrom(preview.TargetFor(parts[index]));
        for (int i = 0; i < partSwatches.Count; i++)
        {
            var border = partSwatches[i] != null ? partSwatches[i].transform.parent.GetComponent<Image>() : null;
            if (border != null) border.color = i == selectedPart ? UiKit.Accent : UiKit.ButtonEdge;
        }
    }

    void PaintSelectedPart(Color colour)
    {
        if (preview == null) return;
        var parts = preview.Parts;
        if (selectedPart < 0 || selectedPart >= parts.Count) return;

        preview.SetPartColor(parts[selectedPart], colour);
        if (selectedPart < partSwatches.Count && partSwatches[selectedPart] != null)
            partSwatches[selectedPart].color = colour;
    }

    // What the player chose, in the form the character record stores: source colour → the colour it now wears.
    void CaptureDye(out string[] sources, out string[] targets)
    {
        var parts = preview != null ? preview.Parts : null;
        var src = new List<string>();
        var dst = new List<string>();
        if (parts != null)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                Color target = preview.TargetFor(parts[i]);
                if (target == parts[i]) continue;   // untouched parts keep the model's own art — nothing to store
                src.Add("#" + ColorUtility.ToHtmlStringRGB(parts[i]));
                dst.Add("#" + ColorUtility.ToHtmlStringRGB(target));
            }
        }
        sources = src.ToArray();
        targets = dst.ToArray();
    }

    void StepModel(int step)
    {
        if (models == null || models.Length == 0) return;
        modelIndex = ((modelIndex + step) % models.Length + models.Length) % models.Length;
        ShowModel();
    }

    // The name and the body in the frame are two views of one choice, so they are set together and never apart.
    void ShowModel()
    {
        if (models == null || models.Length == 0) return;
        if (modelLabel != null) modelLabel.text = models[modelIndex];
        if (preview != null) preview.Show(models[modelIndex]);
    }

    void CloseCreate()
    {
        if (createPanel == null) return;
        Destroy(createPanel);
        createPanel = null;
        nameField = null;
        modelLabel = null;
        // The preview owns a camera, a light and a picture in memory — none of it should outlive the panel.
        if (preview != null) { Destroy(preview.gameObject); preview = null; }
        partsRow = null;
        partSwatches.Clear();
        redSlider = greenSlider = blueSlider = null;
        mixedSwatch = null;
        Say("");
    }

    void OnCreate()
    {
        if (nameField == null || models == null || models.Length == 0) return;

        string name = nameField.text.Trim();
        if (name.Length < 3) { Say("A name needs at least 3 characters."); return; }

        CaptureDye(out var dyeSources, out var dyeTargets);

        Say("Creating " + name + "…");
        AccountClient.CreateCharacter(name, models[modelIndex], dyeSources, dyeTargets, (info, error) =>
        {
            if (this == null) return;
            if (info == null) { Say(error ?? "Could not create that character."); return; }
            CloseCreate();
            Refresh();
        });
    }

    void Say(string message) { if (status != null) status.text = message; }
}
