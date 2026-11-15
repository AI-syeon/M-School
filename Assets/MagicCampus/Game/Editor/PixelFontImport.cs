using UnityEditor;
using UnityEngine;

namespace MagicCampus.Game.Editor
{
    /// <summary>
    /// The Galmuri pixel fonts import as dynamic fonts rasterized without antialiasing (Hinted Raster), so text drawn at a whole
    /// multiple of their pixel grid is sharp. Fonts imported before this script existed are reimported once on load.
    /// </summary>
    public sealed class PixelFontImport : AssetPostprocessor
    {
        const string Folder = "Assets/MagicCampus/Resources/Fonts/";

        void OnPreprocessAsset()
        {
            if (assetPath.StartsWith(Folder) && assetImporter is TrueTypeFontImporter font) Configure(font);
        }

        static bool Configure(TrueTypeFontImporter font)
        {
            if (font.fontRenderingMode == FontRenderingMode.HintedRaster && font.fontTextureCase == FontTextureCase.Dynamic) return false;
            font.fontRenderingMode = FontRenderingMode.HintedRaster;
            font.fontTextureCase = FontTextureCase.Dynamic;
            return true;
        }

        [InitializeOnLoadMethod]
        static void ReimportStale()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { Folder.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TrueTypeFontImporter font && Configure(font)) font.SaveAndReimport();
            }
        }
    }
}
