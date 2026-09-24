using UnityEngine.SceneManagement;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CocoCourier
{
    public sealed partial class CourierGame : MonoBehaviour
    {
        public static bool NextIsRush;
        public bool IsRush { get; private set; }
        public CourierSettings settings;
        public CourierRun Run { get; private set; }
        public CourierEconomy Wallet { get; private set; }
        private Camera view;
        private Sprite square;
        private Transform courier, shadow, client, hat;
        private GameObject briefing;
        private Text briefTitle, briefBody;
        private bool awaitingStart;
        private Image clientPortrait, resultPortrait;
        private Text clientLabel, trafficLabel;
        public int ClientIndex { get; private set; }
        private readonly Dictionary<CourierRun.Car, Transform> carViews = new Dictionary<CourierRun.Car, Transform>();
        private readonly List<CourierRun.Car> removed = new List<CourierRun.Car>();
        private RectTransform safeArea;
        private Text timer, progress, average, hint, resultTitle, resultBody, jumpLabel, balance, resultReward, resultBalance, resultAverage;
        private Button jumpButton;
        private GameObject result;
        private Image[] milestones;
        private bool finished, energyPaid, hungry;
        private Font font;
        private readonly Color mint = new Color(0.48f, 0.94f, 0.71f);
        private readonly Color ink = new Color(0.055f, 0.11f, 0.15f);

        private void Awake()
        {
            if (!settings) settings = Resources.Load<CourierSettings>("CourierSettings");
            if (!settings) settings = ScriptableObject.CreateInstance<CourierSettings>();
            IsRush=NextIsRush;NextIsRush=false;
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            font = PixelArt.Font;
            square = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
            view = Camera.main;
            if (!view) view = new GameObject("Game Camera", typeof(Camera)).GetComponent<Camera>();
            view.orthographic = true; view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = ink; view.transform.rotation = Quaternion.identity;
            Wallet = new CourierEconomy(); Wallet.ResumeCareer();
            BuildRoad(); BuildUI(); Restart();
        }
        private Transform Block(string label, Vector2 position, Vector2 size, Color color, int order = 0, Transform parent = null)
        {
            var obj = new GameObject(label, typeof(SpriteRenderer));
            obj.transform.SetParent(parent ? parent : transform, false);
            obj.transform.localPosition = position; obj.transform.localScale = size;
            var renderer = obj.GetComponent<SpriteRenderer>(); renderer.sprite = square; renderer.color = color; renderer.sortingOrder = order;
            return obj.transform;
        }
        private void BuildRoad()
        {
            for(int i=0;i<=CourierRun.LaneCount;i++)
            {
                float y=i*CourierRun.IslandSpacing;
                var island=Block("Safe island "+i,new Vector2(0,y),new Vector2(40,.95f),Color.white);
                PixelArt.Tile(island,PixelArt.World(13),new Vector2(40,.95f));
                Block("Landing pad "+i,new Vector2(0,y),new Vector2(.65f,.20f),new Color(.95f,.86f,.58f),1);
                if(i==CourierRun.LaneCount)continue;
                var lane=Block("Lane "+(i+1),new Vector2(0,y+CourierRun.IslandSpacing/2),new Vector2(40,CourierRun.RoadWidth),Color.white);
                PixelArt.Tile(lane,PixelArt.World(12),new Vector2(40,CourierRun.RoadWidth));
                for(int side=-1;side<=1;side+=2)
                {
                    var tree=Block("Street planter",new Vector2(side*4.1f,y+.3f),Vector2.one,Color.white,2);
                    PixelArt.WorldSprite(tree,PixelArt.World(15),.75f,.9f);
                }
            }
            shadow=Block("Ground collision marker",Vector2.zero,new Vector2(.48f,.16f),new Color(.03f,.08f,.12f,.75f),3);
            courier=Block("Coco",Vector2.zero,Vector2.one,Color.white,6);
            PixelArt.WorldSprite(courier,PixelArt.Courier(),1.08f,1.3f);
            hat = CourierCosmetics.CreateWorld(transform);
            client=Block("Client",new Vector2(1.1f,24.55f),Vector2.one,Color.white,4);
            PixelArt.WorldSprite(client,PixelArt.Client(0),1.15f,1.5f);
            var parcel=Block("Destination",new Vector2(-1.05f,24.25f),Vector2.one,Color.white,3);
            PixelArt.WorldSprite(parcel,PixelArt.World(11),.6f,.6f);
        }
        private RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false);
            r.anchorMin = min; r.anchorMax = max; r.offsetMin = offsetMin; r.offsetMax = offsetMax; return r;
        }
        private Image Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color)
        {
            var r = Rect(name, parent, min, max, Vector2.zero, Vector2.zero); var image = r.gameObject.AddComponent<Image>(); image.color = color;
            if(name=="Header"||name=="Controls"||name=="Result overlay") PixelArt.Image(image,PixelArt.Panel(name=="Result overlay"?5:0),true);
            return image;
        }
        private Text Label(string name, Transform parent, string value, int size, Vector2 min, Vector2 max, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var r = Rect(name, parent, min, max, new Vector2(16, 0), new Vector2(-16, 0));
            var text = r.gameObject.AddComponent<Text>(); text.font = font; text.text = value; text.fontSize = size;
            text.color = color; text.alignment = align; text.resizeTextForBestFit = true; text.resizeTextMinSize = 12; text.resizeTextMaxSize = size;
            text.raycastTarget = false; return text;
        }
        private Button Button(string name, Transform parent, string title, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action)
        {
            var image = Panel(name, parent, min, max, mint); PixelArt.Image(image,PixelArt.Panel(2),true); var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.disabledColor = new Color(0.65f, 0.65f, 0.65f); button.colors = colors;
            button.onClick.AddListener(action); Label("Label", image.transform, title, 30, Vector2.zero, Vector2.one, ink, TextAnchor.MiddleCenter); return button;
        }
        private void BuildUI()
        {
            var canvas = new GameObject("Portrait HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;canvas.GetComponent<Canvas>().pixelPerfect=true;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(540, 960); scaler.matchWidthOrHeight = 0.5f;
            safeArea = Rect("Safe Area", canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var header = Panel("Header", safeArea, new Vector2(0, 0.79f), Vector2.one, ink);
            clientPortrait=Panel("Client portrait",header.transform,new Vector2(.025f,.08f),new Vector2(.18f,.93f),Color.white);
            clientLabel=Label("Client name",header.transform,"",24,new Vector2(.19f,.70f),new Vector2(.97f,.96f),mint);
            timer = Label("Timer", header.transform, "", 29, new Vector2(0.19f, 0.36f), new Vector2(0.67f, 0.70f), Color.white);
            progress = Label("Progress", header.transform, "", 29, new Vector2(0.67f, 0.36f), new Vector2(0.97f, 0.70f), Color.white, TextAnchor.MiddleRight);
            average = Label("Average", header.transform, "", 16, new Vector2(0.19f, 0.18f), new Vector2(0.97f, 0.34f), new Color(0.65f, 0.76f, 0.78f));
            balance = Label("Coin balance", header.transform, "", 18, new Vector2(0.19f, 0.03f), new Vector2(0.97f, 0.18f), mint);
            var footer = Panel("Controls", safeArea, Vector2.zero, new Vector2(1, 0.22f), ink);
            hint = Label("Hint", footer.transform, "", 21, new Vector2(0.02f, 0.68f), new Vector2(0.98f, 1), Color.white, TextAnchor.MiddleCenter);
            jumpButton = Button("Jump", footer.transform, "NEXT", new Vector2(0.07f, 0.22f), new Vector2(0.93f, 0.65f), Jump);
            jumpLabel = jumpButton.GetComponentInChildren<Text>();
            trafficLabel = Label("Keyboard hint", footer.transform, "TAP NEXT OR PRESS SPACE  •  ONE LANE PER JUMP", 15, Vector2.zero, new Vector2(1, 0.22f), new Color(0.65f, 0.76f, 0.78f), TextAnchor.MiddleCenter);
            milestones = new Image[10];
            for (int i = 0; i < 10; i++) milestones[i] = Panel("Progress " + i, safeArea, new Vector2(0.07f + i * 0.087f, 0.775f), new Vector2(0.145f + i * 0.087f, 0.781f), Color.gray);
            result = Panel("Result overlay", safeArea, Vector2.zero, Vector2.one, new Color(0.025f, 0.065f, 0.09f, 0.97f)).gameObject;
            Label("Result eyebrow", result.transform, "COCO COURIER  /  DELIVERY REPORT", 20, new Vector2(0.04f, 0.88f), new Vector2(0.96f, 0.95f), mint, TextAnchor.MiddleCenter);
            resultTitle = Label("Result title", result.transform, "", 46, new Vector2(0.03f, 0.77f), new Vector2(0.97f, 0.87f), Color.white, TextAnchor.MiddleCenter);
            resultPortrait=Panel("Result customer",result.transform,new Vector2(.075f,.625f),new Vector2(.24f,.76f),Color.white);
            resultBody = Label("Result details", result.transform, "", 24, new Vector2(0.25f, 0.62f), new Vector2(0.95f, 0.76f), Color.white, TextAnchor.MiddleCenter);
            resultReward = Label("Reward breakdown", result.transform, "", 27, new Vector2(0.07f, 0.36f), new Vector2(0.93f, 0.6f), mint, TextAnchor.MiddleCenter);
            resultBalance = Label("Result balance", result.transform, "", 30, new Vector2(0.05f, 0.27f), new Vector2(0.95f, 0.35f), Color.white, TextAnchor.MiddleCenter);
            resultAverage = Label("Result average", result.transform, "", 18, new Vector2(0.05f, 0.21f), new Vector2(0.95f, 0.27f), new Color(0.65f, 0.76f, 0.78f), TextAnchor.MiddleCenter);
            Button("Retry", result.transform, "TRY AGAIN", new Vector2(0.1f, 0.09f), new Vector2(0.9f, 0.18f), Retry);
            Button("Main menu", result.transform, "MAIN MENU", new Vector2(0.1f, 0.02f), new Vector2(0.9f, 0.075f), MainMenu);
            briefing = Panel("Order briefing", safeArea, Vector2.zero, Vector2.one, ink).gameObject;
            PixelArt.Image(briefing.GetComponent<Image>(), PixelArt.Panel(5), true);
            briefTitle = Label("Order title", briefing.transform, "", 32, new Vector2(.06f,.71f), new Vector2(.94f,.92f), mint, TextAnchor.MiddleCenter);
            briefBody = Label("Order conditions", briefing.transform, "", 23, new Vector2(.07f,.25f), new Vector2(.93f,.70f), Color.white, TextAnchor.MiddleCenter);
            Button("Accept order", briefing.transform, "ACCEPT ORDER", new Vector2(.1f,.13f), new Vector2(.9f,.22f), AcceptOrder);
            Button("Briefing menu", briefing.transform, "MAIN MENU", new Vector2(.1f,.04f), new Vector2(.9f,.10f), MainMenu);
            BuildGameActivities();
            if (!FindFirstObjectByType<EventSystem>())
            {
                var events = new GameObject("UI Input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
        }
        public void MainMenu() { Wallet.ResumeCareer(); SceneManager.LoadScene("MainMenu"); }
        public void AcceptOrder() { awaitingStart = false; briefing.SetActive(false); EventSystem.current?.SetSelectedGameObject(null); }
        private void Retry()
        {
            Wallet.Reload();
            if (Wallet.Energy == 0) { CourierMenu.OpenShopOnArrival = true; MainMenu(); }
            else Restart();
        }
        public void Jump()
        {
            if (awaitingStart || hungry || Run.Jumping || Run.State != CourierRun.RunState.Playing) return;
            if (!energyPaid)
            {
                if (!Wallet.TryStartOrder()) { ShowHungry(); return; }
                energyPaid = true; Wallet.BeginRoute(Run);
            }
            Run.Jump();
        }
        private void ShowHungry()
        {
            hungry = true; result.SetActive(true); resultTitle.text = "COCO IS HUNGRY";
            resultBody.text = "Eat a meal to start your next route.";
            resultReward.text = "Recovery: +1 energy every 10 minutes\nNext energy in " + CourierMenu.Countdown(Wallet.NextEnergySeconds);
            resultBalance.text = "BALANCE   " + Wallet.Balance + " COINS"; resultAverage.text = AverageText();
            result.transform.Find("Retry").GetComponentInChildren<Text>().text = "GET FOOD";
            UpdateGameActivities();
        }
        public void Restart()
        {
            foreach (var item in carViews.Values) Destroy(item.gameObject); carViews.Clear();
            ClientIndex=IsRush?3:CourierRating.Count%PixelArt.ClientNames.Length;
            var contract = new DeliveryContract(ClientIndex, IsRush?2:Wallet.ShiftStep, settings, IsRush);
            Run = new CourierRun(settings, IsRush?2409:UnityEngine.Random.Range(1, int.MaxValue), true, contract); finished = false; energyPaid = false; hungry = false; result.SetActive(false);
            result.transform.Find("Retry").GetComponentInChildren<Text>().text = "TRY AGAIN";
            ClientIndex=IsRush?3:CourierRating.Count%PixelArt.ClientNames.Length;
            PixelArt.WorldSprite(client,PixelArt.Client(ClientIndex),1.15f,1.5f);
            PixelArt.Image(clientPortrait,PixelArt.Client(ClientIndex));PixelArt.Image(resultPortrait,PixelArt.Client(ClientIndex));
            clientLabel.text="TO: "+PixelArt.ClientNames[ClientIndex];
            ResetGameActivities();
            awaitingStart = true; briefing.SetActive(true);
            briefTitle.text = "SHIFT " + (contract.ShiftStep + 1) + "/3  /  " + contract.Stage + "\n" + contract.Title;
            briefBody.text = "CLIENT: " + PixelArt.ClientNames[ClientIndex] + "\n\n" + contract.Condition +
                "\nCONTRACT BONUS  +" + settings.contractCoins * (contract.ShiftStep == 2 ? 2 : 1) + " coins" +
                "\n\nCars get faster toward the client.\nVans: long & slow. Scooters: small & fast.\n\nCLOSE CALL: +" + settings.closeCallCoins + " coins per lane\n3 deliveries in a row: +" + settings.streakCoins +
                "\nPerfect 3-order shift: +" + settings.shiftCoins + "\n\n1 energy on your first jump";
            if(IsRush)
            {
                briefTitle.text="LUNCH RUSH / RECORD CHALLENGE";
                briefBody.text="Deliver across 10 lanes within "+settings.rushDeadline.ToString("0")+" seconds.\n\nDense traffic. Faster vehicles.\nThe same road every attempt.\n\nSILVER: "+settings.rushSilverSeconds.ToString("0")+"s  |  GOLD: "+settings.rushGoldSeconds.ToString("0")+"s\nSilver unlocks the RUSH VISOR.\n\n1 energy. Normal delivery coins.\nYour career streak and shift are unaffected.";
            }
            EventSystem.current?.SetSelectedGameObject(null);
            UpdatePresentation();
        }
        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) { if(LastMeterActive) StopThrow(); else Jump(); }
            if (!hungry && !awaitingStart) Run.Tick(Time.deltaTime);
            if (hungry) resultReward.text = "Recovery: +1 energy every 10 minutes\nNext energy in " + CourierMenu.Countdown(Wallet.NextEnergySeconds);
            if (hungry && Wallet.Energy > 0) { hungry = false; result.SetActive(false); }
            if (!finished && Run.State != CourierRun.RunState.Playing)
            {
                finished = true;
                bool delivered = Run.State == CourierRun.RunState.Delivered;
                bool paid = Wallet.TryReward(Run, out var reward);
                lastReward=reward;
                if (paid && !IsRush) CourierRating.Record(Run.Rating);
                string shiftReport = delivered ? (reward.ShiftCompleted ? "SHIFT COMPLETE  " + reward.ShiftDelivered + "/3  |  " + reward.ShiftEarned + " coins" : "STREAK " + Wallet.Streak + "  |  SHIFT " + (Wallet.ShiftStep + 1) + "/3 NEXT") : Wallet.RecordFailure(Run);
                if(IsRush)shiftReport=delivered?Wallet.RushBadge+"  |  BEST "+Wallet.RushBestSeconds.ToString("0.00")+"s":"RUSH OVER / TRY AGAIN";
                resultReward.text = delivered
                    ? "Delivery +" + reward.BaseCoins + "   Rating +" + reward.RatingBonus +
                      "\nClose calls +" + reward.Tips + "   Contract +" + reward.ContractBonus +
                      "\nLost parcel +" + reward.LostParcelBonus + "   Streak +" + reward.StreakBonus + "   Shift +" + reward.ShiftBonus +
                      "\nTOTAL +" + reward.Total + " COINS\n" + shiftReport +
                      (IsRush ? "\n"+(Wallet.RushMedal>=2?"RUSH VISOR UNLOCKED":"SILVER UNLOCKS RUSH VISOR") : "\nCLIENT TRUST " + Wallet.Reputation(ClientIndex) + "/3" + (Wallet.Reputation(ClientIndex) == 3 ? "  HAT UNLOCKED!" : ""))
                    : "ORDER NOT COMPLETED\nTOTAL EARNED   0 COINS\n" + shiftReport;
                resultBalance.text = "BALANCE   " + Wallet.Balance + " COINS";
                resultAverage.text = AverageText();
                result.transform.Find("Retry").GetComponentInChildren<Text>().text = Wallet.Energy == 0 ? "GET FOOD" : delivered ? "NEXT ORDER" : "TRY AGAIN";
                result.SetActive(true); resultTitle.text = delivered ? "ORDER DELIVERED!" : Run.State==CourierRun.RunState.TimedOut?"TIME IS UP!":"TRAFFIC COLLISION";
                resultBody.text = (delivered ? "CUSTOMER RATING\n" + Run.Rating + " / 10" : "Take a breath. Watch the next gap.\nNo rating for this attempt.")
                    + "\n\nTime   " + Run.Elapsed.ToString("0.0", CultureInfo.InvariantCulture) + " s    •    " + Run.Progress + " / 10 lanes";
                if(delivered) BeginThrow();
            }
            UpdatePresentation();
            UpdateGameActivities();
        }
        private string AverageText() => CourierRating.Count == 0 ? "CAREER RATING   —   No deliveries yet" : "CAREER RATING   " + CourierRating.Average.ToString("0.0", CultureInfo.InvariantCulture) + " / 10   •   " + CourierRating.Count + (CourierRating.Count == 1 ? " delivery" : " deliveries");
        private void UpdatePresentation()
        {
            var area = Screen.safeArea;
            safeArea.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            // Fixed horizontal visibility ensures traffic has readable approach time on narrow phones.
            view.orthographicSize = Mathf.Max(4.5f, 4.8f / view.aspect);
            view.transform.position = new Vector3(0, Run.GroundY + view.orthographicSize * 0.27f, -10);
            shadow.position = new Vector3(0, Run.GroundY, 0);
                        int pose=Run.State==CourierRun.RunState.Delivered?3:Run.Jumping?1:0;
            PixelArt.WorldSprite(courier,PixelArt.Courier(pose,Wallet.SunriseEquipped),1.08f,1.3f);
            courier.position = new Vector3(0, Run.GroundY + (Run.Jumping ? Mathf.Sin(Run.JumpFraction * Mathf.PI) * settings.arcHeight : 0) + .55f, 0);
            courier.rotation = Quaternion.identity;
            CourierCosmetics.UpdateWorld(hat, Wallet.EquippedHat, courier.position + new Vector3(0,.50f,0));
            removed.Clear(); foreach (var item in carViews) if (!Run.Cars.Contains(item.Key)) removed.Add(item.Key);
            foreach (var car in removed) { Destroy(carViews[car].gameObject); carViews.Remove(car); }
            foreach (var car in Run.Cars)
            {
                if (!carViews.TryGetValue(car, out var body))
                {
                    body=Block("Car on lane "+(car.lane+1),Vector2.zero,Vector2.one,Color.white,4);carViews.Add(car,body);
                    if (car.kind == CourierRun.VehicleKind.Scooter) PixelArt.WorldSprite(body, PixelArt.Scooter, car.HalfWidth*2, .9f);
                    else
                    {
                        var sprite = PixelArt.World(car.kind == CourierRun.VehicleKind.Van ? 2 : 0);
                        body.GetComponent<SpriteRenderer>().sprite = sprite;
                        body.localScale = new Vector3(car.HalfWidth*2/sprite.bounds.size.x,car.HalfHeight*2/sprite.bounds.size.y,1);
                    }
                    body.GetComponent<SpriteRenderer>().flipX=car.speed<0;
                }
                body.position = new Vector3(car.x, (car.lane + 0.5f) * CourierRun.IslandSpacing, 0);
            }
            timer.text = "TIME  " + Run.Elapsed.ToString("0.0", CultureInfo.InvariantCulture) + " s"; progress.text = Run.Progress + " / 10";
            average.text = IsRush ? "LUNCH RUSH  |  "+Mathf.Max(0,settings.rushDeadline-Run.Elapsed).ToString("0.0")+"s LEFT" : "SHIFT " + (Run.Contract.ShiftStep+1) + "/3  |  STREAK " + Wallet.Streak + "  |  " + Run.Contract.Title; balance.text = "COINS   " + Wallet.Balance + "     ENERGY   " + Wallet.Energy + " / 10"; jumpButton.interactable = !awaitingStart && !hungry && !Run.Jumping && Run.State == CourierRun.RunState.Playing;
            int nextLane = Mathf.Min(Run.Progress, 9);
            string kind = nextLane % 3 == 0 ? "CAR" : nextLane % 3 == 1 ? "LONG VAN" : "FAST SCOOTER";
            trafficLabel.text = "NEXT: " + kind + (nextLane % 2 == 0 ? "  >>>" : "  <<<") + "   |   TAP NEXT / SPACE";
            jumpLabel.text = Run.Jumping ? "JUMPING..." : "NEXT";
            hint.text = Run.Elapsed - Run.LastCloseCallAt < 1.25f ? "CLOSE CALL!  +" + settings.closeCallCoins + " tip on delivery" :
                IsRush ? "LUNCH RUSH  "+Mathf.Max(0,settings.rushDeadline-Run.Elapsed).ToString("0.0")+"s LEFT" :
                Run.Contract.Kind == OrderKind.Express ? "HOT LUNCH  " + Mathf.Max(0,Run.Contract.Deadline-Run.Elapsed).ToString("0.0") + "s  |  TIPS " + Run.Tips :
                Run.Contract.Kind == OrderKind.Fragile ? (Run.CloseCalls == 0 ? "FRAGILE: KEEP YOUR DISTANCE" : "FRAGILE BONUS LOST  |  FINISH THE DELIVERY") : "HEAVY PARCEL  |  LONGER JUMPS";
            for (int i = 0; i < 10; i++) milestones[i].color = i < Run.Progress ? mint : new Color(0.25f, 0.34f, 0.37f);
        }
        private void OnDestroy() { if (square) Destroy(square); }
    }
}





