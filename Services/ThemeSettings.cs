using System;
using System.Windows.Media;
using Microsoft.Win32;

namespace RealmeBudsPopup.Services
{
    public enum AppTheme
    {
        Light,
        Dark
    }

    public enum BudColor
    {
        Blue,
        Black,
        White,
        Green,
        Orange,
        Purple,
        Red
    }

    public enum HeadphoneModelType
    {
        InEar,
        OverEar,
        Earbuds
    }

    public sealed class ThemeSettings
    {
        private const string SettingsRegistryKey = @"Software\RealmeBudsPopup";
        public static ThemeSettings Current { get; } = new();

        public AppTheme Theme { get; set; } = AppTheme.Light;
        public BudColor ColorPreset { get; set; } = BudColor.Blue;
        public HeadphoneModelType ModelType { get; set; } = HeadphoneModelType.InEar;
        public bool ShowBatteryInTray { get; set; } = true;
        public double DisplayDurationSeconds { get; set; } = 4.5;
        public double TargetTopY { get; set; } = 42.0;

        public event EventHandler? SettingsChanged;

        private ThemeSettings()
        {
            Load();
        }

        public void Load()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryKey, false);
                if (key != null)
                {
                    string? themeStr = key.GetValue("Theme") as string;
                    if (Enum.TryParse<AppTheme>(themeStr, true, out var t))
                        Theme = t;

                    string? colorStr = key.GetValue("BudColor") as string;
                    if (Enum.TryParse<BudColor>(colorStr, true, out var c))
                        ColorPreset = c;

                    string? modelStr = key.GetValue("ModelType") as string;
                    if (Enum.TryParse<HeadphoneModelType>(modelStr, true, out var m))
                        ModelType = m;

                    if (key.GetValue("ShowBatteryInTray") is int trayVal)
                        ShowBatteryInTray = trayVal != 0;

                    if (key.GetValue("DisplayDuration") is string durStr && double.TryParse(durStr, out var dur))
                        DisplayDurationSeconds = Math.Clamp(dur, 2.0, 15.0);

                    if (key.GetValue("TargetTopY") is string topStr && double.TryParse(topStr, out var top))
                        TargetTopY = Math.Clamp(top, 10.0, 200.0);
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(SettingsRegistryKey, true);
                if (key != null)
                {
                    key.SetValue("Theme", Theme.ToString());
                    key.SetValue("BudColor", ColorPreset.ToString());
                    key.SetValue("ModelType", ModelType.ToString());
                    key.SetValue("ShowBatteryInTray", ShowBatteryInTray ? 1 : 0);
                    key.SetValue("DisplayDuration", DisplayDurationSeconds.ToString("F1"));
                    key.SetValue("TargetTopY", TargetTopY.ToString("F0"));
                }
            }
            catch { }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public SolidColorBrush GetBudBrush()
        {
            return ColorPreset switch
            {
                BudColor.Blue => new SolidColorBrush(Color.FromRgb(0, 102, 204)),    // #0066CC
                BudColor.Black => new SolidColorBrush(Color.FromRgb(31, 41, 55)),    // #1F2937
                BudColor.White => new SolidColorBrush(Color.FromRgb(226, 232, 240)), // #E2E8F0
                BudColor.Green => new SolidColorBrush(Color.FromRgb(16, 185, 129)),  // #10B981
                BudColor.Orange => new SolidColorBrush(Color.FromRgb(249, 115, 22)), // #F97316
                BudColor.Purple => new SolidColorBrush(Color.FromRgb(139, 92, 246)), // #8B5CF6
                BudColor.Red => new SolidColorBrush(Color.FromRgb(225, 29, 72)),     // #E11D48
                _ => new SolidColorBrush(Color.FromRgb(0, 102, 204))
            };
        }

        public SolidColorBrush GetProgressBrush()
        {
            return ColorPreset switch
            {
                BudColor.Blue => new SolidColorBrush(Color.FromRgb(0, 102, 204)),
                BudColor.Black => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                BudColor.White => new SolidColorBrush(Color.FromRgb(14, 165, 233)),
                BudColor.Green => new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                BudColor.Orange => new SolidColorBrush(Color.FromRgb(249, 115, 22)),
                BudColor.Purple => new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                BudColor.Red => new SolidColorBrush(Color.FromRgb(225, 29, 72)),
                _ => new SolidColorBrush(Color.FromRgb(0, 102, 204))
            };
        }

        public SolidColorBrush GetWindowBackground()
        {
            return Theme == AppTheme.Light
                ? new SolidColorBrush(Color.FromArgb(250, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(246, 18, 18, 22));
        }

        public SolidColorBrush GetWindowBorder()
        {
            return Theme == AppTheme.Light
                ? new SolidColorBrush(Color.FromArgb(180, 226, 232, 240))
                : new SolidColorBrush(Color.FromArgb(180, 42, 42, 48));
        }

        public SolidColorBrush GetTrackBrush()
        {
            return Theme == AppTheme.Light
                ? new SolidColorBrush(Color.FromRgb(229, 231, 235)) // #E5E7EB
                : new SolidColorBrush(Color.FromRgb(42, 42, 48));   // #2A2A30
        }

        public SolidColorBrush GetTextPrimary()
        {
            return Theme == AppTheme.Light
                ? new SolidColorBrush(Color.FromRgb(15, 23, 42))  // #0F172A
                : new SolidColorBrush(Color.FromRgb(248, 250, 252)); // #F8FAFC
        }

        public SolidColorBrush GetTextSecondary()
        {
            return Theme == AppTheme.Light
                ? new SolidColorBrush(Color.FromRgb(100, 116, 139)) // #64748B
                : new SolidColorBrush(Color.FromRgb(148, 163, 184)); // #94A3B8
        }
    }
}
