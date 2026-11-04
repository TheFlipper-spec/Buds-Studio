using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RealmeBudsPopup.Services
{
    public static class StartupManager
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "RealmeBudsPopup";

        public static string GetExecutablePath()
        {
            return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                if (key == null)
                    return false;

                var value = key.GetValue(AppName) as string;
                if (string.IsNullOrEmpty(value))
                    return false;

                string currentExe = GetExecutablePath();
                return value.Contains(currentExe, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to check autostart: {ex.Message}");
                return false;
            }
        }

        public static bool SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key == null)
                    return false;

                if (enable)
                {
                    string currentExe = GetExecutablePath();
                    if (string.IsNullOrEmpty(currentExe))
                        return false;

                    // Add quotes around path to handle spaces in folder names
                    key.SetValue(AppName, $"\"{currentExe}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set autostart: {ex.Message}");
                return false;
            }
        }
    }
}
