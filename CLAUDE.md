# SidebarLauncher

## Purpose
A lightweight Windows 11 launcher bar that auto-hides at the screen edge. Hover the edge to reveal a vertical strip of shortcut icons for apps, folders, URLs, and scripts. Supports auto-hide and pinned (AppBar) modes.

## Tech Stack
- **Framework**: WPF on .NET 8.0 (Windows)
- **Language**: C#
- **Dependencies**: None (only framework libraries)
- **WinForms**: Referenced only for `Screen.AllScreens`, `FolderBrowserDialog`, and `ExtractAssociatedIcon`

## Build / Run / Test
```bash
# Build and deploy (kills running instance, builds, copies, launches)
buildandcopy.cmd

# Build (debug)
%LOCALAPPDATA%\dotnet\dotnet.exe build -c Release

# Publish (single-file, self-contained ~155MB)
%LOCALAPPDATA%\dotnet\dotnet.exe publish -c Release

# Output location
bin\Release\net8.0-windows\win-x64\publish\SidebarLauncher.exe
```

## Key Architecture
- **EdgeDetector.xaml** — Invisible 2px Topmost window at screen edge; triggers sidebar on hover
- **SidebarWindow.xaml** — The visible icon strip with canvas-based grid slot system, slide-in/out animation
- **EditShortcutWindow.xaml** — Dialog for adding/editing shortcuts (custom dark ComboBox template, icon picker)
- **ImportShortcutsWindow.xaml** — Checklist dialog for importing shortcuts from Taskbar/Start Menu
- **Services/AppBarService.cs** — SHAppBarMessage P/Invoke for pinned mode (reserves screen space)
- **Services/ConfigService.cs** — JSON config at `%APPDATA%\SidebarLauncher\config.json`
- **Services/IconExtractor.cs** — Extracts icons from exe/lnk/folders via shell APIs (IShellLink COM, ExtractIconEx, SHGetFileInfo with PIDL)
- **Services/ShellLauncher.cs** — Launches shortcuts (Process.Start, special .ps1 handling, auto working directory)
- **Services/StartupService.cs** — HKCU Run key for launch-on-startup
- **Models/JsonContext.cs** — Source-generated JSON serializer (required for trimming)

## Config
User config lives at `%APPDATA%\SidebarLauncher\config.json`. Supports:
- Edge: Left or Right
- Icon size, bar width, opacity
- Auto-hide delay, slide animation speed
- Pinned mode toggle
- Lock icons toggle
- Monitor selection
- Shortcut types: Application, Folder, Url, Script, Separator

## Icon Extraction (.lnk files)
The `IconExtractor` resolves .lnk shortcut icons without the overlay arrow using a 3-step chain:
1. `GetIconLocation` → `ExtractIconEx` (handles custom icons like Hyper-V)
2. `GetPath` → `ExtractAssociatedIcon` on resolved target exe
3. `GetIDList` → `SHGetFileInfo` with `SHGFI_PIDL` (handles shell objects like This PC)

## Important Notes
- `System.IO.Path` must be fully qualified in SidebarWindow.xaml.cs due to ambiguity with `System.Windows.Shapes.Path`
- JSON serialization uses source generators (`AppJsonContext`) — do NOT use reflection-based serialization
- `buildandcopy.cmd` must kill the running instance BEFORE building (file lock on single-file EXE)
- No system tray icon — all interaction via the sidebar itself
- Double-click bar to toggle pin; right-click for context menu
