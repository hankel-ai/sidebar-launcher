using System.Text.Json.Serialization;

namespace SidebarLauncher.Models;

public class LauncherSettings
{
    [JsonPropertyName("edge")]
    public ScreenEdge Edge { get; set; } = ScreenEdge.Left;

    [JsonPropertyName("iconSize")]
    public int IconSize { get; set; } = 32;

    [JsonPropertyName("autoHideDelayMs")]
    public int AutoHideDelayMs { get; set; } = 400;

    [JsonPropertyName("hoverShowDelayMs")]
    public int HoverShowDelayMs { get; set; } = 1000;

    [JsonPropertyName("slideAnimationMs")]
    public int SlideAnimationMs { get; set; } = 200;

    [JsonPropertyName("barWidth")]
    public int BarWidth { get; set; } = 48;

    [JsonPropertyName("barOpacity")]
    public double BarOpacity { get; set; } = 0.92;

    [JsonPropertyName("pinned")]
    public bool Pinned { get; set; } = false;

    [JsonPropertyName("launchOnStartup")]
    public bool LaunchOnStartup { get; set; } = false;

    [JsonPropertyName("monitorIndex")]
    public int MonitorIndex { get; set; } = 0;

    [JsonPropertyName("columns")]
    public int Columns { get; set; } = 1;

    // Rows per column, independent of screen height. Slots are a linear index split by
    // this value, so it must stay fixed for icons to keep their row and column.
    // Changing it reflows every icon.
    [JsonPropertyName("layoutRows")]
    public int LayoutRows { get; set; } = 21;

    [JsonPropertyName("locked")]
    public bool Locked { get; set; } = true;
}

[JsonConverter(typeof(JsonStringEnumConverter<ScreenEdge>))]
public enum ScreenEdge
{
    Left,
    Right
}
