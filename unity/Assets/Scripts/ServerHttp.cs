using System;
using UnityEngine.Networking;

/// <summary>
/// How one SERVER talks to another. Separate from <see cref="AccountClient"/> on purpose: that one speaks as a
/// PERSON, carrying whoever's sign-in proof, and everything it can reach is scoped to that person. This one
/// speaks as a MACHINE, carrying the secret the two servers share, and what it can reach — registering a map
/// server, spending somebody's arrival ticket — must never be reachable by a player at all.
///
/// <para>Keeping them apart is the point. One function that took "some credential" would eventually be handed
/// a player's token for a server's job, or the other way round, and neither would look wrong.</para>
/// </summary>
public static class ServerHttp
{
    /// <summary>Told whether it landed, what came back, why it did not, and the answer's status number (0 when
    /// nothing answered at all) — which is what tells "refused for good" apart from "try again later".</summary>
    public delegate void Reply(bool ok, string body, string error, long status);

    public static void Send(string method, string url, string body, string serverSecret, Reply onDone)
    {
        var request = new UnityWebRequest(url, method)
        {
            downloadHandler = new DownloadHandlerBuffer(),
            // A DEADLINE, because the alternative is silence for ever. Without one, a character server that
            // accepts the connection and then never answers leaves this request open — and whatever was waiting
            // on it waits with it. That is how a player stopped being saved for the rest of the session while
            // everything looked fine.
            timeout = 10,
        };
        if (body != null)
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            request.SetRequestHeader("Content-Type", "application/json");
        }
        if (!string.IsNullOrEmpty(serverSecret))
        {
            request.SetRequestHeader("x-server-secret", serverSecret);
            // Which character build this server runs: the character server refuses its saves and storage reads when it
            // is not the one it understands, so a server left running the old build after a deploy cannot rewrite a
            // piece without the parts it does not know.
            request.SetRequestHeader("x-character-build", MapServerLink.CharacterBuild);
        }

        request.SendWebRequest().completed += _ =>
        {
            bool ok = request.result == UnityWebRequest.Result.Success;
            string text = request.downloadHandler != null ? request.downloadHandler.text : "";
            string error = ok ? null : (request.error ?? "the other server did not answer");
            try { onDone?.Invoke(ok, text, error, request.responseCode); }
            finally { request.Dispose(); }
        };
    }
}
