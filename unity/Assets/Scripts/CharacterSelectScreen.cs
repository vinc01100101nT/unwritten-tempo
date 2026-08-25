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
    // Selection wears the cyan the rest of the UI already uses for "this one"; everything else stays gold.
    static readonly Color RowIdle = new Color(0.07f, 0.075f, 0.09f, 1f);

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

    public static void Open()
    {
        var go = new GameObject("CharacterSelectScreen") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        go.AddComponent<CharacterSelectScreen>();
    }

    void Start()
    {
        var canvas = FrontScreen.Canvas(transform);
        panel = FrontScreen.Panel(canvas, new Vector2(720, 520));

        var title = UiKit.Label("Title", panel, "Your Characters", 28, FontStyle.Normal, TextAnchor.MiddleCenter, title: true);
        title.color = UiKit.Gold;
        FrontScreen.Row(title.rectTransform, -28, 36);

        var listBox = UiKit.Img("List", panel, new Color(0, 0, 0, 0.18f));
        listBox.rectTransform.anchorMin = new Vector2(0, 1);
        listBox.rectTransform.anchorMax = new Vector2(1, 1);
        listBox.rectTransform.pivot = new Vector2(0.5f, 1);
        listBox.rectTransform.offsetMin = new Vector2(24, 0);
        listBox.rectTransform.offsetMax = new Vector2(-24, 0);
        listBox.rectTransform.sizeDelta = new Vector2(-48, 340);
        listBox.rectTransform.anchoredPosition = new Vector2(0, -76);
        listRoot = listBox.transform;

        status = UiKit.Label("Status", panel, "", 13, FontStyle.Normal, TextAnchor.MiddleCenter);
        status.color = UiKit.Gold;
        FrontScreen.Row(status.rectTransform, -424, 18);

        enterButton = UiKit.Btn("Enter World", panel, OnEnter);
        FrontScreen.Place(enterButton.GetComponent<RectTransform>(), new Vector2(-243, -458), new Vector2(150, 34));

        var newButton = UiKit.Btn("New", panel, OpenCreate);
        FrontScreen.Place(newButton.GetComponent<RectTransform>(), new Vector2(-81, -458), new Vector2(150, 34));

        deleteButton = UiKit.Btn("Delete", panel, OnDelete);
        FrontScreen.Place(deleteButton.GetComponent<RectTransform>(), new Vector2(81, -458), new Vector2(150, 34));

        var signOut = UiKit.Btn("Sign Out", panel, OnSignOut);
        FrontScreen.Place(signOut.GetComponent<RectTransform>(), new Vector2(243, -458), new Vector2(150, 34));

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

            var border = UiKit.Img("Row" + i, listRoot, UiKit.Gold);
            border.rectTransform.anchorMin = new Vector2(0, 1);
            border.rectTransform.anchorMax = new Vector2(1, 1);
            border.rectTransform.pivot = new Vector2(0.5f, 1);
            border.rectTransform.offsetMin = new Vector2(8, 0);
            border.rectTransform.offsetMax = new Vector2(-8, 0);
            border.rectTransform.sizeDelta = new Vector2(-16, 60);
            border.rectTransform.anchoredPosition = new Vector2(0, -8 - i * 68);
            rowBorders.Add(border);

            var fill = UiKit.Img("Fill", border.transform, RowIdle);
            UiKit.Fill(fill.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));

            var name = UiKit.Label("Name", fill.transform, info.name, 18, FontStyle.Normal, TextAnchor.MiddleLeft, title: true);
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
            if (rowBorders[i] != null) rowBorders[i].color = i == selected ? UiKit.Cyan : UiKit.Gold;

        bool has = selected >= 0 && selected < characters.Count;
        if (enterButton != null) enterButton.interactable = has;
        if (deleteButton != null) deleteButton.interactable = has;
    }

    void OnEnter()
    {
        if (selected < 0 || selected >= characters.Count) return;
        var info = characters[selected];

        // From here the game knows who it is playing: SaveManager sees the session go live and loads them.
        AccountSession.PlayAs(info.id, info.name, info.modelName, info.dyeSources, info.dyeTargets);
        Destroy(gameObject);
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
        var go = new GameObject("SignInScreen") { hideFlags = HideFlags.DontSaveInEditor };
        DontDestroyOnLoad(go);
        go.AddComponent<SignInScreen>();
        Destroy(gameObject);
    }

    // ---- making one ------------------------------------------------------------------------------------

    void OpenCreate()
    {
        if (createPanel != null) return;

        var box = UiKit.Img("Create", panel, UiKit.Gold);
        box.rectTransform.anchorMin = new Vector2(0, 1);
        box.rectTransform.anchorMax = new Vector2(1, 1);
        box.rectTransform.pivot = new Vector2(0.5f, 1);
        box.rectTransform.offsetMin = new Vector2(24, 0);
        box.rectTransform.offsetMax = new Vector2(-24, 0);
        box.rectTransform.sizeDelta = new Vector2(-48, 340);
        box.rectTransform.anchoredPosition = new Vector2(0, -76);
        createPanel = box.gameObject;

        var fill = UiKit.Img("Fill", box.transform, UiKit.PanelRaise);
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        var inner = fill.transform;

        var heading = UiKit.Label("Heading", inner, "A new character", 20, FontStyle.Normal, TextAnchor.MiddleCenter, title: true);
        heading.color = UiKit.Gold;
        FrontScreen.Row(heading.rectTransform, -16, 28);

        // The body you are choosing, shown as itself. A name in a box tells you nothing about what you will play.
        var frame = UiKit.Img("PreviewFrame", inner, UiKit.Gold);
        FrontScreen.Place(frame.rectTransform, new Vector2(-216, -56), new Vector2(210, 260));
        var shot = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        shot.transform.SetParent(frame.transform, false);
        UiKit.Fill((RectTransform)shot.transform, new Vector2(2, 2), new Vector2(-2, -2));
        preview = CharacterPreview.Create(shot.GetComponent<RawImage>(), transform);

        // Name and body sit to the RIGHT of the preview, sharing its lines.
        const float TextLeft = 250f;
        nameField = FrontScreen.Field(inner, "Name", -64, false, TextLeft, 30f);

        var bodyCaption = UiKit.Label("BodyLabel", inner, "Body", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        bodyCaption.color = UiKit.TextDim;
        FrontScreen.Row(bodyCaption.rectTransform, -132, 18, TextLeft, 30f);

        modelLabel = UiKit.Label("Body", inner, "…", 16, FontStyle.Normal, TextAnchor.MiddleCenter);
        modelLabel.color = UiKit.TextMain;
        FrontScreen.Place(modelLabel.rectTransform, new Vector2(110, -154), new Vector2(200, 32));

        var prev = UiKit.Btn("<", inner, () => StepModel(-1));
        FrontScreen.Place(prev.GetComponent<RectTransform>(), new Vector2(-20, -154), new Vector2(40, 32));
        var next = UiKit.Btn(">", inner, () => StepModel(1));
        FrontScreen.Place(next.GetComponent<RectTransform>(), new Vector2(240, -154), new Vector2(40, 32));

        var create = UiKit.Btn("Create", inner, OnCreate);
        FrontScreen.Place(create.GetComponent<RectTransform>(), new Vector2(30, -262), new Vector2(140, 34));
        var cancel = UiKit.Btn("Cancel", inner, CloseCreate);
        FrontScreen.Place(cancel.GetComponent<RectTransform>(), new Vector2(190, -262), new Vector2(140, 34));

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
        Say("");
    }

    void OnCreate()
    {
        if (nameField == null || models == null || models.Length == 0) return;

        string name = nameField.text.Trim();
        if (name.Length < 3) { Say("A name needs at least 3 characters."); return; }

        Say("Creating " + name + "…");
        AccountClient.CreateCharacter(name, models[modelIndex], (info, error) =>
        {
            if (this == null) return;
            if (info == null) { Say(error ?? "Could not create that character."); return; }
            CloseCreate();
            Refresh();
        });
    }

    void Say(string message) { if (status != null) status.text = message; }
}
