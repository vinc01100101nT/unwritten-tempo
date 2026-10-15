using System;
using Mirror;
using UnityEngine;

/// <summary>
/// The cape emblem half of a player shell: the picture a person uploads, checked and stored by the server, and shown on
/// the back of their cape on every screen that can see them.
///
/// <para><b>Who decides.</b> The player's machine only sends the file it read. The server reads it with the same rules
/// the window shows (<see cref="EmblemImage"/>), writes the small PNG that is stored, and tells everyone the emblem's id
/// (<see cref="emblem"/>). Screens fetch each id's picture once and share one texture per id (<see cref="EmblemCache"/>).</para>
/// </summary>
public partial class NetPlayer
{
    /// <summary>The id of the emblem this person wears — the first 16 hex digits of its stored picture's hash — or empty.</summary>
    [SyncVar(hook = nameof(OnEmblemArrived))] string emblem = "";

    /// <summary>One accepted upload per this many seconds per character: each one is a database write and a fetch by
    /// everyone in view.</summary>
    const float SecondsBetweenEmblemUploads = 10f;
    /// <summary>At most this many emblem pictures sent to one connection per second; asks past it are dropped.</summary>
    const int EmblemAsksPerSecond = 20;

    byte[] emblemPng;
    float nextEmblemUploadAt;
    float emblemAskWindowStart;
    int emblemAsksInWindow;

    // ---- server ---------------------------------------------------------------------------------------------------

    /// <summary>SERVER: the emblem row the character server sent with the character (null or empty when none).</summary>
    public void EmblemArrived(EmblemWire wire)
    {
        if (!isServer) return;
        byte[] png = null;
        if (wire != null && !string.IsNullOrEmpty(wire.png))
        {
            try { png = Convert.FromBase64String(wire.png); }
            catch (FormatException) { png = null; }
        }
        WearEmblemOnServer(png);
    }

    // Put on the stored picture (or none): the store holds it for watchers, and everyone is told its id.
    void WearEmblemOnServer(byte[] png)
    {
        string id = png != null && png.Length > 0 ? EmblemImage.Id(png) : "";
        if (id == emblem && (id.Length == 0 || emblemPng != null)) return;
        EmblemStore.Release(emblem);
        emblemPng = id.Length > 0 ? png : null;
        if (id.Length > 0) EmblemStore.Hold(id, png);
        emblem = id;
    }

    [Command]
    void CmdUploadEmblem(byte[] file)
    {
        if (Leaving) return;
        var c = ServerCharacter;
        if (c == null || string.IsNullOrEmpty(ServerCharacterId)) return;
        if (Time.time < nextEmblemUploadAt)
        {
            TargetEmblemAnswer(connectionToClient, false, "Wait a few seconds before uploading again");
            return;
        }
        // Never more than one byte past the limit is needed to know a file is too big; anything longer is not read.
        if (file != null && file.Length > EmblemImage.MaxFileBytes + 1) file = null;
        var rgba = EmblemImage.Decode(file, out string refusal);
        if (rgba == null)
        {
            if (GameLog.On(LogCat.Server)) LogDid(c, "had an emblem refused: " + refusal);
            TargetEmblemAnswer(connectionToClient, false, refusal);
            return;
        }
        nextEmblemUploadAt = Time.time + SecondsBetweenEmblemUploads;
        var png = EmblemImage.EncodePng(rgba);
        WearEmblemOnServer(png);
        SaveEmblemNow(null);
        if (GameLog.On(LogCat.Server)) LogDid(c, "uploaded emblem " + emblem + " (" + png.Length + " bytes stored)");
        TargetEmblemAnswer(connectionToClient, true, "");
    }

    /// <summary>SERVER: take this person's emblem off and store that it is gone — the GM's removal.</summary>
    public void RemoveEmblemOnServer()
    {
        if (!isServer) return;
        WearEmblemOnServer(null);
        SaveEmblemNow(null);
    }

    [Command]
    void CmdAskEmblem(string id)
    {
        float now = Time.unscaledTime;
        if (now - emblemAskWindowStart >= 1f) { emblemAskWindowStart = now; emblemAsksInWindow = 0; }
        if (++emblemAsksInWindow > EmblemAsksPerSecond) return;
        var png = EmblemStore.Picture(id);
        if (png != null) TargetEmblemPicture(connectionToClient, id, png);
    }

    // Write the emblem's row now — it changes only on an upload or a removal, so it is never written on a timer.
    // Passed to the leave sequence as a flush: whoever takes the character over reads the row as it stands.
    void SaveEmblemNow(Action<bool> whenDone)
    {
        if (string.IsNullOrEmpty(ServerCharacterId)) { whenDone?.Invoke(true); return; }
        string png = emblemPng != null ? Convert.ToBase64String(emblemPng) : "";
        // The door's body: { emblem: { v: 1, png: "…" }, handover: n } — see charserver/CharacterRows.cs.
        string body = string.Concat("{\"emblem\":{\"v\":1,\"png\":\"", png, "\"},\"handover\":", ServerHandover.ToString(), "}");
        SaveOutbox.SubmitEmblem(ServerCharacterId, body, whenDone);
    }

    // Called from OnStopServer: the store forgets this person's hold on the picture.
    void ReleaseEmblemOnServer()
    {
        EmblemStore.Release(emblem);
        emblemPng = null;
    }

    [TargetRpc]
    void TargetEmblemAnswer(NetworkConnectionToClient target, bool accepted, string reason)
        => EmblemWindow.Answered(accepted, reason);

    [TargetRpc]
    void TargetEmblemPicture(NetworkConnectionToClient target, string id, byte[] png) => EmblemCache.Arrived(id, png);

    // ---- asked from the player's machine --------------------------------------------------------------------------

    /// <summary>Send the file the player picked; the answer comes back to the upload window.</summary>
    public void UploadEmblem(byte[] file) { if (file != null) CmdUploadEmblem(file); }

    /// <summary>Ask the server for the picture of <paramref name="id"/>.</summary>
    public void AskEmblem(string id) { if (!string.IsNullOrEmpty(id)) CmdAskEmblem(id); }

    /// <summary>The id of the emblem this person wears, or empty.</summary>
    public string Emblem => emblem;

    // ---- on a screen ----------------------------------------------------------------------------------------------

    string emblemWanted = "";
    GarmentSocket bodyGarment;

    void OnEmblemArrived(string _, string now) => ShowEmblem(now);

    // This machine's own person: their emblem goes on their own avatar from the moment they are in the world.
    void ShowMyEmblemOnStart() => ShowEmblem(emblem);

    // Want the new picture before letting go of the old, so a body changing emblem never drops a shared texture
    // another body still draws. Your own emblem goes on your own avatar; anyone else's on the body built for them.
    void ShowEmblem(string id)
    {
        if (GameServer.IsServer) return;
        if (id == emblemWanted) return;
        string old = emblemWanted;
        emblemWanted = id ?? "";
        bool mine = isLocalPlayer;
        EmblemCache.Want(emblemWanted, t =>
        {
            if (this == null || emblemWanted != id) return;
            if (mine) GarmentSocket.ShowMyEmblem(t);
            else if (bodyGarment != null) bodyGarment.ShowEmblem(t);
        });
        EmblemCache.Release(old);
        if (string.IsNullOrEmpty(emblemWanted))
        {
            if (mine) GarmentSocket.ShowMyEmblem(null);
            else if (bodyGarment != null) bodyGarment.ShowEmblem(null);
        }
    }

    // A body built for this person after the emblem arrived — and on every rebuild — is given it again.
    void GiveBodyItsEmblem(GarmentSocket socket)
    {
        bodyGarment = socket;
        string held = emblemWanted;
        emblemWanted = "";
        ShowEmblem(emblem);
        EmblemCache.Release(held);
    }

    // The body went: its hold on the shared texture goes with it.
    void ReleaseBodyEmblem()
    {
        EmblemCache.Release(emblemWanted);
        emblemWanted = "";
        bodyGarment = null;
        if (isLocalPlayer) GarmentSocket.ShowMyEmblem(null);
    }
}

/// <summary>The emblem row as the character server sends it with a character: { v: 1, png: "&lt;base64&gt;" }, png
/// empty when there is none.</summary>
[Serializable]
public class EmblemWire
{
    public int v;
    public string png;
}
