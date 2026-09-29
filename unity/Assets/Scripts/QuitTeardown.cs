using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Destroys objects made during Play, properly, the moment the game quits.
///
/// <para>An object marked not-to-be-saved (<c>HideFlags.DontSaveInEditor</c>) is removed when Play ends WITHOUT its
/// components' OnDisable/OnDestroy running — measured 2026-09-29: the creation preview's own teardown never ran, so
/// its camera still held its picture when Unity released it ("Releasing render texture that is set as
/// Camera.targetTexture!"), and every Play-made body's Animator warned "PlayableGraph was not destroyed". Anything
/// registered here is destroyed on <see cref="Application.quitting"/> instead, while its teardown still runs.</para>
/// </summary>
public static class QuitTeardown
{
    static readonly List<GameObject> registered = new List<GameObject>();
    static bool hooked;

    public static void Register(GameObject go)
    {
        if (go == null) return;
        if (!hooked) { Application.quitting += DestroyAll; hooked = true; }
        // Drop entries already destroyed the ordinary way, so a long session of body swaps does not pile them up.
        for (int i = registered.Count - 1; i >= 0; i--) if (registered[i] == null) registered.RemoveAt(i);
        registered.Add(go);
    }

    static void DestroyAll()
    {
        foreach (var go in registered) if (go != null) Object.DestroyImmediate(go);
        registered.Clear();
    }
}
