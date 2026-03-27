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
            // Handle shell: protocol paths (UWP/PWA apps)
            if (item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", item.Path));
                return;
            }

            var workDir = GetWorkingDirectory(item.Path);

            switch (item.Type)
            {
                case ShortcutType.Script:
                    LaunchScript(item.Path, workDir);
                    break;

                case ShortcutType.Url:
                    Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                    break;

                case ShortcutType.Folder:
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.Path}\""));
                    break;

                default:
                    Process.Start(new ProcessStartInfo(item.Path)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = workDir
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch:\n{item.Path}\n\n{ex.Message}",
                "Sidebar Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string GetWorkingDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return path;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                return dir;
        }
        catch { }
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static void LaunchScript(string path, string workDir)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".ps1":
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{path}\"",
                    WorkingDirectory = workDir,
                    UseShellExecute = true
                });
                break;

            case ".bat":
            case ".cmd":
                Process.Start(new ProcessStartInfo(path)
                {
                    WorkingDirectory = workDir,
                    UseShellExecute = true
                });
                break;

            default:
                Process.Start(new ProcessStartInfo(path)
                {
                    WorkingDirectory = workDir,
                    UseShellExecute = true
                });
                break;
        }
    }
}
