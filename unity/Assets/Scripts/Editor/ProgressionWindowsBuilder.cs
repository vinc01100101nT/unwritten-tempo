using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One-click setup for the Inventory / Equipment / Character window trio: ensures the starter
/// Item + Card assets exist, then drops a canvas carrying <see cref="ProgressionWindows"/> (+ its
/// <see cref="InventoryStore"/>) into the open scene. Press Play and toggle with I / E / C (Esc
/// closes). Reads the live <see cref="Character"/> from the Systems scene for real stats.
/// </summary>
public static class ProgressionWindowsBuilder
{
    const string CanvasName = "ProgressionWindowsCanvas";
    const string CardDir = "Assets/Resources/Cards";

    [MenuItem("Tools/unwritten/UI/Build Progression Windows", priority = 121)]
    static void Build()
    {
        ItemStarterSet.EnsureStarterItems();
        EnsureStarterCards();

        var existing = GameObject.Find(CanvasName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var go = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(go.transform, false);
        }

        go.AddComponent<InventoryStore>();
        go.AddComponent<ProgressionWindows>();

        Undo.RegisterCreatedObjectUndo(go, "Build Progression Windows");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("[unwritten] Built Progression Windows (Inventory/Equipment/Character). Press Play: " +
                  "I = Inventory, E = Equipment, C = Character, Esc = close. Stats read the live Character " +
                  "(load via Systems scene); layout works standalone too.");
    }

    public static void EnsureStarterCards()
    {
        EnsureFolder(CardDir);
        AddCard("Andre Card",       "+20 ATK",          c => { c.bonusAttack = 20; });
        AddCard("Poring Card",      "+2 LUK",           c => { c.bonusLuk = 2; });
        AddCard("Whisper Card",     "+6 ASPD",          c => { c.bonusAspdPercent = 6; });
        AddCard("Zipper Bear Card", "+30 ATK, -3 ASPD", c => { c.bonusAttack = 30; c.bonusAspdPercent = -3; });
        AssetDatabase.SaveAssets();
    }

    static void AddCard(string name, string blurb, System.Action<Card> set)
    {
        string path = $"{CardDir}/{name}.asset";
        var card = AssetDatabase.LoadAssetAtPath<Card>(path);
        bool isNew = card == null;
        if (isNew) card = ScriptableObject.CreateInstance<Card>();
        card.displayName = name;
        card.blurb = blurb;
        set(card);
        if (isNew) AssetDatabase.CreateAsset(card, path);
        else EditorUtility.SetDirty(card);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
