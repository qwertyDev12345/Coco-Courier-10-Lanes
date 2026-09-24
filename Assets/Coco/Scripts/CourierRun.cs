using System;
using System.Collections.Generic;
using UnityEngine;

namespace CocoCourier
{
    // Ground-space simulation: visual jump height is deliberately absent from collision tests.
    public sealed class CourierRun
    {
        public const int LaneCount = 10;
        public const float IslandSpacing = 2.4f;
        public const float RoadWidth = 1.45f;
        public const float CarHalfWidth = 0.72f, CarHalfHeight = 0.4f;
        public const float CourierRadius = 0.22f;
        public const float TrafficEdge = 9f;
        public enum RunState { Playing, Crashed, Delivered, TimedOut }
        public enum VehicleKind { Car, Van, Scooter }
        public sealed class Car
        {
            public int lane; public float x, speed; public VehicleKind kind;
            public float HalfWidth => kind == VehicleKind.Van ? 1.02f : kind == VehicleKind.Scooter ? .43f : CarHalfWidth;
            public float HalfHeight => kind == VehicleKind.Scooter ? .28f : CarHalfHeight;
        }
        public DeliveryContract Contract { get; }
        public int CloseCalls { get; private set; }
        public float LastCloseCallAt { get; private set; } = -100;
        private bool nearMiss;
        public bool IsRush => Contract != null && Contract.IsRush;
        public int LostParcelIsland { get; }
        public bool HasLostParcel { get; private set; }
        public bool CanPickUpParcel => Contract != null && !IsRush && !HasLostParcel && !Jumping && State == RunState.Playing && Progress == LostParcelIsland;
        public bool PickUpParcel() { if (!CanPickUpParcel) return false; HasLostParcel = true; return true; }
        public int LostParcelBonus => HasLostParcel && State == RunState.Delivered ? settings.lostParcelCoins : 0;
        public int RushMedal => !IsRush || State != RunState.Delivered ? 0 : Elapsed <= settings.rushGoldSeconds ? 3 : Elapsed <= settings.rushSilverSeconds ? 2 : 1;
        private float SpawnInterval() => Range(settings.minimumSpawnInterval, settings.maximumSpawnInterval) * (IsRush ? settings.rushSpawnMultiplier : 1);
        public float JumpDuration => settings.jumpDuration * (Contract != null && Contract.Kind == OrderKind.Heavy ? settings.heavyJumpMultiplier : 1) * (HasLostParcel ? settings.lostParcelJumpMultiplier : 1);
        public bool ContractCompleted => Contract != null && State == RunState.Delivered &&
            (Contract.Kind == OrderKind.Heavy || (Contract.Kind == OrderKind.Express ? Elapsed <= Contract.Deadline : CloseCalls == 0));
        public int Tips => CloseCalls * settings.closeCallCoins;
        public int ContractBonus => !IsRush && ContractCompleted ? settings.contractCoins * (Contract.ShiftStep == 2 ? 2 : 1) : 0;
        public int StreakBonusCoins => settings.streakCoins;
        public int ShiftBonusCoins => settings.shiftCoins;
        public readonly List<Car> Cars = new List<Car>();
        public string OrderId { get; } = Guid.NewGuid().ToString("N");
        internal bool ClosedShift;
        internal bool RewardClaimed { get; set; }
        public RunState State { get; private set; }
        public int Progress { get; private set; }
        public float Elapsed { get; private set; }
        public bool Jumping { get; private set; }
        public float JumpFraction { get; private set; }
        public float GroundY => (Progress + (Jumping ? JumpFraction : 0f)) * IslandSpacing;
        private readonly CourierSettings settings;
        private readonly System.Random random;
        private readonly float[] nextSpawn = new float[LaneCount];
        private readonly bool traffic;
        public CourierRun(CourierSettings settings, int seed, bool traffic = true, DeliveryContract contract = null)
        {
            Contract = contract; this.settings = settings; random = new System.Random(seed); this.traffic = traffic; LostParcelIsland = random.Next(3,7);
            if (!traffic) return;
            for (int lane = 0; lane < LaneCount; lane++)
            {
                Spawn(lane, 0);
            }
            // Seed the complete visible route with the same spacing as ongoing traffic.
            // First ten entries retain one vehicle per lane for inspection/debug tooling.
            for (int lane = 0; lane < LaneCount; lane++)
            {
                Car first = Cars[lane];
                float speed = Mathf.Abs(first.speed), direction = Mathf.Sign(first.speed);
                first.x = Range(-.5f, .5f) * speed * SpawnInterval();
                float x = first.x;
                while (true)
                {
                    x += direction * speed * SpawnInterval();
                    if (Mathf.Abs(x) > TrafficEdge) break;
                    Spawn(lane, x);
                }
                x = first.x;
                while (true)
                {
                    x -= direction * speed * SpawnInterval();
                    if (Mathf.Abs(x) > TrafficEdge) break;
                    Spawn(lane, x);
                }
                nextSpawn[lane] = Mathf.Max(.025f, (Mathf.Abs(x) - TrafficEdge) / speed);
            }
        }
        public bool Jump()
        {
            if (State != RunState.Playing || Jumping) return false;
            nearMiss = false; Jumping = true; JumpFraction = 0; return true;
        }
        public void Tick(float delta)
        {
            if (State != RunState.Playing || delta <= 0) return;
            // Split a long frame at landing, retaining exact swept collision checks on both sides.
            while (delta > 0.000001f && State == RunState.Playing)
            {
                float step = Mathf.Min(delta, 0.025f);
                if (Jumping) step = Mathf.Min(step, (1f - JumpFraction) * JumpDuration);
                Step(step); delta -= step;
            }
        }
        private void Step(float dt)
        {
            float oldY = GroundY;
            if (Jumping) JumpFraction = Mathf.Min(1f, JumpFraction + dt / JumpDuration);
            float newY = GroundY;
            Elapsed += dt;
            if (IsRush && Elapsed > Contract.Deadline) { State = RunState.TimedOut; return; }
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                Car car = Cars[i];
                float oldX = car.x; car.x += car.speed * dt;
                float laneY = (car.lane + 0.5f) * IslandSpacing;
                if (SweptHit(new Vector2(-oldX, oldY - laneY), new Vector2(-car.x, newY - laneY),
                    new Vector2(car.HalfWidth + CourierRadius, car.HalfHeight + CourierRadius)))
                { State = RunState.Crashed; return; }
                // A trailing-edge pass only: the vehicle must already be moving away.
                if (Jumping && car.lane == Progress && car.x * car.speed > 0 &&
                    SweptHit(new Vector2(-oldX, oldY - laneY), new Vector2(-car.x, newY - laneY),
                    new Vector2(car.HalfWidth + CourierRadius + settings.closeCallMargin, car.HalfHeight + CourierRadius))) nearMiss = true;
                if (Mathf.Abs(car.x) > TrafficEdge + 2f) Cars.RemoveAt(i);
            }
            if (Jumping && JumpFraction >= 0.999999f)
            {
                if (nearMiss) { CloseCalls++; LastCloseCallAt = Elapsed; }
                Progress++; Jumping = false; JumpFraction = 0;
                if (Progress == LaneCount) { State = RunState.Delivered; return; }
            }
            if (!traffic) return;
            for (int lane = 0; lane < LaneCount; lane++)
            {
                nextSpawn[lane] -= dt;
                if (nextSpawn[lane] > 0) continue;
                Spawn(lane, lane % 2 == 0 ? -TrafficEdge : TrafficEdge);
                nextSpawn[lane] += SpawnInterval();
            }
        }
        private void Spawn(int lane, float x)
        {
            float t = lane / (float)(LaneCount - 1);
            var kind = (VehicleKind)(lane % 3);
            float typeSpeed = kind == VehicleKind.Van ? settings.vanSpeedMultiplier : kind == VehicleKind.Scooter ? settings.scooterSpeedMultiplier : 1;
            float speed = Mathf.Lerp(settings.minimumCarSpeed, settings.maximumCarSpeed, t) *
                Mathf.Lerp(1, settings.finalLaneSpeedMultiplier, t) * typeSpeed * (1 + (Contract?.ShiftStep ?? 0) * settings.shiftSpeedStep) * (IsRush ? settings.rushSpeedMultiplier : 1);
            Cars.Add(new Car { lane = lane, x = x, kind = kind, speed = speed * (lane % 2 == 0 ? 1 : -1) });
        }
        private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        public int Rating => Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(10, 1, Mathf.InverseLerp(settings.perfectDeliverySeconds, settings.onePointDeliverySeconds, Elapsed))), 1, 10);
        public static bool SweptHit(Vector2 start, Vector2 end, Vector2 halfSize)
        {
            float enter = 0, exit = 1;
            Vector2 movement = end - start;
            for (int axis = 0; axis < 2; axis++)
            {
                if (Mathf.Abs(movement[axis]) < 0.000001f)
                { if (Mathf.Abs(start[axis]) > halfSize[axis]) return false; continue; }
                float a = (-halfSize[axis] - start[axis]) / movement[axis];
                float b = (halfSize[axis] - start[axis]) / movement[axis];
                enter = Mathf.Max(enter, Mathf.Min(a, b)); exit = Mathf.Min(exit, Mathf.Max(a, b));
                if (enter > exit) return false;
            }
            return true;
        }
    }
    public static class CourierRating
    {
        private const string CountKey = "CocoCourier.DeliveryCount.v1", SumKey = "CocoCourier.RatingSum.v1";
        public static int Count => PlayerPrefs.GetInt(CountKey, 0);
        public static float Average => Count == 0 ? 0 : (float)PlayerPrefs.GetInt(SumKey, 0) / Count;
        public static void Record(int rating)
        {
            PlayerPrefs.SetInt(SumKey, PlayerPrefs.GetInt(SumKey, 0) + Mathf.Clamp(rating, 1, 10));
            PlayerPrefs.SetInt(CountKey, Count + 1); PlayerPrefs.Save();
        }
    }
}

