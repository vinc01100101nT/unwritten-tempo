using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Guardrail against silently-reverting project settings. Some critical render settings live only in serialized
/// asset files (URP pipeline asset, etc.) and, if a change is made live in the inspector but never saved/committed,
/// quietly snap back to whatever's on disk on the next reserialize — which reads as a feature "mysteriously
/// disappearing" (e.g. lamp shadows vanishing because Additional Lights flipped to Per Vertex). This tool is the
/// single source of truth for those known-good values: it <b>checks</b> them and one-click <b>repairs</b> drift,
/// writing the correct values back to disk so they're committable and can't silently revert.
///
/// This is GENERAL, not light-specific. Add an invariant by adding ONE row to <see cref="Invariants"/> — any asset,
/// any serialized property. Keep it to settings whose wrong value causes a real, hard-to-diagnose visual/behaviour
/// bug; this is a safety net, not a style enforcer.
/// </summary>
public static class RenderSettingsGuard
{
    // One row per setting we never want to drift. expected is the serialized int (enums + bools serialize as int).
    struct Invariant
    {
        public string assetPath;     // asset to load (URP pipeline asset, quality asset, …)
        public string property;      // SerializedProperty path
        public long expected;        // known-good value
        public string why;           // human reason — printed on drift so the next person understands the stakes
        public Invariant(string a, string p, long e, string w) { assetPath = a; property = p; expected = e; why = w; }
    }

    const string Urp = "Assets/Settings/UniversalRP.asset";

    static readonly Invariant[] Invariants =
    {
        new(Urp, "m_AdditionalLightsRenderingMode", 2,
            "Additional Lights must be Per Pixel (2) — Per Vertex (1) kills lamp/torch shadows on ground & sprites."),
        new(Urp, "m_AdditionalLightShadowsSupported", 1,
            "Additional Light Shadows must be ON (1) — off → lamps light but cast no shadow."),
        // Add future invariants here, one line each. e.g. main-light shadows, soft shadows, shadow distance…
    };

    [MenuItem("Tools/unwritten/Debug/Verify Render Settings", priority = 800)]
    static void Verify() => Run(repair: false);

    [MenuItem("Tools/unwritten/Debug/Verify & Repair Render Settings", priority = 801)]
    static void Repair() => Run(repair: true);

    static void Run(bool repair)
    {
        var drift = new StringBuilder();
        var fixedUp = new StringBuilder();
        int driftCount = 0, fixedCount = 0;
        var dirtyObjects = new HashSet<Object>();
        var soCache = new Dictionary<string, SerializedObject>();

        foreach (var inv in Invariants)
        {
            if (!soCache.TryGetValue(inv.assetPath, out var so))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(inv.assetPath);
                if (asset == null) { drift.AppendLine($"• MISSING ASSET: {inv.assetPath}"); driftCount++; continue; }
                so = new SerializedObject(asset);
                soCache[inv.assetPath] = so;
            }

            var prop = so.FindProperty(inv.property);
            if (prop == null)
            {
                drift.AppendLine($"• {inv.assetPath} → '{inv.property}' NOT FOUND (URP version changed?)");
                driftCount++;
                continue;
            }

            long actual = prop.propertyType == SerializedPropertyType.Boolean ? (prop.boolValue ? 1 : 0)
                                                                              : prop.intValue;
            if (actual == inv.expected) continue;

            driftCount++;
            drift.AppendLine($"• {System.IO.Path.GetFileName(inv.assetPath)} → {inv.property}: is {actual}, expected {inv.expected}\n    {inv.why}");

            if (repair)
            {
                if (prop.propertyType == SerializedPropertyType.Boolean) prop.boolValue = inv.expected != 0;
                else prop.intValue = (int)inv.expected;
                dirtyObjects.Add(so.targetObject);
                fixedUp.AppendLine($"• {inv.property} → {inv.expected}");
                fixedCount++;
            }
        }

        if (repair && fixedCount > 0)
        {
            foreach (var kv in soCache) kv.Value.ApplyModifiedPropertiesWithoutUndo();
            foreach (var o in dirtyObjects) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        if (driftCount == 0)
        {
            Debug.Log("[unwritten] Render settings OK — all invariants match known-good values.");
            EditorUtility.DisplayDialog("Verify Render Settings", "All good — no drift.", "OK");
            return;
        }

        if (repair)
        {
            Debug.Log($"[unwritten] Repaired {fixedCount} render setting(s) and saved to disk:\n{fixedUp}\n" +
                      "⚠️ COMMIT the changed asset(s) so the fix is permanent.");
            EditorUtility.DisplayDialog("Render settings repaired",
                $"Fixed {fixedCount} drifted setting(s) and saved to disk:\n\n{fixedUp}\n" +
                "⚠️ Commit the changed .asset file(s) so this can't silently revert again.", "OK");
        }
        else
        {
            Debug.LogWarning($"[unwritten] Render settings DRIFT ({driftCount}):\n{drift}\n" +
                             "Run Tools ▸ unwritten ▸ Debug ▸ Verify & Repair Render Settings to fix.");
            EditorUtility.DisplayDialog("Render settings drift detected",
                $"{driftCount} setting(s) differ from known-good:\n\n{drift}\n" +
                "Run 'Verify & Repair Render Settings' to fix them.", "OK");
        }
    }
}
