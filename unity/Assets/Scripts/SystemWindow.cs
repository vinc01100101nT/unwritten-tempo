using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The <b>System</b> window — what Escape opens when nothing else answers it. Holds the out-of-game menu;
/// today its only entry is <b>Settings</b>, which shows the <see cref="SettingsWindow"/>.
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

        var content = UiKit.Window(canvas.transform, "System", new Vector2(240, 150), Vector2.zero, out root);

        var list = UiKit.Img("Entries", content, new Color(0, 0, 0, 0));
        list.raycastTarget = false;
        UiKit.Fill(list.rectTransform, Vector2.zero, Vector2.zero);
        var vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        var btn = UiKit.Btn("Settings", list.transform, OpenSettings);
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
