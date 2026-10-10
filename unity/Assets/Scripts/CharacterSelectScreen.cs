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
    GameObject selectWindow;
    Transform listRoot;
    TextMeshProUGUI status;
    Button enterButton, deleteButton;

    readonly List<AccountClient.CharacterInfo> characters = new List<AccountClient.CharacterInfo>();
    readonly List<Image> rowBorders = new List<Image>();
    int selected = -1;

    // Character creation — a window of its own, shown in place of the character list.
    GameObject createPanel;
    TextMeshProUGUI createStatus;
    TMP_InputField nameField;
    TextMeshProUGUI modelLabel;
    StatHexagon hexagon;
    ColourPicker picker;
    CharacterPreview preview;
    string[] models;
    int modelIndex;

    // The hairstyles the body on show may wear ("none" first). The choice itself is held on the persistent character.
    TextMeshProUGUI hairLabel;
    List<string> hairs = new List<string>();

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
        // The title is the blue bar's; the room the old big title took (56) comes off the top of everything.
        panel = FrontScreen.Panel(canvas, new Vector2(780, 564), "Your Characters", out selectWindow);

        var listBox = UiKit.Img("List", panel, UiKit.PanelRaise);
        listBox.rectTransform.anchorMin = new Vector2(0, 1);
        listBox.rectTransform.anchorMax = new Vector2(1, 1);
        listBox.rectTransform.pivot = new Vector2(0.5f, 1);
        listBox.rectTransform.offsetMin = new Vector2(24, 0);
        listBox.rectTransform.offsetMax = new Vector2(-24, 0);
        listBox.rectTransform.sizeDelta = new Vector2(-48, 430);
        listBox.rectTransform.anchoredPosition = new Vector2(0, -20);
        listRoot = listBox.transform;

        status = UiKit.Label("Status", panel, "", 13, FontStyle.Normal, TextAnchor.MiddleCenter);
        status.color = UiKit.TitleText;
        FrontScreen.Row(status.rectTransform, -462, 18);

        enterButton = UiKit.Btn("Enter World", panel, OnEnter);
        FrontScreen.Place(enterButton.GetComponent<RectTransform>(), new Vector2(-267, -500), new Vector2(150, 34));

        var newButton = UiKit.Btn("New", panel, OpenCreate);
        FrontScreen.Place(newButton.GetComponent<RectTransform>(), new Vector2(-89, -500), new Vector2(150, 34));

        deleteButton = UiKit.Btn("Delete", panel, OnDelete);
        FrontScreen.Place(deleteButton.GetComponent<RectTransform>(), new Vector2(89, -500), new Vector2(150, 34));

        var signOut = UiKit.Btn("Sign Out", panel, OnSignOut);
        FrontScreen.Place(signOut.GetComponent<RectTransform>(), new Vector2(267, -500), new Vector2(150, 34));

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
            string line = info.level > 0 ? "Level " + info.level : "Unplayed";
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
        AccountSession.PlayAs(info.id, info.name, info.modelName, info.dyeSources, info.dyeTargets,
                              info.hairStyle, info.hairColour);
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

        // Its own window, in place of the list: the preview and its turn buttons on the left, the stat hexagon in the
        // middle, name, body and colours on the right.
        var canvas = (Transform)selectWindow.transform.parent;
        var inner = FrontScreen.Panel(canvas, new Vector2(900, 560), "Character Creation", out createPanel);
        selectWindow.SetActive(false);

        // The body, shown as itself, with the two buttons that turn it.
        var frame = UiKit.Img("PreviewFrame", inner, UiKit.ButtonEdge);
        FrontScreen.Place(frame.rectTransform, new Vector2(-324, -20), new Vector2(212, 300));
        var shot = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        shot.transform.SetParent(frame.transform, false);
        UiKit.Fill((RectTransform)shot.transform, new Vector2(2, 2), new Vector2(-2, -2));
        preview = CharacterPreview.Create(shot.GetComponent<RawImage>(), transform);
        preview.OnBodyChanged += RebuildParts;
        var turnLeft = UiKit.Btn("⟲", inner, () => preview?.Turn(TurnStep));
        FrontScreen.Place((RectTransform)turnLeft.transform, new Vector2(-351, -330), new Vector2(44, 32));
        var turnRight = UiKit.Btn("⟳", inner, () => preview?.Turn(-TurnStep));
        FrontScreen.Place((RectTransform)turnRight.transform, new Vector2(-297, -330), new Vector2(44, 32));

        // The six stats, pulled on the hexagon.
        var statsCaption = UiKit.Label("StatsLabel", inner, "Stats", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        statsCaption.color = UiKit.TextDim;
        FrontScreen.Row(statsCaption.rectTransform, -20, 18, 256, 394);
        hexagon = StatHexagon.Build(inner);
        FrontScreen.Place(hexagon.rectTransform, new Vector2(-69, -44), hexagon.rectTransform.sizeDelta);

        // Name, body, colours — the right-hand column.
        const float Left = 530f, Right = 20f;
        nameField = FrontScreen.Field(inner, "Name", -20, false, Left, Right);

        modelLabel = Stepper(inner, "Body", -76, Left, Right, StepModel);
        hairLabel = Stepper(inner, "Hair", -136, Left, Right, StepHair);

        var colourCaption = UiKit.Label("ColourLabel", inner, "Colours", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        colourCaption.color = UiKit.TextDim;
        FrontScreen.Row(colourCaption.rectTransform, -196, 18, Left, Right);
        partsRow = MakeSwatchRow(inner, -218, Left, Right).transform;

        picker = ColourPicker.Build(inner);
        FrontScreen.Row(picker.Root, -254, ColourPicker.Height, Left, Right);
        picker.OnChanged += PaintSelectedPart;

        createStatus = UiKit.Label("Status", inner, "", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        createStatus.color = UiKit.TitleText;
        FrontScreen.Row(createStatus.rectTransform, -514, 18, 20, 340);
        var create = UiKit.Btn("Create", inner, OnCreate);
        FrontScreen.Place(create.GetComponent<RectTransform>(), new Vector2(213, -506), new Vector2(140, 34));
        var cancel = UiKit.Btn("Cancel", inner, CloseCreate);
        FrontScreen.Place(cancel.GetComponent<RectTransform>(), new Vector2(365, -506), new Vector2(140, 34));

        nameField.ActivateInputField();

        // A new character starts with nothing chosen — each body shows the hair it was made with — never with the
        // hair of whichever character this screen was last left from.
        ChooseHair("", "");

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

    // A caption with "<" and ">" either side of a label naming the choice — the body's and the hair's, built by one
    // function so the two can never come to look or behave differently.
    static TextMeshProUGUI Stepper(Transform inner, string caption, float top, float left, float right,
                                   System.Action<int> step)
    {
        var cap = UiKit.Label(caption + "Label", inner, caption, 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        cap.color = UiKit.TextDim;
        FrontScreen.Row(cap.rectTransform, top, 18, left, right);
        var label = UiKit.Label(caption, inner, "…", 15, FontStyle.Normal, TextAnchor.MiddleCenter);
        label.color = UiKit.TextMain;
        FrontScreen.Place(label.rectTransform, new Vector2(255, top - 20), new Vector2(160, 30));
        var prev = UiKit.Btn("<", inner, () => step(-1));
        FrontScreen.Place(prev.GetComponent<RectTransform>(), new Vector2(150, top - 20), new Vector2(40, 30));
        var next = UiKit.Btn(">", inner, () => step(1));
        FrontScreen.Place(next.GetComponent<RectTransform>(), new Vector2(360, top - 20), new Vector2(40, 30));
        return label;
    }

    // How far one press of a turn button turns the body.
    const float TurnStep = 45f;

    const int SwatchSize = 26;

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

    // The body decides which parts exist, so the parts strip is rebuilt whenever the body changes.
    void RebuildParts()
    {
        if (partsRow == null || preview == null) return;
        for (int i = partsRow.childCount - 1; i >= 0; i--) Destroy(partsRow.GetChild(i).gameObject);
        partSwatches.Clear();
        selectedPart = 0;

        var parts = preview.Parts;
        // The clothes, then the hair — one more swatch, painted by the same picker, whenever there is hair.
        int count = parts.Count + (HasHair ? 1 : 0);
        for (int i = 0; i < count; i++)
        {
            int index = i;
            var border = UiKit.Img("Part", partsRow, i == 0 ? UiKit.Accent : UiKit.ButtonEdge);
            var le = border.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredWidth = le.preferredHeight = SwatchSize;

            var fill = UiKit.Img("Fill", border.transform, ColourOf(index));
            UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
            fill.raycastTarget = true;
            partSwatches.Add(fill);

            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            button.onClick.AddListener(() => SelectPart(index));
        }

        if (count > 0) picker?.Set(ColourOf(0));
    }

    // The swatch after the last clothes part is the hair's.
    bool IsHairSwatch(int index) => preview != null && index == preview.Parts.Count && HasHair;

    Color ColourOf(int index)
    {
        if (IsHairSwatch(index)) return HairColour();
        var parts = preview.Parts;
        return index >= 0 && index < parts.Count ? preview.TargetFor(parts[index]) : Color.white;
    }

    void SelectPart(int index)
    {
        selectedPart = index;
        if (preview != null) picker?.Set(ColourOf(index));
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
        if (IsHairSwatch(selectedPart)) ChooseHair(ChosenHair(), "#" + ColorUtility.ToHtmlStringRGB(colour));
        else if (selectedPart < 0 || selectedPart >= parts.Count) return;
        else preview.SetPartColor(parts[selectedPart], colour);
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

    // The label and the body in the frame are two views of one choice, so they are set together and never apart.
    // The label is the body's place in the list; the model's own file name is never shown.
    void ShowModel()
    {
        if (models == null || models.Length == 0) return;
        if (modelLabel != null) modelLabel.text = "Body " + (modelIndex + 1) + " of " + models.Length;
        // The hair the new body may wear. A hair it cannot wear (an animal's head) becomes no hair.
        hairs = HeadPieces.ChoicesFor(models[modelIndex]);
        if (!hairs.Contains(ChosenHair())) ChooseHair(HeadPieces.NoHair, "");
        ShowHair();
        if (preview != null) preview.Show(models[modelIndex]);
        RebuildParts();
    }

    // ---- the hair --------------------------------------------------------------------------------------
    // Held on the persistent character, exactly as the colours are, so the preview shows the one choice; only
    // written down when the character is created.

    void StepHair(int step)
    {
        if (hairs.Count == 0) return;
        int at = Mathf.Max(0, hairs.IndexOf(ChosenHair()));
        at = ((at + step) % hairs.Count + hairs.Count) % hairs.Count;
        string id = hairs[at];
        ChooseHair(id, id == HeadPieces.NoHair ? "" : "#" + ColorUtility.ToHtmlStringRGB(HairColour()));
        ShowHair();
        RebuildParts();
    }

    void ShowHair()
    {
        if (hairLabel == null) return;
        string id = ChosenHair();
        int at = hairs.IndexOf(id);
        hairLabel.text = id == HeadPieces.NoHair || at < 0 ? "No hair" : "Hair " + at + " of " + (hairs.Count - 1);
    }

    bool HasHair => ChosenHair() != HeadPieces.NoHair;

    // The hairstyle on show: the one chosen, or — before anything is chosen — the one the body was made with.
    string ChosenHair()
    {
        Resolved(out var id, out _);
        return id;
    }

    Color HairColour()
    {
        Resolved(out var id, out var hex);
        return ColorUtility.TryParseHtmlString(hex, out var c) ? c : HeadPieces.OwnColourOf(id);
    }

    void Resolved(out string id, out string hex)
    {
        var c = Character.Instance;
        string model = models != null && models.Length > 0 ? models[modelIndex] : "";
        if (c == null) { id = HeadPieces.NoHair; hex = ""; return; }
        HeadPieces.Resolve(model, c.HairStyle, c.HairColour, c.DyeSources, c.DyeTargets, out id, out hex);
    }

    static void ChooseHair(string id, string hex)
    {
        var c = Character.Instance;
        if (c != null) c.SetHair(id, hex);
    }

    void CloseCreate()
    {
        if (createPanel == null) return;
        Destroy(createPanel);
        createPanel = null;
        createStatus = null;
        nameField = null;
        modelLabel = null;
        hairLabel = null;
        hexagon = null;
        picker = null;
        if (selectWindow != null) selectWindow.SetActive(true);
        // The preview owns a camera, a light and a picture in memory — none of it should outlive the panel.
        if (preview != null) { Destroy(preview.gameObject); preview = null; }
        partsRow = null;
        partSwatches.Clear();
        Say("");
    }

    void OnCreate()
    {
        if (nameField == null || models == null || models.Length == 0) return;

        string name = nameField.text.Trim();
        if (name.Length < 3) { Say("A name needs at least 3 characters."); return; }

        CaptureDye(out var dyeSources, out var dyeTargets);
        // Always an explicit hair: the one on show, even when nobody pressed the hair buttons.
        Resolved(out var hairStyle, out var hairColour);

        Say("Creating " + name + "…");
        AccountClient.CreateCharacter(name, models[modelIndex], dyeSources, dyeTargets, hairStyle, hairColour,
                                      hexagon.Spread, (info, error) =>
        {
            if (this == null) return;
            if (info == null) { Say(error ?? "Could not create that character."); return; }
            CloseCreate();
            Refresh();
        });
    }

    // Said on whichever window is showing — the creation window while it is up, the list otherwise.
    void Say(string message)
    {
        var line = createStatus != null ? createStatus : status;
        if (line != null) line.text = message;
    }
}
