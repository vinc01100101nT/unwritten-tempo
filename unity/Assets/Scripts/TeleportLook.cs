using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HOW A TELEPORT LOOKS — a Fly Wing, a Butterfly Wing, a door, and arriving in the world at sign-in all share it.
///
/// <para><b>Leaving:</b> on the frame the screen starts fading to black, the teleport effect starts where the person
/// stands and their body vanishes. <b>Arriving:</b> on the frame the screen has finished fading back in, the effect
/// starts where they stand and their body appears. Vanishing and appearing happen on the exact frame the effect
/// starts, never a frame either side.</para>
///
/// <para>The same calls draw it on everybody else's screen: the copy of this person a watching machine draws is hidden
/// and shown when the server's "away" state for them changes (see <see cref="NetPlayer"/>).</para>
///
/// <para>Each time the effect starts, its sound starts with it (<see cref="CharacterSpec.teleportSfx"/>).</para>
///
/// <para>PERFORMANCE: nothing runs between teleports. While a body is hidden, its renderers are switched off once a
/// frame — one walk of that one body's parts into a reused list, so a weapon drawn or a model swapped mid-trip stays
/// hidden too. About 0.6 seconds per trip for your own body, at most a few seconds for a watched one.</para>
/// </summary>
public static class TeleportLook
{
    /// <summary>The teleport effect, by its effect-table name.</summary>
    public const string EffectId = "env.teleport";

    // This machine's own person has vanished (or has not appeared yet at sign-in) and appears when the screen clears.
    static bool ownWaitingToAppear;

    /// <summary>True from this machine's own person vanishing (or signing in) until they appear — their plate and
    /// level smoke are hidden for as long.</summary>
    public static bool OwnWaitingToAppear => ownWaitingToAppear;

    // ---- this machine's own person ------------------------------------------------------------------------

    /// <summary>A teleport within the map you stand in: the effect and the vanish now, the screen fades to black,
    /// <paramref name="whileBlack"/> moves you, and you appear once the screen has faded back in.</summary>
    public static void CoverWithFade(Action whileBlack)
    {
        LeaveHere();
        ScreenFade.Through(whileBlack);
    }

    /// <summary>A teleport into another map: the effect and the vanish now, the screen fades to black and the loading
    /// screen goes up over it — and only then <paramref name="thenAtBlack"/> runs (the ask, or the move itself), so
    /// the old map never vanishes before the screen is black. In the new map the loading screen goes, the black fades
    /// away, and you appear.</summary>
    public static void CoverWithCurtain(Action thenAtBlack = null)
    {
        LeaveHere();
        ScreenFade.ToBlack(ShowCurtain);
        ScreenFade.AtBlack(thenAtBlack);
    }

    static readonly Action ShowCurtain = () => LoadingScreen.Show();

    /// <summary>Signing in: there is nowhere to leave from, so only the arrival — you appear once the loading screen
    /// has faded away.</summary>
    public static void AppearWhenScreenClears()
    {
        ownWaitingToAppear = true;
        Ticker.Ensure();
    }

    /// <summary>Called by both fades on the frame the screen is fully clear again. Does nothing unless this machine's
    /// own person is waiting to appear.</summary>
    public static void ScreenCleared()
    {
        if (!ownWaitingToAppear) return;
        ownWaitingToAppear = false;
        var body = OwnBody();
        if (body != null) Appear(body, true);
        if (NetPlayer.Mine != null) NetPlayer.Mine.SayShown(true);
    }

    /// <summary>This machine just joined a world. A person not mid-teleport is shown to everybody at once; one who
    /// is mid-teleport is shown when their screen clears (<see cref="ScreenCleared"/>).</summary>
    public static void JoinedWorld()
    {
        if (!ownWaitingToAppear && NetPlayer.Mine != null) NetPlayer.Mine.SayShown(false);
    }

    // A second leave before the first has landed (a door taken mid-fade) is the same trip: no second effect.
    static void LeaveHere()
    {
        if (ownWaitingToAppear) return;
        ownWaitingToAppear = true;
        // An aim still on screen goes with them: nothing they pick while hidden may be cast.
        if (UnitTargeter.Active) UnitTargeter.Cancel();
        if (GroundTargeter.Active) GroundTargeter.Cancel();
        var body = OwnBody();
        if (body != null) Vanish(body);
        Ticker.Ensure();
    }

    // THE MODEL, NOT THE AVATAR: the person at this keyboard is drawn by a separate object riding their avatar, not a
    // child of it, and its hand is the one thing named for it (WeaponSocket.OnMyAvatar).
    static Transform OwnBody()
    {
        var hand = WeaponSocket.OnMyAvatar;
        return hand != null ? hand.transform : null;
    }

    // ---- any body ---------------------------------------------------------------------------------------

    /// <summary>The effect starts at <paramref name="body"/> and the body vanishes, on this frame.</summary>
    public static void Vanish(Transform body)
    {
        if (body == null) return;
        PlayAt(body.position);
        Hide(body);
    }

    /// <summary>The body appears on this frame — with the effect starting there when <paramref name="withEffect"/>.</summary>
    public static void Appear(Transform body, bool withEffect)
    {
        if (body == null) return;
        Show(body);
        if (withEffect) PlayAt(body.position);
    }

    // The effect and its sound — the one Blink plays — start together, at the person's feet.
    static void PlayAt(Vector3 point)
    {
        Vfx.PlayAt(EffectId, point);
        var spec = CharacterSpec.Active;
        if (spec != null) Sfx.PlayAt(spec.teleportSfx, point);
    }

    /// <summary>Hide <paramref name="body"/> with no effect — a body built while its person is away.</summary>
    public static void Hide(Transform body)
    {
        if (body == null || hidden.Contains(body)) return;
        hidden.Add(body);
        SetDrawn(body, false);
        Ticker.Ensure();
    }

    /// <summary>Show <paramref name="body"/> again with no effect. Safe on a body that was never hidden.</summary>
    public static void Show(Transform body)
    {
        if (body == null || !hidden.Remove(body)) return;
        SetDrawn(body, true);
    }

    /// <summary>True while <paramref name="body"/> is hidden — read by what else is drawn on a person (their plate).</summary>
    public static bool IsHidden(Transform body)
    {
        if (hidden.Count == 0 || body == null) return false;
        return hidden.Contains(body);
    }

    /// <summary>TRUE when <paramref name="t"/> is, or rides inside, a person hidden on a teleport — this machine's own
    /// (their avatar or their model) or a watched one. Every effect and sound riding such a person is hidden while it
    /// is (<see cref="Vfx"/>, <see cref="VfxDirector"/>). Two plain checks while nobody is hidden.</summary>
    public static bool Covers(Transform t)
    {
        if (t == null || (hidden.Count == 0 && !ownWaitingToAppear)) return false;
        if (ownWaitingToAppear)
        {
            var avatar = PersistentPlayer3D.Instance;
            if (avatar != null && t.IsChildOf(avatar.transform)) return true;
            var own = OwnBody();
            if (own != null && t.IsChildOf(own)) return true;
        }
        for (int i = 0; i < hidden.Count; i++)
            if (hidden[i] != null && t.IsChildOf(hidden[i])) return true;
        return false;
    }

    static readonly List<Transform> hidden = new List<Transform>(4);
    static readonly List<Renderer> scratch = new List<Renderer>(16);

    // forceRenderingOff rather than enabled: nothing else in the game uses it, so showing again cannot switch on a
    // part something else had switched off (a put-away weapon, a hidden piece). It takes the body's shadow with it.
    static void SetDrawn(Transform body, bool drawn)
    {
        body.GetComponentsInChildren(true, scratch);
        for (int i = 0; i < scratch.Count; i++)
            if (scratch[i] != null) scratch[i].forceRenderingOff = !drawn;
        scratch.Clear();
    }

    // Once a frame, and only while something is hidden or this machine's own person is waiting to appear.
    static void Step()
    {
        if (ownWaitingToAppear)
        {
            var own = OwnBody();
            if (own != null && !hidden.Contains(own)) Hide(own);
        }
        for (int i = hidden.Count - 1; i >= 0; i--)
        {
            if (hidden[i] == null) { hidden.RemoveAt(i); continue; }
            SetDrawn(hidden[i], false);   // perf-ok: only while a body is hidden — one body's parts, into a reused list
        }
    }

    sealed class Ticker : MonoBehaviour
    {
        static Ticker instance;
        public static void Ensure()
        {
            if (instance != null) { instance.enabled = true; return; }
            var go = new GameObject("TeleportLookTicker") { hideFlags = HideFlags.DontSaveInEditor };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Ticker>();
        }
        void LateUpdate()
        {
            Step();
            if (hidden.Count == 0 && !ownWaitingToAppear) enabled = false;
        }
    }
}
