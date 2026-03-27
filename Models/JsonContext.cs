using System.Text.Json.Serialization;

namespace SidebarLauncher.Models;

[JsonSerializable(typeof(LauncherConfig))]
[JsonSerializable(typeof(LauncherSettings))]
[JsonSerializable(typeof(ShortcutItem))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class AppJsonContext : JsonSerializerContext
{
}
