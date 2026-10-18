using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE GRADE LOOKS. Raw shows none of them; each grows with the grade.
///
/// <para><b>The weapon's enchant</b> (<see cref="WeaponSocket"/>) — its weapon's theme (<see cref="EnchantTheme"/>)
/// streaming off the weapon's model: Lvl 1 at F and E, Lvl 2 at D and C, Lvl 3 at B and A. It rides the weapon, drawn
/// or put away. A graded weapon lying on the floor (<see cref="GroundDrop"/>) wears the same enchant on its picture,
/// and a graded Body, Garment or Shoes piece lying there wears the aura below at its own grade. Every item picture
/// in a window — the bag, a list, the hotbar, the tooltip — shows the same look, filmed (<see cref="GradeLookFilm"/>).</para>
///
/// <para><b>The armour set's aura</b> — the lightning aura around the body, at the LOWEST grade among Body, Garment
/// and Shoes (<see cref="SetGrade"/>; the hat does not count, a missing piece is Raw): the cloud grows first and is full
/// from D, then lightning joins at C and is full at A (<see cref="AuraCloud"/>, <see cref="AuraLightning"/>).</para>
///
/// <para>Every body gets them — a person's, someone else's, a creature's — because each one's look is built by the same
/// <see cref="WeaponSocket"/>.</para>
///
/// <para><b>Cost.</b> The enchant and the aura are particle effects, built once on a change and never per frame, but
/// the engine updates every layer every frame: up to 7 layers on a weapon at Lvl 3 (both copies of a pair weapon), the
/// same on every graded weapon lying on the floor in this map, and 3 for the aura. Pictures in windows: see
/// <see cref="GradeLookFilm"/>.</para>
/// </summary>
public static class GradeGlow
{
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

    /// <summary>Does a piece worn in <paramref name="slot"/> count toward the armour set's grade (<see cref="SetGrade"/>)?</summary>
    public static bool InSet(EquipSlotType slot) =>
        slot == EquipSlotType.Armor || slot == EquipSlotType.Garment || slot == EquipSlotType.Boots;

    static Material ui;

    /// <summary>The one material every picture's look draws with.</summary>
    public static Material UiMaterial => ui != null ? ui : (ui = Resources.Load<Material>("Glow/GradeGlowUI"));

    // ---- on an item's picture -----------------------------------------------------------------------------------

    /// <summary>A picture's look filling the square <paramref name="square"/>, drawn just over
    /// <paramref name="picture"/> as it is on the floor, switched off until <see cref="Show"/> gives it one.</summary>
    public static RawImage MakeForSquare(Transform square, Transform picture)
    {
        var go = new GameObject("GradeLook", typeof(RectTransform)) { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.SetParent(square, false);
        var img = go.AddComponent<RawImage>();
        img.material = UiMaterial;
        img.raycastTarget = false;
        UiKit.Fill(img.rectTransform, Vector2.zero, Vector2.zero);
        go.AddComponent<GradeLookWatcher>();
        // Over the picture: behind it, a look was hidden by every picture that fills its square (armour does).
        if (picture != null && picture.parent == square) go.transform.SetSiblingIndex(picture.GetSiblingIndex() + 1);
        // Square whatever its box: a look is filmed square, and a tall box would stretch it.
        var fit = go.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = 1f;
        go.SetActive(false);
        return img;
    }

    /// <summary>Show <paramref name="copy"/>'s look on <paramref name="look"/>; nothing, a Raw piece, or a piece with
    /// no look switches it off.</summary>
    public static void Show(RawImage look, EquipmentInstance copy)
    {
        if (look == null) return;
        look.GetComponent<GradeLookWatcher>().Set(GradeLookFilm.LookOf(copy));
    }

    /// <summary>The same, for a piece known only by its item and its <paramref name="grade"/>.</summary>
    public static void Show(RawImage look, Item def, Quality grade)
    {
        if (look == null) return;
        look.GetComponent<GradeLookWatcher>().Set(GradeLookFilm.LookOf(def, grade));
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
        if (weapon == null) return null;
        var model = weapon.GetComponentInChildren<MeshFilter>(true);   // perf-ok: on a weapon or grade change only
        if (model == null || model.sharedMesh == null) return null;
        // On the model's own transform, so its shape is the model's box in the model's own units. The effects were
        // made for a sword about one unit long, and every weapon model is about one unit long too, so their sizes carry.
        return MakeThemeEnchant(model.transform, model.sharedMesh.bounds, 1f, EnchantTheme(type), EnchantLevel(grade));
    }

    /// <summary>The same enchant on <paramref name="icon"/>, a graded weapon's picture lying on the floor — streaming
    /// off the picture, its longest side standing in for the weapon's length. Null as for <see cref="MakeWeaponEnchant"/>.</summary>
    public static GameObject MakeIconEnchant(SpriteRenderer icon, WeaponType type, Quality grade)
    {
        if (icon == null || icon.sprite == null) return null;
        var box = icon.sprite.bounds;
        float length = Mathf.Max(box.size.x, box.size.y);
        if (length <= 0f) return null;
        var look = MakeThemeEnchant(icon.transform, box, length, EnchantTheme(type), EnchantLevel(grade));
        DrawOverIcon(look, icon);
        return look;
    }

    // A look on a floor picture is drawn over it, in its sorting layer, so the picture never hides it.
    static void DrawOverIcon(GameObject look, SpriteRenderer icon)
    {
        if (look == null) return;
        foreach (var r in look.GetComponentsInChildren<ParticleSystemRenderer>(true))   // perf-ok: once per graded drop shown
        {
            r.sortingLayerID = icon.sortingLayerID;
            r.sortingOrder = icon.sortingOrder + 1;
        }
    }

    /// <summary>THE ONE ENCHANT BUILDER: <paramref name="theme"/>'s effect at <paramref name="level"/> on
    /// <paramref name="on"/>, streaming from <paramref name="box"/> (in its own units), made <paramref name="length"/>
    /// times the one-unit sword it was made for. Null for no theme or level 0.</summary>
    public static GameObject MakeThemeEnchant(Transform on, Bounds box, float length, Enchant theme, int level)
    {
        if (on == null || theme == Enchant.None || level == 0) return null;
        var def = EffectCatalog.Get("weaponenchant." + theme.ToString().ToLowerInvariant() + "-lvl-" + level);
        if (def == null || def.prefab == null) return null;

        var look = Object.Instantiate(def.prefab, on, false);
        look.name = LookName;
        look.hideFlags = HideFlags.DontSaveInEditor;
        // A MIRRORED model (a pair weapon's other-hand copy carries a negative x scale) would mirror every particle
        // too, turning each one back to front, and the enchant materials cull back faces — so the whole effect
        // vanished from the second hand. Mirroring the effect again cancels it; the box centre flips with it.
        var s = on.lossyScale;
        bool mirrored = s.x * s.y * s.z < 0f;
        look.transform.localScale = new Vector3(mirrored ? -length : length, length, length);
        var boxCentre = (mirrored ? new Vector3(-box.center.x, box.center.y, box.center.z) : box.center) / length;
        var boxSize = box.size / length;
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
                shape.position = boxCentre;
                shape.rotation = Vector3.zero;
                shape.scale = boxSize;
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

    /// <summary>The same aura on <paramref name="icon"/>, a graded Body, Garment or Shoes piece's picture lying on the
    /// floor — at the strength of the piece's own <paramref name="grade"/>, standing on the picture's bottom edge and
    /// as tall as the picture. Null for Raw.</summary>
    public static GameObject MakeIconAura(SpriteRenderer icon, Quality grade)
    {
        if (icon == null || icon.sprite == null) return null;
        var box = icon.sprite.bounds;
        var t = icon.transform;
        var look = MakeSetAura(t, new Vector3(box.center.x, box.min.y, 0f), box.size.y * Mathf.Abs(t.lossyScale.y), grade);
        DrawOverIcon(look, icon);
        return look;
    }
}
