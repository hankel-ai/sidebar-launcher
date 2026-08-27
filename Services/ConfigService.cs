using System;
using System.IO;
using System.Text.Json;
using SidebarLauncher.Models;

namespace SidebarLauncher.Services;

public class ConfigService
{
    public LauncherConfig Load()
    {
        AppPaths.MigrateLegacyData();

        if (!File.Exists(AppPaths.ConfigPath))
            return CreateDefault();

        LauncherConfig config;
        try
        {
            var json = File.ReadAllText(AppPaths.ConfigPath);
            config = JsonSerializer.Deserialize(json, AppJsonContext.Default.LauncherConfig) ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }

        if (RepointLegacyPaths(config))
            Save(config);

        return config;
    }

    public void Save(LauncherConfig config)
    {
        AppPaths.EnsureCreated();
        var json = JsonSerializer.Serialize(config, AppJsonContext.Default.LauncherConfig);
        File.WriteAllText(AppPaths.ConfigPath, json);
    }

    /// <summary>
    /// Points shortcuts and icons copied out of %APPDATA%\SidebarLauncher at their
    /// new home. Idempotent - unrelated paths are left untouched.
    /// </summary>
    private static bool RepointLegacyPaths(LauncherConfig config)
    {
        var changed = false;

        foreach (var shortcut in config.Shortcuts)
        {
            var path = AppPaths.Repoint(shortcut.Path);
            if (!string.Equals(path, shortcut.Path, StringComparison.Ordinal))
            {
                shortcut.Path = path ?? string.Empty;
                changed = true;
            }

            var icon = AppPaths.Repoint(shortcut.IconPath);
            if (!string.Equals(icon, shortcut.IconPath, StringComparison.Ordinal))
            {
                shortcut.IconPath = icon;
                changed = true;
            }
        }

        return changed;
    }

    private LauncherConfig CreateDefault()
    {
        var config = new LauncherConfig();
        Save(config);
        return config;
    }
}
