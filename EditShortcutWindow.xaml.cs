using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SidebarLauncher.Models;

namespace SidebarLauncher;

public partial class EditShortcutWindow : Window
{
    public ShortcutItem? Result { get; private set; }

    public EditShortcutWindow(ShortcutItem? existing = null)
    {
        InitializeComponent();

        TypeCombo.ItemsSource = Enum.GetValues<ShortcutType>()
            .Where(t => t != ShortcutType.Separator)
            .ToList();
        TypeCombo.SelectedItem = existing?.Type ?? ShortcutType.Application;

        if (existing != null)
        {
            Title = "Edit Shortcut";
            NameBox.Text = existing.Name;
            PathBox.Text = existing.Path;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var selectedType = (ShortcutType)(TypeCombo.SelectedItem ?? ShortcutType.Application);

        if (selectedType == ShortcutType.Folder)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                PathBox.Text = dialog.SelectedPath;
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                    NameBox.Text = Path.GetFileName(dialog.SelectedPath);
            }
        }
        else
        {
            var dialog = new OpenFileDialog
            {
                Filter = selectedType switch
                {
                    ShortcutType.Script => "Scripts (*.ps1;*.bat;*.cmd)|*.ps1;*.bat;*.cmd|All files (*.*)|*.*",
                    _ => "Applications (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*"
                }
            };
            if (dialog.ShowDialog() == true)
            {
                PathBox.Text = dialog.FileName;
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                    NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathBox.Text))
        {
            MessageBox.Show("Path is required.", "Sidebar Launcher",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var type = (ShortcutType)(TypeCombo.SelectedItem ?? ShortcutType.Application);

        // Auto-detect type from path if user didn't change it
        if (TypeCombo.SelectedItem is ShortcutType.Application)
            type = DetectType(PathBox.Text);

        Result = new ShortcutItem
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text)
                ? Path.GetFileNameWithoutExtension(PathBox.Text)
                : NameBox.Text,
            Path = PathBox.Text,
            Type = type
        };

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static ShortcutType DetectType(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
            return ShortcutType.Url;

        if (Directory.Exists(path))
            return ShortcutType.Folder;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ps1" or ".bat" or ".cmd" => ShortcutType.Script,
            _ => ShortcutType.Application
        };
    }
}
