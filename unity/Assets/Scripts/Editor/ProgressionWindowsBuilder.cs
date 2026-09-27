using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One-click setup for the Inventory and Character windows: drops a canvas carrying
/// <see cref="ProgressionWindows"/> (+ its <see cref="InventoryStore"/>)
/// into the open scene. Press Play and toggle with B / C (Esc closes). Reads the live <see cref="Character"/> from the Systems scene for real stats.
/// </summary>
public static class ProgressionWindowsBuilder
{
    const string CanvasName = "ProgressionWindowsCanvas";

    [MenuItem("Tools/unwritten/UI/Build Progression Windows", priority = 121)]
    static void Build()
    {
        var existing = GameObject.Find(CanvasName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var go = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        UiKit.ApplyTextCrispness(scaler);

        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(go.transform, false);
        }

        go.AddComponent<InventoryStore>();
        go.AddComponent<ProgressionWindows>();

        Undo.RegisterCreatedObjectUndo(go, "Build Progression Windows");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("[unwritten] Built Progression Windows (Inventory/Character). Press Play: " +
                  "B = Inventory, C = Character, Esc = close. Stats read the live Character " +
                  "(load via Systems scene); layout works standalone too.");
    }
}
