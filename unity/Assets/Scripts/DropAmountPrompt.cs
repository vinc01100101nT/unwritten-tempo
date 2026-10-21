using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "How many?" — THE one small box in the game that asks for a number: when you drop a stack of more than one, and
/// when a stack goes into the shop's cart to buy or sell. It refuses anything outside 1…the most it was given, and hands
/// the answer back on Confirm.
///
/// <para>It exists because dropping is the one bag action that CANNOT be undone by clicking again: what leaves
/// your bag is on the floor, and anybody standing there can take it. A single item needs no box — there is
/// nothing to ask — so it is never shown for one.</para>
///
/// <para><b>There is ONE of these in the game</b>, and <see cref="DropFromScreen"/> is the only way anything on a
/// screen puts an item down — dragging an item out of the window goes through it, so there is one answer to when
/// you are asked and what happens when you answer.</para>
///
/// <para>Opened where the item was let go: one thin rounded edge around a flat panel, floating above every window
/// on the HUD canvas.</para>
/// </summary>
public class DropAmountPrompt
{
    const float Width = 210f;

    static DropAmountPrompt shared;

    /// <summary>The one box, built on first use under <paramref name="host"/>.</summary>
    public static DropAmountPrompt Shared(Canvas host)
    {
        if (shared != null && shared.root != null) return shared;
        if (host == null) return null;
        shared = new DropAmountPrompt();
        shared.Build(host);
        return shared;
    }

    /// <summary>THE way a screen puts an item down. A stack of more than one is asked how many first; a single
    /// item and any wearable go straight down, because there is nothing to ask. <paramref name="onChanged"/> is
    /// raised once the bag has actually changed, which for a stack is after the answer comes back.</summary>
    public static void DropFromScreen(BagEntry entry, Vector2 screenPos, Canvas host, Action onChanged)
    {
        var store = InventoryStore.Instance;
        if (store == null || entry == null || entry.def == null) return;

        if (!entry.IsEquip && entry.count > 1)
        {
            var box = Shared(host);
            if (box == null) return;
            box.Open(entry, screenPos, howMany =>
            {
                store.Drop(entry, howMany);
                onChanged?.Invoke();
            });
            return;
        }

        store.Drop(entry, 1);
        onChanged?.Invoke();
    }

    /// <summary>Take the box down, wherever it was opened from — the inventory window closing, for instance.</summary>
    public static void CloseShared()
    {
        if (shared != null) shared.Close();
    }

    RectTransform root;
    TMP_Text title;
    TMP_InputField field;
    Canvas canvas;
    Action<int> onConfirm;
    int most = 1;
    // What the box opened holding — what an empty or unreadable field means on Confirm.
    int startedAt = 1;
    int openedFrame = -1;
    // THE NUMBER IS SELECTED, ready to be typed over, when the box opens and whenever the field is clicked. Done once
    // the field has the keyboard, which the text package hands it a frame or so after it is asked to.
    bool selectAllPending;

    public bool IsOpen => root != null && root.gameObject.activeSelf;

    /// <summary>Create the (hidden) box once, under the HUD canvas.</summary>
    public void Build(Canvas hostCanvas)
    {
        canvas = hostCanvas;
        if (canvas == null) return;

        var border = UiKit.Round(UiKit.Img("DropAmountPrompt", canvas.transform, UiKit.WindowEdge), UiKit.WindowRadius);
        root = border.rectTransform;
        root.anchorMin = root.anchorMax = new Vector2(0, 0);
        root.pivot = new Vector2(0f, 1f);
        root.sizeDelta = new Vector2(Width, 0f);

        var frame = border.gameObject.AddComponent<VerticalLayoutGroup>();
        frame.padding = new RectOffset(1, 1, 1, 1);
        frame.childControlWidth = frame.childControlHeight = true;
        frame.childForceExpandWidth = true;
        frame.childForceExpandHeight = false;
        var fitter = border.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var fill = UiKit.Round(UiKit.Img("Fill", root, UiKit.PanelFill), UiKit.WindowRadius - 1);
        var v = fill.gameObject.AddComponent<VerticalLayoutGroup>();
        // The top padding makes room for the blue bar, which sits across the whole top outside the layout.
        v.padding = new RectOffset(8, 8, (int)UiKit.TitleBarHeight + 6, 8);
        v.spacing = 6f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        var bar = UiKit.TitleBar(fill.transform, "Title");
        bar.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var brt = bar.rectTransform;
        brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 1);
        brt.sizeDelta = new Vector2(0, UiKit.TitleBarHeight); brt.anchoredPosition = Vector2.zero;
        title = UiKit.MakeText("Text", bar.transform, "", 12, FontStyle.Bold, TextAnchor.MiddleLeft, null, false);
        UiKit.Fill(title.rectTransform, new Vector2(8, 0), new Vector2(-8, 0));
        UiKit.Paint(title, UiKit.TitleText);

        BuildField(fill.transform);
        BuildButtons(fill.transform);

        // IT WATCHES FOR ITS OWN DISMISSAL. The menu used to do that for it, which was fine while the menu was the
        // only thing that could open it — dragging an item out of the window can too, and that has nobody ticking
        // anything.
        var ticker = border.gameObject.AddComponent<DropAmountPromptTicker>();
        ticker.box = this;

        root.gameObject.SetActive(false);
    }

    void BuildField(Transform parent)
    {
        var box = UiKit.Field(UiKit.Img("Field", parent, UiKit.SlotFill));
        var le = box.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = 26f;

        var text = UiKit.MakeText("Text", box.transform, "", 14, FontStyle.Normal,
                                  TextAnchor.MiddleLeft, null, false);
        UiKit.Fill(text.rectTransform, new Vector2(6, 0), new Vector2(-6, 0));

        field = box.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = box.rectTransform;
        field.textComponent = text;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.contentType = TMP_InputField.ContentType.IntegerNumber;
        UiKit.StyleTyping(field);
    }

    void BuildButtons(Transform parent)
    {
        var row = new GameObject("Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(parent, false);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = true;
        var le = row.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = 26f;

        UiKit.PrimaryBtn("Confirm", row, Confirm);
        UiKit.Btn("Cancel", row, Close);
    }

    /// <summary>Ask how many of <paramref name="entry"/> to put down, at <paramref name="screenPos"/>. The box
    /// opens holding the WHOLE stack, because dropping the lot is what the number is most often meant to be.</summary>
    public void Open(BagEntry entry, Vector2 screenPos, Action<int> confirmed)
    {
        if (entry == null || entry.def == null) return;
        int all = Mathf.Max(1, entry.count);
        Ask("Drop how many? (1–" + all + ")", all, all, screenPos, confirmed);
    }

    /// <summary>The most a box ever accepts when it is given no most of its own — a guard on what is typed, the same
    /// as the server's own cap on one deal.</summary>
    public const int MostEverTyped = 999;

    /// <summary>Ask for a number under <paramref name="heading"/>, starting at <paramref name="start"/>, from 1 to
    /// <paramref name="mostAllowed"/> (0 = no most of its own, <see cref="MostEverTyped"/>). <paramref name="guide"/> is
    /// the part of the tutorial guide the box counts as while it is open — none, unless the asker says.</summary>
    public void Ask(string heading, int start, int mostAllowed, Vector2 screenPos, Action<int> confirmed,
                    GuideSpot guide = GuideSpot.None)
    {
        if (root == null) return;
        var marker = root.GetComponent<GuideMarker>();
        if (guide != GuideSpot.None) GuideMarker.Put(root.gameObject, guide);
        else if (marker != null) marker.spot = GuideSpot.None;

        most = mostAllowed > 0 ? mostAllowed : MostEverTyped;
        onConfirm = confirmed;
        title.text = heading;
        startedAt = Mathf.Clamp(start, 1, most);
        field.text = startedAt.ToString();

        UiLayers.Raise(root, UiLayers.Layer.Menu);
        root.gameObject.SetActive(true);
        openedFrame = Time.frameCount;
        Place(screenPos);

        field.Select();
        field.ActivateInputField();
        selectAllPending = true;
    }

    /// <summary>Select the whole number once the field has the keyboard. Run after the frame's clicks are handled, so a
    /// click into the field — which puts the caret where it landed — ends with the number selected instead.</summary>
    public void TickSelectAll()
    {
        if (!IsOpen) return;
        if (PointerInput.PrimaryDown)
        {
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)field.transform, PointerInput.Position, cam))
                selectAllPending = true;
        }
        if (!selectAllPending || !field.isFocused) return;
        selectAllPending = false;
        field.selectionStringAnchorPosition = field.text.Length;
        field.selectionStringFocusPosition = 0;
    }

    public void Close()
    {
        if (root != null) root.gameObject.SetActive(false);
        onConfirm = null;
    }

    void Confirm()
    {
        var act = onConfirm;
        // CLOSED FIRST. The box is taken down before the drop happens, so the window redrawing underneath cannot
        // leave a prompt standing over a stack that is no longer there.
        int typed;
        if (!int.TryParse(field.text, out typed)) typed = startedAt;
        int amount = Mathf.Clamp(typed, 1, most);
        Close();
        act?.Invoke(amount);
    }

    /// <summary>Close on Esc, or on a click that lands anywhere but on this box. Call once a frame from whatever
    /// owns it.</summary>
    public void TickDismiss()
    {
        if (!IsOpen || Time.frameCount == openedFrame) return;

        if (Hotkeys.CancelDown()) { Hotkeys.MarkCancelHandled(); Close(); return; }
        if (!PointerInput.PrimaryDown && !PointerInput.SecondaryDown) return;

        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        if (!RectTransformUtility.RectangleContainsScreenPoint(root, PointerInput.Position, cam))
            Close();
    }

    void Place(Vector2 screenPos)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float w = Width * scale;
        float h = root.rect.height * scale;

        float x = screenPos.x;
        float y = screenPos.y;
        if (x + w > Screen.width) x = Mathf.Max(0f, screenPos.x - w);
        if (y - h < 0f) y = Mathf.Min(Screen.height, screenPos.y + h);
        root.position = new Vector3(x, y, 0f);
    }
}

/// <summary>Gives the "how many?" box its one per-frame job — noticing Esc, or a click that landed somewhere
/// else. It rides on the box itself so the box works the same however it was opened.</summary>
[DisallowMultipleComponent]
public class DropAmountPromptTicker : MonoBehaviour
{
    internal DropAmountPrompt box;
    void Update() { if (box != null) box.TickDismiss(); }
    void LateUpdate() { if (box != null) box.TickSelectAll(); }
}
