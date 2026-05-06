using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using SidebarLauncher.Models;

namespace SidebarLauncher;

public partial class ImportShortcutsWindow : Window
{
    private static readonly string ShortcutsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SidebarLauncher", "shortcuts");

    private static readonly string IconsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SidebarLauncher", "icons");

    public List<ShortcutItem> SelectedShortcuts { get; } = new();

    private readonly List<ImportEntry> _entries = new();
    private readonly HashSet<string> _existingPaths;

    public ImportShortcutsWindow(string title, string searchPath, IEnumerable<string> existingPaths)
    {
        InitializeComponent();
        TitleText.Text = title;
        _existingPaths = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
        LoadShortcuts(searchPath);
    }

    public ImportShortcutsWindow(string title, string[] searchPaths, IEnumerable<string> existingPaths)
    {
        InitializeComponent();
        TitleText.Text = title;
        _existingPaths = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var path in searchPaths)
            LoadShortcuts(path);
        _entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        ShortcutList.ItemsSource = _entries;
    }

    /// <summary>
    /// Constructor for pre-built entries (e.g., installed UWP/PWA apps).
    /// </summary>
    public ImportShortcutsWindow(string title, List<ImportEntry> entries)
    {
        InitializeComponent();
        TitleText.Text = title;
        _existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _entries.AddRange(entries);
        _entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        ShortcutList.ItemsSource = _entries;
    }

    private void LoadShortcuts(string searchPath)
    {
        if (!Directory.Exists(searchPath)) return;

        var files = Directory.GetFiles(searchPath, "*.lnk", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            // Skip Startup folder shortcuts (auto-start items, not user apps)
            if (file.Contains(@"\Startup\", StringComparison.OrdinalIgnoreCase)) continue;

            var name = Path.GetFileNameWithoutExtension(file);

            // Check if already imported (by original path, copied path, name, or duplicate in this batch)
            if (_existingPaths.Contains(file)) continue;
            if (_existingPaths.Contains(name)) continue;
            var copiedPath = Path.Combine(ShortcutsFolder, Path.GetFileName(file));
            if (_existingPaths.Contains(copiedPath)) continue;
            if (_entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

            ImageSource? icon = null;

            try
            {
                icon = Services.IconExtractor.GetFileIcon(file);
            }
            catch { }

            _entries.Add(new ImportEntry
            {
                Name = name,
                FilePath = file,
                Icon = icon,
                IsSelected = false
            });
        }

        if (ShortcutList.ItemsSource == null)
        {
            _entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            ShortcutList.ItemsSource = _entries;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void ShortcutList_Loaded(object sender, RoutedEventArgs e)
    {
        if (_entries.Count == 0) return;
        ShortcutList.Focus();
        ShortcutList.SelectedIndex = 0;
        if (ShortcutList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item)
            item.Focus();
    }

    private void OnSelectAll(object sender, MouseButtonEventArgs e)
    {
        foreach (var entry in _entries) entry.IsSelected = true;
    }

    private void OnSelectNone(object sender, MouseButtonEventArgs e)
    {
        foreach (var entry in _entries) entry.IsSelected = false;
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ShortcutsFolder);
        Directory.CreateDirectory(IconsFolder);

        foreach (var entry in _entries.Where(x => x.IsSelected))
        {
            var path = entry.FilePath;
            string? iconPath = null;

            // For .lnk files, copy to our data folder so we don't depend on the source
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                var destName = GetUniqueName(ShortcutsFolder, Path.GetFileName(path));
                var destPath = Path.Combine(ShortcutsFolder, destName);
                try
                {
                    File.Copy(path, destPath, true);
                    path = destPath;
                }
                catch { }
            }

            // Save the icon to our data folder for persistence
            if (entry.Icon != null)
            {
                try
                {
                    var iconName = GetUniqueName(IconsFolder, entry.Name + ".png");
                    var iconFile = Path.Combine(IconsFolder, iconName);
                    SaveIconToFile(entry.Icon, iconFile);
                    iconPath = iconFile;
                }
                catch { }
            }

            SelectedShortcuts.Add(new ShortcutItem
            {
                Name = entry.Name,
                Path = path,
                IconPath = iconPath,
                Type = ShortcutType.Application,
                Slot = -1
            });
        }
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string GetUniqueName(string folder, string fileName)
    {
        if (!File.Exists(Path.Combine(folder, fileName)))
            return fileName;

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        int i = 2;
        while (File.Exists(Path.Combine(folder, $"{baseName}_{i}{ext}")))
            i++;
        return $"{baseName}_{i}{ext}";
    }

    private static void SaveIconToFile(ImageSource source, string filePath)
    {
        if (source is not BitmapSource bmp) return;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = new FileStream(filePath, FileMode.Create);
        encoder.Save(fs);
    }

    /// <summary>
    /// Discovers installed apps via Get-StartApps plus special shell items, returns ImportEntry items.
    /// </summary>
    public static List<ImportEntry> DiscoverInstalledApps(IEnumerable<string> existingPaths)
    {
        var existing = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
        var entries = new List<ImportEntry>();

        // Add special shell namespace items that aren't in Get-StartApps
        var specialItems = new[]
        {
            ("This PC", "shell:MyComputerFolder"),
            ("Recycle Bin", "shell:RecycleBinFolder"),
            ("Control Panel", "shell:ControlPanelFolder"),
            ("Network", "shell:NetworkPlacesFolder"),
        };

        foreach (var (name, shellPath) in specialItems)
        {
            if (existing.Contains(shellPath)) continue;
            if (existing.Contains(name)) continue;

            ImageSource? icon = null;
            try { icon = Services.IconExtractor.GetShellPathIcon(shellPath); }
            catch { }

            entries.Add(new ImportEntry
            {
                Name = name,
                FilePath = shellPath,
                Icon = icon,
                IsSelected = false
            });
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Get-StartApps | ForEach-Object { Write-Output \\\"$($_.Name)|$($_.AppID)\\\" }\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return entries;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split('|', 2);
                if (parts.Length != 2) continue;

                var name = parts[0].Trim();
                var appId = parts[1].Trim();

                var shellPath = $"shell:AppsFolder\\{appId}";
                if (existing.Contains(shellPath)) continue;
                if (existing.Contains(name)) continue;

                ImageSource? icon = null;
                try
                {
                    // Try shell PIDL icon first (works for all apps)
                    icon = Services.IconExtractor.GetShellPathIcon(shellPath);

                    // Fall back to package manifest for UWP/PWA apps
                    if (icon == null && appId.Contains('!'))
                        icon = Services.IconExtractor.GetAppPackageIcon(appId);
                }
                catch { }

                entries.Add(new ImportEntry
                {
                    Name = name,
                    FilePath = shellPath,
                    Icon = icon,
                    IsSelected = false
                });
            }
        }
        catch { }

        return entries;
    }
}

public class ImportEntry : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public ImageSource? Icon { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
