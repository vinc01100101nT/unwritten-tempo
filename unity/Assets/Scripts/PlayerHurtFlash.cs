using System.Collections;
using UnityEngine;

/// <summary>
/// Flashes the player billboard red for a beat whenever the <see cref="Character"/> takes damage —
/// the player-side counterpart to <see cref="Health"/>'s monster hit-flash. A red tint works under
/// the alpha-clipped <c>BillboardLit</c> shader (which multiplies the sprite by the renderer color),
/// where a white flash wouldn't. Listens to <see cref="Character.OnDamaged"/>.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerHurtFlash : MonoBehaviour
{
    public Color flashColor = new Color(1f, 0.35f, 0.35f);
    public float flashTime = 0.12f;

    SpriteRenderer sr;
    Character ch;
    Coroutine co;
    // cached: Flash() runs per hit taken, and flashTime is fixed (same pattern as Health.flashWait)
    WaitForSeconds flashWait;

    void Awake()
    {
        if (GameServer.RemovesPresentation(this)) return;
        sr = GetComponent<SpriteRenderer>();
        flashWait = new WaitForSeconds(flashTime);
    }

    void Start()
    {
        ch = Character.Instance;
        if (ch != null) ch.OnDamaged += OnDamaged;
    }

    void OnDestroy() { if (ch != null) ch.OnDamaged -= OnDamaged; }

    void OnDamaged(int dmg)
    {
        if (!isActiveAndEnabled) return;
        if (co != null) StopCoroutine(co);
        co = StartCoroutine(Flash());
    }

    IEnumerator Flash()
    {
        sr.color = flashColor;
        yield return flashWait;
        sr.color = Color.white;
        co = null;
    }
}
