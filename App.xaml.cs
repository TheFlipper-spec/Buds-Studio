using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using RealmeBudsPopup.Bluetooth;
using RealmeBudsPopup.Models;
using RealmeBudsPopup.Services;
using RealmeBudsPopup.Views;

namespace RealmeBudsPopup
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public partial class App : Application
    {
        private static EventWaitHandle? _wakeUpEvent;
        private NotifyIcon? _notifyIcon;
        private BluetoothCoordinator? _coordinator;
        private BudsPopupWindow? _activePopup;
        private SettingsWindow? _settingsWindow;
        private BudsBatteryState? _lastKnownRealState;
        private DispatcherTimer? _backgroundPollTimer;
        private ToolStripMenuItem? _autostartMenuItem;
        private bool _lastCaseWasAvailable = false;
        private bool _isCurrentlyConnected = false;
        private DateTime _lastPopupShownTime = DateTime.MinValue;

        public static void AutoDetectHeadphoneModel(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return;
            string lower = deviceName.ToLowerInvariant();

            var targetModel = (lower.Contains("edifier") ||
                               lower.Contains("w800") ||
                               lower.Contains("wh-") ||
                               lower.Contains("max") ||
                               lower.Contains("over-ear") ||
                               lower.Contains("headphone"))
                ? HeadphoneModelType.OverEar
                : HeadphoneModelType.InEar;

            if (ThemeSettings.Current.ModelType != targetModel)
            {
                ThemeSettings.Current.ModelType = targetModel;
                ThemeSettings.Current.Save();
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try { System.IO.File.WriteAllText("appdomain_crash.log", args.ExceptionObject.ToString()); } catch { }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                try { System.IO.File.WriteAllText("dispatcher_crash.log", args.Exception.ToString()); } catch { }
                args.Handled = true;
            };

            try
            {

            if (e.Args.Contains("--autostart", StringComparer.OrdinalIgnoreCase))
            {
                StartupManager.SetAutoStart(true);
            }
            else if (e.Args.Contains("--remove-autostart", StringComparer.OrdinalIgnoreCase))
            {
                StartupManager.SetAutoStart(false);
            }

            bool isTestMode = e.Args.Contains("--test", StringComparer.OrdinalIgnoreCase) ||
                              e.Args.Contains("--preview", StringComparer.OrdinalIgnoreCase);

            bool isAutostartLaunch = e.Args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);

            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var existingProcesses = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName)
                .Where(p => p.Id != currentProcess.Id)
                .ToList();

            if (existingProcesses.Count > 0 && !isTestMode)
            {
                // Another instance is already active: wake it up to show SettingsWindow
                try
                {
                    using var wake = EventWaitHandle.OpenExisting("RealmeBudsPopup_WakeUpEvent");
                    wake.Set();
                }
                catch { }

                Shutdown();
                return;
            }

            try
            {
                _wakeUpEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "RealmeBudsPopup_WakeUpEvent");
                Task.Run(() =>
                {
                    while (true)
                    {
                        _wakeUpEvent.WaitOne();
                        Dispatcher.Invoke(() => ShowSettingsWindow());
                    }
                });
            }
            catch { }

            InitTrayIcon();
            InitBluetooth();
            InitBackgroundPolling();

            if (isTestMode)
            {
                _ = TriggerDesktopPopupAsync();
            }
            else if (!isAutostartLaunch)
            {
                // Open Settings Window on normal launch so the user can customize right away
                ShowSettingsWindow();
            }
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText("startup_crash.log", ex.ToString()); } catch { }
            }
        }

        private void InitBackgroundPolling()
        {
            // Responsive background monitor (every 2.5 seconds) for live battery and connection events
            _backgroundPollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _backgroundPollTimer.Tick += async (s, e) =>
            {
                try
                {
                    // 1. Query active Bluetooth audio device (Edifier, Sony, Apple, etc.)
                    BudsBatteryState? live = null;
                    if (_coordinator != null)
                    {
                        live = await _coordinator.QueryActiveDeviceBatteryAsync();
                    }

                    // 2. Query Realme serial protocol
                    if (live == null)
                    {
                        live = await RealmeSerialProtocol.QueryBatteryViaSerialAsync("realme Buds T500 Pro");
                    }

                    if (live != null)
                    {
                        bool caseNowAvailable = live.Case.IsAvailable && live.Case.Percent.HasValue && live.Case.Percent.Value > 0;

                        if (!_isCurrentlyConnected)
                        {
                            // NEW PHYSICAL CONNECTION DETECTED! Trigger entrance animation
                            _isCurrentlyConnected = true;
                            _lastCaseWasAvailable = caseNowAvailable;
                            _lastKnownRealState = live;
                            AutoDetectHeadphoneModel(live.DeviceName);
                            UpdateTrayBatteryDisplay(live);
                            ShowPopupWindow(live);
                        }
                        else if (!_lastCaseWasAvailable && caseNowAvailable)
                        {
                            // CASE OPENED / BUDS INSERTED INTO CASE: Trigger popup to show case battery
                            _lastCaseWasAvailable = true;
                            _lastKnownRealState = live;
                            UpdateTrayBatteryDisplay(live);
                            ShowPopupWindow(live);
                        }
                        else
                        {
                            // Continuous telemetry update
                            _lastCaseWasAvailable = caseNowAvailable;
                            _lastKnownRealState = live;
                            UpdateTrayBatteryDisplay(live);
                            if (_activePopup != null && _activePopup.IsLoaded)
                            {
                                _activePopup.UpdateState(live);
                            }
                        }

                        // Forward to settings window overview if open
                        if (_settingsWindow != null && _settingsWindow.IsLoaded)
                        {
                            _settingsWindow.UpdateLiveBatteryState(live);
                        }
                    }
                    else
                    {
                        if (_isCurrentlyConnected && (_coordinator == null || !_coordinator.HasActiveDevices))
                        {
                            // PHYSICAL DISCONNECTION
                            _isCurrentlyConnected = false;
                            _lastCaseWasAvailable = false;
                            _lastKnownRealState = null;
                            UpdateTrayBatteryDisplay(null);
                            if (_settingsWindow != null && _settingsWindow.IsLoaded)
                            {
                                _settingsWindow.UpdateLiveBatteryState(null);
                            }
                        }
                    }
                }
                catch { }
            };
            _backgroundPollTimer.Start();
        }

        public async Task TriggerDesktopPopupAsync()
        {
            // 1. Query live active device from Bluetooth coordinator (Edifier, Sony, etc.)
            if (_coordinator != null)
            {
                var btBattery = await _coordinator.QueryActiveDeviceBatteryAsync();
                if (btBattery != null)
                {
                    _lastKnownRealState = btBattery;
                    AutoDetectHeadphoneModel(btBattery.DeviceName);
                    UpdateTrayBatteryDisplay(btBattery);
                    ShowPopupWindow(btBattery);
                    return;
                }
            }

            // 2. Query Realme serial protocol
            var realBattery = await RealmeSerialProtocol.QueryBatteryViaSerialAsync("realme Buds T500 Pro");
            if (realBattery != null)
            {
                _lastKnownRealState = realBattery;
                AutoDetectHeadphoneModel(realBattery.DeviceName);
                UpdateTrayBatteryDisplay(realBattery);
                ShowPopupWindow(realBattery);
                return;
            }

            // 3. Fallback: last confirmed real state
            if (_lastKnownRealState != null)
            {
                AutoDetectHeadphoneModel(_lastKnownRealState.DeviceName);
                ShowPopupWindow(_lastKnownRealState);
                return;
            }

            // 4. Default realistic preview
            var initialRealistic = new BudsBatteryState(
                "realme Buds T500 Pro",
                new BatteryComponent(80, false),
                new BatteryComponent(80, false),
                new BatteryComponent(60, false)
            );
            AutoDetectHeadphoneModel(initialRealistic.DeviceName);
            ShowPopupWindow(initialRealistic);
        }

        private void InitTrayIcon()
        {
            try
            {
                _notifyIcon = new NotifyIcon
                {
                    Text = "realme Buds T500 Pro Manager",
                    Icon = CreateTrayIcon(null),
                    Visible = true
                };

                var contextMenu = new ContextMenuStrip();

                var titleItem = new ToolStripMenuItem("Bluetooth Buds Manager")
                {
                    Enabled = false,
                    Font = new Font(contextMenu.Font, System.Drawing.FontStyle.Bold)
                };
                contextMenu.Items.Add(titleItem);
                contextMenu.Items.Add(new ToolStripSeparator());

                var settingsItem = new ToolStripMenuItem("Настройки и кастомизация...", null, (s, e) =>
                {
                    ShowSettingsWindow();
                });
                contextMenu.Items.Add(settingsItem);

                var testItem = new ToolStripMenuItem("Показать всплывающее окно", null, async (s, e) =>
                {
                    await TriggerDesktopPopupAsync();
                });
                contextMenu.Items.Add(testItem);

                contextMenu.Items.Add(new ToolStripSeparator());

                _autostartMenuItem = new ToolStripMenuItem("Автозагрузка при старте Windows")
                {
                    CheckOnClick = true,
                    Checked = StartupManager.IsAutoStartEnabled()
                };
                _autostartMenuItem.Click += (s, e) =>
                {
                    if (_autostartMenuItem != null)
                    {
                        StartupManager.SetAutoStart(_autostartMenuItem.Checked);
                    }
                };
                contextMenu.Items.Add(_autostartMenuItem);

                contextMenu.Items.Add(new ToolStripSeparator());

                var exitItem = new ToolStripMenuItem("Выход", null, (s, e) =>
                {
                    ExitApplication();
                });
                contextMenu.Items.Add(exitItem);

                _notifyIcon.ContextMenuStrip = contextMenu;
                _notifyIcon.DoubleClick += (s, e) =>
                {
                    ShowSettingsWindow();
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing Tray Icon: {ex.Message}");
            }
        }

        public void UpdateTrayBatteryDisplay(BudsBatteryState? state)
        {
            if (_notifyIcon == null) return;

            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (state == null)
                    {
                        _notifyIcon.Icon = CreateTrayIcon(null);
                        _notifyIcon.Text = "Buds Studio Pro (Ожидание подключения)";
                        return;
                    }

                    int? mainBattery = state.Left.Percent ?? state.Right.Percent;
                    if (ThemeSettings.Current.ShowBatteryInTray && mainBattery.HasValue)
                    {
                        _notifyIcon.Icon = CreateTrayIcon(mainBattery.Value);
                    }
                    else
                    {
                        _notifyIcon.Icon = CreateTrayIcon(null);
                    }

                    string leftStr = state.Left.DisplayText;
                    string rightStr = state.Right.DisplayText;
                    string caseStr = state.Case.DisplayText;

                    _notifyIcon.Text = $"{state.DeviceName}\nL: {leftStr} | R: {rightStr} | Case: {caseStr}".Length > 63
                        ? $"{state.DeviceName}: {leftStr}/{rightStr}".Substring(0, Math.Min(63, $"{state.DeviceName}: {leftStr}/{rightStr}".Length))
                        : $"{state.DeviceName}\nL: {leftStr} | R: {rightStr} | Case: {caseStr}";
                }
                catch { }
            });
        }

        private static Icon CreateTrayIcon(int? batteryPercent)
        {
            if (!batteryPercent.HasValue)
            {
                try
                {
                    var uri = new Uri("pack://application:,,,/Resources/app.ico");
                    var streamInfo = System.Windows.Application.GetResourceStream(uri);
                    if (streamInfo != null)
                    {
                        using var s = streamInfo.Stream;
                        return new Icon(s);
                    }
                }
                catch { }
            }

            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            // Background badge
            using var bgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(14, 116, 218)); // Royal Blue
            g.FillEllipse(bgBrush, 1, 1, 30, 30);

            if (batteryPercent.HasValue)
            {
                // Draw battery number in tray icon without wrapping
                string numStr = batteryPercent.Value.ToString();
                float fontSize = numStr.Length >= 3 ? 8.2f : (numStr.Length == 2 ? 11f : 12f);
                using var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold);
                using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                };
                g.DrawString(numStr, font, textBrush, new RectangleF(0, 0, 32, 32), format);
            }
            else
            {
                // Default headphone silhouette
                using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2.5f);
                g.DrawArc(pen, 7, 6, 18, 16, 180, 180);

                using var budBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
                g.FillEllipse(budBrush, 6, 14, 5, 8);
                g.FillEllipse(budBrush, 21, 14, 5, 8);
            }

            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }

        public void ShowSettingsWindow()
        {
            Dispatcher.Invoke(() =>
            {
                if (_settingsWindow == null)
                {
                    _settingsWindow = new SettingsWindow();
                }

                _settingsWindow.Show();
                _settingsWindow.Activate();
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }
                if (_lastKnownRealState != null)
                {
                    _settingsWindow.UpdateLiveBatteryState(_lastKnownRealState);
                }
            });
        }

        private void InitBluetooth()
        {
            try
            {
                _coordinator = new BluetoothCoordinator();
                _coordinator.BudsConnected += (s, state) =>
                {
                    _isCurrentlyConnected = true;
                    _lastKnownRealState = state;
                    AutoDetectHeadphoneModel(state.DeviceName);
                    UpdateTrayBatteryDisplay(state);
                    ShowPopupWindow(state);
                };
                _coordinator.BatteryUpdated += (s, state) =>
                {
                    _lastKnownRealState = state;
                    UpdateTrayBatteryDisplay(state);
                    Dispatcher.Invoke(() =>
                    {
                        if (_activePopup != null && _activePopup.IsLoaded)
                        {
                            _activePopup.UpdateState(state);
                        }
                        if (_settingsWindow != null && _settingsWindow.IsLoaded)
                        {
                            _settingsWindow.UpdateLiveBatteryState(state);
                        }
                    });
                };
                _coordinator.BudsDisconnected += (s, name) =>
                {
                    _isCurrentlyConnected = false;
                    _lastCaseWasAvailable = false;
                    _lastKnownRealState = null;
                    UpdateTrayBatteryDisplay(null);
                    Dispatcher.Invoke(() =>
                    {
                        if (_settingsWindow != null && _settingsWindow.IsLoaded)
                        {
                            _settingsWindow.UpdateLiveBatteryState(null);
                        }
                    });
                };
                _coordinator.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bluetooth coordinator error: {ex.Message}");
            }
        }

        public void ShowPopupWindow(BudsBatteryState state)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var now = DateTime.UtcNow;
                    if (_activePopup != null && _activePopup.IsLoaded)
                    {
                        _activePopup.UpdateState(state);
                        return;
                    }

                    if ((now - _lastPopupShownTime).TotalSeconds < 3.0)
                    {
                        return;
                    }
                    _lastPopupShownTime = now;

                    _activePopup = new BudsPopupWindow(state);
                    _activePopup.Closed += (s, e) => _activePopup = null;
                    _activePopup.Show();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error displaying popup: {ex.Message}");
                }
            });
        }

        private void ExitApplication()
        {
            try { System.IO.File.AppendAllText("startup_trace.log", "ExitApplication called: " + Environment.StackTrace + "\n"); } catch { }
            _backgroundPollTimer?.Stop();
            _coordinator?.Dispose();

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _wakeUpEvent?.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ExitApplication();
            base.OnExit(e);
        }
    }
}
