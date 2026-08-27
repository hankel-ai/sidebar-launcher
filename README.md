# SidebarLauncher

A lightweight, auto-hiding launcher bar for Windows 11. Pin your favorite apps, folders, URLs, and scripts to a sleek sidebar that appears when you hover the screen edge.

## Features

- **Auto-hide**: Bar slides in when you hover the screen edge, slides out when you leave
- **Pinned mode**: Double-click the bar or use the context menu to pin it (reserves screen space via AppBar API)
- **Configurable edge**: Left or right side of any monitor
- **Dark theme**: Semi-transparent dark aesthetic with smooth slide animations
- **Shortcut types**: Applications (.exe, .lnk), folders, URLs, scripts (.ps1, .bat, .cmd)
- **Command-line arguments**: Optional per-shortcut args, e.g. path `msedge.exe` with args `--remote-debugging-port=9222 --user-data-dir=C:\EdgeDebugProfile`
- **Import shortcuts**: Import from Taskbar and Start Menu with clean icons (no overlay arrows)
- **Auto icon extraction**: Icons pulled from executables, shortcuts, and shell associations
- **Drag and drop**: Reorder shortcuts by dragging, or drop files/folders onto the bar to add them
- **Right-click management**: Add, edit, remove, and reorder shortcuts from the bar
- **Lock icons**: Prevent accidental reordering
- **Launch on startup**: Optional auto-start with Windows
- **Lightweight**: Single-file EXE, no runtime needed

## Usage

1. Run `SidebarLauncher.exe`
2. Hover the left edge of your screen to reveal the sidebar
3. Click the **+** button to add shortcuts
4. Right-click any shortcut for options (Edit, Remove, Move Up/Down)
5. Right-click empty area for Import, Pin, Lock, Edge, Startup, and Exit options
6. Double-click the bar to toggle pinned mode
7. Drag and drop files or folders onto the bar to add them

## Build

Requires .NET 8.0 SDK.

```bash
# Build, copy, and launch
buildandcopy.cmd

# Or manually
dotnet publish -c Release
# Output: bin\Release\net8.0-windows\win-x64\publish\SidebarLauncher.exe
```

## Configuration

Sidebar Launcher is portable. Settings live in a `SidebarLauncherData\` folder **next to
`SidebarLauncher.exe`**, wherever you put the exe:

```
SidebarLauncher.exe
SidebarLauncherData\
  config.json     settings + shortcuts
  crash.log
  icons\          extracted icons
  shortcuts\      copies of imported .lnk files
```

Nothing is written to your user profile. Move the exe and take `SidebarLauncherData\` with it to
keep your layout; leave it behind to start fresh. `config.json` is editable both through the
app's UI and by hand. Right-click the bar → **Open Data Folder** to see which folder the running
copy is using.

Settings previously kept in `%APPDATA%\SidebarLauncher` are migrated automatically on first run
(the old folder is left in place).
