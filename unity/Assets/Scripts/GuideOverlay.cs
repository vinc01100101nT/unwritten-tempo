using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE TUTORIAL GUIDE: the whole screen dark except the one part to press next, an arrow pointing at it, and a short
/// text. It decides nothing — which quest it teaches, which step it is on and whether that step can be done now all
/// come from the server (<see cref="QuestStore.Guide"/>): it teaches the tutorial that is playing, and nothing else; the step's hints come from the quest's data; the parts of the screen
/// are found by the names their windows gave them (<see cref="GuideMarker"/>).
///
/// <para><b>Which hint shows:</b> the last of the step's hints whose part is on screen right now — in an open window,
/// even if its list is scrolled past it; that one part, and only it, is scrolled into view. Going back a page, or
/// picking the wrong piece, simply brings an earlier hint back — nothing is remembered, so nothing can get stuck.</para>
///
/// <para><b>It never traps anyone.</b> The dark stops the mouse only while its one part is really on screen and is the
/// station the step is worked at; it never touches the keyboard, so Esc, movement and F behave exactly as always; it is
/// gone while the character is dead or not in the world; and when the server says the step cannot be done right now
/// (nothing to pay with, the piece is worn) it is only a sentence, with nothing dark.</para>
/// </summary>
public class GuideOverlay : MonoBehaviour
{
    const float Darkness = 0.62f;
    const float FramePad = 3f, FrameWidth = 2f, TipWidth = 240f;

    RectTransform root;
    readonly Image[] dark = new Image[4];
    readonly Image[] frame = new Image[4];
    Image pointer, waitCover;
    RectTransform tip;
    TMPro.TMP_Text tipText;
    Canvas canvas;
    readonly Vector3[] corners = new Vector3[4];
    string shownText;
    Button hookedButton;
    Transform raisedWindow;

    void Start()
    {
        if (GameServer.IsServer && !GameNetwork.TheWorldIsElsewhere) { enabled = false; return; }
        canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { enabled = false; return; }
        Build();
    }

    void Build()
    {
        var go = new GameObject("TutorialGuide", typeof(RectTransform));
        root = (RectTransform)go.transform;
        root.SetParent(canvas.transform, false);
        UiKit.Fill(root, Vector2.zero, Vector2.zero);

        var shade = new Color(0f, 0f, 0f, Darkness);
        for (int i = 0; i < 4; i++)
        {
            // The dark is what stops the mouse: each panel takes the click, so nothing under it is pressed.
            dark[i] = UiKit.Img("Dark", root, shade);
            dark[i].raycastTarget = true;
            frame[i] = UiKit.Img("Frame", root, UiKit.Accent);
            frame[i].raycastTarget = false;
        }
        // Laid over the lit part while the server answers a press that costs something, so a quick second press
        // cannot spend the same materials twice.
        waitCover = UiKit.Img("Waiting", root, new Color(0f, 0f, 0f, Darkness * 0.5f));
        waitCover.raycastTarget = true;

        pointer = UiKit.Img("Pointer", root, Color.white);
        pointer.raycastTarget = false;
        pointer.sprite = Resources.Load<Sprite>("Guide/GuidePointer");
        pointer.preserveAspect = true;
        pointer.rectTransform.sizeDelta = new Vector2(40f, 40f);

        var tipBody = UiKit.Img("Tip", root, UiKit.WindowEdge);
        tipBody.raycastTarget = false;
        tip = tipBody.rectTransform;
        tip.pivot = new Vector2(0f, 0.5f);
        var fill = UiKit.Img("Fill", tip, UiKit.PanelFill);
        fill.raycastTarget = false;
        UiKit.Fill(fill.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        tipText = UiKit.Label("Text", fill.transform, "", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        tipText.color = UiKit.TextMain;
        tipText.raycastTarget = false;
        tipText.textWrappingMode = TMPro.TextWrappingModes.Normal;
        UiKit.Fill(tipText.rectTransform, new Vector2(10, 6), new Vector2(-10, -6));

        go.SetActive(false);
    }

    void LateUpdate()
    {
        if (root == null) return;
        if (!Decide(out var target, out string text, out bool costs))
        {
            if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
            Unhook();
            return;
        }

        if (!root.gameObject.activeSelf)
        {
            root.gameObject.SetActive(true);
            UiLayers.Raise(root, UiLayers.Layer.Guide);
        }
        if (text != shownText) { shownText = text; tipText.text = text; }

        bool lit = target != null;
        for (int i = 0; i < 4; i++) { dark[i].enabled = lit; frame[i].enabled = lit; }
        pointer.enabled = lit;

        if (!lit)
        {
            // A SENTENCE ONLY, low in the middle of the screen, with nothing dark.
            waitCover.enabled = false;
            Unhook();
            PlaceTip(new Vector2(-TipWidth * 0.5f, -root.rect.height * 0.5f + 90f), 2);
            return;
        }

        // The window holding the part comes to the front of the windows, so no other window can cover it.
        var window = WindowOf(target);
        if (window != raisedWindow) { raisedWindow = window; if (window != null) UiLayers.Raise(window, UiLayers.Layer.Window); UiLayers.Raise(root, UiLayers.Layer.Guide); }

        ((RectTransform)target.transform).GetWorldCorners(corners);
        Vector2 min = root.InverseTransformPoint(corners[0]), max = root.InverseTransformPoint(corners[2]);
        min -= new Vector2(FramePad, FramePad);
        max += new Vector2(FramePad, FramePad);
        var r = root.rect;
        Place(dark[0], new Vector2(r.xMin, max.y), new Vector2(r.xMax, r.yMax));      // above
        Place(dark[1], new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, min.y));      // below
        Place(dark[2], new Vector2(r.xMin, min.y), new Vector2(min.x, max.y));        // left
        Place(dark[3], new Vector2(max.x, min.y), new Vector2(r.xMax, max.y));        // right
        Place(frame[0], new Vector2(min.x, max.y - FrameWidth), new Vector2(max.x, max.y));
        Place(frame[1], new Vector2(min.x, min.y), new Vector2(max.x, min.y + FrameWidth));
        Place(frame[2], new Vector2(min.x, min.y), new Vector2(min.x + FrameWidth, max.y));
        Place(frame[3], new Vector2(max.x - FrameWidth, min.y), new Vector2(max.x, max.y));

        float midY = (min.y + max.y) * 0.5f;
        var p = pointer.rectTransform;
        p.anchorMin = p.anchorMax = new Vector2(0.5f, 0.5f);
        p.anchoredPosition = new Vector2(max.x + 6f + p.sizeDelta.x * 0.5f, midY);
        PlaceTip(new Vector2(max.x + 12f + p.sizeDelta.x, midY), 0);

        // WAITING FOR THE SERVER after a press that spends something.
        if (costs) Hook(target.Button); else Unhook();
        waitCover.enabled = costs && QuestStore.AwaitingAnswer;
        if (waitCover.enabled) Place(waitCover, min, max);
    }

    // What to show, if anything: the part to light (or none, for a sentence only) and its text.
    bool Decide(out GuideMarker target, out string text, out bool costs)
    {
        target = null; text = null; costs = false;
        var t = QuestStore.Guide;
        if (!t.any) return false;
        var me = Character.Instance;
        if (me == null || me.IsDead || GameNetwork.MyPlayer == null) return false;
        var def = QuestCatalog.Get(t.questId);
        int index = def != null ? def.IndexOf(t.stepId) : -1;
        if (index < 0) return false;
        var step = def.steps[index];
        var station = (CraftStation)t.station;
        string item = step.objective.itemId;

        // THE STEP CANNOT BE DONE RIGHT NOW: said where it would be done, never by lighting a button that would fail.
        if (t.block != (int)QuestBlock.None)
        {
            if (station != CraftStation.None && CraftStationPanel.OpenStation != station) return false;
            text = BlockedText((QuestBlock)t.block, item);
            return text != null;
        }

        int textOnly = -1;
        GuideMarker found = null;
        int foundAt = -1;
        for (int i = 0; i < step.hints.Length; i++)
        {
            var h = step.hints[i];
            if (h == null) continue;
            if (h.spot == GuideSpot.None) { if (found == null) textOnly = i; continue; }
            // A station's rows count only in the station the step is worked at.
            if (h.spot != GuideSpot.BagRow && CraftStationPanel.OpenStation != station) continue;
            var m = GuideMarker.Find(h.spot, item, station);
            if (m == null) continue;
            found = m; foundAt = i; textOnly = -1;
        }
        // ONLY THE CHOSEN PART IS SCROLLED TO. Scrolling to every hint's part in turn made two parts of one list —
        // Back at its top, the row to forge near its bottom — each scroll the other out of view, every frame, so
        // neither was ever lit and the list was held where the last one left it.
        if (found != null && !InView(found)) found = null;
        if (found != null)
        {
            target = found;
            text = step.hints[foundAt].text;
            costs = found.spot == GuideSpot.StationForgeRow || found.spot == GuideSpot.StationFoldButton;
            return true;
        }
        if (textOnly >= 0) { text = step.hints[textOnly].text; return true; }
        return false;
    }

    // Is the part inside the area its list shows? A row scrolled out of view is brought into it, and counts once it is.
    bool InView(GuideMarker m)
    {
        var rt = (RectTransform)m.transform;
        var scroll = m.Scroll;
        if (scroll == null || scroll.viewport == null) return true;
        rt.GetWorldCorners(corners);
        var vp = scroll.viewport;
        Vector2 a = vp.InverseTransformPoint(corners[0]), b = vp.InverseTransformPoint(corners[2]);
        var vr = vp.rect;
        if (a.y >= vr.yMin && b.y <= vr.yMax) return true;
        ScrollInto(scroll, rt);
        return false;
    }

    static void ScrollInto(ScrollRect scroll, RectTransform row)
    {
        var content = scroll.content;
        if (content == null) return;
        float viewH = scroll.viewport.rect.height;
        float rowTop = -content.InverseTransformPoint(row.TransformPoint(new Vector3(0, row.rect.yMax, 0))).y;
        float y = Mathf.Clamp(rowTop - viewH * 0.5f, 0f, Mathf.Max(0f, content.rect.height - viewH));
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
    }

    static string BlockedText(QuestBlock block, string itemId)
    {
        var item = ItemCatalog.Resolve(itemId);
        string name = item != null ? item.displayName : itemId;
        switch (block)
        {
            case QuestBlock.CannotPay: return "You are carrying too little to pay for this. Creatures in the fields drop what you need.";
            case QuestBlock.TakeItOff: return "Your " + name + " is being worn. Take it off in your bag first — a station works only on what you carry.";
            case QuestBlock.NotCarried: return "You are not carrying a " + name + ".";
            default: return null;
        }
    }

    Transform WindowOf(GuideMarker m)
    {
        var t = m.transform;
        while (t.parent != null && t.parent != canvas.transform) t = t.parent;
        return t.parent == canvas.transform ? t : null;
    }

    void Hook(Button b)
    {
        if (b == hookedButton) return;
        Unhook();
        hookedButton = b;
        if (b != null) b.onClick.AddListener(QuestStore.PressedLitSpot);
    }

    void Unhook()
    {
        if (hookedButton != null) hookedButton.onClick.RemoveListener(QuestStore.PressedLitSpot);
        hookedButton = null;
    }

    void PlaceTip(Vector2 leftMiddle, int lines)
    {
        tip.anchorMin = tip.anchorMax = new Vector2(0.5f, 0.5f);
        tip.sizeDelta = new Vector2(TipWidth, Mathf.Max(44f, tipText.preferredHeight + 14f));
        // Kept on screen: a part near the right edge has its text on its left instead.
        var r = root.rect;
        if (leftMiddle.x + TipWidth > r.xMax - 8f) leftMiddle.x = Mathf.Max(r.xMin + 8f, leftMiddle.x - TipWidth - 90f);
        tip.anchoredPosition = leftMiddle;
    }

    static void Place(Image img, Vector2 min, Vector2 max)
    {
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = min;
        rt.sizeDelta = new Vector2(Mathf.Max(0f, max.x - min.x), Mathf.Max(0f, max.y - min.y));
    }
}
