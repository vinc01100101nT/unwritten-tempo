using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// WHAT YOU WEAR, beside the bag: a see-through frame on the bag window's left, a box for each slot around it — the
/// head's beside the head, the weapon's beside the hand, the shoes' beside the feet — and in the middle, YOU. Not a
/// picture of you: while the bag is open the game camera moves in on your character the way it does when you talk to
/// somebody, and frames it so your body stands in this frame, wearing exactly what it wears in the world.
///
/// <para><b>What the pointer does.</b> Dragging a piece of gear out of the bag lights the box it belongs in; letting go
/// of it ANYWHERE in the frame puts it on, in its own slot. A worn piece dragged into the bag, or double-clicked, comes
/// off; dragged onto the hotbar it goes there. Shift-click puts it in the chat field as a link. Every one of these is a
/// request: the server decides.</para>
///
/// <para>The close-up yields to a conversation: while you talk to somebody the camera is theirs, and the bag takes it
/// back when the talk ends.</para>
/// </summary>
public class EquipmentPanel : IItemBoxOwner
{
    public const float Width = 168f;
    const float EdgeThickness = 1f, GapToBag = 4f;
    // How much of the frame's height the body stands in.
    const float BodyShare = 0.82f;
    // How far the close-up looks down on the body, in degrees.
    const float LookDown = 8f;

    // Each slot's box, beside the part of the body it dresses, from the frame's top-left (x right, y down). The shoes sit
    // at the bottom, whatever the frame's height.
    static readonly (EquipSlotType slot, string word, Vector2 at)[] Places =
    {
        (EquipSlotType.Head,    "Head",    new Vector2(4, 30)),
        (EquipSlotType.Garment, "Garment", new Vector2(Width - ItemBox.Size - 4, 70)),
        (EquipSlotType.Armor,   "Body",    new Vector2(4, 120)),
        (EquipSlotType.Weapon,  "Weapon",  new Vector2(Width - ItemBox.Size - 4, 150)),
        (EquipSlotType.Boots,   "Shoes",   new Vector2(4, -1)),
    };

    readonly ItemBox[] boxes = new ItemBox[Places.Length];
    // The worn pieces as the boxes hold them — made once, so a redraw allocates nothing.
    readonly BagEntry[] wornRows = new BagEntry[Places.Length];
    RectTransform frame;
    Canvas canvas;
    bool open, framing;
    Player3DController player;
    static readonly Vector3[] corners = new Vector3[4];

    public int BoxCount => 0;

    /// <summary>Build the frame on the left of <paramref name="window"/>, as tall as it.</summary>
    public void Build(RectTransform window)
    {
        var host = UiKit.Img("Equipment", window, new Color(0, 0, 0, 0));   // see-through, but a drop lands on it
        frame = host.rectTransform;
        frame.anchorMin = frame.anchorMax = new Vector2(0, 0.5f);
        frame.pivot = new Vector2(1, 0.5f);
        frame.anchoredPosition = new Vector2(-GapToBag, 0);
        frame.sizeDelta = new Vector2(Width, window.rect.height > 0 ? window.rect.height : window.sizeDelta.y);
        // A drop anywhere in the frame means "wear this".
        host.gameObject.AddComponent<ItemGridDropZone>().owner = this;

        // The frame's thin edge, in the windows' own edge colour: four lines, nothing between them.
        Line(frame, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, EdgeThickness));
        Line(frame, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, EdgeThickness));
        Line(frame, new Vector2(0, 0), new Vector2(0, 1), new Vector2(EdgeThickness, 0));
        Line(frame, new Vector2(1, 0), new Vector2(1, 1), new Vector2(EdgeThickness, 0));

        float height = frame.sizeDelta.y;
        for (int i = 0; i < Places.Length; i++)
        {
            var box = ItemBox.Make(frame, "Slot_" + Places[i].word, this);
            float y = Places[i].at.y < 0 ? height - ItemBox.Size - 30 : Places[i].at.y;
            ((RectTransform)box.transform).anchoredPosition = new Vector2(Places[i].at.x, -y);
            box.index = i;
            boxes[i] = box;
            wornRows[i] = new BagEntry { count = 1, worn = true, wornSlot = Places[i].slot };
        }

        ItemDrag.Started += OnDragStarted;
        ItemDrag.Ended += OnDragEnded;
    }

    static void Line(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
    {
        var line = UiKit.Img("Edge", parent, UiKit.WindowEdge);
        line.raycastTarget = false;
        var rt = line.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>The bag opened: the camera starts framing your character in the frame.</summary>
    public void Opened()
    {
        open = true;
        if (canvas == null && frame != null) canvas = frame.GetComponentInParent<Canvas>();
        if (player == null && PersistentPlayer3D.Instance != null)
            player = PersistentPlayer3D.Instance.GetComponentInChildren<Player3DController>();
        Refresh();
    }

    /// <summary>The bag closed: the camera goes back to following.</summary>
    public void Closed()
    {
        open = false;
        LetGoOfCamera();
    }

    void LetGoOfCamera()
    {
        if (!framing) return;
        framing = false;
        if (CameraFollow3D.Main != null && !PlayerInteractor.Talking) CameraFollow3D.Main.ExitCinematic();
    }

    /// <summary>Once a frame while the bag is open: keep your character standing in the frame — you may walk, and the
    /// window may be dragged. A conversation has the camera while it lasts.</summary>
    public void Tick()
    {
        if (!open) return;
        if (PlayerInteractor.Talking) { framing = false; return; }
        var follow = CameraFollow3D.Main;
        var cam = CameraRig.Cam;
        if (follow == null || cam == null || player == null || frame == null) return;

        var uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        frame.GetWorldCorners(corners);
        var low = RectTransformUtility.WorldToScreenPoint(uiCam, corners[0]);
        var high = RectTransformUtility.WorldToScreenPoint(uiCam, corners[2]);
        var spot = (low + high) * 0.5f;
        float spotHeight = Mathf.Abs(high.y - low.y) * BodyShare;
        if (spotHeight < 1f || cam.pixelHeight <= 0) return;

        var feet = player.transform.position;
        float bodyHeight = Character3DBody.BodyHeight;
        var bodyMiddle = feet + Vector3.up * (bodyHeight * 0.5f);
        var facing = CameraRig.ForwardXZ;

        CloseUpFraming.Place(bodyMiddle, bodyHeight, facing, LookDown, cam.fieldOfView, cam.aspect,
                             spot.x / cam.pixelWidth, spot.y / cam.pixelHeight, spotHeight / cam.pixelHeight,
                             out var camPos, out var lookAt);
        follow.EnterCinematic(camPos, lookAt);
        framing = true;
        // Turned to the camera, as somebody you talk to turns to you.
        player.FaceWorldPoint(camPos);
    }

    /// <summary>Draw what is worn now.</summary>
    public void Refresh()
    {
        var who = Character.Instance;
        for (int i = 0; i < Places.Length; i++)
        {
            if (boxes[i] == null) continue;
            var piece = who != null ? who.GetEquippedInstance(Places[i].slot) : null;
            var row = wornRows[i];
            row.def = piece != null ? piece.def : null;
            row.instance = piece;
            if (piece != null && piece.def != null) boxes[i].Show(row, ItemBoxSource.Worn);
            else boxes[i].ShowEmpty();
            boxes[i].SetCorner(Places[i].word, piece != null ? UiKit.TextDim : UiKit.TextOnDim);
        }
        // A piece being dragged right now keeps its box lit through a redraw.
        if (ItemDrag.Current != null) OnDragStarted(ItemDrag.Current);
    }

    void OnDragStarted(BagEntry what)
    {
        var slot = what != null && what.def != null && !what.worn ? what.def.slot : EquipSlotType.None;
        for (int i = 0; i < Places.Length; i++)
            if (boxes[i] != null) boxes[i].Light(slot != EquipSlotType.None && Places[i].slot == slot);
    }

    void OnDragEnded()
    {
        for (int i = 0; i < boxes.Length; i++)
            if (boxes[i] != null) boxes[i].Light(false);
    }

    public void Bind(int index, ItemBox box) { }

    public void Clicked(ItemBox box, PointerEventData e)
    {
        var entry = box.Entry;
        if (entry == null || entry.def == null || e.button != PointerEventData.InputButton.Left) return;
        if (Hotkeys.ShiftHeld) { ChatController.InsertItemLink(entry); return; }
        if (e.clickCount >= 2 && InventoryStore.Instance != null) InventoryStore.Instance.Unequip(entry.wornSlot);
    }

    public bool Dropped(ItemBox onto, ItemSlot from)
    {
        var fromBox = from != null ? from.GetComponent<ItemBox>() : null;
        var moving = from != null ? from.boundEntry : null;
        // ONLY A PIECE FROM THE BAG is put on by a drop here — whatever box or spot in the frame it lands on, it goes to
        // its own slot. A worn piece let go over the frame stays where it is.
        if (fromBox == null || moving == null || fromBox.source != ItemBoxSource.Bag) return fromBox != null;
        if (moving.IsEquip && InventoryStore.Instance != null) InventoryStore.Instance.Equip(moving);
        return true;
    }

    /// <summary>The window is going for good.</summary>
    public void Dispose()
    {
        ItemDrag.Started -= OnDragStarted;
        ItemDrag.Ended -= OnDragEnded;
        Closed();
    }
}

/// <summary>
/// WHERE THE CAMERA STANDS so a body lands on a given spot of the screen at a given size — the bag's close-up. Pure
/// numbers in, a camera position and a point to look at out.
/// </summary>
public static class CloseUpFraming
{
    /// <param name="bodyMiddle">The middle of the body, half its height above its feet.</param>
    /// <param name="facing">The flat direction the camera looks along (it keeps the side it is already on).</param>
    /// <param name="spotX">Where the body's middle should land, as a share of the screen's width (0 left, 1 right).</param>
    /// <param name="spotY">The same, of its height (0 bottom, 1 top).</param>
    /// <param name="spotHeight">How tall the body should stand, as a share of the screen's height.</param>
    public static void Place(Vector3 bodyMiddle, float bodyHeight, Vector3 facing, float lookDownDegrees,
                             float fovDegrees, float aspect, float spotX, float spotY, float spotHeight,
                             out Vector3 camPos, out Vector3 lookAt)
    {
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
        var turn = Quaternion.LookRotation(facing.normalized, Vector3.up) * Quaternion.Euler(lookDownDegrees, 0f, 0f);

        float halfTan = Mathf.Tan(fovDegrees * 0.5f * Mathf.Deg2Rad);
        // How far away the body must be to stand spotHeight of the screen tall.
        float depth = bodyHeight / (2f * halfTan * Mathf.Max(0.01f, spotHeight));
        // The line through the spot, in the camera's own space, out to that depth.
        float ndcX = spotX * 2f - 1f, ndcY = spotY * 2f - 1f;
        var throughSpot = new Vector3(ndcX * halfTan * aspect * depth, ndcY * halfTan * depth, depth);

        camPos = bodyMiddle - turn * throughSpot;
        lookAt = camPos + turn * Vector3.forward * depth;
    }
}
