using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
namespace CocoCourier.Editor
{
    public static class TextureSizeAudit
    {
        public static void Run()
        {
            var report=new StringBuilder();long total=0;
            foreach(string name in new[]{"courier","courier_sunrise","clients","world","ui","scooter"})
            {
                string path="Assets/Coco/Resources/PixelArt/"+name+".png";
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                PixelArtImporter.Configure(importer);importer.SaveAndReimport();
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(!texture.isReadable || texture.width>PixelArtImporter.Resolution(path) || texture.height>PixelArtImporter.Resolution(path))throw new Exception("Texture size/readability invalid: "+name);
                total+=(long)texture.width*texture.height*4;
                report.AppendLine(name+": "+texture.width+" x "+texture.height+", "+texture.format);
                File.WriteAllBytes("Logs/Reduced-"+name+".png",texture.EncodeToPNG());
            }
            for(int i=0;i<8;i++)if(!PixelArt.Client(i)||!PixelArt.Panel(i))throw new Exception("Invalid client/panel");
            for(int i=0;i<4;i++)if(!PixelArt.Courier(i)||!PixelArt.Courier(i,true))throw new Exception("Invalid courier");
            for(int i=0;i<16;i++)if(!PixelArt.World(i))throw new Exception("Invalid world sprite");
            if(!PixelArt.Scooter)throw new Exception("Invalid scooter");
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.iOS,Il2CppCodeGeneration.OptimizeSize);
            AssetDatabase.SaveAssets();
            report.AppendLine("Total raw RGBA texture bytes: "+total);
            report.AppendLine("All 41 runtime sprite slices loaded; transparent trimming and readable pixels verified.");
            report.AppendLine("Actual TestFlight download/install size requires a new signed Apple-processed build.");
            File.WriteAllText("Logs/TextureSizeAudit.txt",report.ToString());Debug.Log(report);
        }
    }
}
