# SidebarLauncher

A lightweight, auto-hiding launcher bar for Windows 11. Pin your favorite apps, folders, URLs, and scripts to a sleek sidebar that appears when you hover the screen edge.

## Features

- **Auto-hide**: Bar slides in when you hover the screen edge, slides out when you leave
- **Pinned mode**: Optionally pin the bar to always stay visible (reserves screen space via AppBar API)
- **Configurable edge**: Left or right side of any monitor
- **Dark theme**: Semi-transparent dark aesthetic with smooth slide animations
- **Shortcut types**: Applications (.exe, .lnk), folders, URLs, scripts (.ps1, .bat, .cmd)
- **Auto icon extraction**: Icons pulled from executables, shortcuts, and shell associations
- **Right-click management**: Add, edit, remove, and reorder shortcuts from the bar
- **System tray**: Access from the notification area
- **Lightweight**: ~61MB single-file EXE (self-contained, no runtime needed)

## Usage

1. Run `SidebarLauncher.exe`
2. Hover the left edge of your screen to reveal the sidebar
3. Click the **+** button to add shortcuts
4. Right-click any shortcut for options (Edit, Remove, Move Up/Down)
5. Right-click for Edge switching, Pin/Unpin, and Exit

## Build

Requires .NET 8.0 SDK.

```bash
dotnet publish -c Release
# Output: bin\Release\net8.0-windows\win-x64\publish\SidebarLauncher.exe
```

## Configuration

Settings are stored at `%APPDATA%\SidebarLauncher\config.json` and are editable both through the app's UI and manually.
