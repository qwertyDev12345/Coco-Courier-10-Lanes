using System;
using UnityEngine;
namespace CocoCourier
{
    public sealed class KitchenRound
    {
        public static readonly string[] Ingredients = { "BREAD", "CHEESE", "TOMATO", "LETTUCE" };
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public int[] Recipe { get; private set; }
        public int Step { get; private set; }
        public int Score { get; private set; }
        public int Mistakes { get; private set; }
        public float Remaining { get; private set; }
        public bool Finished => Remaining <= 0;
        internal bool Claimed;
        private readonly System.Random random;
        private readonly int doubleRewardScore;
        public int FoodReward => !Finished || Score == 0 ? 0 : Score >= doubleRewardScore ? 2 : 1;
        public KitchenRound(CourierSettings settings, int seed)
        { Remaining = settings.kitchenSeconds; doubleRewardScore = Mathf.Max(1, settings.kitchenDoubleRewardScore); random = new System.Random(seed); NextRecipe(); }
        private void NextRecipe() { Recipe = new[] { 0, random.Next(1,4), random.Next(1,4), 0 }; Step = 0; }
        public bool Tap(int ingredient)
        {
            if (Finished || ingredient < 0 || ingredient >= Ingredients.Length) return false;
            if (ingredient != Recipe[Step]) { Step = 0; Mistakes++; return false; }
            Step++; if (Step == Recipe.Length) { Score++; NextRecipe(); } return true;
        }
        public void Tick(float seconds) { if (seconds > 0) Remaining = Mathf.Max(0,Remaining-seconds); }
    }
    public sealed class LastMeterRound
    {
        public bool Resolved { get; private set; }
        public bool Hit { get; private set; }
        public float Position => Mathf.PingPong(elapsed / sweep, 1);
        public float GreenWidth { get; }
        private float elapsed;
        private readonly float sweep;
        public LastMeterRound(CourierSettings settings) { sweep = Mathf.Max(.5f,settings.lastMeterSweepSeconds); GreenWidth = settings.lastMeterGreenWidth; }
        public void Tick(float dt) { if(!Resolved && dt>0)elapsed+=dt; }
        public bool Stop(bool skip = false)
        { if(Resolved)return false; Hit=!skip && Mathf.Abs(Position-.5f)<=GreenWidth/2; Resolved=true;return true; }
    }
    public sealed partial class CourierEconomy
    {
        public int KitchenBest => data.kitchenBest;
        public float RushBestSeconds => data.rushBestSeconds;
        public int RushMedal => data.rushMedal;
        public string RushBadge => data.rushMedal == 3 ? "GOLD" : data.rushMedal == 2 ? "SILVER" : data.rushMedal == 1 ? "BRONZE" : "NO BADGE YET";
        public bool ClaimKitchen(KitchenRound round)
        {
            if(round == null || !round.Finished || round.Claimed)return false;
            Reload();if(data.lastKitchenId == round.Id){round.Claimed=true;return false;}
            data.sandwiches += round.FoodReward;data.kitchenBest=Math.Max(data.kitchenBest,round.Score);data.lastKitchenId=round.Id;Save();round.Claimed=true;return true;
        }
        public int ClaimThrow(CourierRun run, LastMeterRound round, int tip)
        {
            if(run == null || round == null || !round.Resolved || run.State != CourierRun.RunState.Delivered)return 0;
            Reload();if(data.lastThrowId==run.OrderId || data.lastRewardedOrderId!=run.OrderId)return 0;
            data.lastThrowId=run.OrderId;int coins=round.Hit?Mathf.Max(0,tip):0;
            data.balance+=coins;data.totalEarned+=coins;
            if(!run.IsRush && run.Contract!=null && !run.ClosedShift)data.shiftEarned+=coins;
            Save();return coins;
        }
    }
}
