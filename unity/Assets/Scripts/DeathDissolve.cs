using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Billboard-mob death sequence: the remains lie on the ground for <see cref="remainSeconds"/> before
/// fading out over <see cref="fadeSeconds"/>.
/// <see cref="Health.Die"/> hands off here when the component is present (otherwise it removes the
/// object instantly, the original Phase D behavior).
///
/// The fade swaps the renderer to an alpha-BLENDED sprite material first, because our
/// <c>BillboardLit</c> shader is alpha-CLIPPED — animating alpha on it just pops at the cutoff.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class DeathDissolve : MonoBehaviour
{
    [Tooltip("Played once on death (a poof / death cry).")]
    public SfxSet deathSfx;

    [Header("Remains")]
    [Tooltip("Seconds the corpse lies on the ground before it starts fading.")]
    public float remainSeconds = 3f;
    [Tooltip("Seconds the corpse takes to fade away.")]
    public float fadeSeconds = 1f;

    static Material _fadeMat;
    bool playing;

    SpriteRenderer sr;
    Material originalMat;     // restored when a MobRespawn recycles the mob
    Color originalColor;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        originalMat = sr.sharedMaterial;
        originalColor = sr.color;
    }

    /// <summary>Undo the death visuals so <see cref="MobRespawn"/> can reuse this object: stop the fade,
    /// restore the lit material + full-alpha color, and allow it to die again.</summary>
    public void ResetAfterRespawn()
    {
        StopAllCoroutines();
        playing = false;
        if (sr != null) { sr.sharedMaterial = originalMat; sr.color = originalColor; }
    }

    /// <summary>Freeze the unit, linger, then fade out; <paramref name="onComplete"/>
    /// fires when done (Health passes Destroy here).</summary>
    public void Play(Action onComplete)
    {
        if (playing) { onComplete?.Invoke(); return; }
        playing = true;

        // Stop it acting, moving, colliding or being targeted — it's now just remains.
        // Tell the freeze switch to keep its hands off first: it owns the mover + animator too, so a player who walks
        // out of range and back mid-fade would otherwise "thaw" the corpse and switch them straight back on.
        var lod = GetComponent<SimulationLod>(); if (lod != null) lod.Suspend();
        Disable<MonsterAI3D>();
        Disable<BillboardAnimator3D>();
        Disable<PathAgent3D>();
        var cc = GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
        foreach (var col in GetComponentsInChildren<Collider>())   col.enabled = false;
        foreach (var col in GetComponentsInChildren<Collider2D>()) col.enabled = false;

        // Sound exactly on death.
        Sfx.PlayAt(deathSfx, transform.position);   // 3D: hear WHERE the mob died

        StartCoroutine(Run(onComplete));
    }

    // Built once per corpse rather than once per death — see MobRespawn.
    WaitForSeconds remainWait;
    float remainWaitFor = -1f;

    IEnumerator Run(Action onComplete)
    {
        // Remains lie on the ground.
        if (remainSeconds > 0f)
        {
            if (remainWait == null || remainWaitFor != remainSeconds)
            {
                remainWait = new WaitForSeconds(remainSeconds);
                remainWaitFor = remainSeconds;
            }
            yield return remainWait;
        }

        // Fade out — swap to an alpha-blended material so the alpha ramp actually shows.
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
