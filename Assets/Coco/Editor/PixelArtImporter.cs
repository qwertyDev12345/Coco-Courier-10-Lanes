using UnityEditor;
using UnityEngine;

namespace CocoCourier.Editor
{
    public sealed class PixelArtImporter : AssetPostprocessor
    {
        public static int Resolution(string path)
        {
            string name=System.IO.Path.GetFileNameWithoutExtension(path);
            return name=="scooter"?128:name=="courier"||name=="courier_sunrise"?256:512;
        }
        public static void Configure(TextureImporter importer)
        {
            importer.textureType=TextureImporterType.Default;
            // Runtime alpha trimming uses GetPixels32: keep readable RGBA32 rather than ASTC.
            importer.isReadable=true;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Point;
            importer.wrapMode=TextureWrapMode.Clamp;importer.alphaIsTransparency=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=Resolution(importer.assetPath);
            foreach(string platform in new[]{"iPhone","Android","Standalone"})
            {
                var settings=importer.GetPlatformTextureSettings(platform);
                settings.name=platform;settings.overridden=true;settings.maxTextureSize=Resolution(importer.assetPath);
                settings.format=TextureImporterFormat.RGBA32;settings.textureCompression=TextureImporterCompression.Uncompressed;
                importer.SetPlatformTextureSettings(settings);
            }
        }
        private void OnPreprocessTexture()
        {
            if(!assetPath.StartsWith("Assets/Coco/Resources/PixelArt/"))return;
            var importer=(TextureImporter)assetImporter;
            Configure(importer);
        }
    }
}
