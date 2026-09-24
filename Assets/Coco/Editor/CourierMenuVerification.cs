using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CocoCourier.Editor
{
    public static class CourierMenuVerification
    {
        private static int count, stage;
        private static double deadline;
        private static void Check(bool ok,string label) { if(!ok)throw new Exception(label);count++;Debug.Log("MENU PASS: "+label); }
        public static void RunBatch()
        {
            try
            {
                SessionState.SetBool("MenuBackupExists",PlayerPrefs.HasKey(CourierEconomy.SaveKey));
                SessionState.SetString("MenuBackup",PlayerPrefs.GetString(CourierEconomy.SaveKey,""));
                SessionState.SetInt("MenuCount",CourierRating.Count);
                SessionState.SetInt("MenuSum",PlayerPrefs.GetInt("CocoCourier.RatingSum.v1",0));
                SessionState.SetBool("MenuBackedUp",true);
                VerifyServices();
                CourierFeatureVerification.Verify();
                ActivityVerification.Verify();
                File.WriteAllText("Logs/CocoMenuVerification.txt",count+" economy/energy/daily checks passed.\n");
                PlayerPrefs.DeleteKey(CourierEconomy.SaveKey);PlayerPrefs.DeleteKey("CocoCourier.DeliveryCount.v1");PlayerPrefs.DeleteKey("CocoCourier.RatingSum.v1");PlayerPrefs.Save();
                CourierProjectSetup.BuildMenu();
                SessionState.SetBool("MenuVerify",true);deadline=EditorApplication.timeSinceStartup+120;
                EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
            }
            catch(Exception e) {Fail(e);}
        }
        private static void VerifyServices()
        {
            PlayerPrefs.SetString(CourierEconomy.SaveKey,"{\"version\":1,\"balance\":200,\"totalEarned\":300,\"ownedItemIds\":[\"old_item\"]}");
            var now=new DateTimeOffset(2026,9,24,12,0,0,TimeSpan.Zero);
            var wallet=new CourierEconomy(()=>now);
            Check(wallet.Balance==200&&wallet.TotalEarned==300&&wallet.Energy==10,"Old wallet migrates without losing coins");
            Check(!wallet.TryBuyFood(CourierEconomy.Food.Apple)&&wallet.Balance==200,"Full-energy purchase rejected without charge");
            for(int i=0;i<10;i++)Check(wallet.TryStartOrder(),"Order spends one energy "+i);
            Check(wallet.Energy==0&&!wallet.TryStartOrder(),"Zero energy blocks another order");
            now=now.AddSeconds(599);Check(wallet.Energy==0,"No early regeneration");
            now=now.AddSeconds(1);Check(wallet.Energy==1,"One energy after ten minutes");
            now=now.AddMinutes(25);wallet=new CourierEconomy(()=>now);
            Check(wallet.Energy==3&&wallet.NextEnergySeconds==300,"Offline recovery keeps fractional interval");
            Check(wallet.TryBuyFood(CourierEconomy.Food.Apple)&&wallet.Energy==5&&wallet.Balance==180,"Apple costs 20 and feeds two");
            Check(wallet.TryBuyFood(CourierEconomy.Food.Sandwich)&&wallet.Energy==10&&wallet.Balance==140,"Sandwich costs 40 and feeds five");
            for(int i=0;i<10;i++)wallet.TryStartOrder();
            Check(wallet.TryBuyFood(CourierEconomy.Food.HotMeal)&&wallet.Energy==10&&wallet.Balance==75,"Hot meal costs 65 and feeds ten");
            Check(wallet.TotalEarned==300,"Food spending never reduces lifetime earnings");
            Check(wallet.TryClaimDaily(out int coins)&&coins==40&&wallet.Sandwiches==1,"First daily package includes 40 coins and a sandwich");
            long balance=wallet.Balance;
            Check(!wallet.TryClaimDaily(out _)&&wallet.Balance==balance,"Same UTC day cannot claim twice");
            Check(!wallet.TryEatSandwich()&&wallet.Sandwiches==1,"Full energy preserves stored sandwich");
            wallet.TryStartOrder();Check(wallet.TryEatSandwich()&&wallet.Energy==10&&wallet.Sandwiches==0,"Saved sandwich feeds later without exceeding cap");
            now=now.AddDays(3);Check(wallet.TryClaimDaily(out coins)&&coins==50&&wallet.PackageStep==2,"Missed days retain route without accumulating packages");
            for(int i=2;i<7;i++){now=now.AddDays(1);Check(wallet.TryClaimDaily(out coins)&&coins==CourierEconomy.PackageCoins(i),"Daily step "+(i+1));}
            Check(wallet.HasSunriseBag&&wallet.PackageStep==0&&wallet.EquipBag(true),"Seventh package unlocks and equips Sunrise bag");
            now=now.AddDays(-2);Check(!wallet.TryClaimDaily(out _),"Clock rollback cannot reclaim a package");
            now=now.AddDays(3);Check(wallet.TryClaimDaily(out coins)&&coins==40,"Next route restarts at first reward");
            now=now.AddDays(1);Check(wallet.Energy==10,"Offline energy is capped at ten");
            PlayerPrefs.SetString(CourierEconomy.SaveKey,"{\"balance\":0}");wallet=new CourierEconomy(()=>now);wallet.TryStartOrder();
            Check(!wallet.TryBuyFood(CourierEconomy.Food.Apple)&&wallet.Balance==0&&wallet.Energy==9,"Insufficient funds cannot buy food");
        }
        [InitializeOnLoadMethod] private static void Resume(){if(SessionState.GetBool("MenuVerify",false)){deadline=EditorApplication.timeSinceStartup+120;EditorApplication.update+=Tick;}}
        private static void Click(string name){GameObject.Find(name).GetComponent<Button>().onClick.Invoke();}
        private static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Menu verification timeout at "+stage);
                if(!EditorApplication.isPlaying)return;
                var menu=UnityEngine.Object.FindFirstObjectByType<CourierMenu>();
                var game=UnityEngine.Object.FindFirstObjectByType<CourierGame>();
                if(stage==0&&menu)
                {
                    Check(menu.Wallet.Energy==10&&game==null,"Menu opens with full energy and no running order");
                    Capture("MainMenu");Click("Customers");Check(GameObject.Find("Customers panel")!=null,"Client reputation and hats panel opens");Capture("ClientReputation");Click("Close customers");CaptureClients();Click("How to play");Check(GameObject.Find("Help panel")!=null,"Instructions open");Capture("Instructions");Click("Close help");
                    Click("Claim package");Check(menu.Wallet.Balance==40&&menu.Wallet.Sandwiches==1,"Daily claim button credits wallet and food");
                    Click("Claim package");Check(menu.Wallet.Balance==40,"Repeated claim button cannot pay twice");Capture("DailyClaimed");
                    Click("Play");stage=1;return;
                }
                if(stage==1&&game)
                {
                    Check(game.Wallet.Energy==10,"Entering gameplay is free");
                    Check(game.ClientIndex==CourierRating.Count%8 && GameObject.Find("Coco").GetComponent<SpriteRenderer>().bounds.size.y>1,"Chicken courier enlarged and client assigned");
                    Check(GameObject.Find("Order briefing") != null,"Order contract shown before timer starts");
                    Capture("OrderBriefing");Click("Accept order");Capture("PixelGameplay");
                    game.Run.Cars.Add(new CourierRun.Car{lane=1,x=1.6f,speed=-3,kind=CourierRun.VehicleKind.Scooter});game.SendMessage("UpdatePresentation");Capture("ScooterTraffic");
                    game.Run.Cars.Clear();game.Run.Cars.Add(new CourierRun.Car{lane=0,x=1.04f,speed=.01f});game.Jump();game.Jump();
                    Check(game.Wallet.Energy==9,"First jump costs one; midair input costs nothing");game.Run.Tick(.1f);Capture("PixelJump");game.Run.Tick(game.Run.JumpDuration-.1f);game.SendMessage("UpdatePresentation");
                    Check(game.Run.CloseCalls==1&&GameObject.Find("Hint").GetComponent<Text>().text.Contains("CLOSE CALL"),"Safe close call shows immediate tip feedback");Capture("CloseCall");
                    game.Run.Cars.Clear();game.Run.Cars.Add(new CourierRun.Car{lane=1,x=0,speed=0});game.Jump();game.Run.Tick(.4f);stage=2;return;
                }
                if(stage==2&&game)
                {
                    Check(game.Run.State==CourierRun.RunState.Crashed&&game.Wallet.Energy==9,"Crash retains energy cost");Click("Main menu");stage=3;return;
                }
                if(stage==3&&menu)
                {
                    Check(menu.Wallet.Energy==9&&menu.Wallet.Balance==40,"Result returns to menu with saved stats");Click("Shop");Capture("FoodShop");Click("Buy APPLE");
                    Check(menu.Wallet.Energy==10&&menu.Wallet.Balance==20,"Shop purchase feeds and deducts coins");Click("Buy APPLE");Check(menu.Wallet.Balance==20,"Full-energy repeat purchase cannot charge");
                    Click("Close shop");Click("Play");stage=4;return;
                }
                if(stage==4&&game)
                {
                    Click("Accept order");for(int i=0;i<10;i++)
                    {
                        if(game.Run.CanPickUpParcel){game.SendMessage("UpdateGameActivities");Capture("LostParcel");Click("Pick up parcel");Check(game.Run.HasLostParcel,"Lost parcel button collects optional order");}
                        game.Run.Cars.Clear();game.Jump();game.Run.Tick(game.Run.JumpDuration+.001f);
                    }stage=5;return;
                }
                if(stage==5&&game)
                {
                    Check(game.Run.State==CourierRun.RunState.Delivered&&game.Wallet.Balance==190&&game.Wallet.Energy==9,"Full delivery pays once and uses only one energy");Capture("LastMeter");game.SkipThrow();Capture("MenuDelivery");
                    Click("Retry");Check(game.ClientIndex==CourierRating.Count%8,"Next successful order advances client");Check(game.Wallet.Energy==9,"Retry itself is free");Click("Accept order");game.Run.Cars.Clear();game.Jump();Check(game.Wallet.Energy==8,"Retry first jump spends next energy");
                    game.Run.Tick(game.Run.JumpDuration+.001f);
                    for(int i=1;i<10;i++){game.Run.Cars.Clear();game.Jump();game.Run.Tick(game.Run.JumpDuration+.001f);}
                    stage=51;return;
                }
                if(stage==51&&game)
                {
                    Check(game.Run.State==CourierRun.RunState.Delivered&&game.Wallet.CompletedShifts==1,"Third attempt completes a mixed shift in Play Mode");game.SkipThrow();Capture("ShiftReport");
                    Click("Retry");Click("Accept order");
                    for(int i=0;i<10;i++){game.Run.Cars.Clear();game.Jump();game.Run.Tick(game.Run.JumpDuration+.001f);}stage=52;return;
                }
                if(stage==52&&game)
                {
                    Check(game.Wallet.Streak==3&&GameObject.Find("Reward breakdown").GetComponent<Text>().text.Contains("Streak +75"),"Three consecutive deliveries show the streak reward");game.SkipThrow();Capture("StreakReport");
                    for(int i=0;i<3;i++)
                    {
                        var hatRun=new CourierRun(game.settings,i,false,new DeliveryContract(1,game.Wallet.ShiftStep,game.settings));
                        for(int lane=0;lane<10;lane++){hatRun.Jump();hatRun.Tick(hatRun.JumpDuration);}
                        game.Wallet.TryReward(hatRun,out _);
                    }
                    game.Wallet.EquipHat(1);game.SendMessage("UpdatePresentation");
                    Check(GameObject.Find("Reputation hat")!=null,"Unlocked equipped hat is visible on courier");
                    game.MainMenu();stage=6;return;
                }
                if(stage==6&&menu)
                {
                    Click("Eat sandwich");Check(menu.Wallet.Energy==10&&menu.Wallet.Sandwiches==0,"Menu eats saved sandwich");
                    for(int i=0;i<10;i++)menu.Wallet.TryStartOrder();menu.Refresh();Click("Play");
                    Check(GameObject.Find("Shop panel")!=null&&UnityEngine.Object.FindFirstObjectByType<CourierGame>()==null,"Hungry Play routes to food shop");Capture("HungryShop");
                    SceneManager.LoadScene("CocoCourier");stage=7;return;
                }
                if(stage==7&&game)
                {
                    Click("Accept order");game.Jump();Check(!game.Run.Jumping&&game.Wallet.Energy==0&&GameObject.Find("Result title").GetComponent<Text>().text=="COCO IS HUNGRY","Direct gameplay cannot bypass zero energy");
                    Capture("HungryGame");Click("Retry");stage=8;return;
                }
                if(stage==8&&menu)
                {
                    Check(GameObject.Find("Shop panel")!=null,"Hungry game GET FOOD returns to shop");
                    Click("Shop cook");Check(GameObject.Find("Kitchen panel")!=null,"Empty-energy shop opens free kitchen");Capture("KitchenReady");Click("Start cooking");stage=9;return;
                }
                if(stage==9&&menu)
                {
                    bool[] used=new bool[8];int[] ingredients={0,1,2,3,0,1,2,3};
                    for(int step=0;step<4;step++)for(int slot=0;slot<8;slot++)if(!used[slot]&&ingredients[slot]==menu.Kitchen.Recipe[menu.Kitchen.Step]){menu.TapIngredient(slot);used[slot]=true;break;}
                    Check(menu.Kitchen.Score==1,"Kitchen ingredient controls complete recipe");Capture("KitchenPlaying");menu.Kitchen.Tick(100);stage=10;return;
                }
                if(stage==10&&menu)
                {
                    Check(menu.Wallet.Sandwiches==1&&menu.Wallet.Energy==0,"Kitchen result awards stored food at zero energy");Capture("KitchenResult");Click("Leave kitchen");Click("Eat sandwich");
                    Check(menu.Wallet.Energy==5,"Cooked food restores ability to deliver");Click("Lunch rush");Capture("RushBriefing");Click("Start rush");stage=11;return;
                }
                if(stage==11&&game)
                {
                    Check(game.IsRush,"Rush menu starts challenge mode");Click("Accept order");game.Run.Cars.Clear();game.Jump();game.Run.Tick(game.Run.JumpDuration+.01f);game.Run.Tick(game.settings.rushDeadline);stage=12;return;
                }
                if(stage==12&&game)
                {
                    Check(game.Run.State==CourierRun.RunState.TimedOut&&GameObject.Find("Result title").GetComponent<Text>().text=="TIME IS UP!","Rush deadline shows timeout result");Capture("RushTimeout");Click("Retry");Click("Accept order");
                    for(int i=0;i<10;i++){game.Run.Cars.Clear();game.Jump();game.Run.Tick(game.Run.JumpDuration+.001f);}stage=13;return;
                }
                if(stage==13&&game)
                {
                    Check(game.Wallet.RushMedal==3&&game.LastMeterActive,"Rush completion awards badge then offers final throw");
                    game.LastMeter.Tick((.5f-game.LastMeter.Position)*game.settings.lastMeterSweepSeconds);
                    long coins=game.Wallet.Balance;Click("Throw");Check(game.LastMeter.Hit&&game.Wallet.Balance==coins+15,"Throw button awards accurate-catch tip");Click("Throw");Check(game.Wallet.Balance==coins+15,"Double throw cannot double-pay");game.SkipThrow();Capture("RushResult");game.MainMenu();stage=14;return;
                }
                if(stage==14&&menu)
                {
                    Click("Lunch rush");Click("Rush visor");Check(menu.Wallet.EquippedHat==8,"Rush reward can be equipped in menu");Capture("RushRecord");
                    for(int i=0;i<10;i++)menu.Wallet.TryStartOrder();SceneManager.LoadScene("CocoCourier");stage=15;return;
                }
                if(stage==15&&game)
                {
                    Click("Accept order");game.Jump();Check(GameObject.Find("Hungry cook")!=null,"Hungry game immediately offers free cooking");Capture("HungryCooking");Click("Hungry cook");stage=16;return;
                }
                if(stage==16&&menu)
                {
                    Check(GameObject.Find("Kitchen panel")!=null&&menu.Wallet.Energy==0,"Hungry game transitions directly into kitchen");
                    File.AppendAllText("Logs/CocoMenuVerification.txt",count+" Play Mode menu/food/energy/transition checks passed.\n");
                    Restore();SessionState.SetBool("MenuVerify",false);EditorApplication.update-=Tick;EditorApplication.Exit(0);
                }
            }
            catch(Exception e){Fail(e);}
        }
        private static void CaptureClients()
        {
            var canvas=UnityEngine.Object.FindFirstObjectByType<Canvas>();
            var root=new GameObject("Client gallery QA",typeof(RectTransform),typeof(Image));var r=root.GetComponent<RectTransform>();r.SetParent(canvas.transform,false);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;root.GetComponent<Image>().color=new Color(.035f,.075f,.11f);
            for(int i=0;i<8;i++)
            {
                float x=.025f+(i%4)*.245f,y=i<4?.54f:.12f;
                var obj=new GameObject("Client "+i,typeof(RectTransform),typeof(Image));var rect=obj.GetComponent<RectTransform>();rect.SetParent(root.transform,false);rect.anchorMin=new Vector2(x,y);rect.anchorMax=new Vector2(x+.22f,y+.31f);rect.offsetMin=rect.offsetMax=Vector2.zero;PixelArt.Image(obj.GetComponent<Image>(),PixelArt.Client(i));
                var label=new GameObject("Name",typeof(RectTransform),typeof(Text));rect=label.GetComponent<RectTransform>();rect.SetParent(root.transform,false);rect.anchorMin=new Vector2(x,y-.06f);rect.anchorMax=new Vector2(x+.22f,y);rect.offsetMin=rect.offsetMax=Vector2.zero;
                var text=label.GetComponent<Text>();text.font=PixelArt.Font;text.text=PixelArt.ClientNames[i];text.fontSize=18;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;
            }
            Capture("CustomerGallery");UnityEngine.Object.DestroyImmediate(root);
        }
        private static void Capture(string name)
        {
            var camera=Camera.main;var canvas=UnityEngine.Object.FindFirstObjectByType<Canvas>();
            var target=new RenderTexture(540,960,24);var pixels=new Texture2D(540,960,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try
            {
                camera.targetTexture=target;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;canvas.sortingOrder=1000;
                var game=UnityEngine.Object.FindFirstObjectByType<CourierGame>();if(game)game.SendMessage("UpdatePresentation");Canvas.ForceUpdateCanvases();
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,540,960),0,0);pixels.Apply();File.WriteAllBytes("Logs/"+name+".png",pixels.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;camera.targetTexture=null;canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=0;UnityEngine.Object.DestroyImmediate(pixels);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        private static void Restore()
        {
            if(!SessionState.GetBool("MenuBackedUp",false))return;
            if(SessionState.GetBool("MenuBackupExists",false))PlayerPrefs.SetString(CourierEconomy.SaveKey,SessionState.GetString("MenuBackup",""));else PlayerPrefs.DeleteKey(CourierEconomy.SaveKey);
            PlayerPrefs.SetInt("CocoCourier.DeliveryCount.v1",SessionState.GetInt("MenuCount",0));PlayerPrefs.SetInt("CocoCourier.RatingSum.v1",SessionState.GetInt("MenuSum",0));PlayerPrefs.Save();SessionState.SetBool("MenuBackedUp",false);
        }
        private static void Fail(Exception e){Restore();SessionState.SetBool("MenuVerify",false);Debug.LogException(e);File.AppendAllText("Logs/CocoMenuVerification.txt","FAILED: "+e+"\n");EditorApplication.Exit(1);}
    }
}


