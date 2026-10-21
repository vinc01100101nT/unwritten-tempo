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
    static ThemeColor RowIdle => UiKit.SlotFill;

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
    StatHexagon hexagon;
    ColourPicker picker;
    CharacterPreview preview;
    string[] models;
    int modelIndex;

    // The hairstyles the body on show may wear ("none" first). The choice itself is held on the persistent character.
    List<string> hairs = new List<string>();

    // The Head and Body tabs. Head holds the hair's colour and its arrows change the hairstyle; Body holds the
    // clothes' colours and its arrows change the body. The label above the colours names the choice on show.
    Button headTab, bodyTab;
    bool headOpen;
    TextMeshProUGUI choiceLabel;

    // What was painted on each body (by model) and on each hairstyle (by id) while this window is open, so paging
    // away and back finds it as it was left. Forgotten when the window closes.
    readonly Dictionary<string, Color[]> bodyLooks = new Dictionary<string, Color[]>();
    readonly Dictionary<string, string> hairLooks = new Dictionary<string, string>();

    // The recolour being chosen: which part is selected, and the swatches showing each part's current colour.
    Transform partsRow;
    readonly List<Image> partSwatches = new List<Image>();
    int selectedPart;

    public static void Open()
    {
        // BACK AT THE FRONT DOOR, no move is going on — whatever was loading failed or was left.
        LoadingScreen.Hide();
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
        UiKit.Paint(status, UiKit.TitleText);
        FrontScreen.Row(status.rectTransform, -462, 18);

        enterButton = UiKit.PrimaryBtn("Enter World", panel, OnEnter);
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
            if (list == null) { Say(""); ErrorPopup.Show(error ?? "Could not read your characters."); return; }

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
            UiKit.Paint(name, UiKit.TextMain);
            name.raycastTarget = false;
            UiKit.Fill(name.rectTransform, new Vector2(14, 22), new Vector2(-14, -6));

            // A level of 0 is the service saying nobody has played this character yet — not a level-zero hero.
            string line = info.level > 0 ? "Level " + info.level : "Unplayed";
            var detail = UiKit.Label("Detail", fill.transform, line, 13, FontStyle.Normal, TextAnchor.MiddleLeft);
            UiKit.Paint(detail, UiKit.TextDim);
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
            if (rowBorders[i] != null) UiKit.Paint(rowBorders[i], i == selected ? UiKit.Accent : UiKit.ButtonEdge);

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
            if (!ok) { Say(""); ErrorPopup.Show(error ?? "Could not delete that character."); return; }
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

        // Its own window, in place of the list: the name across the top; under it the preview with a turn button
        // either side, the stat hexagon, and the Head / Body tabs holding the colours.
        var canvas = (Transform)selectWindow.transform.parent;
        var inner = FrontScreen.Panel(canvas, new Vector2(900, 560), "Character Creation", out createPanel);
        selectWindow.SetActive(false);

        nameField = FrontScreen.Field(inner, "Name", -14, false, 356, 20, NameCaptionWidth);

        // The body, shown as itself, between the two buttons that turn it.
        var frame = UiKit.Img("PreviewFrame", inner, UiKit.ButtonEdge);
        FrontScreen.Place(frame.rectTransform, new Vector2(-272, -60), new Vector2(212, 300));
        var shot = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        shot.transform.SetParent(frame.transform, false);
        UiKit.Fill((RectTransform)shot.transform, new Vector2(2, 2), new Vector2(-2, -2));
        preview = CharacterPreview.Create(shot.GetComponent<RawImage>(), transform);
        preview.OnBodyChanged += OnBodyShown;
        var turnLeft = UiKit.Btn("⟲", inner, () => preview?.Turn(TurnStep));
        FrontScreen.Place((RectTransform)turnLeft.transform, new Vector2(-408, -194), new Vector2(44, 32));
        var turnRight = UiKit.Btn("⟳", inner, () => preview?.Turn(-TurnStep));
        FrontScreen.Place((RectTransform)turnRight.transform, new Vector2(-136, -194), new Vector2(44, 32));

        // The six stats, pulled on the hexagon.
        var statsCaption = UiKit.Label("StatsLabel", inner, "Stats", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        UiKit.Paint(statsCaption, UiKit.TextDim);
        FrontScreen.Row(statsCaption.rectTransform, -60, 18, 356, 294);
        hexagon = StatHexagon.Build(inner);
        FrontScreen.Place(hexagon.rectTransform, new Vector2(31, -80), hexagon.rectTransform.sizeDelta);

        // The tabs, the choice on show between its two arrows with the colours, and the colour picker.
        const float Left = 626f, Right = 20f;
        headTab = UiKit.Btn("Head", inner, () => OpenTab(true));
        FrontScreen.Place((RectTransform)headTab.transform, new Vector2(238.5f, -60), new Vector2(125, 30));
        bodyTab = UiKit.Btn("Body", inner, () => OpenTab(false));
        FrontScreen.Place((RectTransform)bodyTab.transform, new Vector2(367.5f, -60), new Vector2(125, 30));

        choiceLabel = UiKit.Label("Choice", inner, "…", 15, FontStyle.Normal, TextAnchor.MiddleCenter);
        UiKit.Paint(choiceLabel, UiKit.TextMain);
        FrontScreen.Row(choiceLabel.rectTransform, -98, 22, Left, Right);
        var prev = UiKit.Btn("<", inner, () => Step(-1));
        FrontScreen.Place((RectTransform)prev.transform, new Vector2(191, -126), new Vector2(30, 30));
        var next = UiKit.Btn(">", inner, () => Step(1));
        FrontScreen.Place((RectTransform)next.transform, new Vector2(415, -126), new Vector2(30, 30));
        partsRow = MakeSwatchRow(inner, -128, Left + 38, Right + 38).transform;

        picker = ColourPicker.Build(inner);
        FrontScreen.Row(picker.Root, -166, ColourPicker.Height, Left, Right);
        picker.OnChanged += PaintSelectedPart;

        createStatus = UiKit.Label("Status", inner, "", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        UiKit.Paint(createStatus, UiKit.TitleText);
        FrontScreen.Row(createStatus.rectTransform, -514, 18, 20, 340);
        var back = UiKit.Btn("Back", inner, CloseCreate);
        FrontScreen.Place(back.GetComponent<RectTransform>(), new Vector2(208, -506), new Vector2(140, 34));
        var create = UiKit.PrimaryBtn("Create", inner, OnCreate);
        FrontScreen.Place(create.GetComponent<RectTransform>(), new Vector2(360, -506), new Vector2(140, 34));

        nameField.ActivateInputField();

        // A new character starts with nothing chosen — each body shows the hair it was made with — never with the
        // hair of whichever character this screen was last left from.
        ChooseHair("", "");
        OpenTab(false);

        // Ask the SERVER which bodies are allowed — it is what will refuse one it doesn't recognise, so it is
        // the only list worth offering.
        AccountClient.ListModels((list, error) =>
        {
            if (this == null || choiceLabel == null) return;
            if (list == null || list.Length == 0) { Say(""); ErrorPopup.Show(error ?? "Could not read the list of bodies."); return; }
            models = list;
            modelIndex = 0;
            ShowModel();
        });
    }

    // The width of the "Name" caption, which sits on the box's own line.
    const float NameCaptionWidth = 44f;

    void OpenTab(bool head)
    {
        headOpen = head;
        UiKit.MarkTab(headTab, head);
        UiKit.MarkTab(bodyTab, !head);
        preview?.FocusHead(head);
        ShowChoice();
        RebuildParts();
    }

    // The arrows beside the colours: the hairstyle on the Head tab, the body on the Body tab.
    void Step(int step)
    {
        if (headOpen) StepHair(step);
        else StepModel(step);
    }

    // The label and the choice on show are two views of one choice, so they are set together. The label is the
    // choice's place in its list; a model's own file name is never shown.
    void ShowChoice()
    {
        if (choiceLabel == null) return;
        if (headOpen)
        {
            string id = ChosenHair();
            int at = hairs.IndexOf(id);
            choiceLabel.text = id == HeadPieces.NoHair || at < 0 ? "No hair" : "Hair " + at + " of " + (hairs.Count - 1);
        }
        else choiceLabel.text = models == null || models.Length == 0 ? "…" : "Body " + (modelIndex + 1) + " of " + models.Length;
    }

    // How far one press of a turn button turns the body.
    const float TurnStep = 45f;

    const int SwatchSize = 26;

    // A strip between the two arrows that swatches are laid into, centred.
    static GameObject MakeSwatchRow(Transform inner, float top, float left, float right)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(inner, false);
        var group = go.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        group.spacing = 6;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        group.childAlignment = TextAnchor.MiddleCenter;
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

        // The hair's one swatch on the Head tab (none with no hair), the clothes on the Body tab.
        int count = headOpen ? (HasHair ? 1 : 0) : preview.Parts.Count;
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

    Color ColourOf(int index)
    {
        if (headOpen) return HairColour();
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
            if (border != null) UiKit.Paint(border, i == selectedPart ? UiKit.Accent : UiKit.ButtonEdge);
        }
    }

    void PaintSelectedPart(Color colour)
    {
        if (preview == null) return;
        var parts = preview.Parts;
        if (headOpen) { if (HasHair) ChooseHair(ChosenHair(), "#" + ColorUtility.ToHtmlStringRGB(colour)); else return; }
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
        KeepBodyLook();
        modelIndex = ((modelIndex + step) % models.Length + models.Length) % models.Length;
        ShowModel();
    }

    void ShowModel()
    {
        if (models == null || models.Length == 0) return;
        // The hair the new body may wear. A hair it cannot wear (an animal's head) becomes no hair.
        hairs = HeadPieces.ChoicesFor(models[modelIndex]);
        if (!hairs.Contains(ChosenHair())) ChooseHair(HeadPieces.NoHair, "");
        ShowChoice();
        if (preview != null) preview.Show(models[modelIndex]);
        RebuildParts();
    }

    // Remember what the body on show is wearing before paging away from it.
    void KeepBodyLook()
    {
        if (preview == null || string.IsNullOrEmpty(preview.BodyOnStage)) return;
        var parts = preview.Parts;
        if (parts.Count == 0) return;
        var worn = new Color[parts.Count];
        for (int i = 0; i < worn.Length; i++) worn[i] = preview.TargetFor(parts[i]);
        bodyLooks[preview.BodyOnStage] = worn;
    }

    // A body just put up wears what was painted on it earlier in this window, if anything was.
    void OnBodyShown()
    {
        if (preview != null && !string.IsNullOrEmpty(preview.BodyOnStage)
            && bodyLooks.TryGetValue(preview.BodyOnStage, out var worn))
        {
            var parts = preview.Parts;
            for (int i = 0; i < parts.Count && i < worn.Length; i++)
                if (preview.TargetFor(parts[i]) != worn[i]) preview.SetPartColor(parts[i], worn[i]);
        }
        RebuildParts();
    }

    // ---- the hair --------------------------------------------------------------------------------------
    // Held on the persistent character, exactly as the colours are, so the preview shows the one choice; only
    // written down when the character is created.

    void StepHair(int step)
    {
        if (hairs.Count == 0) return;
        string from = ChosenHair();
        if (from != HeadPieces.NoHair) hairLooks[from] = "#" + ColorUtility.ToHtmlStringRGB(HairColour());
        int at = Mathf.Max(0, hairs.IndexOf(from));
        at = ((at + step) % hairs.Count + hairs.Count) % hairs.Count;
        string id = hairs[at];
        // The colour this hairstyle was given earlier in this window, or else its own colour.
        ChooseHair(id, id != HeadPieces.NoHair && hairLooks.TryGetValue(id, out var hex) ? hex : "");
        ShowChoice();
        RebuildParts();
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
        choiceLabel = null;
        headTab = bodyTab = null;
        bodyLooks.Clear();
        hairLooks.Clear();
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
        if (name.Length < 3) { ErrorPopup.Show("A name needs at least 3 characters."); return; }

        CaptureDye(out var dyeSources, out var dyeTargets);
        // Always an explicit hair: the one on show, even when nobody pressed the hair buttons.
        Resolved(out var hairStyle, out var hairColour);

        Say("Creating " + name + "…");
        AccountClient.CreateCharacter(name, models[modelIndex], dyeSources, dyeTargets, hairStyle, hairColour,
                                      hexagon.Spread, (info, error) =>
        {
            if (this == null) return;
            if (info == null) { Say(""); ErrorPopup.Show(error ?? "Could not create that character."); return; }
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
