using System;
using RealmeBudsPopup.Models;

namespace RealmeBudsPopup.Bluetooth
{
    public static class AppleAdvertisementParser
    {
        public static BudsBatteryState? TryParse(ReadOnlySpan<byte> data, string defaultName = "AirPods Pro")
        {
            // Apple Proximity message format (Company 0x004C)
            // data[0]: Type (0x07 = Proximity Pairing / AirPods, 0x10 = Proximity Nearby)
            // data[1]: Length (usually 0x19 = 25 bytes)
            if (data.Length < 25)
                return null;

            if (data[0] != 0x07 && data[0] != 0x10)
                return null;

            try
            {
                // Validate model ID
                ushort modelId = (ushort)(((data[3] & 0xFF) << 8) | (data[4] & 0xFF));
                string? recognizedModel = GetAppleModelName(modelId);
                if (recognizedModel == null)
                    return null; // Reject non-AirPods Apple devices (iPhones, Watches, AirTags, etc.)

                int statusByte = data[5] & 0xFF;
                int podsBattery = data[6] & 0xFF;
                int flagsCase = data[7] & 0xFF;
                int lid = data[8] & 0xFF;

                bool primaryLeft = ((statusByte >> 5) & 0x01) == 1;
                bool thisInCase = ((statusByte >> 6) & 0x01) == 1;
                bool xorFactor = primaryLeft ^ thisInCase;
                bool isFlipped = !primaryLeft;

                int leftNibble = isFlipped ? (podsBattery >> 4) & 0x0F : podsBattery & 0x0F;
                int rightNibble = isFlipped ? podsBattery & 0x0F : (podsBattery >> 4) & 0x0F;
                int caseNibble = flagsCase & 0x0F;
                int flags = (flagsCase >> 4) & 0x0F;

                bool isLeftCharging = isFlipped ? (flags & 0x02) != 0 : (flags & 0x01) != 0;
                bool isRightCharging = isFlipped ? (flags & 0x01) != 0 : (flags & 0x02) != 0;
                bool isCaseCharging = (flags & 0x04) != 0;

                int? leftLevel = DecodeNibble(leftNibble);
                int? rightLevel = DecodeNibble(rightNibble);
                int? caseLevel = DecodeNibble(caseNibble);

                if (leftLevel == null && rightLevel == null && caseLevel == null)
                    return null;

                string modelName = GetAppleModelName(modelId) ?? defaultName;

                var left = leftLevel.HasValue ? new BatteryComponent(leftLevel.Value, isLeftCharging) : null;
                var right = rightLevel.HasValue ? new BatteryComponent(rightLevel.Value, isRightCharging) : null;
                var @case = caseLevel.HasValue ? new BatteryComponent(caseLevel.Value, isCaseCharging) : null;

                return new BudsBatteryState(modelName, left, right, @case);
            }
            catch
            {
                return null;
            }
        }

        private static int? DecodeNibble(int nibble)
        {
            return nibble switch
            {
                >= 0x0 and <= 0x9 => nibble * 10,
                >= 0xA and <= 0xE => 100,
                _ => null
            };
        }

        private static string? GetAppleModelName(ushort modelId)
        {
            return modelId switch
            {
                0x0220 => "AirPods (1st Gen)",
                0x0F20 => "AirPods (2nd Gen)",
                0x1320 => "AirPods (3rd Gen)",
                0x0E20 => "AirPods Pro",
                0x1420 => "AirPods Pro (2nd Gen)",
                0x0A20 => "AirPods Max",
                0x200E => "AirPods Pro",
                0x2014 => "AirPods Pro (2nd Gen)",
                0x0520 => "Powerbeats Pro",
                0x0620 => "Beats Solo Pro",
                0x0B20 => "Beats Studio Buds",
                0x1120 => "Beats Fit Pro",
                _ => null
            };
        }
    }
}
