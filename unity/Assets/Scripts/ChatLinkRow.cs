using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// A chat row that names items (<see cref="ChatLinks"/>): while the pointer is on one of them its full name shows,
/// and a right-click on it opens its card — the card the sender's own bag shows for it, built by the server and drawn
/// here as given (<see cref="ItemTooltip"/>).
/// </summary>
public class ChatLinkRow : MonoBehaviour, IPointerMoveHandler, IPointerExitHandler, IPointerClickHandler
{
    TMP_Text text;
    readonly List<string> titles = new List<string>(ChatLinks.MostLinksInALine);
    readonly List<Color> colours = new List<Color>(ChatLinks.MostLinksInALine);
    readonly List<string> bodies = new List<string>(ChatLinks.MostLinksInALine);
    readonly List<string> items = new List<string>(ChatLinks.MostLinksInALine);
    int showing = -1;

    /// <summary>Give <paramref name="row"/> the tooltips of the links in it, in the order its text numbers them.</summary>
    public static void Attach(TMP_Text row, List<string> linkTitles, List<Color> linkColours, List<string> linkBodies,
                              List<string> linkItems)
    {
        row.raycastTarget = true;   // the pointer has to land on the row to find a link in it
        var hover = row.gameObject.AddComponent<ChatLinkRow>();
        hover.text = row;
        hover.titles.AddRange(linkTitles);
        hover.colours.AddRange(linkColours);
        hover.bodies.AddRange(linkBodies);
        hover.items.AddRange(linkItems);
    }

    public void OnPointerMove(PointerEventData e)
    {
        if (text == null) return;
        var canvas = text.canvas;
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        int link = TMP_TextUtilities.FindIntersectingLink(text, e.position, cam);
        int which = -1;
        if (link >= 0 && int.TryParse(text.textInfo.linkInfo[link].GetLinkID(), out int id) && id >= 0 && id < titles.Count)
            which = id;
        if (which == showing) return;
        showing = which;
        if (which < 0) ItemTooltip.HideTip();
        else ItemTooltip.ShowName(titles[which]);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Right || showing < 0) return;
        ItemTooltip.PinText(titles[showing], colours[showing], bodies[showing], ItemCatalog.Resolve(items[showing]));
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (showing >= 0) ItemTooltip.HideTip();
        showing = -1;
    }

    void OnDisable()
    {
        if (showing >= 0) ItemTooltip.HideTip();
        showing = -1;
    }
}
