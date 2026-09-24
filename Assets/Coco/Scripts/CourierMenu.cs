using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CocoCourier
{
    public sealed partial class CourierMenu : MonoBehaviour
    {
        public static bool OpenShopOnArrival, OpenKitchenOnArrival;
        public CourierEconomy Wallet { get; private set; }
        private RectTransform safe, avatar, envelopeFlap;
        private GameObject shop, help, customers;
        private readonly Text[] trustLabels = new Text[8];
        private readonly Button[] hatButtons = new Button[8];
        private Text stats, energy, stock, dailyStatus, claimText, playText, shopStats, feedback;
        private Button claim, play, eat, shopEat, sunrise;
        private readonly Button[] food = new Button[3];
        private readonly Text[] foodText = new Text[3];
        private readonly Image[] stamps = new Image[7], energyBars = new Image[10];
        private readonly RectTransform[] cars = new RectTransform[5];
        private Image bag;
        private Font font;
        private float refreshAt, celebrationUntil;
        private readonly Color ink = new Color(.045f,.09f,.13f), card = new Color(.08f,.16f,.2f);
        private readonly Color mint = new Color(.48f,.94f,.71f), muted = new Color(.64f,.75f,.79f), gold = new Color(1,.72f,.36f);

        private void Awake()
        {
            Screen.orientation = ScreenOrientation.Portrait; Application.targetFrameRate = 60;
            Wallet = new CourierEconomy(); Wallet.ResumeCareer(); font = PixelArt.Font;
            var canvas = new GameObject("Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;canvas.GetComponent<Canvas>().pixelPerfect=true;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(540,960); scaler.matchWidthOrHeight = .5f;
            Box("Background", canvas.transform, 0,0,1,1,ink);
            for(int i=0;i<5;i++)
            {
                float y = .12f + i*.18f;
                Box("Decorative lane", canvas.transform, 0,y,1,y+.08f,new Color(.10f,.15f,.20f));

                cars[i]=Box("Menu traffic",canvas.transform,0,y+.015f,.14f,y+.065f,i%2==0?gold:new Color(.45f,.65f,.87f)).rectTransform;
                PixelArt.Image(cars[i].GetComponent<Image>(),PixelArt.World(i%4));
            }
            safe = Box("Menu Safe Area", canvas.transform,0,0,1,1,Color.clear).rectTransform;
            PixelArt.Image(Box("Game logo",safe,.13f,.89f,.87f,.995f,Color.white),PixelArt.Panel(6));
            Title("Subtitle",safe,"YOUR NEXT ROUTE STARTS HERE",14,.04f,.875f,.96f,.905f,muted);
            var profile=Box("Courier card",safe,.05f,.65f,.95f,.875f,card).transform;
            avatar=Box("Coco portrait",profile,.025f,.38f,.275f,.96f,mint).rectTransform;
            bag=Box("Courier bag",avatar,.62f,-.02f,1.05f,.32f,gold);
            stats=Title("Career stats",profile,"",21,.30f,.46f,.96f,.95f,Color.white,TextAnchor.MiddleLeft);
            energy=Title("Energy",profile,"",18,.03f,.28f,.97f,.46f,mint);
            for(int i=0;i<10;i++) energyBars[i]=Box("Energy bar",profile,.06f+i*.09f,.23f,.13f+i*.09f,.27f,mint);
            stock=Title("Food stock",profile,"",17,.03f,.03f,.66f,.21f,muted,TextAnchor.MiddleLeft);
            eat=Action("Eat sandwich",profile,"EAT",.69f,.035f,.95f,.20f,Eat);
            var daily=Box("Daily dispatch",safe,.05f,.365f,.95f,.635f,card).transform;
            var envelope=Box("Dispatch envelope",daily,.035f,.835f,.13f,.955f,gold).transform;
            envelopeFlap=Box("Envelope flap",envelope,.04f,.48f,.96f,.92f,new Color(.72f,.43f,.20f)).rectTransform;
            Title("Daily title",daily,"DAILY DISPATCH",25,.03f,.80f,.97f,.99f,gold);
            for(int i=0;i<7;i++)
            {
                float x=.035f+i*.135f;
                stamps[i]=Box("Stamp "+(i+1),daily,x,.48f,x+.12f,.78f,ink);
                Title("Stamp reward",stamps[i].transform,(i+1)+"\n+"+CourierEconomy.PackageCoins(i),16,0,0,1,1,Color.white);
            }
            dailyStatus=Title("Daily status",daily,"",15,.01f,.33f,.99f,.47f,muted);
            claim=Action("Claim package",daily,"CLAIM PACKAGE",.07f,.07f,.93f,.31f,Claim);
            claimText=claim.GetComponentInChildren<Text>();
            play=Action("Play",safe,"PLAY",.05f,.255f,.95f,.345f,Play);
            playText=play.GetComponentInChildren<Text>();
            Action("Shop",safe,"SHOP",.05f,.19f,.48f,.25f,ShowShop);
            Action("Kitchen",safe,"KITCHEN",.52f,.19f,.95f,.25f,OpenKitchen);
            Action("Lunch rush",safe,"LUNCH RUSH",.05f,.12f,.95f,.18f,OpenRush);
            Action("How to play",safe,"HELP",.05f,.055f,.46f,.105f,()=>help.SetActive(true));
            Action("Customers",safe,"CLIENTS",.50f,.055f,.95f,.105f,()=>{customers.SetActive(true);Refresh();});
            feedback=Title("Menu message",safe,"Fresh route. Fresh start.",15,.03f,.005f,.97f,.045f,muted);
            BuildShop(); BuildHelp(); BuildCustomers(); BuildActivities();
            if(!FindFirstObjectByType<EventSystem>())
            {
                var events=new GameObject("Menu Input",typeof(EventSystem),typeof(InputSystemUIInputModule)); events.transform.SetParent(transform);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            Refresh();
            if(OpenKitchenOnArrival) { OpenKitchenOnArrival=false; OpenKitchen(); }
            if(OpenShopOnArrival) { OpenShopOnArrival=false; ShowShop(); }
        }
        private Image Box(string name,Transform parent,float x,float y,float right,float top,Color color)
        {
            var r=new GameObject(name,typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>(); r.SetParent(parent,false);
            r.anchorMin=new Vector2(x,y); r.anchorMax=new Vector2(right,top); r.offsetMin=r.offsetMax=Vector2.zero;
            var image=r.GetComponent<Image>(); image.color=color; image.raycastTarget=color.a>0;
            if(name=="Courier card"||name=="APPLE"||name=="SANDWICH"||name=="HOT MEAL")PixelArt.Image(image,PixelArt.Panel(name=="Courier card"?0:1),true);
            else if(name=="Daily dispatch")PixelArt.Image(image,PixelArt.Panel(7),true);
            else if(name.StartsWith("Stamp "))PixelArt.Image(image,PixelArt.Panel(4),true);
            else if(name=="Coco portrait")PixelArt.Image(image,PixelArt.Courier());
            else if(name=="Courier bag")PixelArt.Image(image,PixelArt.World(9));
            else if(name=="Dispatch envelope")PixelArt.Image(image,PixelArt.World(8));
            else if(name=="Envelope flap")image.color=Color.clear;
            else if(name=="Decorative lane"){PixelArt.Image(image,PixelArt.World(12));image.type=Image.Type.Tiled;image.preserveAspect=false;image.pixelsPerUnitMultiplier=.45f;}
            return image;
        }
        private Text Title(string name,Transform parent,string value,int size,float x,float y,float right,float top,Color color,TextAnchor align=TextAnchor.MiddleCenter)
        {
            var r=new GameObject(name,typeof(RectTransform),typeof(Text)).GetComponent<RectTransform>();r.SetParent(parent,false);
            r.anchorMin=new Vector2(x,y);r.anchorMax=new Vector2(right,top);r.offsetMin=new Vector2(5,0);r.offsetMax=new Vector2(-5,0);
            var t=r.GetComponent<Text>();t.font=font;t.text=value;t.fontSize=size;t.color=color;t.alignment=align;t.raycastTarget=false;
            t.resizeTextForBestFit=true;t.resizeTextMinSize=11;t.resizeTextMaxSize=size;return t;
        }
        private Button Action(string name,Transform parent,string text,float x,float y,float right,float top,UnityEngine.Events.UnityAction action)
        {
            var image=Box(name,parent,x,y,right,top,mint);PixelArt.Image(image,PixelArt.Panel(name=="Claim package"?3:2),true);var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;
            var colors=button.colors;colors.disabledColor=new Color(.65f,.65f,.65f);button.colors=colors;
            button.onClick.AddListener(action);Title("Label",image.transform,text,25,0,0,1,1,ink);return button;
        }
        private void BuildShop()
        {
            shop=Box("Shop panel",safe,0,0,1,1,ink).gameObject;
            Title("Shop title",shop.transform,"COCO'S CORNER STORE",31,.04f,.90f,.96f,.98f,mint);
            shopStats=Title("Shop balance",shop.transform,"",20,.04f,.825f,.96f,.90f,Color.white);
            Action("Shop cook",shop.transform,"COOK A MEAL / FREE",.05f,.77f,.95f,.82f,OpenKitchen);
            string[] names={"APPLE","SANDWICH","HOT MEAL"};
            for(int i=0;i<3;i++)
            {
                int index=i;float y=.635f-i*.14f;
                var row=Box(names[i],shop.transform,.05f,y,.95f,y+.125f,card).transform;
                PixelArt.Image(Box("Food icon",row,.02f,.14f,.18f,.86f,Color.white),PixelArt.World(4+i));
                Title("Food name",row,names[i],21,.19f,.53f,.53f,.94f,gold,TextAnchor.MiddleLeft);
                foodText[i]=Title("Food effect",row,"",16,.19f,.08f,.54f,.53f,muted,TextAnchor.MiddleLeft);
                food[i]=Action("Buy "+names[i],row,"",.56f,.16f,.96f,.84f,()=>Buy((CourierEconomy.Food)index));
            }
            shopEat=Action("Shop eat",shop.transform,"",.05f,.275f,.95f,.335f,Eat);
            Title("Bags title",shop.transform,"BAGS  /  DAY 7 UNLOCKS SUNRISE",16,.04f,.225f,.96f,.27f,muted);
            Action("Basic bag",shop.transform,"CLASSIC",.05f,.155f,.48f,.215f,()=>{Wallet.EquipBag(false);Refresh();});
            sunrise=Action("Sunrise bag",shop.transform,"SUNRISE",.52f,.155f,.95f,.215f,()=>{Wallet.EquipBag(true);Refresh();});
            Action("Close shop",shop.transform,"BACK TO MENU",.05f,.045f,.95f,.12f,()=>{shop.SetActive(false);Refresh();});
            shop.SetActive(false);
        }
        private void BuildCustomers()
        {
            customers=Box("Customers panel",safe,0,0,1,1,ink).gameObject;
            Title("Customers title",customers.transform,"CLIENTS & REPUTATION",30,.04f,.91f,.96f,.98f,mint);
            Title("Trust help",customers.transform,"3 deliveries per client unlock a cosmetic hat",17,.04f,.86f,.96f,.91f,Color.white);
            for(int i=0;i<8;i++)
            {
                int index=i;float y=.77f-i*.087f;
                var row=Box("Client row "+i,customers.transform,.04f,y,.96f,y+.08f,card).transform;
                PixelArt.Image(Box("Portrait",row,.01f,.02f,.14f,.98f,Color.white),PixelArt.Client(i));
                trustLabels[i]=Title("Trust",row,"",17,.15f,0,.65f,1,Color.white,TextAnchor.MiddleLeft);
                hatButtons[i]=Action("Hat "+i,row,"",.67f,.1f,.99f,.9f,()=>{Wallet.EquipHat(index);Refresh();});
            }
            Action("Remove hat",customers.transform,"NO HAT",.05f,.08f,.46f,.14f,()=>{Wallet.EquipHat(-1);Refresh();});
            Action("Close customers",customers.transform,"BACK",.51f,.08f,.95f,.14f,()=>customers.SetActive(false));
            customers.SetActive(false);
        }
        private void BuildHelp()
        {
            help=Box("Help panel",safe,0,0,1,1,ink).gameObject;
            Title("Help title",help.transform,"HOW TO PLAY",36,.04f,.81f,.96f,.94f,mint);
            Title("Instructions",help.transform,"Tap NEXT or press SPACE to cross one lane.\nCars can hit you during a jump.\n\nTraffic gets faster near the client.\nVans are long; scooters are fast.\n\nRead each client's contract before starting.\nA safe close call earns a tip on delivery.\nFragile orders need extra distance.\n\n3 orders make a shift. 3 wins earn a bonus.\nCrashes or leaving a paid route break a streak.\n3 deliveries per client unlock a hat.\n\nFirst jump: 1 energy. Food restores energy.\nDaily packages include coins and food.",24,.06f,.21f,.94f,.79f,Color.white);
            Action("Close help",help.transform,"GOT IT",.08f,.075f,.92f,.16f,()=>help.SetActive(false));help.SetActive(false);
        }
        public void ShowShop() { shop.SetActive(true);Refresh(); }
        public void Play()
        {
            Wallet.Reload();if(Wallet.Energy==0) { ShowShop();feedback.text="COCO IS HUNGRY";return; }
            CourierGame.NextIsRush = false; SceneManager.LoadScene("CocoCourier");
        }
        private void Buy(CourierEconomy.Food item)
        {
            if(Wallet.TryBuyFood(item)) { feedback.text="Meal enjoyed. Ready for the road!";celebrationUntil=Time.unscaledTime+1; }
            Refresh();
        }
        private void Eat() { if(Wallet.TryEatSandwich()) {feedback.text="One sandwich. Fresh energy.";celebrationUntil=Time.unscaledTime+1;} Refresh(); }
        private void Claim()
        {
            if(Wallet.TryClaimDaily(out int coins)) { feedback.text="PACKAGE OPENED  +"+coins+" COINS  +1 SANDWICH";celebrationUntil=Time.unscaledTime+1.5f; }
            Refresh();
        }
        public void Refresh()
        {
            Wallet.Reload();int value=Wallet.Energy;
            stats.text="COINS  "+Wallet.Balance+"\nCAREER RATING  "+(CourierRating.Count==0?"—":CourierRating.Average.ToString("0.0",CultureInfo.InvariantCulture)+" / 10")+"\nSHIFT "+(Wallet.ShiftStep+1)+"/3   STREAK "+Wallet.Streak;
            energy.text="ENERGY  "+value+" / 10"+(value<10?"   +1 IN "+Countdown(Wallet.NextEnergySeconds):"   FULL");
            for(int i=0;i<10;i++) energyBars[i].color=i<value?mint:ink;
            stock.text="SANDWICHES  "+Wallet.Sandwiches;eat.interactable=Wallet.Sandwiches>0&&value<10;
            playText.text=value>0?"PLAY  /  1 ENERGY":"COCO IS HUNGRY  /  GET FOOD";
            claim.interactable=Wallet.CanClaimDaily;claimText.text=Wallet.CanClaimDaily?"CLAIM PACKAGE":"PACKAGE RECEIVED";
            dailyStatus.text=Wallet.CanClaimDaily?(Wallet.HasSunriseBag ? "Coins + a sandwich each day  |  Day 7: 150 coins" : "Coins + a sandwich each day  |  Day 7: Sunrise bag"):"NEXT PACKAGE IN "+Countdown(Wallet.NextPackageSeconds);
            int completed = !Wallet.CanClaimDaily && Wallet.PackageStep == 0 ? 7 : Wallet.PackageStep;
            for(int i=0;i<7;i++) stamps[i].color=i<completed?new Color(.2f,.44f,.36f):i==Wallet.PackageStep&&Wallet.CanClaimDaily?new Color(.45f,.31f,.16f):ink;
            shopStats.text="COINS  "+Wallet.Balance+"   /   ENERGY  "+value+" / 10\n"+(value<10?"Next energy in "+Countdown(Wallet.NextEnergySeconds):"Fully fed. Ready to deliver.");
            for(int i=0;i<3;i++)
            {
                var item=(CourierEconomy.Food)i;int price=CourierEconomy.FoodPrice(item);
                foodText[i].text="Restore +"+Math.Min(10-value,CourierEconomy.FoodEnergy(item))+" energy";
                food[i].interactable=value<10&&Wallet.Balance>=price;
                food[i].GetComponentInChildren<Text>().text=value==10?"FULL ENERGY":Wallet.Balance<price?price+" COINS\nNOT ENOUGH":price+" COINS";
            }
            shopEat.interactable=eat.interactable;shopEat.GetComponentInChildren<Text>().text="EAT SAVED SANDWICH  ("+Wallet.Sandwiches+")";
            sunrise.interactable=Wallet.HasSunriseBag;sunrise.GetComponentInChildren<Text>().text=Wallet.HasSunriseBag?(Wallet.SunriseEquipped?"EQUIPPED":"SUNRISE"):"LOCKED / DAY 7";
            for(int i=0;i<8;i++)
            {
                int trust=Wallet.Reputation(i);
                trustLabels[i].text=PixelArt.ClientNames[i]+"  "+trust+"/3\n"+CourierEconomy.HatNames[i];
                hatButtons[i].interactable=trust>=3;
                hatButtons[i].GetComponentInChildren<Text>().text=trust<3?"LOCKED":Wallet.EquippedHat==i?"EQUIPPED":"WEAR";
            }
            PixelArt.Image(bag,PixelArt.World(Wallet.SunriseEquipped?10:9));
            PixelArt.Image(avatar.GetComponent<Image>(),PixelArt.Courier(0,Wallet.SunriseEquipped));
        }
        public static string Countdown(long seconds) { var t=TimeSpan.FromSeconds(Math.Max(0,seconds));return t.TotalHours>=1?((int)t.TotalHours).ToString("00")+":"+t.Minutes.ToString("00")+":"+t.Seconds.ToString("00"):t.Minutes.ToString("00")+":"+t.Seconds.ToString("00"); }
        private void Update()
        {
            UpdateActivities();
            var area=Screen.safeArea;safe.anchorMin=new Vector2(area.xMin/Screen.width,area.yMin/Screen.height);safe.anchorMax=new Vector2(area.xMax/Screen.width,area.yMax/Screen.height);
            for(int i=0;i<cars.Length;i++) {float x=Mathf.Repeat(Time.unscaledTime*(i%2==0?.07f:-.09f)+i*.23f,1.3f)-.15f;var a=cars[i].anchorMin;var b=cars[i].anchorMax;a.x=x;b.x=x+.14f;cars[i].anchorMin=a;cars[i].anchorMax=b;}
            avatar.localRotation=Quaternion.Euler(0,0,Time.unscaledTime<celebrationUntil?Mathf.Sin(Time.unscaledTime*16)*12:0);
            envelopeFlap.localRotation=Quaternion.Euler(Time.unscaledTime<celebrationUntil?Mathf.Sin((celebrationUntil-Time.unscaledTime)*Mathf.PI/1.5f)*80:0,0,0);
            if(Time.unscaledTime>=refreshAt) {refreshAt=Time.unscaledTime+1;Refresh();}
            if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame) {shop.SetActive(false);help.SetActive(false);customers.SetActive(false);kitchen.SetActive(false);rushPanel.SetActive(false);}
        }
    }
}



