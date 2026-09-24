using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CocoCourier
{
    public static class PixelArt
    {
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        public static readonly string[] ClientNames = { "OFFICE CLERK", "BUILDER", "DOCTOR", "CHEF", "MECHANIC", "TEACHER", "GARDENER", "EXECUTIVE" };
        public static Sprite Courier(int pose=0,bool sunrise=false) => Cell(sunrise?"courier_sunrise":"courier",pose,2,2);
        public static Sprite Client(int index) => Cell("clients",index,4,2);
        public static Sprite Scooter => Cell("scooter",0,1,1);
        public static Sprite World(int index) => Cell("world",index,4,4);
        public static Font Font => Resources.Load<Font>("Fonts/PixelifySans") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        public static Sprite Panel(int index=0) => Cell("ui",index,2,4,true,true);
        private static Sprite Cell(string sheet,int index,int columns,int rows,bool trim=true,bool border=false)
        {
            string key=sheet+index;
            if(sprites.TryGetValue(key,out var sprite) && sprite) return sprite;
            var texture=Resources.Load<Texture2D>("PixelArt/"+sheet);
            if(!texture) return null;
            texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
            int w=texture.width/columns,h=texture.height/rows,x=(index%columns)*w,y=texture.height-(index/columns+1)*h;
            // The generator delivered 1254px atlases with optical, not mathematical, row gutters.
            // These measured regions exclude neighboring assets; alpha trimming supplies tight bounds.
            if(sheet=="ui")
            {
                Rect[] areas={new Rect(.02f,.03f,.47f,.21f),new Rect(.51f,.03f,.47f,.21f),new Rect(.02f,.27f,.47f,.16f),new Rect(.51f,.27f,.47f,.16f),new Rect(.11f,.47f,.26f,.22f),new Rect(.48f,.47f,.49f,.22f),new Rect(.02f,.72f,.47f,.23f),new Rect(.52f,.71f,.45f,.24f)};
                var a=areas[index];x=Mathf.RoundToInt(a.x*texture.width);y=Mathf.RoundToInt((1-a.y-a.height)*texture.height);w=Mathf.RoundToInt(a.width*texture.width);h=Mathf.RoundToInt(a.height*texture.height);
            }
            if(sheet=="world"&&index>=8)
            {
                float t=index<12?.50f:.716f, end=index<12?.712f:.961f;
                y=Mathf.RoundToInt((1-end)*texture.height);h=Mathf.RoundToInt((end-t)*texture.height);
            }
            int left=x,right=x+w-1,bottom=y,top=y+h-1;
            if(trim)
            {
                var pixels=texture.GetPixels32();left=x+w;right=x;bottom=y+h;top=y;
                for(int yy=y;yy<y+h;yy++)for(int xx=x;xx<x+w;xx++)
                    if(pixels[yy*texture.width+xx].a>100){left=Mathf.Min(left,xx);right=Mathf.Max(right,xx);bottom=Mathf.Min(bottom,yy);top=Mathf.Max(top,yy);}
                if(right<left){left=x;right=x+w-1;bottom=y;top=y+h-1;}
            }
            var rect=new Rect(left,bottom,right-left+1,top-bottom+1);
            float b=border&&index!=6?Mathf.Min(rect.width,rect.height)*.35f:0;
            sprite=Sprite.Create(texture,rect,new Vector2(.5f,.5f),w/2f,0,SpriteMeshType.FullRect,new Vector4(b,b,b,b));
            sprite.name=key;sprites[key]=sprite;return sprite;
        }
        public static void Image(Image image,Sprite sprite,bool sliced=false)
        {
            if(!sprite)return;
            image.sprite=sprite;image.color=Color.white;image.type=sliced?UnityEngine.UI.Image.Type.Sliced:UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect=!sliced;
            image.pixelsPerUnitMultiplier=sprite.name=="ui4"?3:1.5f;
        }
        public static void WorldSprite(Transform target,Sprite sprite,float width,float height)
        {
            if(!sprite)return;
            var r=target.GetComponent<SpriteRenderer>();r.sprite=sprite;r.color=Color.white;
            float scale=Mathf.Min(width/sprite.bounds.size.x,height/sprite.bounds.size.y);
            target.localScale=new Vector3(scale,scale,1);
        }
        public static void Tile(Transform target,Sprite sprite,Vector2 size)
        {
            if(!sprite)return;
            var r=target.GetComponent<SpriteRenderer>();r.sprite=sprite;r.color=Color.white;r.drawMode=SpriteDrawMode.Tiled;
            target.localScale=new Vector3(1,size.y/sprite.bounds.size.y,1);r.size=new Vector2(size.x,sprite.bounds.size.y);
        }
    }
}




