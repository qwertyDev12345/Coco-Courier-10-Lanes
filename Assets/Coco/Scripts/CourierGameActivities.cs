using UnityEngine;
using UnityEngine.UI;
namespace CocoCourier
{
    public sealed partial class CourierGame
    {
        private GameObject throwPanel;
        private Transform lostParcel;
        private Button pickup, hungryCook, throwButton;
        private Text throwMessage;
        private RectTransform throwPointer, flyingParcel;
        private float throwAnimation;
        private OrderReward lastReward;
        public LastMeterRound LastMeter { get; private set; }
        public bool LastMeterActive => throwPanel && throwPanel.activeSelf;
        private void BuildGameActivities()
        {
            lostParcel=Block("Lost parcel",Vector2.zero,Vector2.one,Color.white,7);
            PixelArt.WorldSprite(lostParcel,PixelArt.World(11),.55f,.55f);
            pickup=Button("Pick up parcel",safeArea,"",new Vector2(.10f,.235f),new Vector2(.90f,.30f),PickUpParcel);
            pickup.GetComponentInChildren<Text>().resizeTextMaxSize=19;
            pickup.GetComponentInChildren<Text>().text="PICK UP +"+settings.lostParcelCoins+" / JUMPS +"+Mathf.RoundToInt((settings.lostParcelJumpMultiplier-1)*100)+"%";
            hungryCook=Button("Hungry cook",result.transform,"COOK A MEAL / FREE",new Vector2(.1f,.19f),new Vector2(.9f,.255f),()=>{CourierMenu.OpenKitchenOnArrival=true;MainMenu();});
            hungryCook.gameObject.SetActive(false);
            throwPanel=Panel("Last meter panel",safeArea,Vector2.zero,Vector2.one,ink).gameObject;
            PixelArt.Image(throwPanel.GetComponent<Image>(),PixelArt.Panel(5),true);
            Label("Last meter title",throwPanel.transform,"THE LAST METER",36,new Vector2(.05f,.83f),new Vector2(.95f,.94f),mint,TextAnchor.MiddleCenter);
            Label("Last meter rules",throwPanel.transform,"Stop the marker in green for +"+settings.lastMeterTip+" coins.\nYour delivery is already complete.\nMissing never takes your reward away.",23,new Vector2(.05f,.66f),new Vector2(.95f,.82f),Color.white,TextAnchor.MiddleCenter);
            var portrait=Panel("Throw client",throwPanel.transform,new Vector2(.7f,.43f),new Vector2(.92f,.63f),Color.white);
            PixelArt.Image(portrait,PixelArt.Client(0));
            flyingParcel=Panel("Flying parcel",throwPanel.transform,new Vector2(.09f,.45f),new Vector2(.22f,.54f),Color.white).rectTransform;
            PixelArt.Image(flyingParcel.GetComponent<Image>(),PixelArt.World(11));
            var bar=Panel("Throw meter",throwPanel.transform,new Vector2(.1f,.34f),new Vector2(.9f,.40f),new Color(.35f,.16f,.15f));
            Panel("Green zone",bar.transform,new Vector2(.5f-settings.lastMeterGreenWidth/2,0),new Vector2(.5f+settings.lastMeterGreenWidth/2,1),mint);
            throwPointer=Panel("Throw pointer",bar.transform,new Vector2(0,-.2f),new Vector2(.018f,1.2f),Color.white).rectTransform;
            throwMessage=Label("Throw feedback",throwPanel.transform,"TAP THROW OR PRESS SPACE",22,new Vector2(.05f,.25f),new Vector2(.95f,.33f),Color.white,TextAnchor.MiddleCenter);
            throwButton=Button("Throw",throwPanel.transform,"THROW",new Vector2(.1f,.13f),new Vector2(.9f,.23f),StopThrow);
            Button("Skip throw",throwPanel.transform,"SKIP / KEEP DELIVERY",new Vector2(.1f,.04f),new Vector2(.9f,.11f),SkipThrow);
            throwPanel.SetActive(false);
        }
        private void ResetGameActivities()
        { throwPanel.SetActive(false);LastMeter=null;hungryCook.gameObject.SetActive(false);pickup.gameObject.SetActive(false); }
        public void PickUpParcel() { if(Run.PickUpParcel())UpdateGameActivities(); }
        private void BeginThrow()
        {
            LastMeter=new LastMeterRound(settings);throwAnimation=0;throwPanel.SetActive(true);throwPanel.transform.SetAsLastSibling();
            PixelArt.Image(throwPanel.transform.Find("Throw client").GetComponent<Image>(),PixelArt.Client(ClientIndex));
            throwButton.interactable=true;throwMessage.text="TAP THROW OR PRESS SPACE";
            flyingParcel.anchorMin=new Vector2(.09f,.45f);flyingParcel.anchorMax=new Vector2(.22f,.54f);
        }
        public void SkipThrow()
        {
            if(!LastMeterActive)return;
            if(LastMeter.Stop(true))ApplyThrowReward();
            throwPanel.SetActive(false);
        }
        public void StopThrow()
        {
            if(!LastMeterActive || !LastMeter.Stop())return;
            ApplyThrowReward();throwButton.interactable=false;
            throwMessage.text=LastMeter.Hit?"PERFECT CATCH! +"+settings.lastMeterTip+" COINS":"MISSED! DELIVERY STILL COMPLETE";
        }
        private void ApplyThrowReward()
        {
            int tip=Wallet.ClaimThrow(Run,LastMeter,settings.lastMeterTip);
            resultReward.text=resultReward.text.Replace("TOTAL +"+lastReward.Total+" COINS","TOTAL +"+(lastReward.Total+tip)+" COINS");
            if(lastReward.ShiftCompleted)resultReward.text=resultReward.text.Replace("| "+lastReward.ShiftEarned+" coins","| "+(lastReward.ShiftEarned+tip)+" coins");
            resultReward.text+="\nLAST METER +"+tip;resultBalance.text="BALANCE   "+Wallet.Balance+" COINS";
        }
        private void UpdateGameActivities()
        {
            pickup.gameObject.SetActive(!awaitingStart && Run.CanPickUpParcel);
            lostParcel.gameObject.SetActive(!IsRush && (Run.HasLostParcel || Run.Progress<=Run.LostParcelIsland));
            lostParcel.position=Run.HasLostParcel?courier.position+new Vector3(-.42f,.12f,0):new Vector3(-.9f,Run.LostParcelIsland*CourierRun.IslandSpacing+.16f,0);
            hungryCook.gameObject.SetActive(hungry);
            if(hungry)resultAverage.text="";
            if(!LastMeterActive)return;
            LastMeter.Tick(Time.unscaledDeltaTime);
            float marker=LastMeter.Position;throwPointer.anchorMin=new Vector2(marker-.009f,-.2f);throwPointer.anchorMax=new Vector2(marker+.009f,1.2f);
            if(LastMeter.Resolved)
            {
                throwAnimation+=Time.unscaledDeltaTime;float t=Mathf.Clamp01(throwAnimation/.7f);
                float x=Mathf.Lerp(.09f,.73f,t), y=.45f+Mathf.Sin(t*Mathf.PI)*.15f-(LastMeter.Hit?0:t*.10f);
                flyingParcel.anchorMin=new Vector2(x,y);flyingParcel.anchorMax=new Vector2(x+.13f,y+.09f);
                if(throwAnimation>1.15f)throwPanel.SetActive(false);
            }
        }
    }
}
