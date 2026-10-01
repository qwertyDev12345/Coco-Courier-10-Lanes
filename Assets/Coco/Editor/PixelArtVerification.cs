using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CocoCourier.Editor
{
    public static class PixelArtVerification
    {
        public static void RunBatch()
        {
            try
            {
                foreach(string name in new[]{"courier","courier_sunrise","clients","world","ui","scooter"})
                {
                    string path="Assets/Coco/Resources/PixelArt/"+name+".png";
                    var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                    PixelArtImporter.Configure(importer);importer.SaveAndReimport();
                    var texture=Resources.Load<Texture2D>("PixelArt/"+name);
                    if(!texture||texture.filterMode!=FilterMode.Point)throw new Exception("Missing pixel atlas: "+name);
                }
                if(!PixelArt.Scooter)throw new Exception("Missing scooter sprite");
                for(int i=0;i<8;i++)if(!PixelArt.Client(i))throw new Exception("Missing client "+i);
                for(int i=0;i<4;i++)if(!PixelArt.Courier(i)||!PixelArt.Courier(i,true))throw new Exception("Missing courier pose "+i);
                for(int i=0;i<16;i++)if(!PixelArt.World(i))throw new Exception("Missing world asset "+i);
                for(int i=0;i<8;i++)if(!PixelArt.Panel(i))throw new Exception("Missing UI asset "+i);
                if(!Resources.Load<Font>("Fonts/PixelifySans"))throw new Exception("Missing pixel font");
                File.WriteAllText("Logs/PixelArtVerification.txt","5 point-filtered atlases; 8 clients; 4 courier poses in 2 bag variants; 16 world assets; 8 UI assets; pixel font verified.\n");
                CourierMenuVerification.RunBatch();
            }
            catch(Exception e){Debug.LogException(e);File.WriteAllText("Logs/PixelArtVerification.txt","FAILED: "+e);EditorApplication.Exit(1);}
        }
    }
}

