using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SidebarLauncher.Models;
using SidebarLauncher.Services;

namespace SidebarLauncher;

public partial class SidebarWindow : Window
{
    private readonly LauncherConfig _config;
    private readonly ConfigService _configService;
    private readonly EdgeDetector _edgeDetector;
    private readonly DispatcherTimer _hideTimer;
    private readonly IconExtractor _iconExtractor = new();
    private ObservableCollection<ShortcutViewModel> _shortcuts = new();
    private AppBarService? _appBar;
    private bool _isVisible;
    private bool _isAnimating;

    public SidebarWindow(LauncherConfig config, ConfigService configService, EdgeDetector edgeDetector)
    {
        _config = config;
        _configService = configService;
        _edgeDetector = edgeDetector;

        InitializeComponent();

        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_config.Settings.AutoHideDelayMs)
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!IsMouseOver && !_config.Settings.Pinned)
                SlideOut();
        };

        ShortcutList.ItemsSource = _shortcuts;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Reposition();
        RefreshShortcuts();
    }

    public void Reposition()
    {
        var screen = GetTargetScreen();
        var workArea = screen.WorkingArea;
        var dpiScale = VisualTreeHelper.GetDpi(this);

        double barWidth = _config.Settings.BarWidth;
        Width = barWidth;

        // Size to fit content but cap at screen height
        double maxHeight = workArea.Height / dpiScale.DpiScaleY;
        Height = maxHeight;

        Top = workArea.Top / dpiScale.DpiScaleY;

        if (_config.Settings.Edge == ScreenEdge.Left)
        {
            Left = workArea.Left / dpiScale.DpiScaleX;
            OuterBorder.CornerRadius = new CornerRadius(0, 8, 8, 0);
            OuterBorder.BorderThickness = new Thickness(0, 1, 1, 1);
        }
        else
        {
            Left = (workArea.Right / dpiScale.DpiScaleX) - barWidth;
            OuterBorder.CornerRadius = new CornerRadius(8, 0, 0, 8);
            OuterBorder.BorderThickness = new Thickness(1, 1, 0, 1);
        }

        // Start off-screen
        if (!_isVisible)
        {
            SlideTransform.X = _config.Settings.Edge == ScreenEdge.Left ? -barWidth : barWidth;
        }
    }

    private System.Windows.Forms.Screen GetTargetScreen()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var idx = _config.Settings.MonitorIndex;
        if (idx >= 0 && idx < screens.Length)
            return screens[idx];
        return System.Windows.Forms.Screen.PrimaryScreen!;
    }

    public void RefreshShortcuts()
    {
        _shortcuts.Clear();
        foreach (var item in _config.Shortcuts.Where(s => s.Type != ShortcutType.Separator))
        {
            var vm = new ShortcutViewModel
            {
                Name = item.Name,
                Path = item.Path,
                Icon = _iconExtractor.GetIcon(item),
                IconSize = _config.Settings.IconSize,
                BarWidth = _config.Settings.BarWidth,
                ShortcutItem = item
            };
            _shortcuts.Add(vm);
        }
    }

    public void RegisterAppBar()
    {
        if (_appBar != null) return;
        _appBar = new AppBarService(this, _config.Settings);
        _appBar.Register();
    }

    public void UnregisterAppBar()
    {
        _appBar?.Dispose();
        _appBar = null;
    }

    public void SlideIn()
    {
        if (_isVisible || _isAnimating) return;

        Show();

        // If pinned, register AppBar and skip animation
        if (_config.Settings.Pinned)
        {
            SlideTransform.X = 0;
            _isVisible = true;
            Dispatcher.BeginInvoke(new Action(RegisterAppBar),
                System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        _isAnimating = true;

        var animation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(_config.Settings.SlideAnimationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) =>
        {
            _isVisible = true;
            _isAnimating = false;
        };

        SlideTransform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    public void SlideOut()
    {
        if (!_isVisible || _isAnimating || _config.Settings.Pinned || _contextMenuOpen) return;

        _isAnimating = true;
        double target = _config.Settings.Edge == ScreenEdge.Left
            ? -_config.Settings.BarWidth
            : _config.Settings.BarWidth;

        var animation = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(_config.Settings.SlideAnimationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            _isVisible = false;
            _isAnimating = false;
            Hide();
        };

        SlideTransform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private bool _contextMenuOpen;

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_config.Settings.Pinned && !_contextMenuOpen)
            _hideTimer.Start();
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _hideTimer.Stop();
    }

    private void TrackContextMenu(ContextMenu menu)
    {
        _contextMenuOpen = true;
        _hideTimer.Stop();
        menu.Closed += (_, _) =>
        {
            // Delay reset so Click handlers run first (they'll call ResetInteractionState)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Only reset if no Click handler already did it
                if (_contextMenuOpen)
                    ResetInteractionState();
            }), System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    private void ResetInteractionState()
    {
        _contextMenuOpen = false;
        if (!IsMouseOver && !_config.Settings.Pinned)
            _hideTimer.Start();
    }

    private void OnScrollWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = sender as ScrollViewer;
        sv?.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private void OnShortcutClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ShortcutViewModel vm)
        {
            ShellLauncher.Launch(vm.ShortcutItem);
        }
    }

    private void OnShortcutRightClick(object sender, MouseButtonEventArgs e)
    {
        _contextMenuOpen = true;
        _hideTimer.Stop();
        if (sender is FrameworkElement fe && fe.DataContext is ShortcutViewModel vm)
        {
            var menu = new ContextMenu();

            var editItem = new MenuItem { Header = "Edit" };
            editItem.Click += (_, _) => { EditShortcut(vm.ShortcutItem); ResetInteractionState(); };
            menu.Items.Add(editItem);

            var removeItem = new MenuItem { Header = "Remove" };
            removeItem.Click += (_, _) =>
            {
                _config.Shortcuts.Remove(vm.ShortcutItem);
                _configService.Save(_config);
                RefreshShortcuts();
                ResetInteractionState();
            };
            menu.Items.Add(removeItem);

            menu.Items.Add(new Separator());

            var idx = _config.Shortcuts.IndexOf(vm.ShortcutItem);
            if (idx > 0)
            {
                var moveUp = new MenuItem { Header = "Move Up" };
                moveUp.Click += (_, _) =>
                {
                    _config.Shortcuts.RemoveAt(idx);
                    _config.Shortcuts.Insert(idx - 1, vm.ShortcutItem);
                    _configService.Save(_config);
                    RefreshShortcuts();
                    ResetInteractionState();
                };
                menu.Items.Add(moveUp);
            }
            if (idx < _config.Shortcuts.Count - 1)
            {
                var moveDown = new MenuItem { Header = "Move Down" };
                moveDown.Click += (_, _) =>
                {
                    _config.Shortcuts.RemoveAt(idx);
                    _config.Shortcuts.Insert(idx + 1, vm.ShortcutItem);
                    _configService.Save(_config);
                    RefreshShortcuts();
                    ResetInteractionState();
                };
                menu.Items.Add(moveDown);
            }

            menu.Items.Add(new Separator());

            var edgeSubMenu = new MenuItem { Header = "Edge" };
            var leftItem = new MenuItem
            {
                Header = "Left",
                IsChecked = _config.Settings.Edge == ScreenEdge.Left
            };
            leftItem.Click += (_, _) => { _edgeDetector.SwitchEdge(ScreenEdge.Left); ResetInteractionState(); };
            edgeSubMenu.Items.Add(leftItem);

            var rightItem = new MenuItem
            {
                Header = "Right",
                IsChecked = _config.Settings.Edge == ScreenEdge.Right
            };
            rightItem.Click += (_, _) => { _edgeDetector.SwitchEdge(ScreenEdge.Right); ResetInteractionState(); };
            edgeSubMenu.Items.Add(rightItem);
            menu.Items.Add(edgeSubMenu);

            var pinItem = new MenuItem
            {
                Header = _config.Settings.Pinned ? "Unpin (Auto-hide)" : "Pin (Always Visible)"
            };
            pinItem.Click += (_, _) => { _edgeDetector.TogglePinned(); ResetInteractionState(); };
            menu.Items.Add(pinItem);

            menu.Items.Add(new Separator());

            var exitItem = new MenuItem { Header = "Exit" };
            exitItem.Click += (_, _) =>
            {
                _edgeDetector.Close();
                Application.Current.Shutdown();
            };
            menu.Items.Add(exitItem);

            TrackContextMenu(menu);
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void OnBarRightClick(object sender, MouseButtonEventArgs e)
    {
        // Don't show bar menu if we already handled a shortcut right-click
        if (e.Handled) return;

        _contextMenuOpen = true;
        _hideTimer.Stop();

        var menu = new ContextMenu();

        var addItem = new MenuItem { Header = "Add Shortcut..." };
        addItem.Click += (_, _) =>
        {
            var dialog = new EditShortcutWindow();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                _config.Shortcuts.Add(dialog.Result);
                _configService.Save(_config);
                RefreshShortcuts();
            }
            ResetInteractionState();
        };
        menu.Items.Add(addItem);

        menu.Items.Add(new Separator());

        var edgeSubMenu = new MenuItem { Header = "Edge" };
        var leftItem = new MenuItem
        {
            Header = "Left",
            IsChecked = _config.Settings.Edge == ScreenEdge.Left
        };
        leftItem.Click += (_, _) => { _edgeDetector.SwitchEdge(ScreenEdge.Left); ResetInteractionState(); };
        edgeSubMenu.Items.Add(leftItem);

        var rightItem = new MenuItem
        {
            Header = "Right",
            IsChecked = _config.Settings.Edge == ScreenEdge.Right
        };
        rightItem.Click += (_, _) => { _edgeDetector.SwitchEdge(ScreenEdge.Right); ResetInteractionState(); };
        edgeSubMenu.Items.Add(rightItem);
        menu.Items.Add(edgeSubMenu);

        var pinItem = new MenuItem
        {
            Header = _config.Settings.Pinned ? "Unpin (Auto-hide)" : "Pin (Always Visible)"
        };
        pinItem.Click += (_, _) => { _edgeDetector.TogglePinned(); ResetInteractionState(); };
        menu.Items.Add(pinItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) =>
        {
            _edgeDetector.Close();
            Application.Current.Shutdown();
        };
        menu.Items.Add(exitItem);

        TrackContextMenu(menu);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnAddClick(object sender, MouseButtonEventArgs e)
    {
        var dialog = new EditShortcutWindow();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _config.Shortcuts.Add(dialog.Result);
            _configService.Save(_config);
            RefreshShortcuts();
        }
    }

    private void EditShortcut(ShortcutItem item)
    {
        var dialog = new EditShortcutWindow(item);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            var idx = _config.Shortcuts.IndexOf(item);
            if (idx >= 0)
                _config.Shortcuts[idx] = dialog.Result;
            _configService.Save(_config);
            RefreshShortcuts();
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            OuterBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)); // Blue highlight
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        OuterBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        OuterBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (paths == null) return;

        int added = 0;
        foreach (var path in paths)
        {
            // Skip duplicates
            if (_config.Shortcuts.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var item = new ShortcutItem
            {
                Path = path,
                Name = Path.GetFileNameWithoutExtension(path),
                Type = DetectType(path)
            };

            // For folders, use the folder name
            if (item.Type == ShortcutType.Folder)
                item.Name = new DirectoryInfo(path).Name;

            // For .lnk files, use the shortcut name without extension
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                item.Name = Path.GetFileNameWithoutExtension(path);

            _config.Shortcuts.Add(item);
            added++;
        }

        if (added > 0)
        {
            _configService.Save(_config);
            RefreshShortcuts();
        }
    }

    private static ShortcutType DetectType(string path)
    {
        if (Directory.Exists(path))
            return ShortcutType.Folder;

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
            return ShortcutType.Url;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ps1" or ".bat" or ".cmd" => ShortcutType.Script,
            _ => ShortcutType.Application
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        UnregisterAppBar();
        base.OnClosed(e);
    }
}

public class ShortcutViewModel
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }
    public int IconSize { get; set; }
    public int BarWidth { get; set; }
    public ShortcutItem ShortcutItem { get; set; } = null!;
}
