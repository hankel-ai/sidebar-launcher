using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using SidebarLauncher.Models;

namespace SidebarLauncher.Services;

public static class ShellLauncher
{
    public static void Launch(ShortcutItem item)
    {
        try
        {
            switch (item.Type)
            {
                case ShortcutType.Script:
                    LaunchScript(item.Path);
                    break;

                case ShortcutType.Url:
                    Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                    break;

                case ShortcutType.Folder:
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.Path}\""));
                    break;

                default:
                    Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch:\n{item.Path}\n\n{ex.Message}",
                "Sidebar Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void LaunchScript(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".ps1":
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{path}\"",
                    UseShellExecute = true
                });
                break;

            case ".bat":
            case ".cmd":
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                break;

            default:
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                break;
        }
    }
}
