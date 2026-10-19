using System;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// THE one place the game talks to the account service. Every request — signing in, listing characters,
/// creating one, saving progress — goes through <see cref="Send"/>, so the address, the sign-in header and
/// the error handling have a single owner instead of a copy per screen.
///
/// <para>Replies are handed back as raw text and read with <see cref="JsonUtility"/> by the caller that knows
/// what it asked for. Each response shape is declared beside the call that uses it.</para>
/// </summary>
public static class AccountClient
{
    /// <summary>What every call reports back: whether it worked, the raw reply, and a message fit to show a
    /// player when it didn't.</summary>
    public delegate void Reply(bool ok, string body, string error);

    [Serializable] class ErrorBody { public string error; }

    /// <summary>Send a request to the account service. <paramref name="body"/> null = no payload;
    /// <paramref name="authorize"/> attaches the signed-in player's token.
    ///
    /// <para><b>A pass refused as out of date is renewed and the request sent once more.</b> The pass lasts minutes
    /// (the account service's ACCESS_TOKEN_SECONDS) and a player stays online for hours, so without this the first
    /// request after it ran out — going back to the character list — was answered "sign in first". A refused pass is
    /// turned away before the request does anything, so sending it again cannot do anything twice.</para></summary>
    public static void Send(string method, string path, string body, bool authorize, Reply onDone)
    {
        if (!authorize) { SendCore(method, path, body, null, (ok, text, error, _) => onDone?.Invoke(ok, text, error)); return; }

        string sentWith = AccountSession.AccessToken;
        SendCore(method, path, body, sentWith, (ok, text, error, status) =>
        {
            if (ok || status != 401 || string.IsNullOrEmpty(AccountSession.RefreshToken))
            {
                onDone?.Invoke(ok, text, error);
                return;
            }
            RenewThen(sentWith, renewed =>
            {
                if (!renewed) { onDone?.Invoke(false, text, error); return; }
                SendCore(method, path, body, AccountSession.AccessToken, (ok2, text2, error2, _) => onDone?.Invoke(ok2, text2, error2));
            });
        });
    }

    // ---- renewing the pass ------------------------------------------------------------------------------
    //
    // ONE renewal at a time. A refresh token is spent the moment it is used, so two requests refused together that
    // each renewed would spend it twice — the second is refused, and the player is signed out by their own game.
    // Everybody refused while a renewal is under way waits for that one.

    [Serializable] class RefreshBody { public string refreshToken; }

    static readonly System.Collections.Generic.List<Action<bool>> waitingForRenewal = new System.Collections.Generic.List<Action<bool>>();
    static bool renewing;

    // Renew the pass that <paramref name="refused"/> was, then answer whether there is a fresh one to send.
    static void RenewThen(string refused, Action<bool> then)
    {
        // Somebody else already renewed it while this request was out.
        if (!string.IsNullOrEmpty(AccountSession.AccessToken) && AccountSession.AccessToken != refused) { then(true); return; }

        waitingForRenewal.Add(then);
        if (renewing) return;
        renewing = true;

        string account = AccountSession.AccountId;
        SendCore("POST", "/sessions/refresh", JsonUtility.ToJson(new RefreshBody { refreshToken = AccountSession.RefreshToken }), null,
                 (ok, text, _, _) =>
        {
            bool renewed = false;
            // Signed out, or signed in as somebody else, while it was out: this answer belongs to nobody now.
            if (ok && AccountSession.AccountId == account)
            {
                var session = JsonUtility.FromJson<SessionReply>(text);
                if (session != null && !string.IsNullOrEmpty(session.accessToken) && session.accountId == account)
                {
                    AccountSession.Renew(session.accessToken, session.refreshToken);
                    renewed = true;
                }
            }
            renewing = false;
            var waiting = waitingForRenewal.ToArray();
            waitingForRenewal.Clear();
            foreach (var w in waiting) w(renewed);
        });
    }

    /// <summary>Which server answers this path. Anything about a CHARACTER — the list, one character's save,
    /// the models one may be created with — belongs to the character server; signing in, refreshing and signing
    /// out stay with the account service.
    ///
    /// <para>Decided by the path in ONE place rather than at each call site, so a request added later cannot be
    /// sent to the wrong server by somebody who did not know there were two. With no character server address
    /// set, a character request still goes to the account service — which no longer answers it: that address is
    /// required, and the settings file says so.</para></summary>
    static string WhereThisGoes(string path)
    {
        var characterServer = AccountSession.CharacterServerUrl;
        if (string.IsNullOrEmpty(characterServer)) return AccountSession.ServerUrl;

        return path != null && (path.StartsWith("/characters", System.StringComparison.Ordinal)
                                || path.StartsWith("/character-models", System.StringComparison.Ordinal))
            ? characterServer
            : AccountSession.ServerUrl;
    }

    /// <summary>The same request, sent as SOMEBODY ELSE — with the sign-in proof they handed over themselves.
    ///
    /// <para>The game server needs this and nothing else does: it holds several people at once, so "the signed-in
    /// player" has no meaning there, and it must read each person's character with that person's own proof. The
    /// account service scopes every answer to whoever the proof belongs to, so this can only ever fetch what its
    /// holder was already entitled to.</para></summary>
    public static void SendAs(string method, string path, string body, string bearerToken, Reply onDone)
        => SendCore(method, path, body, bearerToken, (ok, text, error, _) => onDone?.Invoke(ok, text, error));

    // The request itself, answering with the reply's status code too, so a refused pass can be told apart.
    static void SendCore(string method, string path, string body, string bearerToken, Action<bool, string, string, long> onDone)
    {
        var request = new UnityWebRequest(WhereThisGoes(path) + path, method)
        {
            downloadHandler = new DownloadHandlerBuffer(),
        };
        if (body != null)
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            request.SetRequestHeader("Content-Type", "application/json");
        }
        if (!string.IsNullOrEmpty(bearerToken)) request.SetRequestHeader("Authorization", "Bearer " + bearerToken);
        // WHICH VERSION THIS COPY IS, so the character server can turn an outdated one away with a message to
        // update instead of letting it into a world it no longer matches. Stamped into the copy's settings file by
        // tools/publish-update.py; a copy that was never published (the editor, a build on this desk) sends none.
        var gameVersion = ServerAddress.Setting("GAME_VERSION");
        if (!string.IsNullOrEmpty(gameVersion)) request.SetRequestHeader("X-Game-Version", gameVersion);

        request.SendWebRequest().completed += _ =>
        {
            string text = request.downloadHandler != null ? request.downloadHandler.text : null;
            bool ok = request.result == UnityWebRequest.Result.Success;
            string error = ok ? null : ReadError(request, text);
            long status = request.responseCode;
            request.Dispose();
            onDone?.Invoke(ok, text, error, status);
        };
    }

    // The service explains its refusals in an "error" field. Prefer that wording — it says what the player
    // did wrong ("that name is taken"), where the transport error only says a request failed.
    static string ReadError(UnityWebRequest request, string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                var parsed = JsonUtility.FromJson<ErrorBody>(text);
                if (parsed != null && !string.IsNullOrEmpty(parsed.error)) return parsed.error;
            }
            catch (Exception) { /* not the shape we expected — fall through to the transport error */ }
        }
        return request.result == UnityWebRequest.Result.ConnectionError
            ? "Can't reach the server."
            : request.error;
    }

    // ---- the calls the screens make -------------------------------------------------------------------

    [Serializable] class Credentials { public string email; public string password; }
    [Serializable] class SessionReply { public string accountId; public string accessToken; public string refreshToken; }

    /// <summary>Create an account. Does NOT sign in — the screen signs in afterwards with the same details,
    /// so there is one path that establishes a session rather than two that must agree.</summary>
    public static void Register(string email, string password, Action<bool, string> onDone)
    {
        Send("POST", "/accounts", JsonUtility.ToJson(new Credentials { email = email, password = password }),
             false, (ok, _, error) => onDone?.Invoke(ok, error));
    }

    public static void SignIn(string email, string password, Action<bool, string> onDone)
    {
        Send("POST", "/sessions", JsonUtility.ToJson(new Credentials { email = email, password = password }),
             false, (ok, body, error) =>
        {
            if (!ok) { onDone?.Invoke(false, error); return; }
            var session = JsonUtility.FromJson<SessionReply>(body);
            if (session == null || string.IsNullOrEmpty(session.accessToken))
            {
                onDone?.Invoke(false, "The server's reply made no sense.");
                return;
            }
            AccountSession.SignIn(session.accountId, session.accessToken, session.refreshToken);
            onDone?.Invoke(true, null);
        });
    }

    /// <summary>One character as the service describes it. A level of 0 means nobody has played it yet.</summary>
    [Serializable]
    public class CharacterInfo
    {
        public string id;
        public string name;
        public string modelName;
        public string[] dyeSources;
        public string[] dyeTargets;
        public string hairStyle;
        public string hairColour;
        public int level;
    }

    [Serializable] class CharacterList { public CharacterInfo[] characters; }
    [Serializable] class ModelList { public string[] models; }
    [Serializable]
    class NewCharacter
    {
        public string name;
        public string modelName;
        public string[] dyeSources;
        public string[] dyeTargets;
        public string hairStyle;
        public string hairColour;
        public int[] primary;
    }

    public static void ListCharacters(Action<CharacterInfo[], string> onDone)
    {
        Send("GET", "/characters", null, true, (ok, body, error) =>
        {
            if (!ok) { onDone?.Invoke(null, error); return; }
            var list = JsonUtility.FromJson<CharacterList>(body);
            onDone?.Invoke(list?.characters ?? Array.Empty<CharacterInfo>(), null);
        });
    }

    /// <summary>The bodies a character may be created with — asked of the SERVER rather than read from the
    /// game's own roster, because the server is what will refuse an appearance it doesn't recognise.</summary>
    public static void ListModels(Action<string[], string> onDone)
    {
        Send("GET", "/character-models", null, true, (ok, body, error) =>
        {
            if (!ok) { onDone?.Invoke(null, error); return; }
            var list = JsonUtility.FromJson<ModelList>(body);
            onDone?.Invoke(list?.models ?? Array.Empty<string>(), null);
        });
    }

    /// <summary>Create a character. The recolour and the six stats from the creation hexagon (by
    /// <see cref="PrimaryStat"/> index) travel WITH the creation, because they are chosen at the same moment as the
    /// name and belong to the character record. They are the player's CHOICE, not a result: the map server checks
    /// the spread against the one rule before using any of it.</summary>
    public static void CreateCharacter(string name, string modelName, string[] dyeSources, string[] dyeTargets,
                                       string hairStyle, string hairColour, int[] primary,
                                       Action<CharacterInfo, string> onDone)
    {
        var body0 = new NewCharacter
        {
            name = name,
            modelName = modelName,
            dyeSources = dyeSources ?? Array.Empty<string>(),
            dyeTargets = dyeTargets ?? Array.Empty<string>(),
            hairStyle = hairStyle ?? "",
            hairColour = hairColour ?? "",
            primary = primary,
        };
        Send("POST", "/characters", JsonUtility.ToJson(body0),
             true, (ok, body, error) =>
        {
            if (!ok) { onDone?.Invoke(null, error); return; }
            onDone?.Invoke(JsonUtility.FromJson<CharacterInfo>(body), null);
        });
    }

    public static void DeleteCharacter(string characterId, Action<bool, string> onDone)
    {
        Send("DELETE", "/characters/" + characterId, null, true, (ok, _, error) => onDone?.Invoke(ok, error));
    }
}
