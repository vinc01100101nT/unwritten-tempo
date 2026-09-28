using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// THE ONE skill hover card — the hotbar and the Skills (K) window both show it, so a skill reads the same wherever
/// the cursor meets it. One card per canvas, parented to the canvas ROOT and raised to the tooltip layer, so no cell
/// drawn later can cover it and no window can clip it. The text is <see cref="SkillTooltip.Build(SkillDef)"/>.
/// </summary>
public static class SkillHoverCard
{
    static GameObject root;
    static RectTransform rt;
    static TMP_Text text;
    static readonly Vector3[] cornerBuf = new Vector3[4];

    /// <summary>Show <paramref name="def"/>'s card while the cursor is on <paramref name="cell"/> — above it, or
    /// to its right when <paramref name="beside"/> (for a cell inside a window, where above could leave the screen).</summary>
    public static void Attach(Transform cell, SkillDef def, bool beside = false)
    {
        if (cell == null || def == null) return;
        var cellRT = (RectTransform)cell;
        HoverRelay.Set(cell.gameObject, () => Show(cellRT, def, beside), Hide);
    }

    public static void Hide() { if (root != null) root.SetActive(false); }

    static void Show(RectTransform cellRT, SkillDef def, bool beside)
    {
        var canvas = cellRT.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        canvas = canvas.rootCanvas;
        var canvasRT = (RectTransform)canvas.transform;
        Ensure(canvasRT);

        text.text = SkillTooltip.Build(def);
        UiLayers.Raise(root.transform, UiLayers.Layer.Tooltip);
        root.SetActive(true);

        Camera cam = canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        cellRT.GetWorldCorners(cornerBuf);
        // [1]=top-left, [2]=top-right, [3]=bottom-right: above grows upward from the top edge's middle; beside
        // grows rightward from the right edge's middle
        Vector3 at = beside ? (cornerBuf[2] + cornerBuf[3]) * 0.5f : (cornerBuf[1] + cornerBuf[2]) * 0.5f;
        rt.pivot = beside ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0f);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, at);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screen, cam, out var local))
            rt.anchoredPosition = local + (beside ? new Vector2(10f, 0f) : new Vector2(0f, 8f));
    }

    // Built once per canvas; rebuilt only if the canvas it lived on is gone.
    static void Ensure(RectTransform canvasRT)
    {
        if (root != null && root.transform.parent == canvasRT) return;
        if (root != null) Object.Destroy(root);

        root = new GameObject("SkillInfoCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) { hideFlags = HideFlags.DontSaveInEditor };
        root.transform.SetParent(canvasRT, false);
        var img = UiKit.Card(root.GetComponent<Image>());
        img.raycastTarget = false;
        rt = (RectTransform)root.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(288f, 120f);
        // width fixed; height is driven by the fitter below

        var vlg = root.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = root.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        text = UiKit.MakeText("Text", root.transform, "", 12, FontStyle.Normal, TextAnchor.UpperLeft, wrap: false);
        text.color = UiKit.TextMain;
        text.raycastTarget = false;
        text.richText = true;
        text.textWrappingMode = TextWrappingModes.Normal;

        root.SetActive(false);
    }

}
