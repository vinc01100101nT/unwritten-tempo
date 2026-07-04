using UnityEngine;

/// <summary>
/// SPIKE (experiment/3d-characters): shows the Mixamo CASTER clip set retargeted onto the Quaternius body
/// via a runtime Humanoid avatar. Plays the Mixamo idle and cross-fades to each spell/attack/death clip on a
/// number key, then eases back to idle — the minimal proof that foreign (Mixamo) Humanoid animation drives our
/// Quaternius character. NOT the real player; movement/combat aren't wired here (that's the next step, once a
/// locomotion clip exists). Built by <c>Tools ▸ unwritten ▸ Spike ▸ Build Mixamo Caster Spike</c>.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CasterSpikeDriver : MonoBehaviour
{
    [Tooltip("State names in the caster controller, triggered by number keys 1..N (Idle is the default state).")]
    public string[] actionStates = { "Cast", "Attack", "AreaAttack", "LookAround", "Death" };
    [Tooltip("CrossFade time into an action and back to Idle.")]
    public float crossFade = 0.15f;

    Animator anim;
    static readonly int Idle = Animator.StringToHash("Idle");
    float returnToIdleAt;
    Vector3 plantedPos;

    void Awake() { anim = GetComponent<Animator>(); plantedPos = transform.localPosition; }

    // The Mixamo clips carry baked root motion; pin the root so the caster performs in place (no drift out of frame).
    void LateUpdate() { transform.localPosition = plantedPos; }

    void Update()
    {
        for (int i = 0; i < actionStates.Length && i < 9; i++)
            if (Hotkeys.DigitDown(i))
            {
                int h = Animator.StringToHash(actionStates[i]);
                if (anim.HasState(0, h))
                {
                    anim.CrossFadeInFixedTime(h, crossFade, 0);
                    // ease back to Idle after the clip (Death holds — no auto-return).
                    returnToIdleAt = actionStates[i] == "Death" ? 0f : Time.time + StateLength(h) + 0.05f;
                }
            }

        if (returnToIdleAt > 0f && Time.time >= returnToIdleAt)
        {
            anim.CrossFadeInFixedTime(Idle, crossFade, 0);
            returnToIdleAt = 0f;
        }
    }

    float StateLength(int stateHash)
    {
        var rac = anim.runtimeAnimatorController;
        if (rac == null) return 1f;
        // States are named after their source, but Mixamo clips are all "mixamo.com"; fall back to the current
        // state's clip length after the CrossFade settles. Use a sane default; the exact value isn't critical.
        var info = anim.GetCurrentAnimatorStateInfo(0);
        return info.length > 0.01f ? info.length : 2f;
    }
}
