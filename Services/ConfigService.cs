using System;
using System.IO;
using System.Text.Json;
using SidebarLauncher.Models;

namespace SidebarLauncher.Services;

public class ConfigService
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SidebarLauncher");

    private static readonly string ConfigPath = Path.Combine(AppDataFolder, "config.json");

    public LauncherConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return CreateDefault();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.LauncherConfig) ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(LauncherConfig config)
    {
        Directory.CreateDirectory(AppDataFolder);
        var json = JsonSerializer.Serialize(config, AppJsonContext.Default.LauncherConfig);
        File.WriteAllText(ConfigPath, json);
    }

    private LauncherConfig CreateDefault()
    {
        var config = new LauncherConfig();
        Save(config);
        return config;
    }
}
