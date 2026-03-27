using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SidebarLauncher.Models;

namespace SidebarLauncher;

public partial class ImportShortcutsWindow : Window
{
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

    private void LoadShortcuts(string searchPath)
    {
        if (!Directory.Exists(searchPath)) return;

        var files = Directory.GetFiles(searchPath, "*.lnk", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            if (_existingPaths.Contains(file)) continue;

            var name = Path.GetFileNameWithoutExtension(file);
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
        foreach (var entry in _entries.Where(x => x.IsSelected))
        {
            SelectedShortcuts.Add(new ShortcutItem
            {
                Name = entry.Name,
                Path = entry.FilePath,
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
