using TMPro;
using UnityEngine;

/// <summary>
/// WHERE THE GROUND ARROWS LEAD, on the left of the screen while they are up (<see cref="QuestArrows"/>): the name of
/// the map the place is on, and how many metres of route are left on this map — to the place, or to the door on the
/// way there. Gone whenever the arrows are.
///
/// <para>Both come from the arrows' own route, measured once per route; this only writes them, and only when the
/// shown metres or the map change.</para>
/// </summary>
public class RouteDestinationHud : MonoBehaviour
{
    // Above the kill combo (KillComboHud: 18 from the left, its top 110 above the middle), in the HUD's units.
    const float Left = 18f, BottomFromMiddle = 122f, Width = 240f, NameSize = 16f, MetresSize = 14f;

    RectTransform holder;
    TextMeshProUGUI nameText, metresText;
    string shownMap;
    int shownMetres = -1;
    bool shownThroughDoor, shown;

    void Start()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { enabled = false; return; }
        holder = (RectTransform)new GameObject("HUD_RouteDestination", typeof(RectTransform)).transform;
        holder.SetParent(canvas.transform, false);
        holder.anchorMin = holder.anchorMax = new Vector2(0f, 0.5f);
        holder.pivot = new Vector2(0f, 0f);
        holder.anchoredPosition = new Vector2(Left, BottomFromMiddle);
        holder.sizeDelta = new Vector2(Width, NameSize + MetresSize + 8f);
        nameText = Line("Map", NameSize, FontStyles.Bold, NameSize + MetresSize + 8f);
        metresText = Line("Metres", MetresSize, FontStyles.Normal, MetresSize + 4f);
        holder.gameObject.SetActive(false);
    }

    TextMeshProUGUI Line(string name, float size, FontStyles style, float top)
    {
        var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rt.SetParent(holder, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(0f, top);
        rt.sizeDelta = new Vector2(Width, size + 4f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = UiKit.Font;
        t.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.Font, 0.25f);
        t.fontSize = size;
        t.fontStyle = style;
        t.color = UiKit.TextOnDim;
        t.alignment = TextAlignmentOptions.Left;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        return t;
    }

    void LateUpdate()
    {
        if (holder == null) return;
        string map = QuestArrows.Route.Count > 0 ? QuestArrows.DestinationMap : null;
        bool on = !string.IsNullOrEmpty(map);
        if (on != shown) { holder.gameObject.SetActive(on); shown = on; }
        if (!on) { shownMap = null; shownMetres = -1; return; }

        int metres = Mathf.RoundToInt(QuestArrows.RouteMetres);
        bool throughDoor = QuestArrows.ThroughADoor;
        if (map != shownMap)
        {
            var atlas = WorldRoutes.Atlas;
            nameText.text = atlas != null ? atlas.NameOf(map) : map;
            shownMap = map;
        }
        if (metres != shownMetres || throughDoor != shownThroughDoor)
        {
            metresText.text = metres.ToString(System.Globalization.CultureInfo.InvariantCulture)
                              + (throughDoor ? " m to the next door" : " m");
            shownMetres = metres;
            shownThroughDoor = throughDoor;
        }
    }
}
