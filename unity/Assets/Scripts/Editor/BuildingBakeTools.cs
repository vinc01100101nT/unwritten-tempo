using UnityEditor;

/// <summary>
/// RETIRED — superseded by <see cref="MapBaker"/> (Tools ▸ unwritten ▸ Bake Map (props + collision)).
///
/// The old "Bake Buildings to Objects" produced the SLOW path: a per-cell BoxCollider2D on every tile
/// and a per-frame <c>YDepthSorter</c> on every house. That contradicts the performance-first rule
/// (merge static colliders, don't sort static props each frame). The menu item is kept only so muscle
/// memory still works — it now just runs the fast Bake Map, which builds one merged Collision tilemap
/// (CompositeCollider2D) and static props with fixed sort orders.
/// </summary>
public static class BuildingBakeTools
{
    [MenuItem("Tools/unwritten/Bake Buildings to Objects")]
    static void BakeMenu() => MapBaker.Bake();
}
