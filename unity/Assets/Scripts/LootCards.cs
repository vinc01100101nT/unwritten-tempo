using UnityEngine;

/// <summary>
/// THE CARDS THAT POP UP AS THINGS ARRIVE IN YOUR BAG — top-centre of the screen, one white horizontal item card
/// (<see cref="ItemRow"/>, the shop's own) per arrival: the picture at the left, the name and how many at the right.
/// Shown exactly when the chat logs "Obtained" (<see cref="ChatController.ItemArrived"/>) and from the same two
/// places, so the two can never disagree about what arrived.
///
/// <para>The newest sits at the top and pushes the older ones down; each stays <see cref="Life"/> seconds and fades
/// out over the last <see cref="Fade"/>. A fixed pool of <see cref="Most"/> cards is built once and reused — a
/// burst bigger than that recycles the oldest.</para>
/// </summary>
public class LootCards : MonoBehaviour
{
    const int Most = 6;
    const float Life = 3f, Fade = 0.4f;
    const float Width = 280f, Height = 34f, Gap = 4f, Top = 12f;

    static LootCards host;

    readonly ItemRow[] cards = new ItemRow[Most];
    readonly CanvasGroup[] groups = new CanvasGroup[Most];
    readonly float[] shownAt = new float[Most];
    // Card indexes, newest first; the first `live` of them are on screen.
    readonly int[] order = new int[Most];
    int live;

    /// <summary>One more card at the top: <paramref name="count"/> of <paramref name="def"/>, refined
    /// <paramref name="folds"/> times, in <paramref name="rarity"/>'s colour.</summary>
    public static void Show(Item def, int count, int folds, Rarity rarity, int cardSlots = 0)
    {
        if (def == null || count <= 0 || GameServer.IsServer) return;
        if (host == null && !Build()) return;
        host.Push(def, count, folds, rarity, cardSlots);
    }

    static bool Build()
    {
        var canvas = UiKit.HudCanvas;
        if (canvas == null) return false;
        var go = new GameObject("LootCards", typeof(RectTransform)) { hideFlags = HideFlags.DontSaveInEditor };
        var rt = (RectTransform)go.transform;
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(Width, Most * (Height + Gap));
        rt.anchoredPosition = new Vector2(0f, -Top);
        host = go.AddComponent<LootCards>();
        for (int i = 0; i < Most; i++)
        {
            var slot = new GameObject("Card", typeof(RectTransform));
            var srt = (RectTransform)slot.transform;
            srt.SetParent(rt, false);
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(Width, Height);
            host.groups[i] = slot.AddComponent<CanvasGroup>();
            host.groups[i].blocksRaycasts = false;
            host.cards[i] = ItemRow.Make(srt, Height, false, raritySquare: false);
            slot.SetActive(false);
            host.order[i] = i;
        }
        return true;
    }

    void Push(Item def, int count, int folds, Rarity rarity, int cardSlots)
    {
        // The oldest card (last in order) is the one reused, and it moves to the front.
        int c = order[Most - 1];
        for (int i = Most - 1; i > 0; i--) order[i] = order[i - 1];
        order[0] = c;
        if (live < Most) live++;

        string name = (folds > 0 ? "+" + folds + " " + def.displayName : def.displayName) + ItemTooltip.SlotSuffix(cardSlots);
        cards[c].Show(def, null, rarity, name, count > 1 ? "×" + count : "", null, Color.white, null, null);
        shownAt[c] = Time.unscaledTime;
        groups[c].alpha = 1f;
        groups[c].gameObject.SetActive(true);
        Place();
    }

    void Place()
    {
        for (int i = 0; i < live; i++)
            ((RectTransform)groups[order[i]].transform).anchoredPosition = new Vector2(0f, -i * (Height + Gap));
    }

    void Update()
    {
        if (live == 0) return;
        float now = Time.unscaledTime;
        // Oldest last: a card past its life comes off the end.
        while (live > 0 && now - shownAt[order[live - 1]] >= Life)
        {
            groups[order[live - 1]].gameObject.SetActive(false);
            live--;
        }
        for (int i = 0; i < live; i++)
        {
            float left = Life - (now - shownAt[order[i]]);
            groups[order[i]].alpha = left < Fade ? left / Fade : 1f;
        }
    }
}
