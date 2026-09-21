using UnityEditor;
using UnityEngine;

/// <summary>
/// Auto-applies crisp pixel-art import settings to every texture under
/// <c>Assets/1_Authored/Art/</c> so you never hand-fix import settings again. Runs on every
/// import / reimport.
///
/// NOTE: art that was imported BEFORE this script existed won't pick up the
/// settings until you reimport it — right-click <c>Assets/1_Authored/Art</c> → Reimport.
///
/// It does NOT slice multi-sprite sheets. For a character/tileset sheet, set
/// Sprite Mode = Multiple in the Inspector and grid-slice it in the Sprite
/// Editor (see unity/README.md). The Multiple mode + your slices are preserved
/// across reimports; only PPU / filtering / compression are enforced.
/// </summary>
public class PixelArtImportPostprocessor : AssetPostprocessor
{
    // Ninja Adventure (and the rest of our packs) are authored at 16 px / tile.
    const int PixelsPerUnit = 16;

    // DELIBERATELY runs on every import, not only the first — unlike the audio and clip postprocessors beside it.
    // This rule is the single OWNER of these settings: a hand-edited .meta under /Art/ is reverted on the next
    // import, and that is the point, because it also means a change HERE reaches every existing texture on its
    // next reimport instead of only new ones. Reimport the folder after editing this file.
    void OnPreprocessTexture()
    {
        string path = assetPath.Replace('\\', '/');

        // Only touch the art folder; leave UI/editor/imported-package textures alone.
        if (!path.Contains("/Art/")) return;

        var importer = (TextureImporter)assetImporter;

        // Kenney Cursor Pack: import as readable, uncompressed Cursor textures so
        // Cursor.SetCursor() (GameCursor) gets a clean Texture2D. NOT sprites — the
        // hardware cursor needs the raw texture with its alpha preserved.
        if (path.Contains("/KenneyCursorPack/"))
        {
            importer.textureType = TextureImporterType.Cursor;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            return;
        }

        // Ground TILE textures are tiled across 3D ground meshes (blended ground, curb/border strips), NOT pixel-art
        // sprites. Import them as REPEATING Default textures with mipmaps so a plain material tiles them seamlessly and
        // they don't shimmer at distance. (GroundBlend uses its own inline repeat sampler, so this never affects it —
        // it only fixes materials, like the curb's URP/Lit, that honour the texture's own wrap mode.)
        //
        // COMPRESSED, unlike the pixel-art sprites below: these are painted 2048-square photos of ground, and block
        // compression is invisible on them (the same images already shipped compressed from the prop-texture folder
        // and nobody could tell). Uncompressed they were 85 MB of a 307 MB build. Compression needs power-of-two
        // sizes, so the four odd-sized tiles are resampled to the nearest — a repeating ground tile has no pixel
        // grid to keep.
        if (path.Contains("/GroundTiles/"))
        {
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;                       // crisp, no blur
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;           // never rescale pixel sheets
        importer.wrapMode = TextureWrapMode.Clamp;

        // Keep Multiple (sliced) sheets sliced; default a fresh texture to Single.
        if (importer.spriteImportMode != SpriteImportMode.Multiple)
            importer.spriteImportMode = SpriteImportMode.Single;
    }
}
