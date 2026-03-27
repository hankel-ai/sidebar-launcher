using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SidebarLauncher.Models;

public class LauncherConfig
{
    [JsonPropertyName("settings")]
    public LauncherSettings Settings { get; set; } = new();

    [JsonPropertyName("shortcuts")]
    public List<ShortcutItem> Shortcuts { get; set; } = new();
}
