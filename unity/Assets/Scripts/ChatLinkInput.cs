using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// ITEM LINKS IN THE CHAT FIELD, each ONE WHOLE THING. A shift-clicked item lands as its "[name]" in its rarity's colour,
/// and from then on it behaves like a single character:
/// <list type="bullet">
/// <item>the caret never stands inside it — an arrow key steps over the whole of it, and a click on it puts the caret at
/// its end;</item>
/// <item>any edit that touches it — a backspace at its end, a delete at its start, a selection that reaches into it —
/// takes the whole of it away;</item>
/// <item>text typed before or after it is ordinary text.</item>
/// </list>
/// The field shows plain words (it never reads markup, so nothing typed can be read as markup); the colours are laid on
/// the drawn letters. On send, <see cref="Encode"/> swaps each link for the reference the server turns into a real link.
/// </summary>
public class ChatLinkInput : MonoBehaviour
{
    struct Span
    {
        public int start, length;
        public string reference;
        public Color colour;
        public int End => start + length;
    }

    readonly List<Span> spans = new List<Span>(ChatLinks.MostLinksInALine);
    TMP_InputField field;
    string lastText = "";
    int lastCaret;
    bool fixing;

    // Which link each letter belongs to, before and after an edit (-1 = ordinary text). Reused, never rebuilt per edit.
    readonly List<int> ownerWas = new List<int>(128);
    readonly List<int> ownerNow = new List<int>(128);
    readonly List<int> lettersOf = new List<int>(ChatLinks.MostLinksInALine);

    public void Bind(TMP_InputField input)
    {
        field = input;
        field.onValueChanged.AddListener(OnChanged);
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextDrawn);
    }

    void OnDestroy() => TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextDrawn);

    /// <summary>Put <paramref name="thing"/> in as a link where the caret is (at the end when the field is not being
    /// typed in) — never inside another link.</summary>
    public void Insert(BagEntry thing)
    {
        if (field == null || thing == null || thing.def == null) return;
        string word = "[" + (thing.instance != null ? ItemTooltip.CopyTitle(thing.instance) : thing.def.displayName) + "]";
        string text = field.text ?? "";
        if (field.characterLimit > 0 && text.Length + word.Length > field.characterLimit) return;

        int at = field.isFocused ? Mathf.Clamp(field.stringPosition, 0, text.Length) : text.Length;
        for (int i = 0; i < spans.Count; i++)
            if (at > spans[i].start && at < spans[i].End) at = spans[i].End;

        for (int i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            if (s.start >= at) { s.start += word.Length; spans[i] = s; }
        }
        spans.Add(new Span { start = at, length = word.Length, reference = ChatLinks.Ref(thing),
                             colour = Rarities.Color(Rarities.Of(thing.def, thing.instance)) });

        fixing = true;
        field.text = text.Insert(at, word);
        fixing = false;
        lastText = field.text;
        PutCaret(at + word.Length);
    }

    /// <summary>The line to send: <paramref name="shown"/> — the field's words — with each link swapped for the
    /// reference the server resolves (<see cref="ChatLinks"/>).</summary>
    public string Encode(string shown)
    {
        if (string.IsNullOrEmpty(shown) || spans.Count == 0) return shown;
        var ordered = new List<Span>(spans);   // perf-ok: once per line sent
        ordered.Sort((a, b) => a.start.CompareTo(b.start));
        var sb = new System.Text.StringBuilder(shown.Length + 64);
        int from = 0;
        foreach (var s in ordered)
        {
            if (s.start < from || s.End > shown.Length) continue;
            sb.Append(shown, from, s.start - from);
            sb.Append(s.reference);
            from = s.End;
        }
        sb.Append(shown, from, shown.Length - from);
        return sb.ToString();
    }

    // AN EDIT HAPPENED. Every link it touched in any way goes whole; the rest move with the text around them.
    void OnChanged(string now)
    {
        if (fixing) return;
        now = now ?? "";
        string was = lastText;
        lastText = now;
        if (spans.Count == 0) return;

        // The edit: letters [p, oldEnd) of the old text became letters [p, newEnd) of the new.
        int p = 0, most = Mathf.Min(was.Length, now.Length);
        while (p < most && was[p] == now[p]) p++;
        int tail = 0;
        while (tail < was.Length - p && tail < now.Length - p && was[was.Length - 1 - tail] == now[now.Length - 1 - tail]) tail++;
        int oldEnd = was.Length - tail, newEnd = now.Length - tail;
        int shift = newEnd - oldEnd;

        ownerWas.Clear();
        for (int i = 0; i < was.Length; i++) ownerWas.Add(-1);
        for (int k = 0; k < spans.Count; k++)
            for (int i = spans[k].start; i < spans[k].End && i < was.Length; i++) ownerWas[i] = k;
        ownerNow.Clear();
        for (int j = 0; j < now.Length; j++)
            ownerNow.Add(j < p ? ownerWas[j] : j < newEnd ? -1 : ownerWas[j - shift]);

        // A link survives only with every one of its letters, side by side.
        lettersOf.Clear();
        for (int k = 0; k < spans.Count; k++) lettersOf.Add(0);
        for (int j = 0; j < now.Length; j++) if (ownerNow[j] >= 0) lettersOf[ownerNow[j]]++;
        bool anyBroken = false;
        for (int k = 0; k < spans.Count; k++)
        {
            bool whole = lettersOf[k] == spans[k].length && !(newEnd > p && p > spans[k].start && p < spans[k].End);
            if (!whole) { lettersOf[k] = -1; anyBroken = true; }
        }

        // The text kept, and where each surviving link now starts in it.
        var kept = new System.Text.StringBuilder(now.Length);   // perf-ok: once per edit of the chat field
        int caretAfter = newEnd, caretKept = 0;
        var starts = new int[spans.Count];
        for (int k = 0; k < starts.Length; k++) starts[k] = -1;
        for (int j = 0; j < now.Length; j++)
        {
            int k = ownerNow[j];
            if (j == caretAfter) caretKept = kept.Length;
            if (k >= 0 && lettersOf[k] < 0) continue;
            if (k >= 0 && starts[k] < 0) starts[k] = kept.Length;
            kept.Append(now[j]);
        }
        if (caretAfter >= now.Length) caretKept = kept.Length;

        for (int k = spans.Count - 1; k >= 0; k--)
        {
            if (lettersOf[k] < 0) { spans.RemoveAt(k); continue; }
            var s = spans[k];
            s.start = starts[k];
            spans[k] = s;
        }

        if (!anyBroken) return;
        fixing = true;
        field.text = kept.ToString();
        fixing = false;
        lastText = field.text;
        PutCaret(caretKept);
    }

    // THE CARET NEVER STANDS INSIDE A LINK: stepped left, it goes to the link's start; any other way in — a click, a step
    // right — to its end.
    void LateUpdate()
    {
        if (field == null || !field.isFocused || spans.Count == 0) { if (field != null) lastCaret = field.stringPosition; return; }
        int caret = field.stringPosition;
        bool goingLeft = Input.GetKey(KeyCode.LeftArrow) || caret < lastCaret && !Input.GetMouseButton(0);
        for (int i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            if (caret > s.start && caret < s.End) { caret = goingLeft ? s.start : s.End; break; }
        }
        if (caret != field.stringPosition && !HasSelection()) PutCaret(caret);
        lastCaret = caret;
    }

    bool HasSelection() => field.selectionStringAnchorPosition != field.selectionStringFocusPosition;

    void PutCaret(int at)
    {
        at = Mathf.Clamp(at, 0, (field.text ?? "").Length);
        field.stringPosition = at;
        field.selectionStringAnchorPosition = at;
        field.selectionStringFocusPosition = at;
        lastCaret = at;
    }

    // THE COLOURS, laid on the drawn letters each time the field's text is drawn again.
    void OnTextDrawn(Object drawn)
    {
        if (field == null || spans.Count == 0 || drawn != field.textComponent) return;
        var t = field.textComponent;
        var info = t.textInfo;
        if (info == null) return;
        bool any = false;
        for (int c = 0; c < info.characterCount; c++)
        {
            var ch = info.characterInfo[c];
            if (!ch.isVisible) continue;
            int at = ch.index;
            for (int i = 0; i < spans.Count; i++)
            {
                if (at < spans[i].start || at >= spans[i].End) continue;
                var colours = info.meshInfo[ch.materialReferenceIndex].colors32;
                int v = ch.vertexIndex;
                Color32 tint = spans[i].colour;
                tint.a = colours[v].a;
                colours[v] = colours[v + 1] = colours[v + 2] = colours[v + 3] = tint;
                any = true;
                break;
            }
        }
        if (any) t.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }
}
