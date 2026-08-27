using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using SidebarLauncher.Services;

namespace SidebarLauncher;

public partial class App : Application
{
    private static string CrashLogPath => AppPaths.CrashLogPath;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Single-instance: kill any existing instances
        var current = Process.GetCurrentProcess();
        foreach (var proc in Process.GetProcessesByName(current.ProcessName))
        {
            if (proc.Id == current.Id) continue;
            try { proc.Kill(); proc.WaitForExit(2000); } catch { }
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog(args.ExceptionObject as Exception);

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog(args.Exception);
            MessageBox.Show(
                $"An error occurred:\n\n{args.Exception.Message}\n\nDetails written to:\n{CrashLogPath}",
                "Sidebar Launcher - Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var configService = new ConfigService();
        var config = configService.Load();

        var edgeDetector = new EdgeDetector(config, configService);
        edgeDetector.Show();
    }

    private static void WriteCrashLog(Exception? ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.AppendAllText(CrashLogPath,
                $"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}" +
                $"{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }
}
