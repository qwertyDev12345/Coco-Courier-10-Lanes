using UnityEngine;

namespace CocoCourier
{
    [CreateAssetMenu(menuName = "Coco Courier/Game Settings")]
    public sealed class CourierSettings : ScriptableObject
    {
        [Header("Jump")]
        [Min(0.15f)] public float jumpDuration = 0.65f;
        [Range(0f, 2f)] public float arcHeight = 0.65f;
        [Header("Traffic (world units / second)")]
        [Min(0.2f)] public float minimumCarSpeed = 2.4f;
        [Min(0.2f)] public float maximumCarSpeed = 4.5f;
        [Min(0.5f)] public float minimumSpawnInterval = 1.2f;
        [Min(0.5f)] public float maximumSpawnInterval = 2f;
        [Header("Route difficulty")]
        [Range(1f, 2.5f)] public float finalLaneSpeedMultiplier = 1.45f;
        [Range(0f, .3f)] public float shiftSpeedStep = .08f;
        [Range(.5f, 1f)] public float vanSpeedMultiplier = .8f;
        [Range(1f, 1.6f)] public float scooterSpeedMultiplier = 1.2f;
        [Range(.05f, .5f)] public float closeCallMargin = .25f;
        [Min(0)] public int closeCallCoins = 5;
        [Min(0)] public int contractCoins = 30;
        [Min(0)] public int streakCoins = 75;
        [Min(0)] public int shiftCoins = 40;
        [Min(10)] public float expressDeadline = 24;
        [Range(1f, 1.6f)] public float heavyJumpMultiplier = 1.2f;
        [Header("Activities")]
        [Min(10)] public float kitchenSeconds = 35;
        [Min(1)] public int kitchenDoubleRewardScore = 4;
        [Min(0)] public int lastMeterTip = 15;
        [Range(.08f,.4f)] public float lastMeterGreenWidth = .22f;
        [Min(.5f)] public float lastMeterSweepSeconds = 1.4f;
        [Min(0)] public int lostParcelCoins = 40;
        [Range(1f,1.5f)] public float lostParcelJumpMultiplier = 1.15f;
        [Min(10)] public float rushDeadline = 45;
        [Range(1f,1.5f)] public float rushSpeedMultiplier = 1.12f;
        [Range(.6f,1f)] public float rushSpawnMultiplier = .85f;
        [Min(5)] public float rushSilverSeconds = 30;
        [Min(5)] public float rushGoldSeconds = 20;
        [Header("Delivery rating (no time limit)")]
        [Min(1f)] public float perfectDeliverySeconds = 14f;
        [Min(2f)] public float onePointDeliverySeconds = 65f;
        private void OnValidate()
        {
            jumpDuration = Mathf.Max(0.15f, jumpDuration);
            minimumCarSpeed = Mathf.Max(0.2f, minimumCarSpeed);
            maximumCarSpeed = Mathf.Max(minimumCarSpeed, maximumCarSpeed);
            minimumSpawnInterval = Mathf.Max(0.5f, minimumSpawnInterval);
            maximumSpawnInterval = Mathf.Max(minimumSpawnInterval, maximumSpawnInterval);
            onePointDeliverySeconds = Mathf.Max(perfectDeliverySeconds + 1f, onePointDeliverySeconds);
        }
    }
}
