using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds and drives the progression window trio (Inventory · Equipment · Character) from the
/// locked HUD spec, wired to the real <see cref="Character"/> + <see cref="InventoryStore"/>.
/// Toggle keys: B = Bag (Inventory + docked Equipment, opened together), C = Character, Esc = close all. (V is now
/// the Sprint key on Player3DController — Equipment no longer has its own key.) Each window self-builds
/// at Start into a <see cref="UiKit.Window"/>; the shared <see cref="ItemTooltip"/> handles hover
/// (incl. the live equip→stat-delta preview). Lives on its own canvas (the builder sets it up).
/// </summary>
[RequireComponent(typeof(InventoryStore))]
public class ProgressionWindows : MonoBehaviour
{
    static readonly EquipSlotType[] PaperdollOrder =
        { EquipSlotType.Head, EquipSlotType.Armor, EquipSlotType.Garment, EquipSlotType.Weapon,
          EquipSlotType.Shield, EquipSlotType.Accessory1, EquipSlotType.Ring, EquipSlotType.Accessory2 };

    // Empty equip-slot silhouettes (faint gold watermarks). Wired by UIShellBuilder from the Ninja
    // Adventure "Items & Weapon" set so an empty paperdoll reads as labelled slots, not black holes.
    // Runtime can't AssetDatabase-load, so these are serialized refs baked into the GameUICanvas prefab.
    [Header("Empty equip-slot silhouettes (wired by UIShellBuilder)")]
    public Sprite silHead, silArmor, silGarment, silWeapon, silShield, silAccessory1, silAccessory2;

    Sprite SilhouetteFor(EquipSlotType s) => s switch
    {
        EquipSlotType.Head       => silHead,
        EquipSlotType.Armor      => silArmor,
        EquipSlotType.Garment    => silGarment,
        EquipSlotType.Weapon     => silWeapon,
        EquipSlotType.Shield     => silShield,
        EquipSlotType.Accessory1 => silAccessory1,
        EquipSlotType.Accessory2 => silAccessory2,
        EquipSlotType.Ring       => null,   // procedural ring watermark (SlotWatermark default) covers this slot
        _                        => null,
    };

    Transform canvas;
    GameObject invRoot, equipRoot, charRoot;

    // Inventory
    ItemSlot[] bagSlots;
    readonly List<InventoryStore.Entry> filtered = new List<InventoryStore.Entry>();
    bool useFilter; ItemCategory tab; // useFilter=false => "All"
    TMP_Text invFooter;
    readonly List<Button> tabButtons = new List<Button>();

    // Equipment
    readonly Dictionary<EquipSlotType, ItemSlot> paperdoll = new Dictionary<EquipSlotType, ItemSlot>();
    EquipSlotType selectedSlot = EquipSlotType.None;
    TMP_Text detailName, detailStats, detailCards;
    GameObject cardDialog; Transform cardListRoot;

    // Character
    static ProgressionWindows active;   // singleton guard (avoid a doubled build if two canvases carry it)
    static readonly PrimaryStat[] StatOrder =
        { PrimaryStat.STR, PrimaryStat.AGI, PrimaryStat.VIT, PrimaryStat.INT, PrimaryStat.DEX, PrimaryStat.LUK };
    TMP_Text charIdentity, charPoints, charDerived;
    readonly TMP_Text[] statValue = new TMP_Text[6];
    readonly TMP_Text[] statCost = new TMP_Text[6];
    readonly Button[] statPlus = new Button[6];

    void Start()
    {
        if (active != null && active != this) { enabled = false; return; }
        active = this;
        canvas = GetComponentInParent<Canvas>() != null ? GetComponentInParent<Canvas>().transform : transform;
        ResetDockedWindowPositionsOnce();   // must run BEFORE the windows build (WindowFrame loads its saved pos on enable)
        EnsureTooltip();
        BuildInventory();
        BuildEquipment();
        BuildCharacter();

        if (Character.Instance != null) Character.Instance.OnStatsChanged += RefreshAll;
        if (InventoryStore.Instance != null) InventoryStore.Instance.OnChanged += RefreshAll;
        Wallet.OnChanged += OnGold;
        RefreshAll();
    }

    void OnDestroy()
    {
        if (active == this) active = null;
        if (Character.Instance != null) Character.Instance.OnStatsChanged -= RefreshAll;
        if (InventoryStore.Instance != null) InventoryStore.Instance.OnChanged -= RefreshAll;
        Wallet.OnChanged -= OnGold;
    }

    // One-time migration: the Equipment window is now DOCKED to the bag's left (new default positions in
    // BuildInventory/BuildEquipment). Clear any position saved by the OLD free-floating layout so the docked default
    // takes effect once; the user's later drags persist normally after that. WindowFrame keys are "win_<title>_x/y".
    static void ResetDockedWindowPositionsOnce()
    {
        if (PlayerPrefs.HasKey("pw_dock_v1")) return;
        PlayerPrefs.DeleteKey("win_Inventory_x"); PlayerPrefs.DeleteKey("win_Inventory_y");
        PlayerPrefs.DeleteKey("win_Equipment_x"); PlayerPrefs.DeleteKey("win_Equipment_y");
        PlayerPrefs.SetInt("pw_dock_v1", 1);
    }

    void OnGold(int _) => RefreshInventoryFooter();

    void Update()
    {
        // Bare-key window hotkeys (no Alt — the Unity/OS Alt menu accelerators interfere with it):
        //   B = Bag (Inventory + the docked Equipment panel, opened together) · C = Character (K = Skills, owned by
        //   SkillDraftPanel). V is now the Sprint key (Player3DController) — Equipment no longer has its own key.
        if (Hotkeys.Down(KeyCode.B))
        {
            bool open = UiAnim.Toggle(invRoot);
            // Equipment is docked to the bag's left side — mirror the bag's state so they open/close as one panel.
            if (open) UiAnim.Open(equipRoot); else UiAnim.Close(equipRoot);
        }
        if (Hotkeys.Down(KeyCode.C)) UiAnim.Toggle(charRoot);
        if (Hotkeys.CancelDown() && AnyWindowOpen())
        {
            if (cardDialog != null) UiAnim.Close(cardDialog);
            UiAnim.Close(invRoot); UiAnim.Close(equipRoot); UiAnim.Close(charRoot);
            Hotkeys.MarkCancelHandled();   // this Escape is spent — it must not also pop the System window
        }

        KeepDerivedLive();
    }

    bool AnyWindowOpen() =>
        UiAnim.IsOpen(invRoot) || UiAnim.IsOpen(equipRoot) || UiAnim.IsOpen(charRoot) ||
        (cardDialog != null && UiAnim.IsOpen(cardDialog));

    // Keep the Character sheet honest while a buff runs, expires, or ramps. Only while the window is actually OPEN, and
    // the text is rebuilt ONLY when a value really moved — the per-frame cost is a struct build + an int compare, no
    // string work and no allocation (PERFORMANCE.md: nothing per-frame may allocate).
    DerivedSnapshot lastLive;
    bool lastLiveValid;
    int lastSta = -1;   // stamina drains/fills outside the snapshot, so it's watched separately
    void KeepDerivedLive()
    {
        if (charRoot == null || !UiAnim.IsOpen(charRoot) || Character.Instance == null) return;
        var c = Character.Instance;
        var now = c.DerivedWithStates;
        // Compare the DISPLAYED (rounded) resource values, not the raw floats — otherwise a draining stamina bar would
        // rebuild the text every single frame instead of only when the number on screen actually changes.
        int sta = Mathf.RoundToInt(c.Stamina);
        if (lastLiveValid && sta == lastSta && Same(now, lastLive)) return;
        lastLive = now; lastLiveValid = true; lastSta = sta;
        RefreshCharacter();
    }

    // Field-by-field so the compare stays allocation-free (a struct's default Equals can box/reflect).
    static bool Same(in DerivedSnapshot a, in DerivedSnapshot b) =>
        a.atkMin == b.atkMin && a.atkMax == b.atkMax && a.matkMin == b.matkMin && a.matkMax == b.matkMax &&
        a.def == b.def && a.mdef == b.mdef && a.hit == b.hit && a.flee == b.flee &&
        a.maxHP == b.maxHP && a.maxMP == b.maxMP && a.moveSpeed == b.moveSpeed &&
        a.hardDefPct == b.hardDefPct && a.hardMdefPct == b.hardMdefPct && a.barrierHP == b.barrierHP &&
        Mathf.Approximately(a.critChance, b.critChance) && Mathf.Approximately(a.critMult, b.critMult) &&
        Mathf.Approximately(a.aspdMult, b.aspdMult) && Mathf.Approximately(a.castTimeMult, b.castTimeMult) &&
        Mathf.Approximately(a.guardCutPct, b.guardCutPct) &&
        Mathf.Approximately(a.lifeStealPct, b.lifeStealPct) && Mathf.Approximately(a.reflectPct, b.reflectPct) &&
        Mathf.Approximately(a.manaLeechPct, b.manaLeechPct);

    void RefreshAll()
    {
        RefreshInventory();
        RefreshPaperdoll();
        RefreshSelectionRings();
        RefreshDetail();
        RefreshCharacter();
    }

    // ---- Inventory window -----------------------------------------------------

    void BuildInventory()
    {
        // Bag sits right-of-centre; the Equipment window docks flush to its LEFT edge (see BuildEquipment), so the two
        // read as one panel that opens together on B. Pair is centred: bag right edge +560, equip left edge -576.
        var content = UiKit.Window(canvas, "Inventory", new Vector2(520, 480), new Vector2(300, 0), out invRoot);

        // tabs
        var tabRow = UiKit.Img("Tabs", content, new Color(0, 0, 0, 0));
        tabRow.rectTransform.anchorMin = new Vector2(0, 1);
        tabRow.rectTransform.anchorMax = new Vector2(1, 1);
        tabRow.rectTransform.pivot = new Vector2(0.5f, 1);
        tabRow.rectTransform.sizeDelta = new Vector2(0, 28);
        tabRow.raycastTarget = false;
        var hl = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 4; hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;

        AddTab(tabRow.transform, "All", false, default);
        AddTab(tabRow.transform, "Equip", true, ItemCategory.Equip);
        AddTab(tabRow.transform, "Use", true, ItemCategory.Use);
        AddTab(tabRow.transform, "Material", true, ItemCategory.Material);
        AddTab(tabRow.transform, "Etc", true, ItemCategory.Etc);

        // grid
        var gridGo = UiKit.Img("Grid", content, new Color(0, 0, 0, 0));
        gridGo.raycastTarget = false;
        UiKit.Fill(gridGo.rectTransform, new Vector2(0, 28), new Vector2(0, -34));
        var grid = gridGo.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(60, 60);
        grid.spacing = new Vector2(6, 6);
        grid.padding = new RectOffset(2, 2, 4, 2);

        const int slotCount = 40;
        bagSlots = new ItemSlot[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            int idx = i;
            var s = UiKit.Slot(gridGo.transform, 60);
            s.previewOnHover = true;
            s.clickOverride = e => OnBagClick(idx, e);
            s.gameObject.AddComponent<SlotDragHandler>();   // drag-to-sort + drag-to-equip
            bagSlots[i] = s;
        }

        // footer
        invFooter = UiKit.Label("Footer", content, "", 14, FontStyle.Bold, TextAnchor.MiddleRight);
        invFooter.rectTransform.anchorMin = new Vector2(0, 0);
        invFooter.rectTransform.anchorMax = new Vector2(1, 0);
        invFooter.rectTransform.pivot = new Vector2(0.5f, 0);
        invFooter.rectTransform.sizeDelta = new Vector2(0, 24);
        invFooter.color = UiKit.Gold;
    }

    void AddTab(Transform parent, string label, bool filtered, ItemCategory cat)
    {
        var btn = UiKit.Btn(label, parent, () => { useFilter = filtered; tab = cat; RefreshInventory(); });
        tabButtons.Add(btn);
    }

    void OnBagClick(int index, PointerEventData e)
    {
        if (index >= filtered.Count) return;
        var entry = filtered[index];
        bool doubleClick = e.clickCount >= 2;
        bool right = e.button == PointerEventData.InputButton.Right;
        if (entry.IsEquip && (doubleClick || right)) InventoryStore.Instance.Equip(entry);
        else if (!entry.IsEquip && right && entry.def != null && entry.def.IsUsable)
            InventoryStore.Instance.Consume(entry);
    }

    void RefreshInventory()
    {
        if (bagSlots == null || InventoryStore.Instance == null) return;
        filtered.Clear();
        foreach (var e in InventoryStore.Instance.Entries)
            if (!useFilter || (e.def != null && e.def.category == tab))
                filtered.Add(e);

        for (int i = 0; i < bagSlots.Length; i++)
        {
            if (i < filtered.Count)
            {
                var entry = filtered[i];
                if (entry.instance != null) bagSlots[i].SetInstance(entry.instance);
                else bagSlots[i].SetItem(entry.def);
                bagSlots[i].previewSlot = entry.def != null ? entry.def.slot : EquipSlotType.None;
                bagSlots[i].boundEntry = entry;     // drag handler reorders/equips against this live entry
            }
            else { bagSlots[i].SetItem(null); bagSlots[i].boundEntry = null; }
        }
        RefreshInventoryFooter();
    }

    void RefreshInventoryFooter()
    {
        if (invFooter == null) return;
        int w = InventoryStore.Instance != null ? InventoryStore.Instance.TotalWeight : 0;
        invFooter.text = $"<color=#A79E88>Weight</color> {w}        <color=#C8A862>{Wallet.Gold:n0} z</color>";
    }

    // ---- Equipment window -----------------------------------------------------

    void BuildEquipment()
    {
        // Docked to the LEFT of the bag (Inventory at x=300, width 520 → left edge x=40). Equipment (width 620) is
        // centred at -266 so its right edge (x=44) meets the bag with a ~10px gap — one combined panel opened on B.
        var content = UiKit.Window(canvas, "Equipment", new Vector2(620, 440), new Vector2(-266, 0), out equipRoot);

        // paperdoll (left)
        var doll = UiKit.Img("Paperdoll", content, new Color(0, 0, 0, 0));
        doll.rectTransform.anchorMin = new Vector2(0, 0);
        doll.rectTransform.anchorMax = new Vector2(0.55f, 1);
        UiKit.Fill(doll.rectTransform, new Vector2(0, 0), new Vector2(0, 0));
        doll.rectTransform.anchorMax = new Vector2(0.55f, 1);
        doll.raycastTarget = false;

        var pos = new Dictionary<EquipSlotType, Vector2>
        {
            { EquipSlotType.Head,        new Vector2(0, 130) },
            { EquipSlotType.Armor,       new Vector2(-85, 45) },
            { EquipSlotType.Garment,     new Vector2(-85, -45) },
            { EquipSlotType.Weapon,      new Vector2(85, 45) },
            { EquipSlotType.Shield,      new Vector2(85, -45) },
            { EquipSlotType.Accessory1,  new Vector2(-72, -130) },
            { EquipSlotType.Ring,        new Vector2(0, -130) },
            { EquipSlotType.Accessory2,  new Vector2(72, -130) },
        };
        foreach (var st in PaperdollOrder)
        {
            var slot = UiKit.Slot(doll.transform, 64, st);
            // Final Watermark Set v1: the Unwritten procedural ink+gold silhouette REPLACES the old borrowed
            // Ninja sprites (a different art language). SilhouetteFor(st) is left wired but NOT applied — it
            // stays available as a future drop-in authored-sprite override (which would then win over this).
            SlotWatermark.Build(slot, st);
            slot.SetItem(null);                          // re-render now so the watermark shows before any Character-gated refresh
            var rt = slot.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos[st];
            var t = st;
            slot.clickOverride = e => OnPaperClick(t, e);
            slot.gameObject.AddComponent<SlotDragHandler>();   // drag equipped piece back to the bag
            paperdoll[st] = slot;
        }

        // detail panel (right) — same charcoal family as the window, lifted a touch so it reads as part
        // of the panel (not a separate gray block).
        var detail = UiKit.Img("Detail", content, UiKit.PanelRaise);
        detail.rectTransform.anchorMin = new Vector2(0.57f, 0);
        detail.rectTransform.anchorMax = new Vector2(1, 1);
        UiKit.Fill(detail.rectTransform, Vector2.zero, Vector2.zero);
        detail.rectTransform.anchorMin = new Vector2(0.57f, 0);
        detail.rectTransform.anchorMax = new Vector2(1, 1);
        detail.raycastTarget = false;

        // Item names use the Cinzel face at Normal weight (one step lighter than window titles, which stay Bold).
        detailName = UiKit.Label("Name", detail.transform, "Select a slot", 17, FontStyle.Normal, TextAnchor.UpperLeft, title: true);
        detailName.rectTransform.anchorMin = new Vector2(0, 1); detailName.rectTransform.anchorMax = new Vector2(1, 1);
        detailName.rectTransform.pivot = new Vector2(0.5f, 1);
        detailName.rectTransform.sizeDelta = new Vector2(-12, 26);
        detailName.rectTransform.anchoredPosition = new Vector2(0, -6);

        detailStats = UiKit.Label("Stats", detail.transform, "", 14, FontStyle.Normal, TextAnchor.UpperLeft);
        detailStats.rectTransform.anchorMin = new Vector2(0, 1); detailStats.rectTransform.anchorMax = new Vector2(1, 1);
        detailStats.rectTransform.pivot = new Vector2(0.5f, 1);
        detailStats.rectTransform.sizeDelta = new Vector2(-12, 150);
        detailStats.rectTransform.anchoredPosition = new Vector2(0, -38);

        detailCards = UiKit.Label("Cards", detail.transform, "", 14, FontStyle.Normal, TextAnchor.UpperLeft);
        detailCards.rectTransform.anchorMin = new Vector2(0, 1); detailCards.rectTransform.anchorMax = new Vector2(1, 1);
        detailCards.rectTransform.pivot = new Vector2(0.5f, 1);
        detailCards.rectTransform.sizeDelta = new Vector2(-12, 90);
        detailCards.rectTransform.anchoredPosition = new Vector2(0, -196);

        var insert = UiKit.Btn("Insert Card…", detail.transform, OpenCardDialog);
        var irt = (RectTransform)insert.transform;
        irt.anchorMin = new Vector2(0, 0); irt.anchorMax = new Vector2(1, 0);
        irt.pivot = new Vector2(0.5f, 0);
        irt.sizeDelta = new Vector2(-12, 30);
        irt.anchoredPosition = new Vector2(0, 10);

        BuildCardDialog(content);
    }

    void OnPaperClick(EquipSlotType slot, PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right) { InventoryStore.Instance.Unequip(slot); return; }
        selectedSlot = slot;
        RefreshSelectionRings();
        RefreshDetail();
    }

    void RefreshPaperdoll()
    {
        if (Character.Instance == null) return;
        foreach (var kv in paperdoll)
        {
            var inst = Character.Instance.GetEquippedInstance(kv.Key);
            if (inst != null) kv.Value.SetInstance(inst);
            else kv.Value.SetItem(null);
        }
    }

    void RefreshSelectionRings()
    {
        foreach (var kv in paperdoll) kv.Value.SetSelected(kv.Key == selectedSlot);
    }

    void RefreshDetail()
    {
        if (detailName == null) return;
        var inst = (selectedSlot != EquipSlotType.None && Character.Instance != null)
            ? Character.Instance.GetEquippedInstance(selectedSlot) : null;

        if (inst == null || inst.def == null)
        {
            detailName.text = selectedSlot == EquipSlotType.None ? "Select a slot" : Spaced(selectedSlot.ToString()) + " — empty";
            detailName.color = UiKit.TextDim;
            detailStats.text = "";
            detailCards.text = "";
            return;
        }

        var def = inst.def;
        detailName.text = (inst.upgrade > 0 ? "+" + inst.upgrade + " " : "") + def.displayName;
        detailName.color = Rarities.Text(def.rarity);

        // This PIECE's own contribution (def + refine + cards), not the whole character.
        var eb = EquipBonuses.Aggregate(new[] { inst });
        var sb = new System.Text.StringBuilder();
        sb.Append($"<color=#A79E88>{Rarities.Label(def.rarity)} · {Spaced(def.slot.ToString())}</color>");
        Stat(sb, "ATK", eb.atk);   Stat(sb, "DEF", eb.def);
        Stat(sb, "M.ATK", eb.matk); Stat(sb, "M.DEF", eb.mdef);
        Stat(sb, "Max HP", eb.hp);  Stat(sb, "Max MP", eb.mp);
        Stat(sb, "STR", eb.str); Stat(sb, "AGI", eb.agi); Stat(sb, "VIT", eb.vit);
        Stat(sb, "INT", eb.intel); Stat(sb, "DEX", eb.dex); Stat(sb, "LUK", eb.luk);
        Stat(sb, "CRIT", eb.critPercent, "%"); Stat(sb, "ASPD", eb.aspdPercent, "%");
        detailStats.text = sb.ToString();

        int slots = def.cardSlots;
        sb.Clear();
        sb.Append("<color=#A79E88>Cards ").Append(inst.FilledSockets).Append('/').Append(slots).Append("</color>");
        if (inst.cards != null)
            foreach (var c in inst.cards)
                if (c != null) sb.Append("\n  <color=#C7B27A>◇ ").Append(c.displayName).Append("</color>");
        detailCards.text = slots > 0 ? sb.ToString() : "<color=#A79E88>No card sockets</color>";
    }

    // ---- Card insert dialog ---------------------------------------------------

    void BuildCardDialog(Transform parent)
    {
        var border = UiKit.Img("CardDialog", parent, UiKit.Gold);
        border.rectTransform.anchorMin = border.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        border.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        border.rectTransform.sizeDelta = new Vector2(260, 280);
        var fill = UiKit.Img("Fill", border.transform, UiKit.PanelFill);
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));

        var title = UiKit.Label("Title", fill.transform, "Insert Card", 16, FontStyle.Bold, TextAnchor.UpperCenter, title: true);
        title.color = UiKit.Gold;
        title.rectTransform.anchorMin = new Vector2(0, 1); title.rectTransform.anchorMax = new Vector2(1, 1);
        title.rectTransform.pivot = new Vector2(0.5f, 1); title.rectTransform.sizeDelta = new Vector2(0, 24);
        title.rectTransform.anchoredPosition = new Vector2(0, -6);

        var list = UiKit.Img("List", fill.transform, new Color(0, 0, 0, 0));
        list.raycastTarget = false;
        UiKit.Fill(list.rectTransform, new Vector2(8, 36), new Vector2(-8, -34));
        var vl = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 4; vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        cardListRoot = list.transform;

        var close = UiKit.Btn("Close", fill.transform, () => UiAnim.Close(cardDialog));
        var crt = (RectTransform)close.transform;
        crt.anchorMin = new Vector2(0, 0); crt.anchorMax = new Vector2(1, 0);
        crt.pivot = new Vector2(0.5f, 0); crt.sizeDelta = new Vector2(-16, 26);
        crt.anchoredPosition = new Vector2(0, 8);

        cardDialog = border.gameObject;
        cardDialog.SetActive(false);
    }

    void OpenCardDialog()
    {
        if (cardDialog == null) return;
        var inst = (selectedSlot != EquipSlotType.None && Character.Instance != null)
            ? Character.Instance.GetEquippedInstance(selectedSlot) : null;
        if (inst == null || inst.def == null || inst.def.cardSlots <= 0 || !inst.HasFreeSocket) return;

        for (int i = cardListRoot.childCount - 1; i >= 0; i--) Destroy(cardListRoot.GetChild(i).gameObject);

        var cards = Resources.LoadAll<Card>("Cards");
        if (cards == null || cards.Length == 0)
            UiKit.Label("None", cardListRoot, "No cards available", 13, FontStyle.Italic, TextAnchor.MiddleCenter).color = UiKit.TextDim;
        else
            foreach (var card in cards)
            {
                var c = card;
                var btn = UiKit.Btn(card.displayName + "  —  " + card.blurb, cardListRoot, () => InsertCard(c));
                btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
            }

        cardDialog.transform.SetAsLastSibling();
        UiAnim.Open(cardDialog);
    }

    void InsertCard(Card card)
    {
        var inst = Character.Instance != null ? Character.Instance.GetEquippedInstance(selectedSlot) : null;
        if (inst == null || card == null || !inst.HasFreeSocket) return;
        inst.cards.Add(card);
        Character.Instance.SetEquipped(selectedSlot, inst);   // re-aggregate + fire stat events
        UiAnim.Close(cardDialog);
        RefreshAll();
    }

    // ---- Character window -----------------------------------------------------

    void BuildCharacter()
    {
        // Taller than the old 500: the derived block now lists every stat (offence, defence, speed, sustain, resources)
        // instead of five summary lines, and a fixed-height window would clip the tail silently.
        var content = UiKit.Window(canvas, "Character", new Vector2(360, 660), new Vector2(0, 0), out charRoot);

        // Ornament Kit v1: a portrait/name backplate behind the identity block (placeholder — a portrait
        // sprite drops into it later). Decorative, sits behind the stat column so it never disturbs layout.
        var plate = Ornament.Backplate(content, new Vector2(-4, 56));
        var prt = plate.rectTransform;
        prt.anchorMin = new Vector2(0, 1); prt.anchorMax = new Vector2(1, 1); prt.pivot = new Vector2(0.5f, 1);
        prt.sizeDelta = new Vector2(-4, 56); prt.anchoredPosition = new Vector2(0, 0);

        var col = UiKit.Img("Col", content, new Color(0, 0, 0, 0));
        col.raycastTarget = false;
        UiKit.Fill(col.rectTransform, Vector2.zero, Vector2.zero);
        var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;

        charIdentity = UiKit.Label("Identity", col.transform, "", 16, FontStyle.Bold, TextAnchor.UpperLeft);
        charPoints = UiKit.Label("Points", col.transform, "", 15, FontStyle.Bold, TextAnchor.UpperLeft);
        charPoints.color = UiKit.Gold;

        // Six allocatable stat rows (moved here from the retired StatPanel): value · next-cost · + button.
        for (int i = 0; i < StatOrder.Length; i++) BuildStatRow(col.transform, i);

        var div = UiKit.Img("Div", col.transform, new Color(1, 1, 1, 0.12f));
        div.gameObject.AddComponent<LayoutElement>().preferredHeight = 2;

        charDerived = UiKit.Label("Derived", col.transform, "", 13, FontStyle.Normal, TextAnchor.UpperLeft);
        charDerived.lineSpacing = 1.1f;   // many more lines now than the old 5-line summary — tighter so they all fit
    }

    void BuildStatRow(Transform parent, int i)
    {
        var stat = StatOrder[i];
        var row = UiKit.Img("Row_" + stat, parent, new Color(1, 1, 1, 0.04f));
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 26;
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6; h.padding = new RectOffset(6, 6, 0, 0);
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandHeight = true; h.childAlignment = TextAnchor.MiddleLeft;

        // Stat Family Icon v1: a procedural gold emblem in the left gutter (recognition by mark, not label).
        StatGlyph.Build(row.transform, stat);

        var name = UiKit.Label("Name", row.transform, stat.ToString(), 15, FontStyle.Bold, TextAnchor.MiddleLeft);
        name.gameObject.AddComponent<LayoutElement>().preferredWidth = 44;

        statValue[i] = UiKit.Label("Val", row.transform, "1", 15, FontStyle.Normal, TextAnchor.MiddleRight);
        statValue[i].gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        statCost[i] = UiKit.Label("Cost", row.transform, "", 12, FontStyle.Normal, TextAnchor.MiddleRight);
        statCost[i].gameObject.AddComponent<LayoutElement>().preferredWidth = 38;

        var btn = UiKit.Btn("+", row.transform, () => { if (Character.Instance != null) Character.Instance.AddStat(stat); });
        btn.gameObject.AddComponent<LayoutElement>().preferredWidth = 28;
        statPlus[i] = btn;
    }

    void RefreshCharacter()
    {
        if (charIdentity == null || Character.Instance == null) return;
        var c = Character.Instance;
        charIdentity.text = $"<color=#C8A862>Adventurer</color>\nLevel {c.Level}    XP {c.XP}/{c.XPToNext}";
        charPoints.text = $"Stat Points: {c.StatPoints}";

        for (int i = 0; i < StatOrder.Length; i++)
        {
            var s = StatOrder[i];
            statValue[i].text = c.GetStat(s).ToString();
            int cost = c.CostToRaise(s);
            bool capped = cost < 0;
            bool can = c.CanRaise(s);
            statCost[i].text = capped ? "MAX" : "(" + cost + ")";
            statCost[i].color = capped ? UiKit.Gold
                              : can    ? new Color(0.65f, 0.85f, 0.65f)
                                       : new Color(0.60f, 0.45f, 0.45f);
            statPlus[i].interactable = can;
        }

        // The number shown is the character's OWN value; whatever buffs/debuffs add or take sits in parentheses after
        // it (green = better, red = worse). Diffing the two views keeps every line honest — no line can silently
        // include a buff while its neighbour doesn't.
        var b = c.BaseDerived;
        var d = c.DerivedWithStates;
        var pc = PlayerBody();
        // Hard DEF/M.DEF ride their DEF line as a "+N%" suffix — they're the same defence read two ways (flat
        // subtraction, then a percentage), so pairing them beats giving each its own row.
        charDerived.text =
            "<color=#A79E88>DERIVED</color>\n" +
            $"ATK   {b.atkMin}-{b.atkMax}{Delta(d.atkMin - b.atkMin, d.atkMax - b.atkMax)}    M.ATK {b.matkMin}-{b.matkMax}{Delta(d.matkMin - b.matkMin, d.matkMax - b.matkMax)}\n" +
            $"DEF   {b.def}{Hard(b.hardDefPct)}{Delta(d.def - b.def)}    M.DEF {b.mdef}{Hard(b.hardMdefPct)}{Delta(d.mdef - b.mdef)}\n" +
            $"HIT   {b.hit}{Delta(d.hit - b.hit)}    FLEE {b.flee}{Delta(d.flee - b.flee)}\n" +
            $"CRIT  {Pct(b.critChance)}{DeltaPct(d.critChance - b.critChance)}  ×{b.critMult:0.00}{Delta(d.critMult - b.critMult)}\n" +
            $"ASPD  ×{b.aspdMult:0.00}{Delta(d.aspdMult - b.aspdMult)}    CAST ×{b.castTimeMult:0.00}{Delta(d.castTimeMult - b.castTimeMult)}\n" +
            $"MOVE  {b.moveSpeed}{Delta(d.moveSpeed - b.moveSpeed)}{Gait(pc)}\n" +
            $"GUARD {Pct(b.guardCutPct)}{DeltaPct(d.guardCutPct - b.guardCutPct)}    BARRIER {b.barrierHP}{Delta(d.barrierHP - b.barrierHP)}\n" +
            $"LEECH {Pct(b.lifeStealPct)}{DeltaPct(d.lifeStealPct - b.lifeStealPct)}    MP LEECH {Pct(b.manaLeechPct)}{DeltaPct(d.manaLeechPct - b.manaLeechPct)}\n" +
            $"REFLECT {Pct(b.reflectPct)}{DeltaPct(d.reflectPct - b.reflectPct)}\n" +
            $"HP {c.MaxHP}  MP {c.MaxMP}  STA {Mathf.RoundToInt(c.Stamina)}/{Mathf.RoundToInt(c.maxStamina)}";
    }

    static string Pct(float f01) => Mathf.RoundToInt(f01 * 100) + "%";
    static string Hard(int pct) => pct > 0 ? $" <color=#A79E88>+{pct}%</color>" : "";

    // Sprint/run gear lives on the player's BODY, not the character sheet — surfaced here because it's a speed the
    // player feels. Shown as the real run speed so it reads next to MOVE.
    static string Gait(Player3DController pc) => pc == null ? "" : $"    RUN {pc.CurrentRunSpeed:0.0}";

    Player3DController playerBody;   // cached: resolved once, re-resolved only if the body was rebuilt
    Player3DController PlayerBody()
    {
        if (playerBody != null) return playerBody;
        var sc = StatusController.Player;
        return playerBody = sc != null ? sc.GetComponent<Player3DController>() : null;
    }

    static readonly string BonusHex = "#6FD46F";   // green = a buff is helping
    static readonly string MalusHex = "#E06A6A";   // red   = a debuff is hurting

    // " (+3)" / " (−2)", coloured; empty when nothing is modifying the stat (no parentheses noise on a clean sheet).
    static string Delta(int diff) => diff == 0 ? "" : $" <color={(diff > 0 ? BonusHex : MalusHex)}>({(diff > 0 ? "+" : "−")}{Mathf.Abs(diff)})</color>";

    // A ranged stat (ATK min-max) shows ONE bonus when both ends move together, and "min-max" when they differ.
    static string Delta(int lo, int hi)
    {
        if (lo == 0 && hi == 0) return "";
        if (lo == hi) return Delta(lo);
        bool up = hi > 0;
        string hex = up ? BonusHex : MalusHex;
        string sign = up ? "+" : "−";
        return $" <color={hex}>({sign}{Mathf.Abs(lo)}-{Mathf.Abs(hi)})</color>";
    }

    // Multiplier stats (ASPD, CAST) read as a ×delta rather than a flat number.
    static string Delta(float diff)
    {
        if (Mathf.Abs(diff) < 0.005f) return "";
        return $" <color={(diff > 0 ? BonusHex : MalusHex)}>({(diff > 0 ? "+" : "−")}{Mathf.Abs(diff):0.00})</color>";
    }

    // Fraction stats (guard cut, leech, reflect, crit chance) read as a percentage-point delta.
    static string DeltaPct(float diff)
    {
        int pts = Mathf.RoundToInt(diff * 100);
        if (pts == 0) return "";
        return $" <color={(pts > 0 ? BonusHex : MalusHex)}>({(pts > 0 ? "+" : "−")}{Mathf.Abs(pts)}%)</color>";
    }

    // ---- helpers --------------------------------------------------------------

    void EnsureTooltip()
    {
        // Always build our own tooltip on a TOP-sorting nested canvas: an existing tooltip from the
        // old GameUICanvas sits at a lower sort order and would render BEHIND these windows. Building
        // ours (its Awake claims ItemTooltip.Instance) keeps hover visible above everything.
        var layer = new GameObject("TooltipLayer", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.DontSaveInEditor };
        layer.transform.SetParent(canvas, false);
        UiKit.Fill((RectTransform)layer.transform, Vector2.zero, Vector2.zero);
        var lc = layer.GetComponent<Canvas>();
        lc.overrideSorting = true;
        lc.sortingOrder = 200;
        var host = layer.transform;

        // The gold border IS the layout root: VerticalLayoutGroup + ContentSizeFitter MUST live on
        // the SAME object (and own the title/body children). The old setup split them — VLG on the
        // inner fill, fitter on the border — so the panel never sized to its content and the title
        // rendered ON TOP of the body's first line (the "doubled title / overlapping header" bug).
        var border = UiKit.Img("ItemTooltip", host, UiKit.Gold);
        var rt = border.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(264, 90);   // width is kept (horizontal fit = Unconstrained); height auto-fits
        border.raycastTarget = false;
        var cg = border.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false; cg.interactable = false;
        var vlg = border.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 10, 12); vlg.spacing = 2;   // 2px gold frame + inner pad
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fitter = border.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Dark inner panel: a background that fills the gold border but is IGNORED by the layout
        // (otherwise the VLG would try to stack it) and drawn behind the text.
        var fill = UiKit.Img("Fill", border.transform, UiKit.PanelFill);
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        fill.raycastTarget = false;
        fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        fill.transform.SetAsFirstSibling();

        var title = UiKit.Label("Title", border.transform, "Item", 18, FontStyle.Normal, TextAnchor.UpperLeft, title: true);
        title.raycastTarget = false;
        var body = UiKit.Label("Body", border.transform, "", 15, FontStyle.Normal, TextAnchor.UpperLeft);
        body.raycastTarget = false;

        // Horizontal strip that ItemTooltip fills with the weapon's granted-skill icons (V2: weapon owns its kit).
        // A layout child of the same VLG so it stacks under the body and the panel auto-sizes to include it.
        var skillRow = new GameObject("SkillIcons", typeof(RectTransform)).GetComponent<RectTransform>();
        skillRow.SetParent(border.transform, false);
        var hlg = skillRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 3; hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        skillRow.gameObject.SetActive(false);

        var tip = border.gameObject.AddComponent<ItemTooltip>();
        tip.panel = rt; tip.titleText = title; tip.bodyText = body; tip.skillRow = skillRow;
    }

    // Green/red "+v LABEL" stat line; skipped when zero.
    static void Stat(System.Text.StringBuilder sb, string label, int v, string suffix = "")
    {
        if (v == 0) return;
        sb.Append(v > 0 ? "\n<color=#7FD17F>+" : "\n<color=#E08A8A>")
          .Append(v).Append(suffix).Append(' ').Append(label).Append("</color>");
    }

    static string Spaced(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
