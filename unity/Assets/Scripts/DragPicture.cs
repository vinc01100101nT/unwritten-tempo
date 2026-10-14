using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE PICTURE THAT FOLLOWS THE CURSOR WHILE ANYTHING IS DRAGGED — a box in the bag or storage, a row in the shop or the
/// station, a hotbar cell, a skill out of the Skills window. One look for every drag in the game: a see-through copy of
/// exactly what was grabbed (its icon, name, count, price — whatever it shows), with the thing left behind dimmed so it
/// reads as lifted out of its place. Every drag handler calls <see cref="Begin"/>, <see cref="Move"/> and
/// <see cref="End"/>; none builds a picture of its own, so a new kind of drag gets this look by calling the same three.
///
/// <para><b>The copy is a picture and nothing else.</b> It is made under a switched-off holder, so none of the copied
/// behaviours ever wakes (a button, a tooltip, a tutorial marker, a drop target), stripped of everything that is not
/// drawing, and blocks no clicks. One copy per drag, destroyed when it ends; nothing runs per frame but its position.</para>
/// </summary>
public static class DragPicture
{
    const float PictureAlpha = 0.8f, LeftBehindAlpha = 0.4f;

    /// <summary>Start a drag of <paramref name="source"/>: its copy on <paramref name="canvas"/>, above everything, at the
    /// cursor; the source dimmed. Returns the copy, to hand back to <see cref="Move"/> and <see cref="End"/>.</summary>
    public static GameObject Begin(RectTransform source, Canvas canvas, Vector2 screenPos)
    {
        if (source == null || canvas == null) return null;
        var copy = Copy(source, canvas.transform);
        UiLayers.Raise(copy.transform, UiLayers.Layer.DragGhost);
        Dim(source, true);
        Move(copy, screenPos);
        return copy;
    }

    /// <summary>Put the picture under the cursor.</summary>
    public static void Move(GameObject picture, Vector2 screenPos)
    {
        if (picture == null) return;
        var canvas = picture.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, screenPos, cam, out var local);
        ((RectTransform)picture.transform).anchoredPosition = local;
    }

    /// <summary>The drag is over, however it ended: the picture goes and the source comes back to full.</summary>
    public static void End(ref GameObject picture, RectTransform source)
    {
        if (picture != null) Object.Destroy(picture);
        picture = null;
        Dim(source, false);
    }

    static void Dim(RectTransform source, bool on)
    {
        if (source == null) return;
        var group = source.GetComponent<CanvasGroup>();
        if (group == null) { if (!on) return; group = source.gameObject.AddComponent<CanvasGroup>(); }
        group.alpha = on ? LeftBehindAlpha : 1f;
    }

    // A copy of what is drawn for `source`, and nothing it does.
    static GameObject Copy(RectTransform source, Transform canvas)
    {
        var holder = new GameObject("DragPictureHolder") { hideFlags = HideFlags.DontSaveInEditor };
        holder.SetActive(false);
        var copy = Object.Instantiate(source.gameObject, holder.transform, false);
        copy.name = "DragPicture";
        copy.hideFlags = HideFlags.DontSaveInEditor;
        // Last-added first, so a behaviour another one requires is removed after the one that requires it.
        var parts = copy.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = parts.Length - 1; i >= 0; i--)
            if (parts[i] is Selectable || !(parts[i] is UnityEngine.EventSystems.UIBehaviour)) Object.DestroyImmediate(parts[i]);
        foreach (var g in copy.GetComponentsInChildren<CanvasGroup>(true)) Object.DestroyImmediate(g);

        var rt = (RectTransform)copy.transform;
        var size = source.rect.size;
        rt.SetParent(canvas, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        var group = copy.AddComponent<CanvasGroup>();
        group.alpha = PictureAlpha;
        group.interactable = false;
        group.blocksRaycasts = false;
        Object.Destroy(holder);
        return copy;
    }
}
