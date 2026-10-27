using System;
using System.IO;
using Microsoft.Win32;

namespace BluetoothRectifier.Core.Services;

/// <summary>
/// Manages Windows autostart on user login via the Registry:
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "BluetoothRectifier";

    /// <summary>
    /// Checks whether the application is configured to run at Windows startup.
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key == null) return false;

            object? val = key.GetValue(AppValueName);
            if (val is string str && !string.IsNullOrWhiteSpace(str))
            {
                string exePath = Environment.ProcessPath ?? "";
                return str.Contains(Path.GetFileName(exePath), StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Enables or disables automatic startup at login.
    /// </summary>
    public static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    string command = $"\"{exePath}\" --minimized";
                    key.SetValue(AppValueName, command, RegistryValueKind.String);
                }
            }
            else
            {
                key.DeleteValue(AppValueName, throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
