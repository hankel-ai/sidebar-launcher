using System;
using System.IO;

namespace SidebarLauncher.Services;

/// <summary>
/// Resolves where config.json, imported shortcuts, icons and the crash log live.
/// Everything sits in a "SidebarLauncherData" folder next to SidebarLauncher.exe,
/// wherever that exe happens to be, so the app is fully portable and writes nothing
/// to the user profile.
/// </summary>
public static class AppPaths
{
    private const string DataFolderName = "SidebarLauncherData";

    public static string DataFolder { get; } =
        Path.Combine(AppContext.BaseDirectory, DataFolderName);

    public static string ConfigPath => Path.Combine(DataFolder, "config.json");
    public static string ShortcutsFolder => Path.Combine(DataFolder, "shortcuts");
    public static string IconsFolder => Path.Combine(DataFolder, "icons");
    public static string CrashLogPath => Path.Combine(DataFolder, "crash.log");

    /// <summary>Pre-move location: %APPDATA%\SidebarLauncher.</summary>
    private static string LegacyFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SidebarLauncher");

    public static void EnsureCreated() => Directory.CreateDirectory(DataFolder);

    /// <summary>
    /// One-time copy of %APPDATA%\SidebarLauncher into the data folder beside the exe.
    /// Leaves the legacy folder alone so an older build keeps working.
    /// </summary>
    public static void MigrateLegacyData()
    {
        try
        {
            if (File.Exists(ConfigPath)) return;
            if (!Directory.Exists(LegacyFolder)) return;
            if (SamePath(LegacyFolder, DataFolder)) return;

            CopyDirectory(LegacyFolder, DataFolder);
        }
        catch
        {
            // A failed migration just means we start from defaults.
        }
    }

    /// <summary>
    /// Rewrites a path that pointed into the legacy folder so it points at the data
    /// folder beside the exe. Returns the original path when it is unrelated.
    /// </summary>
    public static string? Repoint(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        var prefix = LegacyFolder + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return path;

        return Path.Combine(DataFolder, path.Substring(prefix.Length));
    }

    private static bool SamePath(string a, string b) => string.Equals(
        Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
