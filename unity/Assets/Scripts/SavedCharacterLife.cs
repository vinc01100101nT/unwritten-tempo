using UnityEngine;

/// <summary>
/// SERVER: A SAVED CHARACTER'S TIME ON THE FIELD. Every one stays <see cref="SavedCharacterRules.StaySecondsForAll"/>;
/// then it stops, a never-fights character or an ally says goodbye and vanishes once the line has been read, and a
/// rebel still standing simply goes. A rebel beaten first says its last line as it falls.
/// </summary>
[RequireComponent(typeof(Health))]
public class SavedCharacterLife : MonoBehaviour
{
    SavedCharacter who;
    Health health;
    MonsterAI3D brain;
    PathAgent3D agent;
    float goodbyeAt = -1f;
    float vanishAt = -1f;

    void Awake()
    {
        health = GetComponent<Health>();
        brain = GetComponent<MonsterAI3D>();
        agent = GetComponent<PathAgent3D>();
        health.onDeath.AddListener(OnDied);
    }

    /// <summary>Start this body's life as <paramref name="character"/>.</summary>
    public void Begin(SavedCharacter character)
    {
        who = character;
        float stay = SavedCharacterRules.StaySeconds(character.behaviour);
        goodbyeAt = stay > 0f ? Time.time + stay : -1f;
        vanishAt = -1f;
        enabled = true;
    }

    /// <summary>Say <paramref name="line"/> over this character's head, to everyone watching it.</summary>
    public void Say(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        var shell = NetMob.ShellForServerMob(gameObject);
        if (shell != null) shell.Say(line);
    }

    void Update()
    {
        if (who == null || health.IsDead) return;

        if (goodbyeAt > 0f && Time.time >= goodbyeAt)
        {
            goodbyeAt = -1f;
            // IT STOPS FIGHTING AS IT SAYS GOODBYE: nobody may strike it while it leaves, and it strikes nobody.
            health.SetSide(Side.None);
            if (brain != null) brain.ThinksForItself = false;
            if (agent != null) agent.Halt();
            // A GOODBYE IS READ BEFORE IT GOES; one with nothing to say (a rebel) goes at once.
            string line = SavedCharacterLines.Pick(SavedCharacterLines.Goodbye(who.behaviour), CombatRng.Range(0, int.MaxValue));
            Say(line);
            vanishAt = Time.time + (string.IsNullOrEmpty(line) ? 0f : SavedCharacterRules.GoodbyeSeconds);
        }

        if (vanishAt >= 0f && Time.time >= vanishAt)
        {
            vanishAt = -1f;
            who = null;
            SavedCharacters.Vanish(gameObject);
        }
    }

    void OnDied()
    {
        if (who == null) return;
        if (who.behaviour == SavedBehaviour.Rebel)
            Say(SavedCharacterLines.Pick(SavedCharacterLines.RebelBeaten, CombatRng.Range(0, int.MaxValue)));
        who = null;
        goodbyeAt = vanishAt = -1f;
    }
}
