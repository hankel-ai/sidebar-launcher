using System.Text.Json.Serialization;

namespace SidebarLauncher.Models;

public class ShortcutItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }

    [JsonPropertyName("icon")]
    public string? IconPath { get; set; }

    [JsonPropertyName("type")]
    public ShortcutType Type { get; set; } = ShortcutType.Application;

    [JsonPropertyName("group")]
    public string? Group { get; set; }

    [JsonPropertyName("newTab")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NewTab { get; set; }

    [JsonPropertyName("slot")]
    public int Slot { get; set; } = -1;
}

[JsonConverter(typeof(JsonStringEnumConverter<ShortcutType>))]
public enum ShortcutType
{
    Application,
    Folder,
    Url,
    Script,
    Terminal,
    Separator
}
