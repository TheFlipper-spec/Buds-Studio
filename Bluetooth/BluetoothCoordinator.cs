using System;
using System.Runtime.Versioning;
using RealmeBudsPopup.Models;

namespace RealmeBudsPopup.Bluetooth
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public sealed class BluetoothCoordinator : IDisposable
    {
        private readonly BleAdvertisementService _bleService;
        private readonly BluetoothDeviceMonitor _deviceMonitor;
        private DateTime _lastTriggerTime = DateTime.MinValue;
        private readonly TimeSpan _debounceWindow = TimeSpan.FromSeconds(6);
        private readonly object _lock = new();

        public event EventHandler<BudsBatteryState>? BudsConnected;
        public event EventHandler<BudsBatteryState>? BatteryUpdated;
        public event EventHandler<string>? BudsDisconnected;

        public BluetoothCoordinator()
        {
            _bleService = new BleAdvertisementService();
            _deviceMonitor = new BluetoothDeviceMonitor(_bleService);

            // 1. Hardware connection event (from DeviceWatcher) -> triggers popup ONCE!
            _deviceMonitor.DeviceJustConnected += OnDeviceJustConnected;

            // 2. Pre-existing connection on startup -> quiet tray update
            _deviceMonitor.BatteryTelemetryUpdated += OnBatteryTelemetryQuietUpdate;

            // 3. Hardware disconnection -> reset tray
            _deviceMonitor.DeviceDisconnected += OnDeviceDisconnected;

            // 4. BLE advertisement from the connected device -> quiet telemetry update ONLY
            _bleService.BatteryAdvertisementReceived += OnBatteryTelemetryQuietUpdate;
        }

        public void Start()
        {
            _bleService.Start();
            _deviceMonitor.Start();
        }

        public void Stop()
        {
            _bleService.Stop();
            _deviceMonitor.Stop();
        }

        private void OnDeviceJustConnected(object? sender, BudsBatteryState state)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastTriggerTime) > _debounceWindow)
                {
                    _lastTriggerTime = now;
                    BudsConnected?.Invoke(this, state);
                }
                else
                {
                    BatteryUpdated?.Invoke(this, state);
                }
            }
        }

        private void OnDeviceDisconnected(object? sender, string deviceName)
        {
            BudsDisconnected?.Invoke(this, deviceName);
        }

        private void OnBatteryTelemetryQuietUpdate(object? sender, BudsBatteryState state)
        {
            // Quiet telemetry update: updates tray icon and active popup if one is open.
            // NEVER opens a new popup window while listening to music!
            BatteryUpdated?.Invoke(this, state);
        }

        public void Dispose()
        {
            _deviceMonitor.DeviceJustConnected -= OnDeviceJustConnected;
            _deviceMonitor.BatteryTelemetryUpdated -= OnBatteryTelemetryQuietUpdate;
            _deviceMonitor.DeviceDisconnected -= OnDeviceDisconnected;
            _bleService.BatteryAdvertisementReceived -= OnBatteryTelemetryQuietUpdate;
            _bleService.Dispose();
            _deviceMonitor.Dispose();
        }
    }
}
