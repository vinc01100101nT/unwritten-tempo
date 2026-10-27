using UnityEngine;

/// <summary>
/// One falling-weather particle effect (rain, snow) loaded from <c>Resources/Weather/</c>, held over whatever the
/// camera follows, and scaled by the weather amount: its emitters run at their authored rate × the amount. The
/// prefab carries its own size for this world (the pack is built for people about 1.8 tall; ours are 0.34).
/// Shared by <see cref="RainOverlay"/> and <see cref="SnowOverlay"/>.
///
/// <para>Costs nothing while the sky is clear: the effect is not loaded until the first time its weather starts,
/// and once the weather stops and the last particle has landed the object is switched off.</para>
/// </summary>
public sealed class WeatherParticles
{
    readonly string resource;
    readonly float heightAboveGround;
    readonly Quaternion restRotation;

    GameObject root;
    ParticleSystem top;
    ParticleSystem[] emitters;
    float[] authoredRates;
    Color[] authoredColours;
    float lastAmount = -1f;
    Color lastTint = new Color(-1f, -1f, -1f, -1f);
    bool missing;

    public WeatherParticles(string resource, float heightAboveGround, Vector3 restEuler)
    {
        this.resource = resource;
        this.heightAboveGround = heightAboveGround;
        restRotation = Quaternion.Euler(restEuler);
    }

    /// <summary>Run the effect at <paramref name="amount"/> (0..1) this frame. <paramref name="leanDeg"/> tips the
    /// fall sideways on screen (positive = drifting right); <paramref name="tint"/> multiplies every emitter's colour.</summary>
    public void Drive(float amount, float leanDeg, Color tint)
    {
        bool on = amount > 0.001f;
        if (root == null)
        {
            if (!on || missing || !Load()) return;
        }

        if (amount != lastAmount)
        {
            lastAmount = amount;
            for (int i = 0; i < emitters.Length; i++)
            {
                var em = emitters[i].emission;
                em.rateOverTimeMultiplier = on ? authoredRates[i] * amount : 0f;
            }
        }

        if (!on)
        {
            // weather stopped: let what is already falling land, then switch the object off
            if (root.activeSelf)
            {
                if (top.isEmitting) top.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (!top.IsAlive(true)) root.SetActive(false);
            }
            if (!root.activeSelf) return;
        }
        else
        {
            if (!root.activeSelf) root.SetActive(true);
            if (!top.isEmitting) top.Play(true);
        }

        if (tint != lastTint)
        {
            lastTint = tint;
            for (int i = 0; i < emitters.Length; i++)
            {
                var main = emitters[i].main;
                main.startColor = authoredColours[i] * tint;
            }
        }

        var follow = CameraFollow3D.Main != null ? CameraFollow3D.Main.target : null;
        if (follow == null) return;
        Vector3 centre = follow.position;
        centre.y += heightAboveGround;
        var t = root.transform;
        t.SetPositionAndRotation(centre, Quaternion.AngleAxis(-leanDeg, CameraRig.ForwardXZ) * restRotation);
    }

    bool Load()
    {
        var prefab = Resources.Load<GameObject>(resource);
        if (prefab == null)
        {
            missing = true;
            GameLog.Warn(LogCat.Unwritten, $"WeatherParticles: no prefab at Resources/{resource} — this weather draws nothing.");
            return false;
        }
        root = Object.Instantiate(prefab);
        root.name = prefab.name;
        root.hideFlags = HideFlags.DontSave;
        Object.DontDestroyOnLoad(root);
        int fx = LayerMask.NameToLayer("TransparentFX");
        foreach (var tr in root.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = fx;
        // excluded from the planar-reflection camera, like every screen-covering weather effect

        top = root.GetComponent<ParticleSystem>();
        var all = root.GetComponentsInChildren<ParticleSystem>(true);
        int n = 0;
        foreach (var ps in all) if (ps.emission.rateOverTimeMultiplier > 0f) n++;
        // only the emitters that run on their own; the splash systems fire off collisions and keep rate 0
        emitters = new ParticleSystem[n];
        authoredRates = new float[n];
        authoredColours = new Color[n];
        n = 0;
        foreach (var ps in all)
        {
            float rate = ps.emission.rateOverTimeMultiplier;
            if (rate <= 0f) continue;
            emitters[n] = ps;
            authoredRates[n] = rate;
            authoredColours[n] = ps.main.startColor.color;
            n++;
        }
        if (top == null) top = all.Length > 0 ? all[0] : null;
        return top != null;
    }

    /// <summary>Switch the effect off at once, falling particles included (its owner was disabled).</summary>
    public void Hide()
    {
        if (root != null) root.SetActive(false);
        lastAmount = -1f;
    }

    public void Destroy()
    {
        if (root != null) Object.Destroy(root);
        root = null;
    }
}
