using System.Collections;
using UnityEngine;

/// <summary>
/// A quick squash-and-stretch "chomp" played when a billboard mob lands a touch attack. The Ninja
/// Adventure monster sheets have no attack frame, so the bite is sold with a scale punch instead of a
/// pose: the sprite stretches wide + flat for a beat, then eases back. Animates <c>localScale</c>
/// only (never position), so it never fights the CharacterController / pathing. Driven by
/// <see cref="MonsterAI3D"/> calling <see cref="Punch"/>.
/// </summary>
public class AttackLunge : MonoBehaviour
{
    [Tooltip("How long the chomp takes, in seconds.")]
    public float punchTime = 0.18f;
    [Tooltip("Peak stretch amount (0.25 = ±25% wider/flatter at the peak).")]
    public float stretch = 0.25f;

    Vector3 baseScale;
    bool captured;
    Coroutine co;

    /// <summary>True while a chomp is animating the scale — idle breathing yields to it.</summary>
    public bool Punching => co != null;

    void Awake() { baseScale = transform.localScale; captured = true; }

    public void Punch()
    {
        if (!captured) { baseScale = transform.localScale; captured = true; }
        if (co != null) StopCoroutine(co);
        co = StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        for (float t = 0f; t < punchTime; t += Time.deltaTime)
        {
            float e = Mathf.Sin(t / punchTime * Mathf.PI);   // 0 → 1 → 0 over the punch
            transform.localScale = new Vector3(
                baseScale.x * (1f + stretch * e),
                baseScale.y * (1f - stretch * e),
                baseScale.z);
            yield return null;
        }
        transform.localScale = baseScale;
        co = null;
    }
}
