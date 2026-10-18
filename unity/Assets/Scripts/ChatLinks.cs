using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// ITEMS NAMED IN CHAT — every kind of chat, now and later: a line said aloud, a private message, and any channel added
/// after them. A link travels INSIDE the line's own text, so anything that carries a line's text carries its links and
/// nothing about a channel has to know they exist.
///
/// <para><b>Two shapes, and only the server makes the second.</b> A typed line names a thing the sender holds by a
/// REFERENCE — <c>␁g:copyId␂</c> for a piece of gear, <c>␁i:itemId␂</c> for a stack. The server that first receives the
/// line looks each reference up in the SENDER'S OWN bag and worn gear (<see cref="Resolve"/>) and replaces it with the
/// finished LINK: <c>␁title␟colour␟itemId␟tooltip␂</c> — the name, its rarity's colour, which item it is (so a card can
/// show its picture) and the very tooltip the sender's bag shows for it, all worked out there. A reference to anything they do not hold is dropped. Every other control
/// character in a typed line becomes a space, so a player can never type a link of their own; a server passing a line on
/// (<see cref="CleanRelayed"/>) keeps finished links and cleans everything else the same way.</para>
///
/// <para><b>A player's machine only draws.</b> <see cref="ToRichText"/> turns a line into the chat row's text — each link
/// its name in its colour — and hands back the tooltips to show on hover; <see cref="Plain"/> is the line with each link
/// as its bare name, for a speech bubble or a log file.</para>
/// </summary>
public static class ChatLinks
{
    public const char Open = '\u0001', Sep = '\u001F', Close = '\u0002';
    public const int MostLinksInALine = 3;

    // ---- the player's machine: the reference a typed line carries -----------------------------------------------

    /// <summary>The reference to one of this player's own things, as a typed line carries it to the server.</summary>
    public static string Ref(BagEntry e)
    {
        if (e == null || e.def == null) return "";
        var copy = e.instance != null ? e.instance.copyId : null;
        return string.Concat(Open.ToString(), string.IsNullOrEmpty(copy) ? "i:" + e.def.id : "g:" + copy, Close.ToString());
    }

    // ---- the server: references become links -----------------------------------------------------------------

    /// <summary>SERVER (or a game with no world): a typed line made fit to pass on. Every reference to a thing
    /// <paramref name="sender"/> holds becomes its finished link (at most <see cref="MostLinksInALine"/>); every other
    /// control character becomes a space; and the line is cut at <paramref name="mostShown"/> characters AS SHOWN, a
    /// link counting as its "[name]" — never through the middle of one.</summary>
    public static string Resolve(string raw, Character sender, int mostShown)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        int shown = 0, links = 0;
        for (int i = 0; i < raw.Length && shown < mostShown;)
        {
            char ch = raw[i];
            if (ch == Open)
            {
                int end = raw.IndexOf(Close, i + 1);
                if (end < 0) { i++; continue; }
                string reference = raw.Substring(i + 1, end - i - 1);
                i = end + 1;
                if (links >= MostLinksInALine || sender == null || !Find(sender, reference, out var def, out var piece)) continue;
                string title = piece != null ? ItemTooltip.CopyTitle(piece) : def.displayName;
                int width = title.Length + 2;
                if (shown + width > mostShown) break;
                AppendLink(sb, title, UiKit.LightHtml(Rarities.Color(Rarities.Of(def))), def.id,
                           ItemTooltip.BodyFor(def, piece));
                shown += width;
                links++;
                continue;
            }
            sb.Append(char.IsControl(ch) ? ' ' : ch);
            shown++;
            i++;
        }
        return sb.ToString().Trim();
    }

    /// <summary>SERVER: a line another server already made fit, passed on again (a private message arriving from the
    /// character server). Its finished links stay; anything else that is not plain text is cleaned exactly as
    /// <see cref="Resolve"/> cleans it, and it is cut the same way.</summary>
    public static string CleanRelayed(string text, int mostShown)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        int shown = 0, links = 0;
        for (int i = 0; i < text.Length && shown < mostShown;)
        {
            char ch = text[i];
            if (ch == Open)
            {
                int end = text.IndexOf(Close, i + 1);
                if (end < 0) { i++; continue; }
                string inner = text.Substring(i + 1, end - i - 1);
                i = end + 1;
                if (links >= MostLinksInALine || !SplitLink(inner, out var title, out var hex, out var id, out var body)) continue;
                if (shown + title.Length + 2 > mostShown) break;
                AppendLink(sb, title, hex, id, body);
                shown += title.Length + 2;
                links++;
                continue;
            }
            sb.Append(char.IsControl(ch) ? ' ' : ch);
            shown++;
            i++;
        }
        return sb.ToString().Trim();
    }

    static void AppendLink(StringBuilder sb, string title, string hex, string itemId, string body)
        => sb.Append(Open).Append(Strip(title)).Append(Sep).Append(Strip(hex)).Append(Sep).Append(Strip(itemId))
             .Append(Sep).Append(Strip(body)).Append(Close);

    // Nothing inside a link may be read as the link's own edges.
    static string Strip(string s)
        => string.IsNullOrEmpty(s) ? "" : s.Replace(Open, ' ').Replace(Close, ' ').Replace(Sep, ' ');

    // A finished link's four parts; false for anything that is not one (wrong count of parts, a colour that is not one).
    static bool SplitLink(string inner, out string title, out string hex, out string itemId, out string body)
    {
        title = hex = itemId = body = "";
        int a = inner.IndexOf(Sep);
        int b = a >= 0 ? inner.IndexOf(Sep, a + 1) : -1;
        int c = b >= 0 ? inner.IndexOf(Sep, b + 1) : -1;
        if (a <= 0 || c < 0) return false;
        title = inner.Substring(0, a);
        hex = inner.Substring(a + 1, b - a - 1);
        itemId = inner.Substring(b + 1, c - b - 1);
        body = inner.Substring(c + 1);
        if (hex.Length != 6) return false;
        for (int k = 0; k < hex.Length; k++)
            if (!System.Uri.IsHexDigit(hex[k])) return false;
        return true;
    }

    // The thing a reference names, among what the sender holds: in the bag, or worn.
    static bool Find(Character who, string reference, out Item def, out EquipmentInstance piece)
    {
        def = null;
        piece = null;
        if (reference.Length < 3 || reference[1] != ':') return false;
        string id = reference.Substring(2);
        bool gear = reference[0] == 'g';
        if (!gear && reference[0] != 'i') return false;

        var bag = who.Bag;
        for (int i = 0; i < bag.Count; i++)
        {
            var e = bag[i];
            if (e == null || e.def == null) continue;
            if (gear ? e.instance != null && e.instance.copyId == id : e.instance == null && e.def.id == id)
            {
                def = e.def;
                piece = e.instance;
                return true;
            }
        }
        foreach (EquipSlotType slot in System.Enum.GetValues(typeof(EquipSlotType)))   // perf-ok: once per link in a line said
        {
            var worn = slot != EquipSlotType.None ? who.GetEquippedInstance(slot) : null;
            if (worn == null || worn.def == null) continue;
            if (gear ? worn.copyId == id : worn.def.id == id)
            {
                def = worn.def;
                piece = worn;
                return true;
            }
        }
        return false;
    }

    // ---- the player's machine: drawing a line ----------------------------------------------------------------

    /// <summary>The line as a chat row's rich text: plain parts made safe by <paramref name="sanitize"/>, each link its
    /// "[name]" in its colour, tagged so the row can tell which one the pointer is over. The tooltips go into
    /// <paramref name="titles"/>, <paramref name="colours"/> and <paramref name="bodies"/>, one per link, in order.</summary>
    public static string ToRichText(string text, System.Func<string, string> sanitize,
                                    List<string> titles, List<Color> colours, List<string> bodies, List<string> itemIds)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(Open) < 0) return sanitize(text);
        var sb = new StringBuilder(text.Length + 32);
        int from = 0;
        while (from < text.Length)
        {
            int open = text.IndexOf(Open, from);
            if (open < 0) { sb.Append(sanitize(text.Substring(from))); break; }
            if (open > from) sb.Append(sanitize(text.Substring(from, open - from)));
            int end = text.IndexOf(Close, open + 1);
            if (end < 0) break;
            from = end + 1;
            if (!SplitLink(text.Substring(open + 1, end - open - 1), out var title, out var hex, out var id, out var body)) continue;
            hex = UiKit.InActiveTheme(hex);
            ColorUtility.TryParseHtmlString("#" + hex, out var colour);
            sb.Append("<link=\"").Append(titles.Count).Append("\"><color=#").Append(hex).Append('>')
              .Append('[').Append(sanitize(title)).Append(']').Append("</color></link>");
            titles.Add(title);
            colours.Add(colour);
            bodies.Add(body);
            itemIds.Add(id);
        }
        return sb.ToString();
    }

    /// <summary>The line with each link as its bare "[name]" — for a speech bubble, which draws no colours, and for a
    /// log written to a file.</summary>
    public static string Plain(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(Open) < 0) return text;
        var sb = new StringBuilder(text.Length);
        int from = 0;
        while (from < text.Length)
        {
            int open = text.IndexOf(Open, from);
            if (open < 0) { sb.Append(text, from, text.Length - from); break; }
            sb.Append(text, from, open - from);
            int end = text.IndexOf(Close, open + 1);
            if (end < 0) break;
            from = end + 1;
            if (SplitLink(text.Substring(open + 1, end - open - 1), out var title, out _, out _, out _))
                sb.Append('[').Append(title).Append(']');
        }
        return sb.ToString();
    }
}
