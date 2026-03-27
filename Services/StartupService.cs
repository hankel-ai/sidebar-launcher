using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace SidebarLauncher.Services;

public static class StartupService
{
    private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SidebarLauncher";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
        return key?.GetValue(AppName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
        if (key == null) return;

        if (enabled)
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
                key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    public static void UpdateStartupPath()
    {
        if (!IsEnabled()) return;
        // Re-register with current path in case exe was moved
        SetEnabled(true);
    }
}
