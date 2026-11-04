using System;
using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;
using RealmeBudsPopup.Models;

namespace RealmeBudsPopup.Bluetooth
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public sealed class BleAdvertisementService : IDisposable
    {
        private readonly BluetoothLEAdvertisementWatcher _watcher;
        private readonly ConcurrentDictionary<ulong, string> _knownTargetDevices = new();
        private bool _isDisposed;

        public volatile bool HasConnectedAppleDevice;
        public volatile string? ConnectedAppleDeviceName;

        public event EventHandler<BudsBatteryState>? BatteryAdvertisementReceived;

        public BleAdvertisementService()
        {
            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            _watcher.Received += OnAdvertisementReceived;
        }

        public void Start()
        {
            try
            {
                if (_watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    _watcher.Start();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start BLE Watcher: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    _watcher.Stop();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to stop BLE Watcher: {ex.Message}");
            }
        }

        public void RegisterKnownDevice(ulong address, string name)
        {
            _knownTargetDevices[address] = name;
        }

        public void UnregisterKnownDevice(ulong address)
        {
            _knownTargetDevices.TryRemove(address, out _);
        }

        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                ulong address = args.BluetoothAddress;
                BudsBatteryState? batteryState = null;

                // 1. Check ManufacturerData
                foreach (var mfg in args.Advertisement.ManufacturerData)
                {
                    byte[] bytes = ReadBuffer(mfg.Data);
                    if (bytes.Length == 0)
                        continue;

                    // Apple AirPods (Company 0x004C)
                    // STRICT REQUIREMENT: Only process if an Apple device is actively connected to this PC,
                    // and RSSI is strong (nearby, > -75 dBm) to prevent neighbor bleed-through!
                    if (mfg.CompanyId == 0x004C)
                    {
                        if (HasConnectedAppleDevice && args.RawSignalStrengthInDBm > -75)
                        {
                            string appleName = ConnectedAppleDeviceName ?? "AirPods Pro";
                            var appleState = AppleAdvertisementParser.TryParse(bytes, appleName);
                            if (appleState != null)
                            {
                                batteryState = appleState;
                                break;
                            }
                        }
                        continue;
                    }

                    // For all other devices (Realme / Oppo), require matching registered address
                    if (!_knownTargetDevices.TryGetValue(address, out var deviceName))
                        continue;

                    // Realme / Oppo (Company 0x079A or 0x02E5)
                    if (mfg.CompanyId == 0x079A || mfg.CompanyId == 0x02E5)
                    {
                        var parsed = BatteryPacketParser.ParseManufacturerData(mfg.CompanyId, bytes, deviceName);
                        if (parsed != null)
                        {
                            batteryState = parsed;
                            break;
                        }
                    }
                }

                // 2. Check ServiceData for Google Fast Pair (0xFE2C)
                if (batteryState == null && _knownTargetDevices.TryGetValue(address, out var knownName))
                {
                    foreach (var section in args.Advertisement.DataSections)
                    {
                        // 0x16 is Service Data - 16-bit UUID
                        if (section.DataType == 0x16)
                        {
                            byte[] bytes = ReadBuffer(section.Data);
                            // Fast Pair 16-bit UUID is 0xFE2C (little endian: 0x2C, 0xFE)
                            if (bytes.Length >= 5 && bytes[0] == 0x2C && bytes[1] == 0xFE)
                            {
                                var parsed = BatteryPacketParser.ParseFastPairPayload(bytes.AsSpan(2), knownName);
                                if (parsed != null)
                                {
                                    batteryState = parsed;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (batteryState != null)
                {
                    // Silent telemetry update ONLY
                    BatteryAdvertisementReceived?.Invoke(this, batteryState);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing BLE Advertisement: {ex.Message}");
            }
        }

        public static bool IsMatchingDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            string lower = name.ToLowerInvariant();
            return lower.Contains("t500") ||
                   lower.Contains("buds") ||
                   lower.Contains("airpods") ||
                   lower.Contains("edifier") ||
                   lower.Contains("realme") ||
                   lower.Contains("oppo") ||
                   lower.Contains("oneplus") ||
                   lower.Contains("freebuds") ||
                   lower.Contains("wh-1000") ||
                   lower.Contains("wf-1000") ||
                   lower.Contains("galaxy buds");
        }

        private static byte[] ReadBuffer(IBuffer buffer)
        {
            if (buffer == null || buffer.Length == 0)
                return Array.Empty<byte>();

            using var reader = DataReader.FromBuffer(buffer);
            byte[] bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            return bytes;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _watcher.Received -= OnAdvertisementReceived;
            Stop();
        }
    }
}
