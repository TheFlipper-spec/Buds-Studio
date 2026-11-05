using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Microsoft.Win32.SafeHandles;
using RealmeBudsPopup.Models;

namespace RealmeBudsPopup.Bluetooth
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public sealed class BluetoothDeviceMonitor : IDisposable
    {
        private static readonly Guid RealmeOppoRfcommUuid = new("0000079A-D102-11E1-9B23-00025B00A5A5");
        private static readonly Guid StandardBatteryServiceUuid = GattServiceUuids.Battery;
        private static readonly Guid BatteryLevelCharacteristicUuid = GattCharacteristicUuids.BatteryLevel;

        private readonly DeviceWatcher _deviceWatcher;
        private readonly BleAdvertisementService _bleService;
        private readonly ConcurrentDictionary<string, (ulong Address, string Name)> _activeConnectedDevices = new();
        private bool _isInitialEnumerationComplete;
        private bool _isDisposed;

        public event EventHandler<BudsBatteryState>? DeviceJustConnected;
        public event EventHandler<BudsBatteryState>? BatteryTelemetryUpdated;
        public event EventHandler<string>? DeviceDisconnected;

        public BluetoothDeviceMonitor(BleAdvertisementService bleService)
        {
            _bleService = bleService;

            string selector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            _deviceWatcher = DeviceInformation.CreateWatcher(
                selector,
                new[] { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" }
            );

            _deviceWatcher.Added += OnDeviceAdded;
            _deviceWatcher.Updated += OnDeviceUpdated;
            _deviceWatcher.Removed += OnDeviceRemoved;
            _deviceWatcher.EnumerationCompleted += (s, e) => _isInitialEnumerationComplete = true;
        }

        public void Start()
        {
            try
            {
                if (_deviceWatcher.Status != DeviceWatcherStatus.Started)
                {
                    _deviceWatcher.Start();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start Bluetooth DeviceWatcher: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                if (_deviceWatcher.Status == DeviceWatcherStatus.Started)
                {
                    _deviceWatcher.Stop();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to stop Bluetooth DeviceWatcher: {ex.Message}");
            }
        }

        private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation deviceInfo)
        {
            CheckDevice(deviceInfo);
        }

        private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (update.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var isConnObj) && isConnObj is bool isConn && !isConn)
                    {
                        if (_activeConnectedDevices.TryRemove(update.Id, out var devInfo))
                        {
                            _bleService.UnregisterKnownDevice(devInfo.Address);
                            bool anyAppleRemaining = _activeConnectedDevices.Values.Any(d =>
                                d.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                                d.Name.Contains("Beats", StringComparison.OrdinalIgnoreCase));
                            _bleService.HasConnectedAppleDevice = anyAppleRemaining;
                            if (!anyAppleRemaining) _bleService.ConnectedAppleDeviceName = null;

                            DeviceDisconnected?.Invoke(this, devInfo.Name);
                        }
                        return;
                    }

                    var info = await DeviceInformation.CreateFromIdAsync(update.Id);
                    if (info != null)
                    {
                        CheckDevice(info);
                    }
                }
                catch { }
            });
        }

        private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            if (_activeConnectedDevices.TryRemove(update.Id, out var devInfo))
            {
                _bleService.UnregisterKnownDevice(devInfo.Address);
                bool anyAppleRemaining = _activeConnectedDevices.Values.Any(d =>
                    d.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                    d.Name.Contains("Beats", StringComparison.OrdinalIgnoreCase));
                _bleService.HasConnectedAppleDevice = anyAppleRemaining;
                if (!anyAppleRemaining) _bleService.ConnectedAppleDeviceName = null;

                DeviceDisconnected?.Invoke(this, devInfo.Name);
            }
        }

        private void CheckDevice(DeviceInformation deviceInfo)
        {
            if (string.IsNullOrWhiteSpace(deviceInfo.Name))
                return;

            if (!BleAdvertisementService.IsMatchingDeviceName(deviceInfo.Name))
                return;

            // Only proceed if this physical device was not already recorded as connected
            if (_activeConnectedDevices.ContainsKey(deviceInfo.Id))
                return;

            _ = Task.Run(async () =>
            {
                BluetoothDevice? bluetoothDevice = null;
                try
                {
                    bluetoothDevice = await BluetoothDevice.FromIdAsync(deviceInfo.Id);
                    if (bluetoothDevice == null)
                        return;

                    string deviceName = deviceInfo.Name;
                    ulong address = bluetoothDevice.BluetoothAddress;

                    if (!_activeConnectedDevices.TryAdd(deviceInfo.Id, (address, deviceName)))
                        return;

                    _bleService.RegisterKnownDevice(address, deviceName);

                    bool isApple = deviceName.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                                   deviceName.Contains("Beats", StringComparison.OrdinalIgnoreCase);
                    if (isApple)
                    {
                        _bleService.HasConnectedAppleDevice = true;
                        _bleService.ConnectedAppleDeviceName = deviceName;
                    }

                    var batteryState = await QueryConnectedBudsInternalAsync(bluetoothDevice, deviceName);
                    if (batteryState == null && isApple)
                    {
                        batteryState = new BudsBatteryState(
                            deviceName,
                            new BatteryComponent(null, false),
                            new BatteryComponent(null, false),
                            BatteryComponent.Unavailable
                        );
                    }

                    if (batteryState != null)
                    {
                        if (_isInitialEnumerationComplete)
                        {
                            // NEW PHYSICAL CONNECTION: Trigger popup ONCE!
                            DeviceJustConnected?.Invoke(this, batteryState);
                        }
                        else
                        {
                            // Pre-existing on launch: Quiet tray update only, NO POPUP!
                            BatteryTelemetryUpdated?.Invoke(this, batteryState);
                        }
                    }
                }
                catch { }
                finally
                {
                    bluetoothDevice?.Dispose();
                }
            });
        }

        private async Task<BudsBatteryState?> QueryConnectedBudsInternalAsync(BluetoothDevice bluetoothDevice, string deviceName)
        {
            try
            {
                string macHex = bluetoothDevice.BluetoothAddress.ToString("X12");

                // 1. Realme / Oppo / OnePlus Serial Protocol
                var serialBattery = await RealmeSerialProtocol.QueryBatteryViaSerialAsync(deviceName, macHex);
                if (serialBattery != null)
                    return serialBattery;

                // 2. Windows PnP Device Property (DEVPKEY_Device_BatteryLevel)
                // Instantly resolves Edifier W800BT/W820NB, Sony, JBL, Bose, and any standard HFP/AVRCP headset connected to Windows!
                var pnpBattery = await TryQueryWindowsPnpBatteryAsync(deviceName, macHex);
                if (pnpBattery != null)
                    return pnpBattery;

                // 3. Edifier Protocol (if device is Edifier and has COM port)
                if (deviceName.Contains("Edifier", StringComparison.OrdinalIgnoreCase))
                {
                    var edifierBattery = await TryQueryEdifierBatteryAsync(deviceName);
                    if (edifierBattery != null)
                        return edifierBattery;
                }

                // 4. Fallback to standard BLE GATT Battery Service (any Bluetooth headset)
                var gattBattery = await TryQueryGattBatteryAsync(bluetoothDevice, deviceName);
                if (gattBattery != null)
                    return gattBattery;

                // 5. RFCOMM SPP socket fallback
                var sppBattery = await TryQueryRfcommBatteryAsync(bluetoothDevice, deviceName);
                if (sppBattery != null)
                    return sppBattery;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error querying device {deviceName}: {ex.Message}");
            }

            return null;
        }

        public bool HasActiveDevices => !_activeConnectedDevices.IsEmpty;

        public async Task<BudsBatteryState?> QueryActiveDeviceBatteryAsync()
        {
            foreach (var kvp in _activeConnectedDevices)
            {
                string deviceId = kvp.Key;
                string deviceName = kvp.Value.Name;

                BluetoothDevice? bluetoothDevice = null;
                try
                {
                    bluetoothDevice = await BluetoothDevice.FromIdAsync(deviceId);
                    if (bluetoothDevice != null)
                    {
                        var state = await QueryConnectedBudsInternalAsync(bluetoothDevice, deviceName);
                        if (state != null)
                            return state;
                    }
                }
                catch { }
                finally
                {
                    bluetoothDevice?.Dispose();
                }
            }

            // Direct PnP scan for connected Bluetooth audio device reporting battery
            try
            {
                var pnpDevs = await DeviceInformation.FindAllAsync(
                    "System.Devices.DeviceInstanceId:~~\"BTHENUM\"",
                    new[] { "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2" },
                    DeviceInformationKind.Device
                );

                foreach (var dev in pnpDevs)
                {
                    if (dev.Properties.TryGetValue("{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2", out var val) && val != null)
                    {
                        byte level = Convert.ToByte(val);
                        if (level > 0 && level <= 100)
                        {
                            string cleanName = dev.Name.Replace(" Hands-Free AG", "").Replace(" Stereo", "").Trim();
                            return new BudsBatteryState(
                                cleanName,
                                new BatteryComponent(level, false),
                                new BatteryComponent(level, false),
                                BatteryComponent.Unavailable
                            );
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static async Task<BudsBatteryState?> TryQueryWindowsPnpBatteryAsync(string deviceName, string macHex)
        {
            try
            {
                string[] props = new[] { "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2" };

                // 1. Search by MAC hex address in DeviceInstanceId
                string aqs = $"System.Devices.DeviceInstanceId:~~\"{macHex}\"";
                var devs = await DeviceInformation.FindAllAsync(aqs, props, DeviceInformationKind.Device);
                foreach (var dev in devs)
                {
                    if (dev.Properties.TryGetValue("{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2", out var val) && val != null)
                    {
                        byte level = Convert.ToByte(val);
                        if (level > 0 && level <= 100)
                        {
                            return new BudsBatteryState(
                                deviceName,
                                new BatteryComponent(level, false),
                                new BatteryComponent(level, false),
                                BatteryComponent.Unavailable
                            );
                        }
                    }
                }

                // 2. Search by deviceName
                string nameAqs = $"System.ItemNameDisplay:~~\"{deviceName}\"";
                var nameDevs = await DeviceInformation.FindAllAsync(nameAqs, props, DeviceInformationKind.Device);
                foreach (var dev in nameDevs)
                {
                    if (dev.Properties.TryGetValue("{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2", out var val) && val != null)
                    {
                        byte level = Convert.ToByte(val);
                        if (level > 0 && level <= 100)
                        {
                            return new BudsBatteryState(
                                deviceName,
                                new BatteryComponent(level, false),
                                new BatteryComponent(level, false),
                                BatteryComponent.Unavailable
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to query Windows PnP battery: {ex.Message}");
            }
            return null;
        }

        private static async Task<BudsBatteryState?> TryQueryEdifierBatteryAsync(string deviceName)
        {
            return await Task.Run(() =>
            {
                var ports = RealmeSerialProtocol.FindAllBluetoothComPorts();
                foreach (var port in ports)
                {
                    string portPath = $@"\\.\{port}";
                    SafeFileHandle handle = CreateFile(portPath, 0xC0000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid) continue;

                    using (handle)
                    {
                        try
                        {
                            using var stream = new FileStream(handle, FileAccess.ReadWrite);
                            // Query: AA 01 D0 21 94
                            byte[] req = new byte[] { 0xAA, 0x01, 0xD0, 0x21, 0x94 };
                            stream.Write(req, 0, req.Length);
                            stream.Flush();

                            Thread.Sleep(100);

                            byte[] buf = new byte[64];
                            int read = stream.Read(buf, 0, buf.Length);
                            if (read >= 4 && (buf[0] == 0xBB || buf[0] == 0xCC) && buf[2] == 0xD0)
                            {
                                int level = buf[3];
                                if (level > 0 && level <= 100)
                                {
                                    return new BudsBatteryState(
                                        deviceName,
                                        new BatteryComponent(level, false),
                                        new BatteryComponent(level, false),
                                        BatteryComponent.Unavailable
                                    );
                                }
                            }
                        }
                        catch { }
                    }
                }
                return null;
            });
        }

        private static async Task<BudsBatteryState?> TryQueryGattBatteryAsync(BluetoothDevice bluetoothDevice, string deviceName)
        {
            try
            {
                var leDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothDevice.BluetoothAddress);
                if (leDevice == null)
                    return null;

                using (leDevice)
                {
                    var gattServices = await leDevice.GetGattServicesForUuidAsync(StandardBatteryServiceUuid);
                    if (gattServices.Status != GattCommunicationStatus.Success || gattServices.Services.Count == 0)
                        return null;

                    var service = gattServices.Services[0];
                    var chars = await service.GetCharacteristicsForUuidAsync(BatteryLevelCharacteristicUuid);
                    if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0)
                        return null;

                    var characteristic = chars.Characteristics[0];
                    var readResult = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                    if (readResult.Status == GattCommunicationStatus.Success && readResult.Value.Length > 0)
                    {
                        using var reader = DataReader.FromBuffer(readResult.Value);
                        byte level = reader.ReadByte();
                        if (level <= 100 && level > 0)
                        {
                            return new BudsBatteryState(
                                deviceName,
                                new BatteryComponent(level, false),
                                new BatteryComponent(level, false),
                                BatteryComponent.Unavailable
                            );
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static async Task<BudsBatteryState?> TryQueryRfcommBatteryAsync(BluetoothDevice bluetoothDevice, string deviceName)
        {
            StreamSocket? socket = null;
            try
            {
                var servicesResult = await bluetoothDevice.GetRfcommServicesForIdAsync(
                    RfcommServiceId.FromUuid(RealmeOppoRfcommUuid),
                    BluetoothCacheMode.Uncached
                );

                if (servicesResult.Services.Count == 0)
                {
                    servicesResult = await bluetoothDevice.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
                }

                if (servicesResult.Services.Count == 0)
                    return null;

                var service = servicesResult.Services[0];
                socket = new StreamSocket();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(cts.Token);

                byte[] handshake = new byte[] { 0xAA, 0x07, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00 };
                using (var writer = new DataWriter(socket.OutputStream))
                {
                    writer.WriteBytes(handshake);
                    await writer.StoreAsync().AsTask(cts.Token);
                    writer.DetachStream();
                }

                await Task.Delay(50, cts.Token);

                byte[] queryBattery = new byte[] { 0xAA, 0x07, 0x00, 0x00, 0x06, 0x01, 0x02, 0x00, 0x00 };
                using (var writer = new DataWriter(socket.OutputStream))
                {
                    writer.WriteBytes(queryBattery);
                    await writer.StoreAsync().AsTask(cts.Token);
                    writer.DetachStream();
                }

                using var reader = new DataReader(socket.InputStream)
                {
                    InputStreamOptions = InputStreamOptions.Partial
                };

                uint loaded = await reader.LoadAsync(256).AsTask(cts.Token);
                if (loaded > 0)
                {
                    byte[] response = new byte[loaded];
                    reader.ReadBytes(response);
                    var state = BatteryPacketParser.ParseOppoProtocolFrame(response, deviceName);
                    if (state != null)
                        return state;
                }
            }
            catch { }
            finally
            {
                socket?.Dispose();
            }

            return null;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _deviceWatcher.Added -= OnDeviceAdded;
            _deviceWatcher.Updated -= OnDeviceUpdated;
            _deviceWatcher.Removed -= OnDeviceRemoved;
            Stop();
        }
    }
}
