using UnityEngine;
namespace CocoCourier
{
    // Small code-native pixel shapes share the courier's world scale and never affect collisions.
    public static class CourierCosmetics
    {
        public static readonly Color[] Colors = { new Color(.2f,.35f,.6f), new Color(1,.72f,.15f), Color.white, Color.white, new Color(.25f,.5f,.7f), new Color(.3f,.2f,.4f), new Color(.65f,.8f,.35f), new Color(.12f,.14f,.22f), new Color(1f,.62f,.12f) };
        public static Transform CreateWorld(Transform parent)
        {
            var root = new GameObject("Reputation hat").transform; root.SetParent(parent,false);
            for(int i=0;i<2;i++)
            {
                var part=new GameObject(i==0?"Hat crown":"Hat brim",typeof(SpriteRenderer)); part.transform.SetParent(root,false);
                var r=part.GetComponent<SpriteRenderer>(); r.sprite=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),new Vector2(.5f,.5f),1); r.sortingOrder=8;
            }
            return root;
        }
        public static void UpdateWorld(Transform root,int index,Vector3 position)
        {
            root.gameObject.SetActive(index>=0); if(index<0)return; root.position=position;
            var crown=root.GetChild(0); crown.localScale=new Vector3(index==3?.42f:.36f,index==3||index==7?.32f:.17f,1);
            crown.localPosition=new Vector3(0,index==3||index==7?.12f:.045f,0);
            var brim=root.GetChild(1);brim.localScale=new Vector3(index==6?.66f:.52f,.07f,1);
            foreach(var r in root.GetComponentsInChildren<SpriteRenderer>())r.color=Colors[index];
        }
    }
}
