using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// THE KILL COMBO on the left edge of the screen: the run's count, big, in the combat numbers' pixel face and the
/// rising-total yellow, popping every time it climbs; "COMBO" and the bonus it is worth under it; a thin bar draining
/// towards the moment the run ends; and the experience and zeny per hour under that.
///
/// <para>Every number is the server's (<see cref="KillComboReadout"/>). The count is hidden once the end moment the
/// server sent has passed — the same compare the hotbar makes to stop drawing a cooldown — and the two rates stay up
/// from the first kill on. Text is rewritten only when a number changes, which is once per kill.</para>
///
/// <para>Self-builds its holder on the persistent GameUICanvas, added by <see cref="HotbarPanel"/> exactly like the
/// stamina gauge. Draws nothing on a server.</para>
/// </summary>
public class KillComboHud : MonoBehaviour
{
    // Left edge, a little above the middle: below the status box (top-left) and above the chat (bottom-left).
    const float Left = 18f, TopFromMiddle = 110f, Width = 240f;
    const float CountSize = 64f, WordSize = 20f, RateSize = 16f;
    const float BarWidth = 130f, BarHeight = 4f;
    // How much larger the count is drawn the instant it climbs — a little more than a damage number's arrival.
    const float ComboPop = 0.5f;

    static readonly Color RateColor = new Color(0.95f, 0.95f, 0.99f);
    static readonly Color BarTrack = new Color(0f, 0f, 0f, 0.55f);

    Canvas canvas;
    RectTransform holder, countGroup, countRect;
    TextMeshProUGUI countText, wordText, experienceText, zenyText;
    Image drain;
    bool built;

    int seenVersion = -1;
    int shownCombo = -1, shownBonus = -1;
    long shownExperience = -1, shownZeny = -1;
    bool countShown;
    float hideAt, window;
    float popAge = DamagePopup.PopSeconds;

    void Start()
    {
        if (GameServer.IsServer) enabled = false;
    }

    void Update()
    {
        if (!built) { TryBuild(); return; }

        int version = KillComboReadout.Version;
        if (version != seenVersion) { seenVersion = version; Refresh(); }

        if (countShown)
        {
            float left = hideAt - Time.unscaledTime;
            if (left <= 0f) ShowCount(false);
            else drain.fillAmount = window > 0f ? Mathf.Clamp01(left / window) : 0f;
        }

        if (popAge < DamagePopup.PopSeconds)
        {
            popAge += Time.unscaledDeltaTime;
            countRect.localScale = Vector3.one * DamagePopup.Pop(ComboPop, popAge);
        }
    }

    // Copy what the server last sent onto the screen. Runs once per message.
    void Refresh()
    {
        if (!KillComboReadout.Has)
        {
            holder.gameObject.SetActive(false);
            ShowCount(false);
            return;
        }
        holder.gameObject.SetActive(true);

        long experience = KillComboReadout.ExperiencePerHour, zeny = KillComboReadout.ZenyPerHour;
        if (experience != shownExperience)
        {
            experienceText.text = "EXP/h " + experience.ToString("N0", CultureInfo.InvariantCulture);
            shownExperience = experience;
        }
        if (zeny != shownZeny)
        {
            zenyText.text = "Zeny/h " + zeny.ToString("N0", CultureInfo.InvariantCulture);
            shownZeny = zeny;
        }

        // When the run ends, on this machine's own steady clock: the server's end moment, turned into time from now once.
        hideAt = Time.unscaledTime + WorldClock.SecondsUntil(WorldClock.FromEpochSeconds(KillComboReadout.EndsAtEpochSeconds));
        window = KillComboReadout.WindowSeconds;
        bool running = KillComboReadout.Combo > 0 && hideAt > Time.unscaledTime;

        int combo = KillComboReadout.Combo;
        if (running && (combo != shownCombo || !countShown))
        {
            countText.text = combo.ToString(CultureInfo.InvariantCulture);
            shownCombo = combo;
            popAge = 0f;
        }
        int bonus = KillComboReadout.BonusPercent;
        if (bonus != shownBonus)
        {
            wordText.text = bonus > 0 ? "COMBO  +" + bonus.ToString(CultureInfo.InvariantCulture) + "%" : "COMBO";
            shownBonus = bonus;
        }
        ShowCount(running);
    }

    void ShowCount(bool on)
    {
        if (countGroup != null && countShown != on) countGroup.gameObject.SetActive(on);
        countShown = on;
        if (!on) shownCombo = -1;
    }

    void TryBuild()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        holder = NewRect("HUD_KillCombo", canvas.transform);
        holder.anchorMin = holder.anchorMax = new Vector2(0f, 0.5f);
        holder.pivot = new Vector2(0f, 1f);
        holder.anchoredPosition = new Vector2(Left, TopFromMiddle);
        holder.sizeDelta = new Vector2(Width, CountSize + WordSize + BarHeight + 2f * RateSize + 24f);

        // The count, "COMBO" and the draining bar come and go together; the rates under them stay.
        countGroup = NewRect("Combo", holder);
        Place(countGroup, 0f, CountSize + WordSize + BarHeight + 10f, Width);

        countRect = NewRect("Count", countGroup);
        Place(countRect, 0f, CountSize, Width);
        // Pops outwards from the middle of its left edge, where it is pinned.
        countRect.pivot = new Vector2(0f, 0.5f);
        countRect.anchoredPosition = new Vector2(0f, -CountSize * 0.5f);
        countText = PixelText(countRect, CountSize);

        var word = NewRect("Word", countGroup);
        Place(word, CountSize - 4f, WordSize, Width);
        wordText = PixelText(word, WordSize);
        wordText.text = "COMBO";

        var track = UiKit.Img("Timer", countGroup, BarTrack);
        track.raycastTarget = false;
        Place(track.rectTransform, CountSize + WordSize + 2f, BarHeight, BarWidth);
        drain = UiKit.Img("Left", track.transform, RepeatsDamageTotal.TotalColor);
        drain.sprite = MobNameplate.SharedWhite;
        drain.raycastTarget = false;
        drain.type = Image.Type.Filled;
        drain.fillMethod = Image.FillMethod.Horizontal;
        drain.fillOrigin = (int)Image.OriginHorizontal.Left;
        UiKit.Fill(drain.rectTransform, Vector2.zero, Vector2.zero);

        float ratesTop = CountSize + WordSize + BarHeight + 12f;
        var experience = NewRect("ExperiencePerHour", holder);
        Place(experience, ratesTop, RateSize + 4f, Width);
        experienceText = RateText(experience);
        var zeny = NewRect("ZenyPerHour", holder);
        Place(zeny, ratesTop + RateSize + 4f, RateSize + 4f, Width);
        zenyText = RateText(zeny);

        countGroup.gameObject.SetActive(false);
        holder.gameObject.SetActive(false);
        built = true;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rt.SetParent(parent, false);
        return rt;
    }

    // Pinned to the top-left of its parent, `top` down from the parent's top edge.
    static void Place(RectTransform rt, float top, float height, float width)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(width, height);
    }

    // The combat numbers' face and edge (DamagePopup), in the rising-total yellow.
    static TextMeshProUGUI PixelText(RectTransform rt, float size)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = UiKit.PixelFont;
        t.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.PixelFont, 0.5f);
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Left;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.richText = false;
        t.color = RepeatsDamageTotal.TotalColor;
        return t;
    }

    // The interface face with its dark edge, as the cast bar's skill name is drawn.
    static TextMeshProUGUI RateText(RectTransform rt)
    {
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = UiKit.Font;
        t.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.Font);
        t.fontSize = RateSize;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Left;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.richText = false;
        t.color = RateColor;
        return t;
    }
}
