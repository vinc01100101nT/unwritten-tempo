using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// A flat creature's death: the remains lie on the ground for <see cref="remainSeconds"/> before fading out over
/// <see cref="fadeSeconds"/>. ONE owner for those two numbers, read by both halves:
///
/// <para><b>The server</b> (<see cref="Expire"/>, from <see cref="Health.Die"/>) stops the body acting, moving and
/// colliding, waits out the remains and the fade, and then hands the creature back for reuse — that wait is what
/// times the respawn. It draws nothing.</para>
///
/// <para><b>Every screen watching</b> (<see cref="Play"/>, from the health the server sends) stops its copy being a
/// body in the same way, then shows the death: the dead picture, the cry, the remains lying, the fade.</para>
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
    bool faded;               // the fade has touched the sprite, so a reuse has something to put back

    SpriteRenderer sr;
    Material originalMat;     // restored when a MobRespawn recycles the mob
    Color originalColor;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        originalMat = sr.sharedMaterial;
        originalColor = sr.color;
    }

    /// <summary>Undo the death so <see cref="MobRespawn"/> can reuse this object: stop the wait or the fade, put the
    /// sprite back if a fade touched it, and allow it to die again.</summary>
    public void ResetAfterRespawn()
    {
        StopAllCoroutines();
        playing = false;
        if (faded && sr != null) { sr.sharedMaterial = originalMat; sr.color = originalColor; }
        faded = false;
    }

    /// <summary>SERVER: stop being a body, lie for the remains and the fade, then <paramref name="onGone"/> — which hands
    /// the creature back for reuse. Nothing is drawn.</summary>
    public void Expire(Action onGone)
    {
        if (playing) { onGone?.Invoke(); return; }
        playing = true;
        StopBeingABody();
        StartCoroutine(WaitThen(remainSeconds + fadeSeconds, onGone));
    }

    /// <summary>A WATCHING SCREEN: stop this copy being a body, then show the death — the dead picture, the cry, the
    /// remains lying, the fade. The copy itself is taken away by the shell that speaks for it, when the server says.</summary>
    public void Play()
    {
        if (playing) return;
        playing = true;
        StopBeingABody();

        // The dead picture first, for a body whose sheet has one — the animator stops right after and holds it.
        var anim = GetComponent<BillboardAnimator3D>(); if (anim != null) anim.ShowDeathPose();
        Disable<BillboardAnimator3D>();

        // Sound exactly on death.
        Sfx.PlayAt(deathSfx, transform.position);   // 3D: hear WHERE the mob died

        StartCoroutine(Fade());
    }

    // Stop it acting, moving, colliding or being targeted — it's now just remains. The SAME on every machine: the
    // server's copy must stop fighting, and a watching copy must stop catching the clicks and rays meant for whatever
    // stands behind it.
    // Tell the freeze switch to keep its hands off first: it owns the mover + animator too, so a player who walks out of
    // range and back mid-fade would otherwise "thaw" the corpse and switch them straight back on.
    void StopBeingABody()
    {
        var lod = GetComponent<SimulationLod>(); if (lod != null) lod.Suspend();
        Disable<MonsterAI3D>();
        Disable<PathAgent3D>();
        var cc = GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
        foreach (var col in GetComponentsInChildren<Collider>())   col.enabled = false;
        foreach (var col in GetComponentsInChildren<Collider2D>()) col.enabled = false;
    }

    // Built once per wait length rather than once per death — see MobRespawn.
    WaitForSeconds wait;
    float waitFor = -1f;

    WaitForSeconds Wait(float seconds)
    {
        if (wait == null || waitFor != seconds) { wait = new WaitForSeconds(seconds); waitFor = seconds; }
        return wait;
    }

    IEnumerator WaitThen(float seconds, Action then)
    {
        if (seconds > 0f) yield return Wait(seconds);
        then?.Invoke();
    }

    IEnumerator Fade()
    {
        // Remains lie on the ground.
        if (remainSeconds > 0f) yield return Wait(remainSeconds);

        // Fade out — swap to an alpha-blended material so the alpha ramp actually shows.
        faded = true;
        var mat = FadeMaterial();
        if (mat != null) sr.sharedMaterial = mat;       // shared is fine: color is a per-renderer property
        Color c = sr.color;
        for (float t = 0f; t < fadeSeconds; t += Time.deltaTime)
        {
            c.a = Mathf.Lerp(1f, 0f, t / fadeSeconds);
            sr.color = c;
            yield return null;
        }
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
