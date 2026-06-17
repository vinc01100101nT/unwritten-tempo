using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Billboard-mob death sequence: a puff of smoke at the moment of death, then the remains lie on the
/// ground for <see cref="remainSeconds"/> before fading out over <see cref="fadeSeconds"/>.
/// <see cref="Health.Die"/> hands off here when the component is present (otherwise it removes the
/// object instantly, the original Phase D behavior).
///
/// The fade swaps the renderer to an alpha-BLENDED sprite material first, because our
/// <c>BillboardLit</c> shader is alpha-CLIPPED — animating alpha on it just pops at the cutoff.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class DeathDissolve : MonoBehaviour
{
    [Header("Smoke puff (NinjaAdventure FX/Smoke)")]
    public Texture2D smokeSheet;
    public int smokeFrameSize = 32;
    public int smokeFrames = 6;
    public float smokeFps = 16f;
    public float smokeScale = 1.2f;

    [Header("Remains")]
    [Tooltip("Seconds the corpse lies on the ground before it starts fading.")]
    public float remainSeconds = 3f;
    [Tooltip("Seconds the corpse takes to fade away.")]
    public float fadeSeconds = 1f;

    static Material _fadeMat;
    bool playing;

    /// <summary>Freeze the unit, puff smoke, linger, then fade out; <paramref name="onComplete"/>
    /// fires when done (Health passes Destroy here).</summary>
    public void Play(Action onComplete)
    {
        if (playing) { onComplete?.Invoke(); return; }
        playing = true;

        // Stop it acting, moving, colliding or being targeted — it's now just remains.
        Disable<MonsterAI3D>(); Disable<MonsterAI>();
        Disable<BillboardAnimator3D>();
        Disable<PathAgent3D>(); Disable<PathAgent>();
        var cc = GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
        foreach (var col in GetComponentsInChildren<Collider>())   col.enabled = false;
        foreach (var col in GetComponentsInChildren<Collider2D>()) col.enabled = false;

        // Puff of smoke exactly on death (reuses the generic one-shot sheet player).
        if (smokeSheet != null)
            SlashEffect.Spawn(smokeSheet, smokeFrameSize, smokeFrames, smokeFps, transform.position, smokeScale);

        StartCoroutine(Run(onComplete));
    }

    IEnumerator Run(Action onComplete)
    {
        // Remains lie on the ground.
        if (remainSeconds > 0f) yield return new WaitForSeconds(remainSeconds);

        // Fade out — swap to an alpha-blended material so the alpha ramp actually shows.
        var sr = GetComponent<SpriteRenderer>();
        var mat = FadeMaterial();
        if (mat != null) sr.sharedMaterial = mat;       // shared is fine: color is a per-renderer property
        Color c = sr.color;
        for (float t = 0f; t < fadeSeconds; t += Time.deltaTime)
        {
            c.a = Mathf.Lerp(1f, 0f, t / fadeSeconds);
            sr.color = c;
            yield return null;
        }
        onComplete?.Invoke();
    }

    static Material FadeMaterial()
    {
        if (_fadeMat == null)
        {
            var sh = Shader.Find("Sprites/Default");
            if (sh != null) _fadeMat = new Material(sh) { name = "BillboardFade" };
        }
        return _fadeMat;
    }

    void Disable<T>() where T : MonoBehaviour { var c = GetComponent<T>(); if (c != null) c.enabled = false; }
}
