using System;
using System.Collections.Generic;
using UnityEngine;

namespace CocoCourier
{
    public readonly struct OrderReward
    {
        public const int DeliveryCoins = 50;
        public const int CoinsPerRatingPoint = 5;
        public int BaseCoins { get; }
        public int RatingBonus { get; }
        public int Tips { get; }
        public int ContractBonus { get; }
        public int StreakBonus { get; }
        public int ShiftBonus { get; }
        public bool ShiftCompleted { get; }
        public int ShiftEarned { get; }
        public int ShiftDelivered { get; }
        public int LostParcelBonus { get; }
        public int Total => BaseCoins + RatingBonus + Tips + ContractBonus + StreakBonus + ShiftBonus + LostParcelBonus;
        private OrderReward(int rating) : this(rating, 0, 0, 0, 0, false, 0, 0) { }
        public OrderReward(int rating, int tips, int contract, int streak, int shift, bool completed, int shiftEarned, int shiftDelivered, int lostParcel = 0)
        { LostParcelBonus = lostParcel; BaseCoins = DeliveryCoins; RatingBonus = rating * CoinsPerRatingPoint; Tips = tips; ContractBonus = contract; StreakBonus = streak; ShiftBonus = shift; ShiftCompleted = completed; ShiftEarned = shiftEarned; ShiftDelivered = shiftDelivered; }
        public static OrderReward ForRating(int rating) => new OrderReward(Mathf.Clamp(rating, 1, 10));
    }

    // A single saved document keeps balance, lifetime earnings and the reward receipt together.
    // Inventory fields reserve stable item IDs for the future cosmetic shop; no shop is required now.
    public sealed partial class CourierEconomy
    {
        public const string SaveKey = "CocoCourier.Economy.v1";
        [Serializable]
        private sealed class SaveData
        {
            public int kitchenBest, rushMedal;
            public float rushBestSeconds;
            public string lastKitchenId = "", lastThrowId = "";
            public int version = 1;
            public long balance;
            public long totalEarned;
            public string lastRewardedOrderId = "";
            public List<string> ownedItemIds = new List<string>();
            public string equippedCourierId = "";
            public string equippedBagId = "";
            public string equippedTrailId = "";
            public bool energyInitialized;
            public int energy;
            public long energyAnchor;
            public long lastSeenUtc;
            public int sandwiches;
            public long lastClaimDay = -1;
            public int claimedPackages;
            public int streak, shiftStep, shiftDelivered, shiftEarned, completedShifts;
            public int[] reputation = new int[8];
            public int equippedHat = -1;
            public string activeOrderId = "", lastFailedOrderId = "";
        }
        private SaveData data;
        private readonly Func<DateTimeOffset> clock;
        public const int MaxEnergy = 10;
        public const int EnergyIntervalSeconds = 600;
        private static readonly int[] dailyCoins = { 40, 50, 60, 70, 80, 100, 150 };
        public enum Food { Apple, Sandwich, HotMeal }
        public int Energy { get { RefreshEnergy(); return data.energy; } }
        public int Sandwiches => data.sandwiches;
        public int PackageStep => data.claimedPackages % 7;
        public bool HasSunriseBag => data.ownedItemIds.Contains("sunrise_bag");
        public bool SunriseEquipped => data.equippedBagId == "sunrise_bag";
        private long Now => Math.Max(clock().ToUnixTimeSeconds(), data.lastSeenUtc);
        public bool CanClaimDaily => Now / 86400 > data.lastClaimDay;
        public long NextPackageSeconds => 86400 - Now % 86400;
        public long NextEnergySeconds => Energy == MaxEnergy ? 0 : Math.Max(0, EnergyIntervalSeconds - (Now - data.energyAnchor));
        public static int PackageCoins(int step) => dailyCoins[Mathf.Clamp(step, 0, 6)];
        public static int FoodPrice(Food food) => food == Food.Apple ? 20 : food == Food.Sandwich ? 40 : 65;
        public static int FoodEnergy(Food food) => food == Food.Apple ? 2 : food == Food.Sandwich ? 5 : 10;
        public static readonly string[] HatNames = { "OFFICE CAP", "HARD HAT", "MEDIC CAP", "CHEF HAT", "MECHANIC CAP", "SCHOLAR CAP", "GARDEN HAT", "TOP HAT", "RUSH VISOR" };
        public int Streak => data.streak;
        public int ShiftStep => data.shiftStep;
        public int CompletedShifts => data.completedShifts;
        public int EquippedHat => data.equippedHat;
        public int Reputation(int client) => data.reputation[Mathf.Clamp(client, 0, 7)];
        public bool EquipHat(int client) { Reload(); if (client < -1 || client > 8 || client == 8 && data.rushMedal < 2 || client >= 0 && client < 8 && Reputation(client) < 3) return false; data.equippedHat = client; Save(); return true; }
        public void BeginRoute(CourierRun run)
        {
            Reload(); if (run.IsRush) return;
            // Leaving an unfinished paid route counts as a failed order, even across restarts.
            if (!string.IsNullOrEmpty(data.activeOrderId) && data.activeOrderId != run.OrderId) FailActive();
            data.activeOrderId = run.OrderId; Save();
        }
        public void ResumeCareer() { Reload(); if (!string.IsNullOrEmpty(data.activeOrderId)) { FailActive(); Save(); } }
        private void FailActive()
        {
            data.lastFailedOrderId = data.activeOrderId; data.activeOrderId = ""; data.streak = 0;
            data.shiftStep++; if (data.shiftStep >= 3) { data.shiftStep = 0; data.shiftDelivered = 0; data.shiftEarned = 0; data.completedShifts++; }
        }
        public string RecordFailure(CourierRun run)
        {
            Reload(); if (run.IsRush) return "RUSH OVER  |  TRY FOR A NEW RECORD";
            if (run.State != CourierRun.RunState.Crashed || data.lastFailedOrderId == run.OrderId) return "";
            int delivered = data.shiftDelivered, earned = data.shiftEarned;
            bool complete = data.shiftStep == 2;
            data.activeOrderId = run.OrderId; FailActive(); Save();
            return complete ? "SHIFT COMPLETE  " + delivered + "/3 delivered  |  " + earned + " coins" : "STREAK RESET  |  SHIFT " + (data.shiftStep + 1) + "/3 NEXT";
        }
        public long Balance => data.balance;
        public long TotalEarned => data.totalEarned;
        public CourierEconomy(Func<DateTimeOffset> clock = null) { this.clock = clock ?? (() => DateTimeOffset.UtcNow); Reload(); }
        public void Reload()
        {
            string saved = PlayerPrefs.GetString(SaveKey, "");
            data = string.IsNullOrEmpty(saved) ? new SaveData() : JsonUtility.FromJson<SaveData>(saved);
            if (data == null) data = new SaveData();
            if (data.reputation == null || data.reputation.Length != 8) data.reputation = new int[8];
            if (data.ownedItemIds == null) data.ownedItemIds = new List<string>();
            if (!data.energyInitialized)
            {
                data.energyInitialized = true; data.energy = MaxEnergy;
                data.energyAnchor = Now; Save();
            }
            RefreshEnergy();
        }
        public bool TryReward(CourierRun run, out OrderReward reward)
        {
            reward = default;
            if (run == null || run.State != CourierRun.RunState.Delivered || run.RewardClaimed) return false;
            Reload();
            if (data.lastRewardedOrderId == run.OrderId) { run.RewardClaimed = true; return false; }
            int streakBonus = 0, shiftBonus = 0;
            bool shiftComplete = false;
            if (run.Contract != null && !run.IsRush)
            {
                data.streak++;
                streakBonus = data.streak % 3 == 0 ? run.StreakBonusCoins : 0;
                data.reputation[run.Contract.ClientIndex]++;
                data.shiftDelivered++; data.shiftStep++;
                shiftComplete = data.shiftStep >= 3;
                shiftBonus = shiftComplete && data.shiftDelivered == 3 ? run.ShiftBonusCoins : 0;
            }
            int earned = 50 + run.Rating * 5 + run.Tips + run.ContractBonus + streakBonus + shiftBonus + run.LostParcelBonus;
            data.shiftEarned += run.Contract != null && !run.IsRush ? earned : 0;
            reward = new OrderReward(run.Rating, run.Tips, run.ContractBonus, streakBonus, shiftBonus, shiftComplete, data.shiftEarned, data.shiftDelivered, run.LostParcelBonus);
            run.ClosedShift=shiftComplete;
            if (shiftComplete) { data.shiftStep = 0; data.shiftEarned = 0; data.shiftDelivered = 0; data.completedShifts++; }
            if (run.IsRush)
            {
                if (data.rushBestSeconds <= 0 || run.Elapsed < data.rushBestSeconds) data.rushBestSeconds = run.Elapsed;
                data.rushMedal = Math.Max(data.rushMedal, run.RushMedal);
            }
            data.activeOrderId = "";
            data.balance = checked(data.balance + reward.Total);
            data.totalEarned = checked(data.totalEarned + reward.Total);
            data.lastRewardedOrderId = run.OrderId;
            Save();
            run.RewardClaimed = true;
            return true;
        }
        private void Save()
        {
            data.lastSeenUtc = Now;
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data)); PlayerPrefs.Save();
        }
        public void RefreshEnergy()
        {
            long now = Now;
            long gained = Math.Max(0, (now - data.energyAnchor) / EnergyIntervalSeconds);
            if (data.energy >= MaxEnergy || gained == 0) return;
            data.energy = (int)Math.Min(MaxEnergy, data.energy + gained);
            data.energyAnchor = data.energy == MaxEnergy ? now : data.energyAnchor + gained * EnergyIntervalSeconds;
            Save();
        }
        public bool TryStartOrder()
        {
            Reload();
            if (data.energy <= 0) return false;
            if (data.energy == MaxEnergy) data.energyAnchor = Now;
            data.energy--; Save(); return true;
        }
        public bool TryBuyFood(Food food)
        {
            if (!Enum.IsDefined(typeof(Food), food)) return false;
            Reload();
            int price = FoodPrice(food);
            if (data.energy == MaxEnergy || data.balance < price) return false;
            data.balance -= price; Feed(FoodEnergy(food)); Save(); return true;
        }
        private void Feed(int amount)
        {
            data.energy = Math.Min(MaxEnergy, data.energy + amount);
            if (data.energy == MaxEnergy) data.energyAnchor = Now;
        }
        public bool TryEatSandwich()
        {
            Reload();
            if (data.sandwiches <= 0 || data.energy == MaxEnergy) return false;
            data.sandwiches--; Feed(5); Save(); return true;
        }
        public bool TryClaimDaily(out int coins)
        {
            Reload(); coins = 0;
            if (!CanClaimDaily) return false;
            coins = PackageCoins(PackageStep);
            if (PackageStep == 6 && !HasSunriseBag) data.ownedItemIds.Add("sunrise_bag");
            data.balance += coins; data.totalEarned += coins; data.sandwiches++;
            data.claimedPackages++; data.lastClaimDay = Now / 86400;
            Save(); return true;
        }
        public bool EquipBag(bool sunrise)
        {
            Reload(); if (sunrise && !HasSunriseBag) return false;
            data.equippedBagId = sunrise ? "sunrise_bag" : ""; Save(); return true;
        }    }
}


