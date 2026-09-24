using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace CocoCourier
{
    public sealed partial class CourierMenu
    {
        private GameObject kitchen, kitchenResult, rushPanel;
        private Text kitchenTimer, recipeText, kitchenScore, kitchenFeedback, kitchenSummary, rushStats;
        private Button kitchenStart, rushWear;
        private readonly RectTransform[] beltItems = new RectTransform[8];
        private readonly float[] beltX = new float[8];
        private readonly bool[] consumed = new bool[8];
        private readonly int[] beltIngredients = {0,1,2,3,0,1,2,3};
        private bool kitchenRewarded;
        private float kitchenMessageUntil;
        private CourierSettings activitySettings;
        public KitchenRound Kitchen { get; private set; }
        private void BuildActivities()
        {
            activitySettings=Resources.Load<CourierSettings>("CourierSettings");
            kitchen=Box("Kitchen panel",safe,0,0,1,1,ink).gameObject;
            Title("Kitchen title",kitchen.transform,"COCO'S KITCHEN",34,.04f,.9f,.96f,.98f,gold);
            Title("Kitchen instructions",kitchen.transform,"Tap moving ingredients in recipe order.\nA mistake resets this sandwich. Entry is free.",20,.06f,.78f,.94f,.88f,Color.white);
            kitchenTimer=Title("Kitchen timer",kitchen.transform,"",27,.04f,.71f,.48f,.78f,mint);
            kitchenScore=Title("Kitchen score",kitchen.transform,"",23,.5f,.71f,.96f,.78f,gold);
            PixelArt.Image(Box("Kitchen customer",kitchen.transform,.04f,.57f,.22f,.7f,Color.white),PixelArt.Client(3));
            recipeText=Title("Recipe",kitchen.transform,"",21,.23f,.56f,.96f,.7f,Color.white);
            var belt=Box("Ingredient belt",kitchen.transform,.04f,.35f,.96f,.53f,card).rectTransform;
            belt.gameObject.AddComponent<RectMask2D>();
            for(int i=0;i<beltItems.Length;i++)
            {
                int slot=i,ingredient=beltIngredients[i];
                var button=Action("Ingredient "+i,belt,KitchenRound.Ingredients[ingredient],0,.08f,.22f,.92f,()=>TapIngredient(slot));
                beltItems[i]=button.GetComponent<RectTransform>();
                PixelArt.Image(button.GetComponent<Image>(),PixelArt.Panel(1),true);
                var label=button.GetComponentInChildren<Text>();label.color=Color.white;label.fontSize=18;label.resizeTextMaxSize=18;
                label.rectTransform.anchorMin=new Vector2(.02f,.06f);label.rectTransform.anchorMax=new Vector2(.98f,.31f);
                IngredientIcon(button.transform,ingredient);
            }
            kitchenFeedback=Title("Kitchen feedback",kitchen.transform,"",21,.04f,.25f,.96f,.34f,mint);
            Title("Kitchen rewards",kitchen.transform,"1 sandwich made: earn 1 saved meal\n"+activitySettings.kitchenDoubleRewardScore+" made: earn 2 saved meals (maximum)\nEach saved meal restores up to 5 energy",19,.04f,.12f,.96f,.25f,Color.white);
            kitchenStart=Action("Start cooking",kitchen.transform,"START / "+activitySettings.kitchenSeconds.ToString("0")+" SECONDS",.08f,.36f,.92f,.48f,StartCooking);
            Action("Leave kitchen",kitchen.transform,"BACK / LEAVING ENDS THE ROUND",.05f,.035f,.95f,.105f,()=>{kitchen.SetActive(false);Refresh();});
            kitchenResult=Box("Kitchen result",kitchen.transform,.035f,.25f,.965f,.76f,ink).gameObject;
            PixelArt.Image(kitchenResult.GetComponent<Image>(),PixelArt.Panel(5),true);
            kitchenSummary=Title("Kitchen summary",kitchenResult.transform,"",27,.06f,.35f,.94f,.95f,Color.white);
            Action("Cook again",kitchenResult.transform,"COOK AGAIN",.08f,.08f,.92f,.27f,StartCooking);
            kitchen.SetActive(false);
            rushPanel=Box("Rush panel",safe,0,0,1,1,ink).gameObject;
            Title("Rush title",rushPanel.transform,"LUNCH RUSH",40,.05f,.85f,.95f,.96f,gold);
            PixelArt.Image(Box("Rush client",rushPanel.transform,.35f,.64f,.65f,.83f,Color.white),PixelArt.Client(3));
            Title("Rush rules",rushPanel.transform,"10 lanes. Finish within "+activitySettings.rushDeadline.ToString("0")+" seconds.\nFaster, denser traffic. Same route every attempt.\nCosts 1 energy on the first jump.\nSeparate from your shift and delivery streak.\n\nBRONZE: finish  |  SILVER: "+activitySettings.rushSilverSeconds.ToString("0")+"s  |  GOLD: "+activitySettings.rushGoldSeconds.ToString("0")+"s\nSilver unlocks the cosmetic RUSH VISOR.",22,.06f,.35f,.94f,.64f,Color.white);
            rushStats=Title("Rush best",rushPanel.transform,"",23,.04f,.24f,.96f,.34f,mint);
            rushWear=Action("Rush visor",rushPanel.transform,"",.08f,.17f,.92f,.23f,()=>{Wallet.EquipHat(8);OpenRush();});
            Action("Start rush",rushPanel.transform,"START RUSH",.08f,.09f,.92f,.16f,StartRush);
            Action("Close rush",rushPanel.transform,"BACK",.08f,.02f,.92f,.075f,()=>rushPanel.SetActive(false));
            rushPanel.SetActive(false);
        }
        private void IngredientIcon(Transform parent,int ingredient)
        {
            // Code-native pixel silhouettes keep the four ingredients readable on small screens.
            Color crust=new Color(.65f,.32f,.1f), bread=new Color(1,.86f,.58f), cheese=new Color(1,.72f,.12f), tomato=new Color(.95f,.22f,.15f), leaf=new Color(.24f,.72f,.27f);
            Color tint=ingredient==0?crust:ingredient==1?cheese:ingredient==2?tomato:leaf;
            var shape=Box("Ingredient icon",parent,.19f,.40f,.81f,.86f,tint).transform;
            Box("Top pixels",shape,.12f,.90f,.88f,1.10f,tint).raycastTarget=false;
            if(ingredient==0)Box("Bread center",shape,.10f,.12f,.90f,.92f,bread).raycastTarget=false;
            if(ingredient==1)
            {
                Box("Cheese hole",shape,.17f,.18f,.32f,.40f,crust).raycastTarget=false;
                Box("Cheese hole",shape,.60f,.58f,.78f,.78f,crust).raycastTarget=false;
            }
            if(ingredient==2)Box("Tomato stem",shape,.4f,.86f,.6f,1.2f,leaf).raycastTarget=false;
            if(ingredient==3)
            {
                Box("Lettuce edge",shape,-.12f,.20f,1.12f,.72f,leaf).raycastTarget=false;
                Box("Leaf vein",shape,.44f,.02f,.56f,.95f,new Color(.7f,.95f,.5f)).raycastTarget=false;
            }
            shape.GetComponent<Image>().raycastTarget=false;
        }
        public void OpenKitchen()
        {
            shop.SetActive(false);rushPanel.SetActive(false);kitchen.SetActive(true);kitchen.transform.SetAsLastSibling();
            Kitchen=null;kitchenResult.SetActive(false);kitchenStart.gameObject.SetActive(true);
            kitchenTimer.text=activitySettings.kitchenSeconds.ToString("0")+" SECONDS";kitchenScore.text="BEST "+Wallet.KitchenBest;
            recipeText.text="Build fresh sandwiches for Coco.";kitchenFeedback.text="Ready to cook?";
            for(int i=0;i<beltItems.Length;i++)beltItems[i].gameObject.SetActive(false);
        }
        public void StartCooking()
        {
            Kitchen=new KitchenRound(activitySettings,Random.Range(1,int.MaxValue));kitchenRewarded=false;
            kitchenResult.SetActive(false);kitchenStart.gameObject.SetActive(false);kitchenFeedback.text="Watch the recipe!";
            for(int i=0;i<beltItems.Length;i++){beltX[i]=i*.27f;consumed[i]=false;}
            UpdateKitchenDisplay();
        }
        public void TapIngredient(int slot)
        {
            if(Kitchen==null || Kitchen.Finished || slot<0 || slot>=beltItems.Length || consumed[slot])return;
            bool good=Kitchen.Tap(beltIngredients[slot]);consumed[slot]=true;
            kitchenFeedback.text=good?"NICE! KEEP COOKING":"WRONG INGREDIENT! START THIS SANDWICH AGAIN";kitchenMessageUntil=Time.unscaledTime+1;
            UpdateKitchenDisplay();
        }
        private void UpdateKitchenDisplay()
        {
            if(Kitchen==null)return;
            kitchenTimer.text=Mathf.CeilToInt(Kitchen.Remaining)+" SECONDS";kitchenScore.text="MADE "+Kitchen.Score;
            recipeText.text="RECIPE\n";
            for(int i=0;i<4;i++)recipeText.text+=(i==Kitchen.Step?"> ":i<Kitchen.Step?"OK ":"")+KitchenRound.Ingredients[Kitchen.Recipe[i]]+(i==1?"\n":i==3?"":"  /  ");
        }
        private void UpdateActivities()
        {
            if(!kitchen || !kitchen.activeSelf || Kitchen==null)return;
            Kitchen.Tick(Time.unscaledDeltaTime);
            for(int i=0;i<beltItems.Length;i++)
            {
                beltX[i]-=Time.unscaledDeltaTime*.27f;
                if(beltX[i]<-.24f){beltX[i]+=beltItems.Length*.27f;consumed[i]=false;}
                beltItems[i].anchorMin=new Vector2(beltX[i],.08f);beltItems[i].anchorMax=new Vector2(beltX[i]+.23f,.92f);
                beltItems[i].gameObject.SetActive(!consumed[i]&&!Kitchen.Finished);
            }
            UpdateKitchenDisplay();
            if(!Kitchen.Finished && Time.unscaledTime>kitchenMessageUntil)kitchenFeedback.text="NEXT: "+KitchenRound.Ingredients[Kitchen.Recipe[Kitchen.Step]];
            if(Kitchen.Finished && !kitchenRewarded)
            {
                kitchenRewarded=true;Wallet.ClaimKitchen(Kitchen);kitchenResult.SetActive(true);
                kitchenSummary.text="KITCHEN CLOSED!\n\nMADE "+Kitchen.Score+"  |  MISTAKES "+Kitchen.Mistakes+"\n+"+Kitchen.FoodReward+" SAVED SANDWICHES\nBEST "+Wallet.KitchenBest+"\n\nEat your reward from the menu.";
            }
        }
        public void OpenRush()
        {
            Wallet.Reload();rushPanel.SetActive(true);rushPanel.transform.SetAsLastSibling();
            rushStats.text=Wallet.RushBadge+"\nPERSONAL BEST: "+(Wallet.RushBestSeconds>0?Wallet.RushBestSeconds.ToString("0.00")+" s":"--");
            rushWear.interactable=Wallet.RushMedal>=2;rushWear.GetComponentInChildren<Text>().text=Wallet.RushMedal<2?"VISOR LOCKED / EARN SILVER":Wallet.EquippedHat==8?"VISOR EQUIPPED":"WEAR RUSH VISOR";
        }
        public void StartRush()
        {
            Wallet.Reload();if(Wallet.Energy==0){rushPanel.SetActive(false);ShowShop();return;}
            CourierGame.NextIsRush=true;SceneManager.LoadScene("CocoCourier");
        }
    }
}
