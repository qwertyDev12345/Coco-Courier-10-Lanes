using System;
using System.IO;
using UnityEngine;
namespace CocoCourier.Editor
{
    public static class ActivityVerification
    {
        private static int count;
        private static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;Debug.Log("ACTIVITY PASS: "+label);}
        private static void Finish(CourierRun r){while(r.State==CourierRun.RunState.Playing&&r.Progress<10){r.Cars.Clear();r.Jump();r.Tick(r.JumpDuration+.001f);}}
        public static void Verify()
        {
            count=0;PlayerPrefs.DeleteKey(CourierEconomy.SaveKey);var w=new CourierEconomy();var s=ScriptableObject.CreateInstance<CourierSettings>();
            try
            {
                var kitchen=new KitchenRound(s,3);Check(!w.ClaimKitchen(kitchen),"Unfinished cooking cannot pay");
                kitchen.Tap(0);kitchen.Tap(0);Check(kitchen.Step==0&&kitchen.Mistakes==1,"Wrong ingredient resets sandwich");
                for(int i=0;i<4;i++)for(int j=0;j<4;j++)kitchen.Tap(kitchen.Recipe[kitchen.Step]);
                Check(kitchen.Score==4,"Four completed recipes counted");kitchen.Tick(s.kitchenSeconds);
                Check(!kitchen.Tap(0)&&kitchen.FoodReward==2,"Timer locks input and caps kitchen prize at two");
                Check(w.ClaimKitchen(kitchen)&&w.Sandwiches==2&&w.Energy==10,"Free cooking saves food without energy charge");
                Check(!w.ClaimKitchen(kitchen)&&new CourierEconomy().KitchenBest==4,"Cooking claim idempotent and best score persists");
                var empty=new KitchenRound(s,2);empty.Tick(s.kitchenSeconds);w.ClaimKitchen(empty);Check(w.Sandwiches==2,"Zero sandwiches earns no food");
                var one=new KitchenRound(s,1);for(int j=0;j<4;j++)one.Tap(one.Recipe[one.Step]);one.Tick(s.kitchenSeconds);w.ClaimKitchen(one);Check(w.Sandwiches==3,"One correct sandwich earns one stored meal");
                for(int i=0;i<10;i++)w.TryStartOrder();Check(w.TryEatSandwich()&&w.Energy==5,"Kitchen food restores energy from empty");
                var r=new CourierRun(s,7,false,new DeliveryContract(0,0,s));Check(!r.PickUpParcel(),"Parcel cannot be collected remotely");
                for(int i=0;i<r.LostParcelIsland;i++){r.Jump();r.Tick(r.JumpDuration);}
                float before=r.JumpDuration;Check(r.PickUpParcel()&&!r.PickUpParcel()&&r.JumpDuration>before,"Parcel pickup is optional, once, and lengthens jumps");
                Finish(r);Check(w.TryReward(r,out var reward)&&reward.LostParcelBonus==s.lostParcelCoins,"Extra parcel pays only on delivery");
                var meter=new LastMeterRound(s);meter.Tick(s.lastMeterSweepSeconds/2);Check(meter.Stop()&&meter.Hit&&!meter.Stop(),"Green timing hits and cannot be stopped twice");
                long balance=w.Balance;Check(w.ClaimThrow(r,meter,s.lastMeterTip)==s.lastMeterTip&&w.Balance==balance+s.lastMeterTip,"Accurate throw pays tip");
                Check(w.ClaimThrow(r,meter,s.lastMeterTip)==0,"Throw cannot pay twice");
                var miss=new LastMeterRound(s);miss.Stop();Check(!miss.Hit,"Outside green is a miss");
                var skip=new LastMeterRound(s);skip.Tick(s.lastMeterSweepSeconds/2);skip.Stop(true);Check(!skip.Hit,"Skipping never awards precision tip");
                var crashed=new CourierRun(s,7,false,new DeliveryContract(0,0,s));for(int i=0;i<crashed.LostParcelIsland;i++){crashed.Jump();crashed.Tick(crashed.JumpDuration);}crashed.PickUpParcel();crashed.Cars.Add(new CourierRun.Car{lane=crashed.Progress,x=0});crashed.Jump();crashed.Tick(.6f);
                Check(crashed.State==CourierRun.RunState.Crashed&&crashed.LostParcelBonus==0&&!w.TryReward(crashed,out _),"Lost parcel pays nothing after collision");
                int streak=w.Streak,shift=w.ShiftStep;var rush=new CourierRun(s,2409,false,new DeliveryContract(3,2,s,true));
                Check(!rush.PickUpParcel(),"Record challenge has no optional parcel");Finish(rush);w.TryReward(rush,out _);
                Check(w.RushMedal==3&&w.RushBestSeconds>0&&w.Streak==streak&&w.ShiftStep==shift,"Rush stores gold record without changing career streak or shift");
                Check(w.EquipHat(8)&&new CourierEconomy().EquippedHat==8,"Rush unlock cosmetic persists");
                float best=w.RushBestSeconds;var slower=new CourierRun(s,2409,false,new DeliveryContract(3,2,s,true));slower.Tick(25);Finish(slower);w.TryReward(slower,out _);
                Check(w.RushBestSeconds==best&&w.RushMedal==3,"Slower result cannot replace personal best or gold badge");
                var timeout=new CourierRun(s,2409,false,new DeliveryContract(3,2,s,true));timeout.Tick(s.rushDeadline+.1f);w.RecordFailure(timeout);
                Check(timeout.State==CourierRun.RunState.TimedOut&&!w.TryReward(timeout,out _)&&w.Streak==streak,"Rush timeout ends attempt without reward or career penalty");
                var route=new CourierRun(s,2409,true,new DeliveryContract(3,2,s,true));var same=new CourierRun(s,2409,true,new DeliveryContract(3,2,s,true));
                Check(route.Cars.Count==same.Cars.Count&&route.Cars[0].x==same.Cars[0].x,"Rush route is reproducible");
                int frames=0;
                while(route.State==CourierRun.RunState.Playing&&frames++<4000)
                {
                    if(!route.Jumping)
                    {
                        bool clear=true;foreach(var car in route.Cars)
                            if(car.lane==route.Progress&&CourierRun.SweptHit(new Vector2(-car.x,-CourierRun.IslandSpacing/2),new Vector2(-car.x-car.speed*route.JumpDuration,CourierRun.IslandSpacing/2),new Vector2(car.HalfWidth+CourierRun.CourierRadius+.04f,car.HalfHeight+CourierRun.CourierRadius+.04f))){clear=false;break;}
                        if(clear)route.Jump();
                    }
                    route.Tick(1f/60);
                }
                Check(route.State==CourierRun.RunState.Delivered,"Dense rush route has safe windows inside time limit");
                float rushTime=route.Elapsed;
                for(int seed=1;seed<=3;seed++)
                {
                    var heavyRoute=new CourierRun(s,seed,true,new DeliveryContract(1,2,s));frames=0;
                    while(heavyRoute.State==CourierRun.RunState.Playing&&frames++<8000)
                    {
                        if(heavyRoute.CanPickUpParcel)heavyRoute.PickUpParcel();
                        if(!heavyRoute.Jumping)
                        {
                            bool clear=true;foreach(var car in heavyRoute.Cars)
                                if(car.lane==heavyRoute.Progress&&CourierRun.SweptHit(new Vector2(-car.x,-CourierRun.IslandSpacing/2),new Vector2(-car.x-car.speed*heavyRoute.JumpDuration,CourierRun.IslandSpacing/2),new Vector2(car.HalfWidth+CourierRun.CourierRadius+.04f,car.HalfHeight+CourierRun.CourierRadius+.04f))){clear=false;break;}
                            if(clear)heavyRoute.Jump();
                        }
                        heavyRoute.Tick(1f/60);
                    }
                    Check(heavyRoute.State==CourierRun.RunState.Delivered&&heavyRoute.HasLostParcel,"Heavy plus lost parcel remains traversable, seed "+seed);
                }
                File.WriteAllText("Logs/ActivityChecks.txt",count+" kitchen, parcel, throw and rush checks passed. Rush safe completion: "+route.Elapsed.ToString("0.00")+"s.\n");
            }
            finally{UnityEngine.Object.DestroyImmediate(s);}
        }
    }
}
