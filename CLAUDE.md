# SidebarLauncher

## Purpose
A lightweight Windows 11 launcher bar that auto-hides at the screen edge. Hover the edge to reveal a vertical strip of shortcut icons for apps, folders, URLs, and scripts. Supports auto-hide and pinned (AppBar) modes.

## Tech Stack
- **Framework**: WPF on .NET 8.0 (Windows)
- **Language**: C#
- **Dependencies**: None (only framework libraries)
- **WinForms**: Referenced only for `NotifyIcon` (tray icon), `Screen.AllScreens`, `FolderBrowserDialog`, and `ExtractAssociatedIcon`

## Build / Run / Test
```bash
# Build (debug)
%LOCALAPPDATA%\dotnet\dotnet.exe build -c Release

# Publish (single-file, self-contained, trimmed ~61MB)
%LOCALAPPDATA%\dotnet\dotnet.exe publish -c Release

# Output location
bin\Release\net8.0-windows\win-x64\publish\SidebarLauncher.exe

# Or use build.cmd
build.cmd
```

## Key Architecture
- **EdgeDetector.xaml** — Invisible 2px Topmost window at screen edge; triggers sidebar on hover
- **SidebarWindow.xaml** — The visible icon strip with slide-in/out animation
- **EditShortcutWindow.xaml** — Dialog for adding/editing shortcuts
- **Services/AppBarService.cs** — SHAppBarMessage P/Invoke for pinned mode (reserves screen space)
- **Services/ConfigService.cs** — JSON config at `%APPDATA%\SidebarLauncher\config.json`
- **Services/IconExtractor.cs** — Extracts icons from exe/lnk/folders via shell APIs
- **Services/ShellLauncher.cs** — Launches shortcuts (Process.Start, special .ps1 handling)
- **Services/StartupService.cs** — HKCU Run key for auto-start
- **Models/JsonContext.cs** — Source-generated JSON serializer (required for full trimming)

## Trimming Notes
- Uses `TrimMode=full` for smallest EXE (~61MB vs 155MB untrimmed)
- JSON serialization uses source generators (`AppJsonContext`) — do NOT use reflection-based `JsonSerializer.Serialize<T>()`
- Enum converters must use generic `JsonStringEnumConverter<T>`, not non-generic `JsonStringEnumConverter`
- WPF/WinForms trim warnings suppressed via `_SuppressWPFTrimError` / `_SuppressWinFormsTrimError`

## Config
User config lives at `%APPDATA%\SidebarLauncher\config.json`. Supports:
- Edge: Left or Right
- Icon size, bar width, opacity
- Auto-hide delay, slide animation speed
- Pinned mode toggle
- Monitor selection
- Shortcut types: Application, Folder, Url, Script, Separator
