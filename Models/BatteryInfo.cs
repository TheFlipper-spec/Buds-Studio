using System;

namespace RealmeBudsPopup.Models
{
    public sealed class BatteryComponent
    {
        public int? Percent { get; }
        public bool IsCharging { get; }

        public BatteryComponent(int? percent, bool isCharging = false)
        {
            Percent = percent.HasValue ? Math.Clamp(percent.Value, 0, 100) : null;
            IsCharging = isCharging;
        }

        public string DisplayText => Percent.HasValue ? $"{Percent.Value}%" : "—";
        public bool IsAvailable => Percent.HasValue;

        public static BatteryComponent Unavailable => new(null, false);
    }

    public sealed class BudsBatteryState
    {
        public string DeviceName { get; }
        public BatteryComponent Left { get; }
        public BatteryComponent Right { get; }
        public BatteryComponent Case { get; }
        public DateTime Timestamp { get; }

        public BudsBatteryState(string deviceName, BatteryComponent? left, BatteryComponent? right, BatteryComponent? @case)
        {
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "realme Buds T500 Pro" : deviceName;
            Left = left ?? BatteryComponent.Unavailable;
            Right = right ?? BatteryComponent.Unavailable;
            Case = @case ?? BatteryComponent.Unavailable;
            Timestamp = DateTime.UtcNow;
        }

        public static BudsBatteryState CreateDemo()
        {
            return new BudsBatteryState(
                "realme Buds T500 Pro",
                new BatteryComponent(85, false),
                new BatteryComponent(80, false),
                new BatteryComponent(70, true)
            );
        }

        public static BudsBatteryState CreateDemoInEars()
        {
            return new BudsBatteryState(
                "realme Buds T500 Pro",
                new BatteryComponent(90, false),
                new BatteryComponent(90, false),
                BatteryComponent.Unavailable // Case is "—" when in ears!
            );
        }
    }
}
