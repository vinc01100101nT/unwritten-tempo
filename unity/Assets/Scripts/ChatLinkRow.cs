using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// A chat row that names items (<see cref="ChatLinks"/>): while the pointer is on one of them, its tooltip shows —
/// the card the sender's own bag shows for it, built by the server and drawn here as given.
/// </summary>
public class ChatLinkRow : MonoBehaviour, IPointerMoveHandler, IPointerExitHandler
{
    TMP_Text text;
    readonly List<string> titles = new List<string>(ChatLinks.MostLinksInALine);
    readonly List<Color> colours = new List<Color>(ChatLinks.MostLinksInALine);
    readonly List<string> bodies = new List<string>(ChatLinks.MostLinksInALine);
    int showing = -1;

    /// <summary>Give <paramref name="row"/> the tooltips of the links in it, in the order its text numbers them.</summary>
    public static void Attach(TMP_Text row, List<string> linkTitles, List<Color> linkColours, List<string> linkBodies)
    {
        row.raycastTarget = true;   // the pointer has to land on the row to find a link in it
        var hover = row.gameObject.AddComponent<ChatLinkRow>();
        hover.text = row;
        hover.titles.AddRange(linkTitles);
        hover.colours.AddRange(linkColours);
        hover.bodies.AddRange(linkBodies);
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
        else ItemTooltip.ShowText(titles[which], colours[which], bodies[which]);
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
