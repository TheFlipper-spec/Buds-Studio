using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using RealmeBudsPopup.Models;

namespace RealmeBudsPopup.Bluetooth
{
    public static class RealmeSerialProtocol
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint OPEN_EXISTING = 3;

        private static readonly SemaphoreSlim _serialLock = new(1, 1);
        private static string? _cachedRealmePort = "COM7";

        public static string? ActivePort => _cachedRealmePort;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCommTimeouts(SafeFileHandle hFile, ref COMMTIMEOUTS lpCommTimeouts);

        [StructLayout(LayoutKind.Sequential)]
        private struct COMMTIMEOUTS
        {
            public uint ReadIntervalTimeout;
            public uint ReadTotalTimeoutMultiplier;
            public uint ReadTotalTimeoutConstant;
            public uint WriteTotalTimeoutMultiplier;
            public uint WriteTotalTimeoutConstant;
        }

        public static List<string> FindAllBluetoothComPorts(string? macFilter = null)
        {
            var ports = new List<string>();
            try
            {
                using var bthKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\BTHENUM");
                if (bthKey == null) return ports;

                foreach (var groupName in bthKey.GetSubKeyNames())
                {
                    if (!groupName.Contains("{00001101-0000-1000-8000-00805f9b34fb}", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var groupKey = bthKey.OpenSubKey(groupName);
                    if (groupKey == null) continue;

                    foreach (var deviceSubKey in groupKey.GetSubKeyNames())
                    {
                        if (macFilter != null && !deviceSubKey.Contains(macFilter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Exclude local loopbacks 000000000000
                        if (deviceSubKey.Contains("000000000000", StringComparison.OrdinalIgnoreCase))
                            continue;

                        using var devParams = groupKey.OpenSubKey($@"{deviceSubKey}\Device Parameters");
                        if (devParams != null)
                        {
                            var port = devParams.GetValue("PortName") as string;
                            if (!string.IsNullOrEmpty(port) && port.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!ports.Contains(port))
                                    ports.Add(port);
                            }
                        }
                    }
                }
            }
            catch { }

            return ports;
        }

        public static async Task<BudsBatteryState?> QueryBatteryViaSerialAsync(string deviceName, string? macAddressHex = null)
        {
            await _serialLock.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    var candidatePorts = new List<string>();

                    // Prioritize ports matching the specific device MAC first
                    if (!string.IsNullOrEmpty(macAddressHex))
                    {
                        var macPorts = FindAllBluetoothComPorts(macAddressHex);
                        foreach (var p in macPorts)
                        {
                            if (!candidatePorts.Contains(p))
                                candidatePorts.Add(p);
                        }
                    }

                    // Check cached port next (e.g. COM7)
                    if (!string.IsNullOrEmpty(_cachedRealmePort) && !candidatePorts.Contains(_cachedRealmePort))
                        candidatePorts.Add(_cachedRealmePort);

                    // Add all remaining hardware bluetooth COM ports
                    var found = FindAllBluetoothComPorts(null);
                    foreach (var p in found)
                    {
                        if (!candidatePorts.Contains(p))
                            candidatePorts.Add(p);
                    }

                    foreach (var port in candidatePorts)
                    {
                        var state = TryQueryPort(port, deviceName);
                        if (state != null)
                        {
                            _cachedRealmePort = port;
                            return state;
                        }
                    }

                    return null;
                });
            }
            finally
            {
                _serialLock.Release();
            }
        }

        private static BudsBatteryState? TryQueryPort(string port, string deviceName)
        {
            string portPath = $@"\\.\{port}";

            SafeFileHandle handle = CreateFile(
                portPath,
                GENERIC_READ | GENERIC_WRITE,
                0,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero
            );

            if (handle.IsInvalid)
                return null;

            using (handle)
            {
                COMMTIMEOUTS timeouts = new COMMTIMEOUTS
                {
                    ReadIntervalTimeout = 40,
                    ReadTotalTimeoutConstant = 1000,
                    ReadTotalTimeoutMultiplier = 0,
                    WriteTotalTimeoutConstant = 1000,
                    WriteTotalTimeoutMultiplier = 0
                };
                SetCommTimeouts(handle, ref timeouts);

                using var stream = new FileStream(handle, FileAccess.ReadWrite);

                // 1. Handshake (Cmd 0x0100): AA 07 00 00 00 01 01 00 00
                byte[] handshake = new byte[] { 0xAA, 0x07, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00 };
                stream.Write(handshake, 0, handshake.Length);
                stream.Flush();

                Thread.Sleep(80);

                // 2. Query Battery (Cmd 0x0106): AA 07 00 00 06 01 02 00 00
                byte[] query = new byte[] { 0xAA, 0x07, 0x00, 0x00, 0x06, 0x01, 0x02, 0x00, 0x00 };
                stream.Write(query, 0, query.Length);
                stream.Flush();

                byte[] buf = new byte[512];
                int totalRead = 0;
                var deadline = DateTime.UtcNow.AddMilliseconds(450);

                while (DateTime.UtcNow < deadline && totalRead < buf.Length - 16)
                {
                    Thread.Sleep(45);
                    try
                    {
                        int n = stream.Read(buf, totalRead, buf.Length - totalRead);
                        if (n > 0)
                        {
                            totalRead += n;
                            // Stop early once we have the 0x8106 battery response frame
                            bool hasBatteryReply = false;
                            for (int i = 0; i < totalRead - 8; i++)
                            {
                                if (buf[i] == 0x06 && buf[i + 1] == 0x81)
                                {
                                    int payLen = buf[i + 3] | (buf[i + 4] << 8);
                                    if (i + 5 + payLen <= totalRead)
                                    {
                                        hasBatteryReply = true;
                                        break;
                                    }
                                }
                            }
                            if (hasBatteryReply) break;
                        }
                    }
                    catch { break; }
                }

                if (totalRead <= 0)
                    return null;

                ReadOnlySpan<byte> data = buf.AsSpan(0, totalRead);

                // Look for battery reply Cmd 0x8106 (bytes: 0x06, 0x81)
                for (int i = 0; i < totalRead - 8; i++)
                {
                    if (data[i] == 0x06 && data[i + 1] == 0x81)
                    {
                        if (i + 4 < totalRead)
                        {
                            int payLen = data[i + 3] | (data[i + 4] << 8);
                            int payStart = i + 5;
                            if (payStart + payLen <= totalRead)
                            {
                                var payload = data.Slice(payStart, payLen);
                                var result = BatteryPacketParser.ParseOppoBatteryPayload(payload, deviceName);
                                if (result != null)
                                    return result;
                            }
                        }
                    }
                }

                return BatteryPacketParser.ParseOppoProtocolFrame(data, deviceName);
            }
        }

        public static async Task<bool> SetAncModeAsync(byte mode)
        {
            return await Task.Run(() =>
            {
                string targetPort = _cachedRealmePort ?? "COM7";
                string portPath = $@"\\.\{targetPort}";

                SafeFileHandle handle = CreateFile(
                    portPath,
                    GENERIC_READ | GENERIC_WRITE,
                    0,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero
                );

                if (handle.IsInvalid)
                    return false;

                using (handle)
                {
                    COMMTIMEOUTS timeouts = new COMMTIMEOUTS
                    {
                        ReadIntervalTimeout = 40,
                        ReadTotalTimeoutConstant = 400,
                        ReadTotalTimeoutMultiplier = 0,
                        WriteTotalTimeoutConstant = 400,
                        WriteTotalTimeoutMultiplier = 0
                    };
                    SetCommTimeouts(handle, ref timeouts);

                    try
                    {
                        using var stream = new FileStream(handle, FileAccess.ReadWrite);

                        // 1. Handshake
                        byte[] handshake = new byte[] { 0xAA, 0x07, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00 };
                        stream.Write(handshake, 0, handshake.Length);
                        stream.Flush();

                        Thread.Sleep(50);

                        // 2. Set ANC mode cmd: 0=Off, 1=Noise Cancel, 2=Transparency
                        byte[] ancCmd = new byte[] { 0xAA, 0x08, 0x00, 0x00, 0x08, 0x01, 0x02, 0x01, mode };
                        stream.Write(ancCmd, 0, ancCmd.Length);
                        stream.Flush();

                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            });
        }
    }
}
