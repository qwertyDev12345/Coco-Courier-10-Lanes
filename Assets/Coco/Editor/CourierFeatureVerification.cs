using System;
using System.IO;
using UnityEngine;
namespace CocoCourier.Editor
{
    public static class CourierFeatureVerification
    {
        private static int checks;
        private static void Check(bool ok,string name) { if(!ok)throw new Exception(name);checks++;Debug.Log("FEATURE PASS: "+name); }
        private static CourierRun Deliver(CourierSettings s, DeliveryContract contract)
        { var r=new CourierRun(s,1,false,contract); for(int i=0;i<10;i++){r.Jump();r.Tick(r.JumpDuration+.001f);} return r; }
        public static void Verify()
        {
            checks=0;var s=ScriptableObject.CreateInstance<CourierSettings>();
            try
            {
                PlayerPrefs.DeleteKey(CourierEconomy.SaveKey);var wallet=new CourierEconomy();
                var traffic=new CourierRun(s,1);
                Check(traffic.Cars[0].kind==CourierRun.VehicleKind.Car && traffic.Cars[1].kind==CourierRun.VehicleKind.Van && traffic.Cars[2].kind==CourierRun.VehicleKind.Scooter,"Three distinct vehicle classes");
                Check(traffic.Cars[1].HalfWidth>traffic.Cars[0].HalfWidth && traffic.Cars[2].HalfWidth<traffic.Cars[0].HalfWidth,"Vehicle collision bounds match different sizes");
                Check(Mathf.Abs(traffic.Cars[9].speed)>Mathf.Abs(traffic.Cars[0].speed)*2,"Final lanes accelerate matching vehicle types");
                Check(traffic.Cars.Count >= 25,"Road starts populated across all lanes");
                Check(s.maximumSpawnInterval<=2f,"Traffic spawns at least twice as often as original slow intervals");
                var special=new CourierRun(s,1,true,new DeliveryContract(0,2,s));
                Check(Mathf.Abs(special.Cars[0].speed)>Mathf.Abs(traffic.Cars[0].speed),"Later orders increase traffic speed");
                var heavy=new CourierRun(s,1,false,new DeliveryContract(1,0,s));
                Check(heavy.JumpDuration>s.jumpDuration,"Heavy parcel extends actual jump duration");
                heavy.Jump();Check(!heavy.Jump(),"Repeated airborne jump is ignored");heavy.Tick(s.jumpDuration);Check(heavy.Jumping&&heavy.Progress==0,"Heavy jump does not land early");
                var near=new CourierRun(s,1,false,new DeliveryContract(2,0,s));near.Cars.Add(new CourierRun.Car{lane=0,x=1.04f,speed=.01f});near.Jump();near.Tick(near.JumpDuration);
                Check(near.CloseCalls==1&&near.Progress==1,"Trailing-edge safe pass earns one close call on landing");near.Tick(1);Check(near.CloseCalls==1,"Waiting cannot farm tips");
                near.Cars.Clear();for(int i=1;i<10;i++){near.Jump();near.Tick(near.JumpDuration);}
                Check(!near.ContractCompleted&&near.Tips==s.closeCallCoins,"Fragile bonus lost by close call but tips preserved");
                var fragile=Deliver(s,new DeliveryContract(2,0,s));Check(fragile.ContractCompleted,"Careful fragile delivery earns bonus");
                var late=new CourierRun(s,1,false,new DeliveryContract(0,0,s));late.Tick(s.expressDeadline+1);for(int i=0;i<10;i++){late.Jump();late.Tick(late.JumpDuration);}
                Check(late.State==CourierRun.RunState.Delivered&&!late.ContractCompleted,"Late express still delivers without contract bonus");
                var crash=new CourierRun(s,1,false,new DeliveryContract(0,0,s));crash.Cars.Add(new CourierRun.Car{lane=0,x=0,speed=0});crash.Jump();crash.Tick(.4f);
                Check(crash.State==CourierRun.RunState.Crashed&&crash.CloseCalls==0&&!wallet.TryReward(crash,out _),"Airborne collision grants no reward or tip");
                for(int i=0;i<3;i++)
                {
                    var run=Deliver(s,new DeliveryContract(1,wallet.ShiftStep,s));
                    Check(wallet.TryReward(run,out var reward),"Successful shift order pays "+i);
                    long balance=wallet.Balance;Check(!wallet.TryReward(run,out _)&&wallet.Balance==balance,"Duplicate reward blocked "+i);
                    if(i==2)Check(reward.StreakBonus==s.streakCoins&&reward.ShiftBonus==s.shiftCoins&&reward.ShiftCompleted&&reward.ShiftDelivered==3,"Perfect shift and three-win streak pay once");
                }
                wallet=new CourierEconomy();Check(wallet.Streak==3&&wallet.ShiftStep==0&&wallet.CompletedShifts==1&&wallet.Reputation(1)==3,"Career persists across reload");
                Check(wallet.EquipHat(1)&&!wallet.EquipHat(2),"Reputation unlocks only the earned hat");wallet=new CourierEconomy();Check(wallet.EquippedHat==1,"Equipped hat persists");
                long saved=wallet.Balance;wallet.RecordFailure(crash);wallet.RecordFailure(crash);
                Check(wallet.Streak==0&&wallet.ShiftStep==1&&wallet.Balance==saved&&wallet.Reputation(1)==3,"Crash resets streak once without losing coins or trust");
                var abandoned=new CourierRun(s,1,false,new DeliveryContract(0,1,s));wallet.BeginRoute(abandoned);wallet=new CourierEconomy();wallet.ResumeCareer();wallet.ResumeCareer();
                Check(wallet.ShiftStep==2,"Abandoned paid route resolved once after restart");
                var final=Deliver(s,new DeliveryContract(0,2,s));wallet.TryReward(final,out var report);
                Check(report.ShiftCompleted&&report.ShiftDelivered==1&&report.ShiftBonus==0,"Mixed shift summary includes only successful deliveries");
                Check(report.ContractBonus==s.contractCoins*2,"Special order doubles contract bonus");
                Check(wallet.EquipHat(-1)&&wallet.EquippedHat==-1,"Cosmetic can be removed");
                // A conservative player waits until the entire ground-space jump is clear.
                for(int seed=1;seed<=12;seed++)
                {
                    var route=new CourierRun(s,seed,true,new DeliveryContract(seed%8,2,s));
                    int frames=0;
                    while(route.State==CourierRun.RunState.Playing&&frames++<7200)
                    {
                        if(!route.Jumping)
                        {
                            bool clear=true;
                            foreach(var car in route.Cars)
                            {
                                if(car.lane!=route.Progress)continue;
                                if(CourierRun.SweptHit(new Vector2(-car.x,-CourierRun.IslandSpacing/2),
                                    new Vector2(-car.x-car.speed*route.JumpDuration,CourierRun.IslandSpacing/2),
                                    new Vector2(car.HalfWidth+CourierRun.CourierRadius+.04f,car.HalfHeight+CourierRun.CourierRadius+.04f))) {clear=false;break;}
                            }
                            if(clear)route.Jump();
                        }
                        route.Tick(1f/60);
                    }
                    Check(route.State==CourierRun.RunState.Delivered,"Safe windows remain available on hardest shift, seed "+seed);
                }
                File.WriteAllText("Logs/CourierFeatureChecks.txt",checks+" traffic, contracts, tips, career, persistence and shift checks passed.\n");
            }
            finally { UnityEngine.Object.DestroyImmediate(s); }
        }
    }
}
