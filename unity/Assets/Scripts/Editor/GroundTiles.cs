using UnityEditor;
using UnityEngine;

/// <summary>
/// The ONE way author-time code reaches a ground tile texture. Every tile the game paints ground with lives in
/// <see cref="Dir"/> (ASSETS.md: one copy, one home) and is named by its file, e.g. <c>ground_grass</c>. The blended
/// ground, the curbs and the brush palette all resolve through here, so a tile is found the same way everywhere
/// and nothing can pick up a same-named texture from another folder.
/// </summary>
public static class GroundTiles
{
    public const string Dir = "Assets/1_Authored/Art/GroundTiles";

    /// <summary>The tile texture called <paramref name="name"/> (no extension), or null when there is no such tile.</summary>
    public static Texture2D Load(string name) =>
        string.IsNullOrEmpty(name) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{name}.png");
}
