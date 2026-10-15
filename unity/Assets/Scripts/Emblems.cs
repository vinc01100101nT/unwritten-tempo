using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SERVER: the picture behind every emblem id worn by somebody on this map server, kept once per id however many wear
/// it, so a watcher asking for one is answered from memory. An id is dropped when the last person holding it leaves or
/// changes emblem.
/// </summary>
public static class EmblemStore
{
    sealed class Held { public byte[] png; public int holders; }
    static readonly Dictionary<string, Held> byId = new Dictionary<string, Held>();

    /// <summary>One more person holds <paramref name="id"/>, whose stored picture is <paramref name="png"/>.</summary>
    public static void Hold(string id, byte[] png)
    {
        if (string.IsNullOrEmpty(id) || png == null) return;
        if (!byId.TryGetValue(id, out var h)) { h = new Held { png = png }; byId[id] = h; }
        h.holders++;
    }

    /// <summary>One fewer person holds <paramref name="id"/>; the picture goes when nobody does.</summary>
    public static void Release(string id)
    {
        if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var h)) return;
        if (--h.holders <= 0) byId.Remove(id);
    }

    /// <summary>The stored picture of <paramref name="id"/>, or null when nobody here holds it.</summary>
    public static byte[] Picture(string id) => !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var h) ? h.png : null;

    public static int Count => byId.Count;
}

/// <summary>
/// ON A SCREEN: one texture per emblem id, built the first time somebody in view wears it and shared by everyone who
/// does — never one per person. A picture this machine has not seen is asked of the server once (merged when several
/// bodies want it at the same moment); the texture is destroyed when the last body wanting it lets go, and the
/// picture's bytes are kept a while longer in case it comes back into view.
/// </summary>
public static class EmblemCache
{
    /// <summary>How many emblem pictures' bytes are kept after nobody on screen wears them (least recently used goes first).</summary>
    const int KeptPictures = 4096;

    sealed class Shown { public Texture2D texture; public int users; }
    static readonly Dictionary<string, Shown> shown = new Dictionary<string, Shown>();
    static readonly Dictionary<string, Action<Texture2D>> waiting = new Dictionary<string, Action<Texture2D>>();
    static readonly Dictionary<string, LinkedListNode<(string id, byte[] png)>> kept = new Dictionary<string, LinkedListNode<(string, byte[])>>();
    static readonly LinkedList<(string id, byte[] png)> keptOrder = new LinkedList<(string, byte[])>();

    /// <summary>Hand <paramref name="whenReady"/> the texture for <paramref name="id"/> — at once when it is already
    /// built, otherwise once its picture arrives. Every call must be matched by one <see cref="Release"/>.</summary>
    public static void Want(string id, Action<Texture2D> whenReady)
    {
        if (string.IsNullOrEmpty(id)) { whenReady?.Invoke(null); return; }
        if (shown.TryGetValue(id, out var s))
        {
            s.users++;
            if (s.texture != null) whenReady?.Invoke(s.texture);
            else waiting[id] = (waiting.TryGetValue(id, out var w) ? w : null) + whenReady;
            return;
        }
        shown[id] = new Shown { users = 1 };
        if (kept.TryGetValue(id, out var node)) { Build(id, node.Value.png); whenReady?.Invoke(shown[id].texture); return; }
        bool askedAlready = waiting.ContainsKey(id);
        waiting[id] = (askedAlready ? waiting[id] : null) + whenReady;
        if (!askedAlready && NetPlayer.Mine != null) NetPlayer.Mine.AskEmblem(id);
    }

    /// <summary>One body stopped wanting <paramref name="id"/>.</summary>
    public static void Release(string id)
    {
        if (string.IsNullOrEmpty(id) || !shown.TryGetValue(id, out var s)) return;
        if (--s.users > 0) return;
        shown.Remove(id);
        waiting.Remove(id);
        if (s.texture != null) UnityEngine.Object.Destroy(s.texture);
    }

    /// <summary>The server sent the picture of <paramref name="id"/> (empty when it no longer holds it).</summary>
    public static void Arrived(string id, byte[] png)
    {
        if (string.IsNullOrEmpty(id) || png == null || png.Length == 0) return;
        Keep(id, png);
        if (!shown.TryGetValue(id, out var s) || s.texture != null) return;
        Build(id, png);
        if (waiting.TryGetValue(id, out var w)) { waiting.Remove(id); w?.Invoke(s.texture); }
    }

    /// <summary>The texture <paramref name="rgba"/> (as <see cref="EmblemImage.Decode"/> returns it) is drawn with: one
    /// pixel of see-through border all round, so the edge of the patch never smears the picture's edge across the
    /// cloth; point-filtered, so a 24-pixel picture stays crisp. Shared by the upload window's preview.</summary>
    public static Texture2D TextureOf(byte[] rgba)
    {
        int side = EmblemImage.Side, size = side + 2;
        var pixels = new Color32[size * size];
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int i = ((side - 1 - y) * side + x) * 4;
                pixels[(y + 1) * size + x + 1] = new Color32(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
            }
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "emblem",
        };
        t.SetPixels32(pixels);
        t.Apply(false, true);
        return t;
    }

    static void Build(string id, byte[] png)
    {
        var rgba = EmblemImage.Decode(png, out _);
        if (rgba != null && shown.TryGetValue(id, out var s)) s.texture = TextureOf(rgba);
    }

    static void Keep(string id, byte[] png)
    {
        if (kept.TryGetValue(id, out var node)) { keptOrder.Remove(node); keptOrder.AddFirst(node); return; }
        kept[id] = keptOrder.AddFirst((id, png));
        if (keptOrder.Count > KeptPictures)
        {
            var oldest = keptOrder.Last;
            keptOrder.RemoveLast();
            kept.Remove(oldest.Value.id);
        }
    }
}
