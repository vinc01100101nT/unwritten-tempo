using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The right-click menu on a bag entry — the first runtime context menu in the game. It lists what can actually
/// be done to the thing under the cursor and nothing else, so a player never reads an action the game cannot
/// perform.
///
/// <para><b>Every enabled row calls an existing method.</b> Equip and Use route into
/// <see cref="InventoryStore"/>, which is already server-authoritative. Nothing here reimplements an action —
/// which is also why DROP is disabled: the store has no way to discard a single entry, only a GM-style wipe of
/// the whole bag, so a Drop that appeared to work would be a lie.</para>
///
/// <para><b>An action the design describes but nobody built is shown DISABLED, tagged "planned".</b> Reforge,
/// Add skill, Transmog and Recycle live in the crafting design and in no source file. Hiding them would make the
/// menu look complete while the feature set is not; showing them enabled would promise what the game cannot do.
/// Disabled-and-labelled is the only honest third option. Delete each row here as its feature lands.</para>
///
/// <para>Named ItemActionMenu rather than ContextMenu on purpose: <c>ContextMenu</c> is a UnityEngine attribute
/// for inspector entries, and a runtime menu sharing that name reads as one.</para>
/// </summary>
public class ItemActionMenu
{
    class Entry
    {
        public string label;
        public Action run;        // null = not clickable
        public string note;       // the small right-hand tag ("planned", "no socket", …)
        public bool separator;
    }

    const float Width = 200f;
    const float RowHeight = 24f;
    const float TitleHeight = 26f;

    RectTransform root;
    Transform rowHost;
    TMP_Text title;
    Canvas canvas;
    readonly List<GameObject> spawned = new List<GameObject>(12);
    readonly List<Entry> entries = new List<Entry>(12);

    Action onChanged;
    int openedFrame = -1;      // the click that opens the menu must not also dismiss it

    /// <summary>Opens the card picker on a piece. Set by the window that owns the picker; null until then,
    /// and the row is shown disabled when it is null so the menu never offers what nobody can do.</summary>
    public static Action<BagEntry> OpenCardPicker;

    public bool IsOpen => root != null && root.gameObject.activeSelf;

    /// <summary>Create the (hidden) panel once, under the HUD canvas so it floats above every window.</summary>
    public void Build(Canvas hostCanvas)
    {
        canvas = hostCanvas;
        if (canvas == null) return;

        // A window's frame in this game is one flat gold border around a flat panel — the item tooltip is built
        // the same way, so a floating menu matching it is the house style rather than a new look.
        var border = UiKit.Img("ItemActionMenu", canvas.transform, UiKit.Gold);
        root = border.rectTransform;
        root.anchorMin = root.anchorMax = new Vector2(0, 0);
        root.pivot = new Vector2(0f, 1f);
        root.sizeDelta = new Vector2(Width, 0f);

        // THE PANEL MUST BE AS TALL AS WHAT IT DRAWS. The rows used to size only the inner fill, leaving this
        // outer box 200 wide and ZERO high — and the dismiss check below asks whether the click landed inside
        // THIS box. It answered "outside" for every row in the menu, so the menu took itself down on the way
        // DOWN of the very click meant to choose one, and nothing in it could ever be run. Growing the height
        // from the rows here leaves one rectangle: the one that is painted is the one that is tested. Place()
        // reads the same height to flip the menu up near the bottom of the screen, which never fired either.
        var frame = border.gameObject.AddComponent<VerticalLayoutGroup>();
        // 1px on every side is the gold border showing around the fill
        frame.padding = new RectOffset(1, 1, 1, 1);
        frame.childControlWidth = frame.childControlHeight = true;
        frame.childForceExpandWidth = true;
        frame.childForceExpandHeight = false;
        var frameFitter = border.gameObject.AddComponent<ContentSizeFitter>();
        frameFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Sized by the frame above, so no stretch-to-parent and no fitter of its own.
        var fill = UiKit.Img("Fill", root, UiKit.PanelFill);

        var v = fill.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(0, 0, 0, 4);
        v.spacing = 0f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        var head = UiKit.Img("Title", fill.transform, new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.16f));
        var hl = head.gameObject.AddComponent<LayoutElement>();
        hl.minHeight = hl.preferredHeight = TitleHeight;
        // wrap:false is the real switch (MakeText sets textWrappingMode); the obsolete property did nothing.
        title = UiKit.MakeText("Text", head.transform, "", 12, FontStyle.Bold,
                               TextAnchor.MiddleLeft, null, false);
        UiKit.Fill(title.rectTransform, new Vector2(8, 0), new Vector2(-8, 0));
        title.overflowMode = TextOverflowModes.Ellipsis;

        rowHost = fill.transform;

        root.gameObject.SetActive(false);
    }

    /// <summary>Open on <paramref name="entry"/> at <paramref name="screenPos"/>. <paramref name="refresh"/> is
    /// raised after an action runs, so the window redraws itself and its footer.</summary>
    public void Open(BagEntry entry, Vector2 screenPos, Action refresh)
    {
        if (root == null || entry == null || entry.def == null) return;
        onChanged = refresh;

        BuildEntries(entry);
        Render(entry);
        Place(screenPos);
        // Come to the front, every time. The right-click that opens this menu ALSO lands on the window
        // underneath, and a window raises itself to the front on any pointer-down — so a menu that keeps
        // the sibling order it was built with opens behind the very window it belongs to.
        UiLayers.Raise(root, UiLayers.Layer.Menu);
        root.gameObject.SetActive(true);
        openedFrame = Time.frameCount;
        ItemTooltip.HideTip();
    }

    public void Close()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    /// <summary>Close on a click that lands anywhere but on this menu. Call once a frame from whatever owns
    /// the menu.
    ///
    /// <para>Deliberately NOT a full-screen catcher sitting under the menu: that would swallow the click, so
    /// right-clicking a different row while the menu is up would dismiss it and never reach the row. Reading
    /// the click instead leaves it to land where the player aimed — the menu closes AND the next row opens
    /// its own, which is one gesture rather than two.</para></summary>
    public void TickDismiss()
    {
        if (!IsOpen || Time.frameCount == openedFrame) return;
        if (!PointerInput.PrimaryDown && !PointerInput.SecondaryDown) return;

        // A ScreenSpaceOverlay canvas takes a null camera; anything else needs its own.
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        if (!RectTransformUtility.RectangleContainsScreenPoint(root, PointerInput.Position, cam))
            Close();
    }

    void BuildEntries(BagEntry e)
    {
        entries.Clear();
        var def = e.def;
        var store = InventoryStore.Instance;

        if (e.IsEquip)
        {
            bool isWeapon = def.slot == EquipSlotType.Weapon;
            if (e.worn)
                entries.Add(new Entry { label = "Unequip", run = store != null ? () => store.Unequip(e.wornSlot) : (Action)null });
            else
                entries.Add(new Entry { label = "Equip", run = store != null ? () => store.Equip(e) : (Action)null });
            entries.Add(new Entry { separator = true });

            int refine = e.instance != null ? e.instance.upgrade : 0;
            entries.Add(new Entry { label = "Refine  +" + (refine + 1), note = "planned" });

            int sockets = def.cardSlots;
            int filled = e.instance != null ? e.instance.FilledSockets : 0;
            bool canSocket = sockets > 0 && filled < sockets && OpenCardPicker != null && e.instance != null;
            entries.Add(new Entry
            {
                label = "Insert card",
                note = sockets == 0 ? "no socket" : (filled >= sockets ? "full" : null),
                run = canSocket ? () => OpenCardPicker(e) : (Action)null
            });

            if (isWeapon)
            {
                entries.Add(new Entry { separator = true });
                entries.Add(new Entry { label = "Reforge skills", note = "planned" });
                entries.Add(new Entry { label = "Add skill",      note = "planned" });
                entries.Add(new Entry { label = "Transmog",       note = "planned" });
            }

            entries.Add(new Entry { separator = true });
            if (def.slot == EquipSlotType.Ring)
            {
                // The Ring is the progression vessel, not gear: it is never recycled and never thrown away.
                entries.Add(new Entry { label = "Recycle", note = "the Ring" });
                entries.Add(new Entry { label = "Drop",    note = "the Ring" });
            }
            else
            {
                entries.Add(new Entry { label = "Recycle → Essence", note = "planned" });
                entries.Add(new Entry { label = "Drop", note = "planned" });
            }
        }
        else if (def.IsUsable)
        {
            entries.Add(new Entry { label = "Use", run = store != null ? () => store.Consume(e) : (Action)null });
            entries.Add(new Entry { separator = true });
            entries.Add(new Entry { label = "Drop", note = "planned" });
        }
        else if (def.category == ItemCategory.Quest)
        {
            entries.Add(new Entry { label = "Drop", note = "quest item" });
        }
        else
        {
            entries.Add(new Entry { label = "Drop", note = "planned" });
        }
    }

    void Render(BagEntry e)
    {
        for (int i = 0; i < spawned.Count; i++)
            if (spawned[i] != null) UnityEngine.Object.Destroy(spawned[i]);
        spawned.Clear();

        var rar = e.instance != null ? e.instance.Rarity : e.def.rarity;
        int refine = e.instance != null ? e.instance.upgrade : 0;
        title.text = (refine > 0 ? "+" + refine + " " : "") + e.def.displayName;
        title.color = Rarities.Text(rar);

        for (int i = 0; i < entries.Count; i++)
        {
            var it = entries[i];
            if (it.separator)
            {
                var sep = UiKit.Img("Sep", rowHost, new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.22f));
                var sl = sep.gameObject.AddComponent<LayoutElement>();
                sl.minHeight = sl.preferredHeight = 1f;
                spawned.Add(sep.gameObject);
                continue;
            }

            bool enabled = it.run != null;
            var rowImg = UiKit.Img("Row", rowHost, new Color(0, 0, 0, 0));
            var rl = rowImg.gameObject.AddComponent<LayoutElement>();
            rl.minHeight = rl.preferredHeight = RowHeight;

            var label = UiKit.MakeText("Label", rowImg.transform, it.label, 14, FontStyle.Normal,
                                       TextAnchor.MiddleLeft, null, false);
            UiKit.Fill(label.rectTransform, new Vector2(8, 0), new Vector2(-70, 0));
            label.color = enabled ? UiKit.TextMain : UiKit.TextDim;
            label.raycastTarget = false;

            if (!string.IsNullOrEmpty(it.note))
            {
                var note = UiKit.MakeText("Note", rowImg.transform, it.note, 11, FontStyle.Normal,
                                          TextAnchor.MiddleRight, null, false);
                UiKit.Fill(note.rectTransform, new Vector2(-70, 0), new Vector2(-8, 0));
                note.color = UiKit.TextDim;
                note.raycastTarget = false;
            }

            if (enabled)
            {
                var act = it.run;
                var btn = rowImg.gameObject.AddComponent<Button>();
                btn.targetGraphic = rowImg;
                var colors = btn.colors;
                colors.normalColor = new Color(1, 1, 1, 0);
                colors.highlightedColor = new Color(UiKit.Cyan.r, UiKit.Cyan.g, UiKit.Cyan.b, 0.14f);
                colors.pressedColor = new Color(UiKit.Cyan.r, UiKit.Cyan.g, UiKit.Cyan.b, 0.24f);
                colors.selectedColor = colors.normalColor;
                colors.disabledColor = colors.normalColor;
                btn.colors = colors;
                btn.onClick.AddListener(() =>
                {
                    Close();
                    act();
                    if (onChanged != null) onChanged();
                });
            }
            else rowImg.raycastTarget = true;   // swallow the click so it can't fall through to the row beneath

            spawned.Add(rowImg.gameObject);
        }
    }

    void Place(Vector2 screenPos)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float w = Width * scale;
        float h = root.rect.height * scale;

        // Pivot is top-left, so the panel occupies [x .. x+w] and [y-h .. y]; flip near an edge rather than clip.
        float x = screenPos.x;
        float y = screenPos.y;
        if (x + w > Screen.width) x = Mathf.Max(0f, screenPos.x - w);
        if (y - h < 0f) y = Mathf.Min(Screen.height, screenPos.y + h);
        root.position = new Vector3(x, y, 0f);
    }
}
