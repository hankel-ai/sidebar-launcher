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
                Process.Start(new ProcessStartInfo("explorer.exe", item.Path) { UseShellExecute = true });
                return;
            }

            var workDir = GetWorkingDirectory(item.Path);
            var args = item.Arguments?.Trim() ?? string.Empty;

            switch (item.Type)
            {
                case ShortcutType.Script:
                    LaunchScript(item.Path, args, workDir);
                    break;

                case ShortcutType.Url:
                    Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                    break;

                case ShortcutType.Folder:
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.Path,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                    break;

                case ShortcutType.Terminal:
                    if (item.NewTab)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "wt.exe",
                            Arguments = $"-w 0 nt cmd /k {Join(item.Path, args)}",
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/k {Join(item.Path, args)}",
                            WorkingDirectory = workDir,
                            UseShellExecute = true
                        });
                    }
                    break;

                default:
                    Process.Start(new ProcessStartInfo(item.Path)
                    {
                        Arguments = args,
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

    public static void OpenTerminalAt(string folderPath)
    {
        try
        {
            // Try Windows Terminal first — opens a new tab in the current window if one exists
            Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-w 0 nt -d \"{folderPath}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // Fall back to cmd if wt.exe is not installed
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = folderPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open terminal at:\n{folderPath}\n\n{ex.Message}",
                    "Sidebar Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

    private static string Join(string a, string b)
        => string.IsNullOrEmpty(b) ? a : a + " " + b;

    private static void LaunchScript(string path, string args, string workDir)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".ps1":
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = Join($"-ExecutionPolicy Bypass -File \"{path}\"", args),
                    WorkingDirectory = workDir,
                    UseShellExecute = true
                });
                break;

            case ".bat":
            case ".cmd":
            default:
                Process.Start(new ProcessStartInfo(path)
                {
                    Arguments = args,
                    WorkingDirectory = workDir,
                    UseShellExecute = true
                });
                break;
        }
    }
}
