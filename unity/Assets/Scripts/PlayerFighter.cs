using System;
using UnityEngine;

/// <summary>
/// THE SERVER'S COPY OF ONE PERSON'S CHARACTER — the thing it rolls their blows with, and the thing a creature
/// actually hurts when it bites them.
///
/// <para><b>Why this had to exist before anything else.</b> A character used to be a single global handle, which is
/// a fact about a screen: one person is at this keyboard. A server holds as many people as have signed in, so it
/// had no way to say whose stats a blow should use — and rather than guess, it refused every swing. Nothing in
/// combat could work until each connection had a character of its own, which is what this builds.</para>
///
/// <para><b>Where the numbers come from.</b> The account service, read with the player's OWN sign-in proof, which
/// they hand over when they join. Nothing about the fighter is taken from what the client says about itself: a
/// person could otherwise arrive claiming to be level 99. The proof also settles WHO they are — the service only
/// answers for characters belonging to the account that proof was issued to — so somebody typing another player's
/// name no longer gets that player's character.</para>
///
/// <para><b>It has no body of its own.</b> The body is the shell the server already keeps for that connection, which
/// is where their reported position lives. Building a second one would be a rig, an animator and a per-frame blend
/// on a machine with no screen — exactly what the shells exist to avoid.</para>
/// </summary>
public static class PlayerFighter
{
    /// <summary>Build the fighter for <paramref name="shell"/> and hand it over as soon as it is ready. Server only.
    ///
    /// <para>The fighter EXISTS immediately, at the character's starting numbers, and is corrected the moment their
    /// real progress arrives. Waiting for the answer instead would leave a person standing in the world unable to
    /// hit anything or be hit for as long as a network round trip takes, which reads as a broken game rather than
    /// as a loading pause.</para></summary>
    public static bool BuildFor(NetPlayer shell, string characterId, string accessToken)
    {
        if (shell == null || !GameServer.IsServer) return false;

        // ONE CHARACTER, ONE PLACE. Two connections playing the same character would each get a live copy of it,
        // both loaded from the same saved progress and both writing back over each other — so the second one is
        // turned away rather than allowed in to fight itself. MY DECISION, and the conservative one: the person
        // already in the world keeps their place. A connection that really has dropped is cleared out by the
        // wire's own timeout, which frees the name a moment later.
        if (!string.IsNullOrEmpty(characterId))
        {
            if (playing.Contains(characterId))
            {
                Debug.LogWarning("[server] '" + shell.playerName + "' is already in this world — the second "
                                 + "connection claiming that character was turned away.");
                return false;
            }
            playing.Add(characterId);
            shell.ServerCharacterId = characterId;
        }

        var host = new GameObject("Fighter_" + shell.playerName) { hideFlags = HideFlags.DontSaveInEditor };
        host.transform.SetParent(shell.transform, false);

        // ORDER MATTERS. The status host reads the unit's state store when it is told who it belongs to, and the
        // character is what owns that store — so the character goes on first and the two are introduced after.
        var character = host.AddComponent<Character>();
        var status = host.AddComponent<StatusController>();
        status.BindUnit(character);
        character.BindStatusHost(status);
        character.BindBody(shell.transform);

        shell.BindCombatUnitOnServer(character);
        PlayerUnits.Enter(character);

        LoadProgressInto(character, shell, characterId, accessToken, shell.playerName);
        return true;
    }

    // Which characters are being played on this server right now. Emptied one name at a time as people leave, so
    // it never grows past the number of people actually here.
    static readonly System.Collections.Generic.HashSet<string> playing = new System.Collections.Generic.HashSet<string>();

    /// <summary>Is this character already being played here? Asked at the door, before a body is made for somebody
    /// who is not going to be let in.</summary>
    public static bool AlreadyPlaying(string characterId)
        => !string.IsNullOrEmpty(characterId) && playing.Contains(characterId);

    /// <summary>Tear the fighter down when its player leaves. The roll of who is here must not keep a name that is
    /// gone, or every creature in the world goes on chasing somebody who is not there.</summary>
    public static void Dismiss(NetPlayer shell)
    {
        if (shell == null) return;

        // Their character is free to be played again — released BEFORE the early return below, because a
        // connection that never got as far as a fighter has still claimed the name.
        if (!string.IsNullOrEmpty(shell.ServerCharacterId)) playing.Remove(shell.ServerCharacterId);

        var unit = shell.ServerCombatUnit;
        if (unit == null) return;

        PlayerUnits.Leave(unit);
        shell.BindCombatUnitOnServer(null);
        if (unit is Character c && c != null) UnityEngine.Object.Destroy(c.gameObject);
    }

    // What the account service answers with — the parts of it this server trusts over anything the client said.
    [Serializable]
    class ProgressReply
    {
        public SaveData progress;

        /// <summary>The NAME and BODY the account service has on record. Taken from here rather than from what the
        /// client announced about itself: a name is what everybody around you reads, and a client that states its
        /// own could state anybody's. The stats were already protected this way; these were not.</summary>
        public string name;
        public string modelName;

        /// <summary>Whether this account is trusted with the game's commands — the ONE answer that decides
        /// whether a summon, a kill or a dummy actually happens. Read here for the same reason as the name
        /// above: a game that asked for it could claim anything, and this reply cannot.</summary>
        public bool isGm;
    }

    static void LoadProgressInto(Character character, NetPlayer shell, string characterId, string accessToken, string who)
    {
        if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(accessToken))
        {
            // SAID OUT LOUD, because the alternative is a player who quietly fights at level 1 forever and a
            // afternoon spent wondering why their gear does nothing.
            Debug.LogWarning("[server] " + who + " joined without sign-in proof, so their character could not be "
                             + "read. They are fighting at starting numbers. An older client sends no proof.");
            return;
        }

        AccountClient.SendAs("GET", "/characters/" + characterId + "/progress", null, accessToken,
                             (ok, body, error) =>
        {
            if (character == null) return;   // they left while the answer was in flight

            if (!ok)
            {
                Debug.LogWarning("[server] could not read " + who + "'s character: " + error
                                 + ". They are fighting at starting numbers.");
                return;
            }

            var reply = JsonUtility.FromJson<ProgressReply>(body);
            if (reply == null) return;

            // WHO THEY REALLY ARE, overwriting what they announced. Everybody around them reads this name and sees
            // this body, so both are corrected to the record the moment it arrives — a person can no longer walk
            // into town wearing somebody else's name.
            if (shell != null)
            {
                if (!string.IsNullOrEmpty(reply.name)) shell.playerName = reply.name;
                if (!string.IsNullOrEmpty(reply.modelName)) shell.modelName = reply.modelName;

                // …and whether they are allowed to change the world. Set unconditionally, so an account that has
                // had the mark taken away loses it here too rather than keeping whatever this body started with.
                shell.ServerIsGm = reply.isGm;
            }

            // A character nobody has played yet comes back as an empty document, which reads as version 0 — that is
            // a fresh character, not a failure, and the starting numbers it already has are the right ones.
            if (reply.progress == null || reply.progress.version <= 0) return;

            character.RestoreSave(reply.progress);
            Debug.Log("[server] " + who + " is level " + character.Level + " — their character is loaded.");
        });
    }
}
