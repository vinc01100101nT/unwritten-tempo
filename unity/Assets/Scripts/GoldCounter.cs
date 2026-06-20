using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD component that keeps a UI Text in sync with the player's Zeny balance.
/// Attach to the "HUD_Gold" Text object built by <see cref="UIShellBuilder"/>.
/// Subscribes to <see cref="Wallet.OnChanged"/> — zero per-frame overhead.
/// </summary>
[RequireComponent(typeof(Text))]
public class GoldCounter : MonoBehaviour
{
    Text label;

    void Awake()
    {
        label = GetComponent<Text>();
        Refresh(Wallet.Gold);
        Wallet.OnChanged += Refresh;
    }

    void OnDestroy() => Wallet.OnChanged -= Refresh;

    void Refresh(int gold) => label.text = gold.ToString("N0") + " z";
}
