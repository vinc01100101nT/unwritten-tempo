using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The <b>System</b> window — what Escape opens when nothing else answers it. Holds the out-of-game menu:
/// <b>Settings</b> shows the <see cref="SettingsWindow"/>; <b>Select character</b> and <b>Log out</b> leave the
/// world through <see cref="GameRoot"/>, one keeping the account signed in and the other not; <b>Quit game</b>
/// ends the process through <see cref="GameExit"/>.
///
/// <para>Escape is shared with every other panel (they all read <see cref="Hotkeys.CancelDown"/>), so this
/// window is deliberately LAST in line: it runs in <c>LateUpdate</c>, after every panel's <c>Update</c>, and
/// only acts when <see cref="Hotkeys.CancelHandledThisFrame"/> is false — i.e. Escape closed nothing and
/// cancelled nothing. So Escape still closes an open bag/chat/aim first, and only opens System once the
/// screen is clear.</para>
///
/// Self-builds a <see cref="UiKit.Window"/> on the persistent GameUICanvas (added by
/// <see cref="Editor.UIShellBuilder"/>), like the other HUD panels.
/// </summary>
public class SystemWindow : MonoBehaviour
{
    GameObject root;
    SettingsWindow settings;

    void Start()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { enabled = false; return; }
        settings = GetComponent<SettingsWindow>();

        var content = UiKit.Window(canvas.transform, "System", new Vector2(240, 220), Vector2.zero, out root);

        var list = UiKit.Img("Entries", content, new Color(0, 0, 0, 0));
        list.raycastTarget = false;
        UiKit.Fill(list.rectTransform, Vector2.zero, Vector2.zero);
        var vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        Entry(list.transform, "Settings", OpenSettings);
        Entry(list.transform, "Select character", GameRoot.BackToCharacterSelect);
        Entry(list.transform, "Log out", () => GameRoot.LogOut());
        Entry(list.transform, "Quit game", () => GameExit.Quit());
    }

    // One menu line. Every entry is the same height, so the list reads as a column whatever is in it.
    static void Entry(Transform list, string label, System.Action onClick)
    {
        var btn = UiKit.Btn(label, list, onClick);
        btn.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
    }

    void OpenSettings()
    {
        if (settings != null) settings.Open();
    }

    void LateUpdate()
    {
        if (root == null) return;
        if (!Hotkeys.CancelDown()) return;
        if (Hotkeys.CancelHandledThisFrame) return;   // a panel/aim already spent this Escape

        Hotkeys.MarkCancelHandled();

        // Settings sits on top of the menu that opened it, so Escape peels it off first.
        if (settings != null && settings.IsOpen) { settings.Close(); return; }

        if (UiAnim.IsOpen(root)) { UiAnim.Close(root); UISounds.PlayClose(); }
        else { UiAnim.Open(root); UISounds.PlayOpen(); }
    }
}
