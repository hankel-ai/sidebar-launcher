using System.Text.Json.Serialization;

namespace SidebarLauncher.Models;

public class ShortcutItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? IconPath { get; set; }

    [JsonPropertyName("type")]
    public ShortcutType Type { get; set; } = ShortcutType.Application;

    [JsonPropertyName("group")]
    public string? Group { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ShortcutType>))]
public enum ShortcutType
{
    Application,
    Folder,
    Url,
    Script,
    Separator
}
