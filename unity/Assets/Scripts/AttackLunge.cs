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

    Vector3 buildScale;   // fallback capture, used only when this mob has no BillboardAnimator3D to own the rest scale
    BillboardAnimator3D anim;
    Coroutine co;

    /// <summary>True while a chomp is animating the scale — idle breathing yields to it.</summary>
    public bool Punching => co != null;

    void Awake()
    {
        anim = GetComponent<BillboardAnimator3D>();
        buildScale = transform.localScale;
    }

    // The rest scale the chomp squashes around — READ from the sprite animator, which owns this mob's live body scale,
    // so the chomp and the idle breathe can't disagree about what "rest" means. The local capture is only a fallback
    // for a mob that has no sprite animator at all.
    Vector3 BaseScale => anim != null ? anim.BaseScale : buildScale * UnitScale.ForMob;

    public void Punch()
    {
        if (co != null) StopCoroutine(co);
        co = StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        for (float t = 0f; t < punchTime; t += Time.deltaTime)
        {
            float e = Mathf.Sin(t / punchTime * Mathf.PI);   // 0 → 1 → 0 over the punch
            Vector3 b = BaseScale;
            transform.localScale = new Vector3(
                b.x * (1f + stretch * e),
                b.y * (1f - stretch * e),
                b.z);
            yield return null;
        }
        transform.localScale = BaseScale;
        co = null;
    }
}
