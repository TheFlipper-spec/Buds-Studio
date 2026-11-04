using System;
using RealmeBudsPopup.Models;

namespace RealmeBudsPopup.Bluetooth
{
    public static class BatteryPacketParser
    {
        /// <summary>
        /// Parses QuickBuds / Oppo protocol packet (Cmd 0x8106 or 0x0204).
        /// Frame format: AA [LEB128 len] [slice=00] 00 [cmd LE] [seq] [payLen LE] [payload...]
        /// </summary>
        public static BudsBatteryState? ParseOppoProtocolFrame(ReadOnlySpan<byte> frame, string deviceName)
        {
            if (frame.Length < 9 || frame[0] != 0xAA)
                return null;

            // Simple LEB128 skip
            int offset = 1;
            while (offset < frame.Length && (frame[offset] & 0x80) != 0)
                offset++;
            offset++; // Skip last length byte

            // Expect: slice (1 byte), zero (1 byte)
            if (offset + 6 > frame.Length)
                return null;

            offset += 2; // skip slice and zero

            ushort cmd = (ushort)(frame[offset] | (frame[offset + 1] << 8));
            offset += 2; // skip cmd
            byte seq = frame[offset++]; // skip seq
            ushort payLen = (ushort)(frame[offset] | (frame[offset + 1] << 8));
            offset += 2;

            if (offset + payLen > frame.Length)
                return null;

            ReadOnlySpan<byte> payload = frame.Slice(offset, payLen);

            // 0x8106 = Battery response, 0x0204 = Active push notification
            if (cmd == 0x8106)
            {
                return ParseOppoBatteryPayload(payload, deviceName);
            }
            else if (cmd == 0x0204)
            {
                // Active report: payload[0] == 0x01 is EVT_BATTERY
                if (payload.Length >= 2 && payload[0] == 0x01)
                {
                    return ParseOppoBatteryPayload(payload.Slice(1), deviceName);
                }
            }

            return null;
        }

        /// <summary>
        /// Parses raw Oppo/Realme battery payload (either after status byte or direct count).
        /// Format: [status 00] [count] [comp, raw]...
        /// comp: 1=Left, 2=Right, 3=Case
        /// raw: percent = raw & 0x7F, charging = (raw & 0x80) != 0
        /// </summary>
        public static BudsBatteryState? ParseOppoBatteryPayload(ReadOnlySpan<byte> data, string deviceName)
        {
            if (data.Length >= 2 && data[0] == 0x00)
                data = data.Slice(1);

            if (data.Length < 1)
                return null;

            byte count = data[0];
            BatteryComponent? left = null;
            BatteryComponent? right = null;
            BatteryComponent? @case = null;

            // Pattern A: count of items followed by pairs (comp, raw)
            if (count > 0 && count <= 8 && data.Length >= 1 + count * 2)
            {
                for (int i = 0; i < count; i++)
                {
                    byte comp = data[1 + i * 2];
                    byte raw = data[2 + i * 2];
                    ApplyOppoEntry(comp, raw, ref left, ref right, ref @case);
                }

                if (left != null || right != null || @case != null)
                {
                    return new BudsBatteryState(deviceName, left, right, @case);
                }
            }

            // Pattern B: direct [comp, raw] pairs without count header
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                byte comp = data[i];
                byte raw = data[i + 1];
                if (comp >= 1 && comp <= 3)
                {
                    ApplyOppoEntry(comp, raw, ref left, ref right, ref @case);
                }
            }

            if (left != null || right != null || @case != null)
            {
                return new BudsBatteryState(deviceName, left, right, @case);
            }

            return null;
        }

        private static void ApplyOppoEntry(byte comp, byte raw, ref BatteryComponent? left, ref BatteryComponent? right, ref BatteryComponent? @case)
        {
            int level = raw & 0x7F;
            bool charging = (raw & 0x80) != 0;

            // If level is 0 or 0x7F or > 100, component is unavailable / lid closed / in ears
            BatteryComponent? compInfo = (level > 0 && level <= 100)
                ? new BatteryComponent(level, charging)
                : null;

            switch (comp)
            {
                case 1:
                    left = compInfo;
                    break;
                case 2:
                    right = compInfo;
                    break;
                case 3:
                    @case = compInfo;
                    break;
            }
        }

        /// <summary>
        /// Parses Google Fast Pair (Service UUID 0xFE2C) battery advertisement.
        /// </summary>
        public static BudsBatteryState? ParseFastPairPayload(ReadOnlySpan<byte> data, string deviceName)
        {
            if (data.Length < 3)
                return null;

            // Fast pair battery packets typically contain 3 components: Left, Right, Case.
            // Often preceded by a salt or header byte.
            int offset = (data.Length >= 4 && (data[0] & 0xF0) != 0) ? 1 : 0;

            if (offset + 3 <= data.Length)
            {
                BatteryComponent? left = ParseComponentByte(data[offset]);
                BatteryComponent? right = ParseComponentByte(data[offset + 1]);
                BatteryComponent? @case = ParseComponentByte(data[offset + 2]);

                if (left != null || right != null)
                {
                    return new BudsBatteryState(deviceName, left, right, @case);
                }
            }

            return null;
        }

        /// <summary>
        /// Parses Oppo/Realme BLE manufacturer data.
        /// Strictly validates companyId: 0x079A (Oppo/Realme) or 0x02E5.
        /// </summary>
        public static BudsBatteryState? ParseManufacturerData(ushort companyId, ReadOnlySpan<byte> data, string deviceName)
        {
            if (companyId != 0x079A && companyId != 0x02E5)
                return null;

            if (data.Length < 3)
                return null;

            // Try Oppo format first
            var oppoState = ParseOppoBatteryPayload(data, deviceName);
            if (oppoState != null)
                return oppoState;

            return null;
        }

        private static BatteryComponent? ParseComponentByte(byte raw)
        {
            int level = raw & 0x7F;
            bool charging = (raw & 0x80) != 0;

            // 0x7F (127) means unavailable / not reporting
            if (level == 0x7F || level == 0 || level > 100)
                return null;

            return new BatteryComponent(level, charging);
        }
    }
}
