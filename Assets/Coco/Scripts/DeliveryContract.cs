using UnityEngine;
namespace CocoCourier
{
    public enum OrderKind { Express, Heavy, Fragile }
    public sealed class DeliveryContract
    {
        public bool IsRush { get; }
        public int ClientIndex { get; }
        public int ShiftStep { get; }
        public OrderKind Kind { get; }
        public float Deadline { get; }
        public string Title => Kind == OrderKind.Express ? "HOT LUNCH" : Kind == OrderKind.Heavy ? "HEAVY PARCEL" : "HANDLE WITH CARE";
        public string Condition => Kind == OrderKind.Express ? "Deliver within " + Deadline.ToString("0") + " seconds" : Kind == OrderKind.Heavy ? "Longer jumps. Deliver the heavy parcel" : "Deliver without any close calls";
        public string Stage => ShiftStep == 0 ? "REGULAR ROUTE" : ShiftStep == 1 ? "BUSY ROUTE" : "SPECIAL DELIVERY";
        public DeliveryContract(int clientIndex, int shiftStep, CourierSettings settings, bool isRush = false)
        {
            IsRush = isRush;
            ClientIndex = Mathf.Clamp(clientIndex, 0, 7); ShiftStep = Mathf.Clamp(shiftStep, 0, 2);
            Kind = ClientIndex == 1 || ClientIndex == 4 ? OrderKind.Heavy : ClientIndex == 2 || ClientIndex == 5 || ClientIndex == 6 ? OrderKind.Fragile : OrderKind.Express;
            Deadline = isRush ? settings.rushDeadline : settings.expressDeadline;
        }
    }
}
