using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The <b>System</b> window — what Escape opens when nothing else answers it. Holds the out-of-game menu:
/// <b>Settings</b> shows the <see cref="SettingsWindow"/>; <b>Auto-Combat</b> shows the <see cref="AutoCombatWindow"/>;
/// <b>Select character</b> and <b>Log out</b> leave the
/// world through <see cref="GameRoot"/>, one keeping the account signed in and the other not; <b>Quit game</b>
/// ends the process through <see cref="GameExit"/>.
///
/// <para><b>While the player is dead, the death window takes System's place.</b> It opens by itself once the death
/// animation has played through, holds <b>Return to last saved point</b> (asks the server to stand them up at their save point) and the same
/// three ways out, and Escape opens and closes IT instead of System until they stand back up — then it closes by
/// itself. The server alone decides the death and the return; this only watches the character's health.</para>
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
    GameObject deathRoot;
    SettingsWindow settings;
    bool wasDead;
    bool deathWindowPending;
    float deathWindowAt;
    // When the person at this keyboard died, for the fade to grey; below zero while they are alive.
    float diedAt = -1f;

    static SystemWindow instance;

    void Start()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { enabled = false; return; }
        settings = GetComponent<SettingsWindow>();

        instance = this;

        var list = EntryList(UiKit.Window(canvas.transform, "System", new Vector2(240, 262), Vector2.zero, out root));
        Entry(list, "Settings", OpenSettings);
        Entry(list, "Auto-Combat", AutoCombatWindow.Open);
        Entry(list, "Emblem", EmblemWindow.Open);
        Entry(list, "Select character", GameRoot.BackToCharacterSelect);
        Entry(list, "Log out", () => GameRoot.LogOut());
        Entry(list, "Quit game", () => GameExit.Quit());

        var deathList = EntryList(UiKit.Window(canvas.transform, "System", new Vector2(240, 220), Vector2.zero, out deathRoot));
        Entry(deathList, "Return to last saved point", ReturnToSavePoint);
        Entry(deathList, "Select character", GameRoot.BackToCharacterSelect);
        Entry(deathList, "Log out", () => GameRoot.LogOut());
        Entry(deathList, "Quit game", () => GameExit.Quit());
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        HandDrawnFX.DeathGrey = 0f;
    }

    // A window's column of menu lines, filling its body.
    static Transform EntryList(RectTransform content)
    {
        var list = UiKit.Img("Entries", content, new Color(0, 0, 0, 0));
        list.raycastTarget = false;
        UiKit.Fill(list.rectTransform, Vector2.zero, Vector2.zero);
        var vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;
        return list.transform;
    }

    // One menu line. Every entry is the same height, so the list reads as a column whatever is in it.
    static void Entry(Transform list, string label, System.Action onClick) => UiKit.ListButton(list, label, onClick, 34);

    void OpenSettings()
    {
        if (settings != null) settings.Open();
    }

    // Only ASKS. The server stands them up and moves them, and the window closes when their health comes back.
    static void ReturnToSavePoint()
    {
        var me = Character.Instance;
        if (me == null || !me.IsDead) return;
        // THE CURTAIN GOES UP ON THE PRESS, not when the server's answer arrives.
        LoadingScreen.Show();
        me.Revive();
    }

    /// <summary>Press "Return to last saved point", if there is anybody dead to press it for. For a player who is not
    /// using a mouse (AgentLink); the button itself goes through the same body.</summary>
    public static bool PressReturnToSavePoint()
    {
        var me = Character.Instance;
        if (instance == null || me == null || !me.IsDead) return false;
        UISounds.PlayClick();
        ReturnToSavePoint();
        return true;
    }

    void LateUpdate()
    {
        if (root == null) return;

        // DYING AND STANDING BACK UP, read off the character's health each frame — a bool compare, no event to
        // lose when the character is rebuilt for another sign-in.
        var me = Character.Instance;
        bool dead = me != null && me.IsDead;
        if (dead != wasDead)
        {
            wasDead = dead;
            if (dead)
            {
                if (settings != null && settings.IsOpen) settings.Close();
                if (UiAnim.IsOpen(root)) UiAnim.Close(root);
                // THE DEATH WINDOW WAITS FOR THE FALL: it opens once the body's death animation has played through,
                // its length read off the clip this body plays (0 with no clip, so it opens at once).
                var body = StanceController.Player;
                deathWindowAt = Time.time + (body != null ? body.NaturalDurationOf(StanceId.Die) : 0f);
                deathWindowPending = true;
                diedAt = Time.time;
            }
            else
            {
                deathWindowPending = false;
                if (UiAnim.IsOpen(deathRoot)) UiAnim.Close(deathRoot);
                diedAt = -1f;
                HandDrawnFX.DeathGrey = 0f;
            }
        }
        // THE SCREEN FADES TO GREY from the moment of death, and comes back at once when they stand up.
        if (diedAt >= 0f) HandDrawnFX.DeathGrey = DeathScreenMath.GreyAt(Time.time - diedAt);
        if (deathWindowPending && Time.time >= deathWindowAt)
        {
            deathWindowPending = false;
            UiAnim.Open(deathRoot);
        }

        if (!Hotkeys.CancelDown()) return;
        // a panel/aim already spent this Escape
        if (Hotkeys.CancelHandledThisFrame) return;
        // While the death animation plays, Escape opens nothing: the death window has not arrived yet.
        if (deathWindowPending) return;

        Hotkeys.MarkCancelHandled();

        // Settings sits on top of the menu that opened it, so Escape peels it off first.
        if (settings != null && settings.IsOpen) { settings.Close(); return; }

        // WHILE DEAD, THE DEATH WINDOW IS WHAT ESCAPE OPENS AND CLOSES — System is not reachable until they stand up.
        var menu = dead ? deathRoot : root;
        if (UiAnim.IsOpen(menu)) { UiAnim.Close(menu); UISounds.PlayClose(); }
        else { UiAnim.Open(menu); UISounds.PlayOpen(); }
    }
}
