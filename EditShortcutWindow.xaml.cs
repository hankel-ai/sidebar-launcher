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
        TypeCombo.SelectionChanged += (_, _) => UpdatePathLabel();

        if (existing != null)
        {
            TitleText.Text = "Edit Shortcut";
            Title = "Edit Shortcut";
            NameBox.Text = existing.Name;
            PathBox.Text = existing.Path;
            ArgsBox.Text = existing.Arguments ?? string.Empty;
            IconPathBox.Text = existing.IconPath ?? "(default)";
            NewTabCheck.IsChecked = existing.NewTab;
        }
        else
        {
            IconPathBox.Text = "(default)";
        }

        UpdatePathLabel();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void UpdatePathLabel()
    {
        var type = (ShortcutType)(TypeCombo.SelectedItem ?? ShortcutType.Application);
        if (type == ShortcutType.Terminal)
        {
            PathLabel.Text = "Command";
            BrowseButton.Visibility = Visibility.Collapsed;
            NewTabCheck.Visibility = Visibility.Visible;
        }
        else if (type == ShortcutType.Url)
        {
            PathLabel.Text = "URL";
            BrowseButton.Visibility = Visibility.Collapsed;
            NewTabCheck.Visibility = Visibility.Collapsed;
        }
        else
        {
            PathLabel.Text = "Path";
            BrowseButton.Visibility = Visibility.Visible;
            NewTabCheck.Visibility = Visibility.Collapsed;
        }

        // Arguments are meaningless for URLs and folders
        var showArgs = type is not (ShortcutType.Url or ShortcutType.Folder);
        ArgsLabel.Visibility = showArgs ? Visibility.Visible : Visibility.Collapsed;
        ArgsBox.Visibility = showArgs ? Visibility.Visible : Visibility.Collapsed;
    }

    private string? GetTargetDirectory()
    {
        var path = PathBox.Text;
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (Directory.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
        }
        catch { }
        return null;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var selectedType = (ShortcutType)(TypeCombo.SelectedItem ?? ShortcutType.Application);
        var initialDir = GetTargetDirectory();

        if (selectedType == ShortcutType.Folder)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (initialDir != null) dialog.SelectedPath = initialDir;
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
            if (initialDir != null) dialog.InitialDirectory = initialDir;
            if (dialog.ShowDialog() == true)
            {
                PathBox.Text = dialog.FileName;
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                    NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void OnBrowseIconClick(object sender, RoutedEventArgs e)
    {
        var initialDir = GetTargetDirectory();
        var dialog = new OpenFileDialog
        {
            Filter = "Image files (*.ico;*.png;*.jpg;*.bmp)|*.ico;*.png;*.jpg;*.bmp|Executables (*.exe;*.dll)|*.exe;*.dll|All files (*.*)|*.*",
            Title = "Select Icon"
        };
        if (initialDir != null) dialog.InitialDirectory = initialDir;
        if (dialog.ShowDialog() == true)
        {
            IconPathBox.Text = dialog.FileName;
        }
    }

    private void OnClearIconClick(object sender, RoutedEventArgs e)
    {
        IconPathBox.Text = "(default)";
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

        var iconPath = IconPathBox.Text;
        if (iconPath == "(default)" || string.IsNullOrWhiteSpace(iconPath))
            iconPath = null;

        Result = new ShortcutItem
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text)
                ? Path.GetFileNameWithoutExtension(PathBox.Text)
                : NameBox.Text,
            Path = PathBox.Text,
            Arguments = string.IsNullOrWhiteSpace(ArgsBox.Text) ? null : ArgsBox.Text.Trim(),
            Type = type,
            IconPath = iconPath,
            NewTab = type == ShortcutType.Terminal && (NewTabCheck.IsChecked == true)
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
