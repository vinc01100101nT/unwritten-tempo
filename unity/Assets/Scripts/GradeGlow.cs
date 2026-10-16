using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE GRADE LOOKS. Raw shows none of them; each grows with the grade.
///
/// <para><b>On an item's picture</b> (<see cref="ItemIcon"/>) — a sky-blue glow at its edge, F a faint slow pulse, then
/// wider, brighter and quicker up the ladder, A with sparkles running round it. Never on the item's card.</para>
///
/// <para><b>Behind a graded piece on the floor</b> (<see cref="GroundDrop"/>) — a sky-blue sphere of light.</para>
///
/// <para><b>The weapon's enchant</b> (<see cref="WeaponSocket"/>) — its weapon's theme (<see cref="EnchantTheme"/>)
/// streaming off the weapon's model: Lvl 1 at F and E, Lvl 2 at D and C, Lvl 3 at B and A. It rides the weapon, drawn
/// or put away.</para>
///
/// <para><b>The armour set's aura</b> — the lightning aura around the body, at the LOWEST grade among Body, Garment
/// and Shoes (<see cref="SetGrade"/>; the hat does not count, a missing piece is Raw): the cloud grows first and is full
/// from D, then lightning joins at C and is full at A (<see cref="AuraCloud"/>, <see cref="AuraLightning"/>).</para>
///
/// <para>Every body gets them — a person's, someone else's, a creature's — because each one's look is built by the same
/// <see cref="WeaponSocket"/>.</para>
///
/// <para><b>Cost.</b> The picture glow and the floor sphere move in their shaders, from the clock: no code per frame.
/// The enchant and the aura are particle effects, built once on a change and never per frame, but the engine updates
/// every layer every frame: up to 7 layers on a weapon at Lvl 3 (both copies of a pair weapon), and 3 for the aura.</para>
/// </summary>
public static class GradeGlow
{
    /// <summary>The colour of the picture glow and the floor sphere.</summary>
    public static readonly Color SkyBlue = new Color(0.45f, 0.82f, 1f);

    /// <summary>The grade's share of the ladder: 0 at Raw (nothing shown), 1 at A.</summary>
    public static float Level(Quality grade) => Mathf.Clamp01((float)grade / (float)Quality.A);

    /// <summary>Does this grade show anything?</summary>
    public static bool Glows(Quality grade) => grade > Quality.Raw;

    /// <summary>THE ARMOUR SET'S GRADE: the lowest of the Body, Garment and Shoes worn; a missing piece is Raw, so a
    /// set shows only when all three are graded. The hat never counts.</summary>
    public static Quality SetGrade(EquipmentInstance body, EquipmentInstance garment, EquipmentInstance shoes)
    {
        if (body == null || garment == null || shoes == null) return Quality.Raw;
        var low = body.quality;
        if (garment.quality < low) low = garment.quality;
        if (shoes.quality < low) low = shoes.quality;
        return low;
    }

    static Material ui, sphereBase;
    static readonly Material[] spheres = new Material[(int)Quality.A + 1];
    static Mesh quad;

    static readonly int LevelId = Shader.PropertyToID("_Level");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>The one material every picture glow draws with.</summary>
    public static Material UiMaterial => ui != null ? ui : (ui = Resources.Load<Material>("Glow/GradeGlowUI"));

    // ---- on an item's picture -----------------------------------------------------------------------------------

    /// <summary>A picture glow laid over the square <paramref name="square"/> fills, hidden until <see cref="Show"/>
    /// lights it.</summary>
    public static Image MakeForSquare(Transform square)
    {
        var img = UiKit.Img("GradeGlow", square, Color.clear);
        img.sprite = null;
        img.material = UiMaterial;
        img.raycastTarget = false;
        UiKit.Fill(img.rectTransform, Vector2.zero, Vector2.zero);
        img.enabled = false;
        return img;
    }

    /// <summary>Light <paramref name="glow"/> for <paramref name="copy"/>'s grade; nothing, or a Raw piece, puts it out.</summary>
    public static void Show(Image glow, EquipmentInstance copy)
    {
        if (glow == null) return;
        bool on = copy != null && Glows(copy.quality);
        if (glow.enabled != on) glow.enabled = on;
        if (!on) return;
        var c = SkyBlue;
        c.a = Level(copy.quality);
        if (glow.color != c) glow.color = c;
    }

    // ---- behind a piece on the floor ----------------------------------------------------------------------------

    /// <summary>A sphere <paramref name="size"/> metres across, under <paramref name="parent"/> at
    /// <paramref name="localPosition"/>, for <paramref name="grade"/>. Null for Raw.</summary>
    public static MeshRenderer MakeSphere(Transform parent, Vector3 localPosition, float size, Quality grade)
    {
        if (!Glows(grade) || parent == null) return null;
        int g = (int)grade;
        if (spheres[g] == null)
        {
            if (sphereBase == null) sphereBase = Resources.Load<Material>("Glow/GradeGlowWorld");
            if (sphereBase == null) return null;
            spheres[g] = new Material(sphereBase) { name = sphereBase.name + " " + grade };
            spheres[g].SetFloat(LevelId, Level(grade));
            spheres[g].SetColor(ColorId, SkyBlue);
        }
        if (quad == null) quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var go = new GameObject("GradeSphere") { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        // The shader reads its size off the object's scale; the parent's own scale is undone so the size is in metres.
        float s = Mathf.Max(1e-4f, Mathf.Abs(parent.lossyScale.x));
        go.transform.localScale = Vector3.one * (size / s);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = spheres[g];
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return r;
    }

    // ---- the weapon's enchant -----------------------------------------------------------------------------------

    /// <summary>The five enchant themes, each the name of its effects in the Effect Library ("weaponenchant.ice-lvl-2").</summary>
    public enum Enchant { None, Fire, Water, Ice, Nature, Dark }

    /// <summary>WHICH THEME A WEAPON WEARS, chosen by the user (2026-10-16) from each weapon's own look.</summary>
    public static Enchant EnchantTheme(WeaponType w)
    {
        switch (w)
        {
            case WeaponType.Axe: case WeaponType.Knuckle: case WeaponType.Staff: return Enchant.Fire;
            case WeaponType.TwoHandSword: case WeaponType.Spear: return Enchant.Water;
            case WeaponType.Bow: case WeaponType.Sword: case WeaponType.Katar: return Enchant.Ice;
            case WeaponType.Wand: case WeaponType.Mace: case WeaponType.Shuriken: return Enchant.Nature;
            case WeaponType.Firearm: case WeaponType.Dagger: case WeaponType.Scythe: return Enchant.Dark;
            default: return Enchant.None;
        }
    }

    /// <summary>The enchant's level for a grade: 0 at Raw (none), 1 at F and E, 2 at D and C, 3 at B and A.</summary>
    public static int EnchantLevel(Quality grade) => grade <= Quality.Raw ? 0 : grade <= Quality.E ? 1 : grade <= Quality.C ? 2 : 3;

    // Every enchant and aura object is named this, so one built onto a copy of the weapon can be found and taken off.
    const string LookName = "GradeLook";

    /// <summary>Put <paramref name="weapon"/>'s themed enchant for <paramref name="grade"/> on it — streaming off its
    /// model, at its size. Null when the weapon has no theme, no model, or the grade is Raw.</summary>
    public static GameObject MakeWeaponEnchant(GameObject weapon, WeaponType type, Quality grade)
    {
        var theme = EnchantTheme(type);
        int level = EnchantLevel(grade);
        if (weapon == null || theme == Enchant.None || level == 0) return null;
        var def = EffectCatalog.Get("weaponenchant." + theme.ToString().ToLowerInvariant() + "-lvl-" + level);
        var model = weapon.GetComponentInChildren<MeshFilter>(true);   // perf-ok: on a weapon or grade change only
        if (def == null || def.prefab == null || model == null || model.sharedMesh == null) return null;

        // On the model's own transform, so its shape is the model's box in the model's own units. The effects were
        // made for a sword about one unit long, and every weapon model is about one unit long too, so their sizes carry.
        var look = Object.Instantiate(def.prefab, model.transform, false);
        look.name = LookName;
        look.hideFlags = HideFlags.DontSaveInEditor;
        var box = model.sharedMesh.bounds;
        foreach (var ps in look.GetComponentsInChildren<ParticleSystem>(true))   // perf-ok: on a weapon or grade change only
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var shape = ps.shape;
            if (shape.enabled)
            {
                // A box the size of the model instead of the model's surface: the same streaming along the weapon, with
                // no need to keep a second readable copy of every weapon's mesh in memory.
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.position = box.center;
                shape.rotation = Vector3.zero;
                shape.scale = box.size;
            }
            HideIfUnpainted(ps);
        }
        return look;
    }

    /// <summary>Take any enchant off <paramref name="weaponCopy"/> — a picture of the weapon made from the held one.</summary>
    public static void StripEnchant(GameObject weaponCopy)
    {
        if (weaponCopy == null) return;
        foreach (var t in weaponCopy.GetComponentsInChildren<Transform>(true))   // perf-ok: once per dropped-weapon copy
            if (t != null && t.name == LookName) Object.Destroy(t.gameObject);
    }

    // A layer with no material to draw with draws nothing anyone should see; it is switched off.
    static void HideIfUnpainted(ParticleSystem ps)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        if (r != null && r.sharedMaterial == null)
        {
            r.enabled = false;
            var em = ps.emission;
            em.enabled = false;
        }
    }

    // ---- the armour set's aura ----------------------------------------------------------------------------------

    /// <summary>The aura's cloud at a set grade, as a share of its full strength: F 0.35, E 0.65, full from D.</summary>
    public static float AuraCloud(Quality grade) =>
        grade <= Quality.Raw ? 0f : grade == Quality.F ? 0.35f : grade == Quality.E ? 0.65f : 1f;

    /// <summary>The aura's lightning at a set grade, as a share of its full strength: none to D, then C 0.3, B 0.65, A full.</summary>
    public static float AuraLightning(Quality grade) =>
        grade <= Quality.D ? 0f : grade == Quality.C ? 0.3f : grade == Quality.B ? 0.65f : 1f;

    /// <summary>The body height the lightning aura was made around, in metres — it is scaled from this to each body.</summary>
    public const float AuraMadeForHeight = 1.8f;

    // The aura's cloud layer, by its name in the effect; every other layer is lightning.
    const string AuraCloudLayer = "Smoke";

    /// <summary>The lightning aura around a body <paramref name="height"/> metres tall, under <paramref name="root"/> at
    /// <paramref name="localPosition"/>, at the strength of <paramref name="setGrade"/>. Null for Raw.</summary>
    public static GameObject MakeSetAura(Transform root, Vector3 localPosition, float height, Quality setGrade)
    {
        if (!Glows(setGrade) || root == null || height <= 0f) return null;
        var def = EffectCatalog.Get("aura.lightning");
        if (def == null || def.prefab == null) return null;
        var look = Object.Instantiate(def.prefab, root, false);
        look.name = LookName;
        look.hideFlags = HideFlags.DontSaveInEditor;
        look.transform.localPosition = localPosition;
        look.transform.localRotation = Quaternion.identity;
        float parent = Mathf.Max(1e-4f, Mathf.Abs(root.lossyScale.y));
        look.transform.localScale = Vector3.one * (height / AuraMadeForHeight / parent);
        float cloud = AuraCloud(setGrade), lightning = AuraLightning(setGrade);
        foreach (var ps in look.GetComponentsInChildren<ParticleSystem>(true))   // perf-ok: on a change of the set's grade only
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            float share = ps.name == AuraCloudLayer ? cloud : lightning;
            var em = ps.emission;
            em.rateOverTimeMultiplier *= share;
            if (share <= 0f) { em.enabled = false; ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); }
            HideIfUnpainted(ps);
        }
        return look;
    }
}
