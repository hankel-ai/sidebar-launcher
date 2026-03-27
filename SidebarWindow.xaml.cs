using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
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
    private AppBarService? _appBar;
    private bool _isVisible;
    private bool _isAnimating;
    private bool _contextMenuOpen;

    // Grid state
    private int _totalSlots;
    private readonly Dictionary<int, SlotView> _slotViews = new();
    private Rectangle? _highlightRect;

    // Drag state
    private const string InternalDragFormat = "SidebarLauncher_Reorder";
    private SlotView? _dragSource;
    private Point _dragStartPoint;
    private bool _isDragging;

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

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Reposition();
        RebuildGrid();
    }

    #region Positioning

    public void Reposition()
    {
        var screen = GetTargetScreen();
        var workArea = screen.WorkingArea;
        var dpiScale = VisualTreeHelper.GetDpi(this);

        double barWidth = _config.Settings.BarWidth;
        Width = barWidth;

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

        if (!_isVisible)
            SlideTransform.X = _config.Settings.Edge == ScreenEdge.Left ? -barWidth : barWidth;

        // Calculate total slots based on available height (minus bottom bar ~49px)
        double availableHeight = maxHeight - 49;
        _totalSlots = Math.Max(1, (int)(availableHeight / barWidth));
    }

    private System.Windows.Forms.Screen GetTargetScreen()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var idx = _config.Settings.MonitorIndex;
        if (idx >= 0 && idx < screens.Length)
            return screens[idx];
        return System.Windows.Forms.Screen.PrimaryScreen!;
    }

    #endregion

    #region Grid Rendering

    public void RebuildGrid()
    {
        SlotCanvas.Children.Clear();
        _slotViews.Clear();

        int bw = _config.Settings.BarWidth;
        SlotCanvas.Width = bw;
        SlotCanvas.Height = _totalSlots * bw;

        // Assign slots to shortcuts that don't have one yet
        AssignSlots();

        // Create highlight rectangle (hidden by default)
        _highlightRect = new Rectangle
        {
            Width = bw,
            Height = bw,
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0x21, 0x96, 0xF3)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            StrokeThickness = 1,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        SlotCanvas.Children.Add(_highlightRect);

        // Render shortcuts at their slot positions
        foreach (var shortcut in _config.Shortcuts)
        {
            if (shortcut.Type == ShortcutType.Separator) continue;
            if (shortcut.Slot < 0 || shortcut.Slot >= _totalSlots) continue;

            var sv = CreateSlotView(shortcut);
            _slotViews[shortcut.Slot] = sv;
            Canvas.SetLeft(sv.Element, 0);
            Canvas.SetTop(sv.Element, shortcut.Slot * bw);
            SlotCanvas.Children.Add(sv.Element);
        }
    }

    public void RefreshShortcuts() => RebuildGrid();

    private void AssignSlots()
    {
        // Find shortcuts without a valid slot and assign them to the next free slot
        var usedSlots = new HashSet<int>(
            _config.Shortcuts
                .Where(s => s.Slot >= 0 && s.Slot < _totalSlots && s.Type != ShortcutType.Separator)
                .Select(s => s.Slot));

        bool changed = false;
        foreach (var shortcut in _config.Shortcuts)
        {
            if (shortcut.Type == ShortcutType.Separator) continue;
            if (shortcut.Slot >= 0 && shortcut.Slot < _totalSlots && !usedSlots.Contains(shortcut.Slot))
            {
                // Slot is valid but check for conflicts (shouldn't happen)
                usedSlots.Add(shortcut.Slot);
                continue;
            }
            if (shortcut.Slot >= 0 && shortcut.Slot < _totalSlots)
                continue; // Already assigned

            // Find next free slot
            for (int i = 0; i < _totalSlots; i++)
            {
                if (!usedSlots.Contains(i))
                {
                    shortcut.Slot = i;
                    usedSlots.Add(i);
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
            _configService.Save(_config);
    }

    private SlotView CreateSlotView(ShortcutItem item)
    {
        int bw = _config.Settings.BarWidth;
        int iconSize = _config.Settings.IconSize;

        var border = new Border
        {
            Width = bw,
            Height = bw,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };

        // Hover effect
        border.MouseEnter += (_, _) =>
        {
            if (!_isDragging)
                border.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        };
        border.MouseLeave += (_, _) =>
        {
            if (!_isDragging)
                border.Background = Brushes.Transparent;
        };

        var grid = new Grid();

        // Icon
        var icon = _iconExtractor.GetIcon(item);
        if (icon != null)
        {
            var img = new System.Windows.Controls.Image
            {
                Source = icon,
                Width = iconSize,
                Height = iconSize,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            grid.Children.Add(img);
        }

        // Name label
        var label = new TextBlock
        {
            Text = item.Name,
            FontSize = 8,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = bw
        };
        label.Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.9, Color = Colors.Black };
        grid.Children.Add(label);

        border.Child = grid;

        // Tooltip
        border.ToolTip = new ToolTip
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x22, 0x22, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Padding = new Thickness(8, 6, 8, 6),
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = item.Name, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, FontSize = 13 },
                    new TextBlock { Text = item.Path, Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)), FontSize = 11 }
                }
            }
        };
        ToolTipService.SetInitialShowDelay(border, 300);

        // Click to launch
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (!_isDragging)
                ShellLauncher.Launch(item);
        };

        // Drag start
        border.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!_config.Settings.Locked)
            {
                _dragSource = _slotViews.Values.FirstOrDefault(sv => sv.Item == item);
                _dragStartPoint = e.GetPosition(this);
            }
        };

        border.PreviewMouseMove += (_, e) =>
        {
            if (_dragSource != null && !_config.Settings.Locked &&
                e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                var pos = e.GetPosition(this);
                if (Math.Abs(pos.Y - _dragStartPoint.Y) > 6)
                {
                    _isDragging = true;
                    _hideTimer.Stop();
                    var data = new DataObject(InternalDragFormat, _dragSource);
                    DragDrop.DoDragDrop(border, data, DragDropEffects.Move);
                    _isDragging = false;
                    _dragSource = null;
                    if (_highlightRect != null)
                        _highlightRect.Visibility = Visibility.Collapsed;
                }
            }
        };

        border.PreviewMouseLeftButtonUp += (_, _) =>
        {
            _dragSource = null;
        };

        return new SlotView { Element = border, Item = item };
    }

    #endregion

    #region AppBar

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

    #endregion

    #region Slide Animation

    public void SlideIn()
    {
        if (_isVisible || _isAnimating) return;

        Show();

        if (_config.Settings.Pinned)
        {
            SlideTransform.X = 0;
            _isVisible = true;
            Dispatcher.BeginInvoke(new Action(RegisterAppBar),
                DispatcherPriority.Loaded);
            return;
        }

        _isAnimating = true;
        var animation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(_config.Settings.SlideAnimationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) => { _isVisible = true; _isAnimating = false; };
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    public void SlideOut()
    {
        if (!_isVisible || _isAnimating || _config.Settings.Pinned || _contextMenuOpen || _isDragging) return;

        _isAnimating = true;
        double target = _config.Settings.Edge == ScreenEdge.Left
            ? -_config.Settings.BarWidth : _config.Settings.BarWidth;

        var animation = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(_config.Settings.SlideAnimationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) => { _isVisible = false; _isAnimating = false; Hide(); };
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    #endregion

    #region Mouse / Hide

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_config.Settings.Pinned && !_contextMenuOpen && !_isDragging)
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
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_contextMenuOpen) ResetInteractionState();
            }), DispatcherPriority.Background);
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

    #endregion

    #region Drag & Drop

    private int GetSlotFromPoint(DragEventArgs e)
    {
        var pos = e.GetPosition(SlotCanvas);
        int slot = (int)(pos.Y / _config.Settings.BarWidth);
        return Math.Clamp(slot, 0, _totalSlots - 1);
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(InternalDragFormat) || e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = e.Data.GetDataPresent(InternalDragFormat) ? DragDropEffects.Move : DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(InternalDragFormat) && _config.Settings.Locked)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(InternalDragFormat) || e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = e.Data.GetDataPresent(InternalDragFormat) ? DragDropEffects.Move : DragDropEffects.Copy;

            // Highlight target slot
            int slot = GetSlotFromPoint(e);
            if (_highlightRect != null)
            {
                Canvas.SetTop(_highlightRect, slot * _config.Settings.BarWidth);
                Canvas.SetLeft(_highlightRect, 0);
                _highlightRect.Visibility = Visibility.Visible;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (_highlightRect != null)
            _highlightRect.Visibility = Visibility.Collapsed;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_highlightRect != null)
            _highlightRect.Visibility = Visibility.Collapsed;

        int targetSlot = GetSlotFromPoint(e);

        // Internal reorder (blocked when locked)
        if (e.Data.GetDataPresent(InternalDragFormat))
        {
            if (_config.Settings.Locked) { e.Handled = true; return; }
            var sv = e.Data.GetData(InternalDragFormat) as SlotView;
            if (sv != null)
            {
                // If target slot is occupied by a different item, swap them
                var occupant = _config.Shortcuts.FirstOrDefault(
                    s => s.Slot == targetSlot && s != sv.Item && s.Type != ShortcutType.Separator);

                if (occupant != null)
                {
                    // Swap slots
                    occupant.Slot = sv.Item.Slot;
                }

                sv.Item.Slot = targetSlot;
                _configService.Save(_config);
                RebuildGrid();
            }
            e.Handled = true;
            return;
        }

        // External file drop
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (paths == null) return;

        int slot = targetSlot;
        foreach (var path in paths)
        {
            if (_config.Shortcuts.Any(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var item = new ShortcutItem
            {
                Path = path,
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                Type = DetectType(path),
                Slot = slot
            };

            if (item.Type == ShortcutType.Folder)
                item.Name = new DirectoryInfo(path).Name;

            if (System.IO.Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                item.Name = System.IO.Path.GetFileNameWithoutExtension(path);

            // If slot is occupied, push to next free slot
            while (_config.Shortcuts.Any(s => s.Slot == slot && s.Type != ShortcutType.Separator))
                slot++;
            item.Slot = slot;

            _config.Shortcuts.Add(item);
            slot++;
        }

        _configService.Save(_config);
        RebuildGrid();
    }

    private static ShortcutType DetectType(string path)
    {
        if (Directory.Exists(path))
            return ShortcutType.Folder;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
            return ShortcutType.Url;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ps1" or ".bat" or ".cmd" => ShortcutType.Script,
            _ => ShortcutType.Application
        };
    }

    #endregion

    #region Context Menus

    private void OnBarDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _edgeDetector.TogglePinned();
            e.Handled = true;
        }
    }

    private void OnBarRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || _isDragging) return;

        _contextMenuOpen = true;
        _hideTimer.Stop();

        // Check if we clicked on a shortcut
        var pos = e.GetPosition(SlotCanvas);
        int slot = (int)(pos.Y / _config.Settings.BarWidth);
        var clickedItem = _config.Shortcuts.FirstOrDefault(
            s => s.Slot == slot && s.Type != ShortcutType.Separator);

        var menu = new ContextMenu();

        if (clickedItem != null)
        {
            var editItem = new MenuItem { Header = "Edit" };
            editItem.Click += (_, _) => { EditShortcut(clickedItem); ResetInteractionState(); };
            menu.Items.Add(editItem);

            var removeItem = new MenuItem { Header = "Remove" };
            removeItem.Click += (_, _) =>
            {
                var result = MessageBox.Show(
                    $"Remove \"{clickedItem.Name}\"?",
                    "Sidebar Launcher",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _config.Shortcuts.Remove(clickedItem);
                    _configService.Save(_config);
                    RebuildGrid();
                }
                ResetInteractionState();
            };
            menu.Items.Add(removeItem);

            menu.Items.Add(new Separator());
        }

        var addItem = new MenuItem { Header = "Add Shortcut..." };
        addItem.Click += (_, _) =>
        {
            var dialog = new EditShortcutWindow();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                _config.Shortcuts.Add(dialog.Result);
                _configService.Save(_config);
                RebuildGrid();
            }
            ResetInteractionState();
        };
        menu.Items.Add(addItem);

        var importMenu = new MenuItem { Header = "Import From" };
        var importTaskbar = new MenuItem { Header = "Taskbar..." };
        importTaskbar.Click += (_, _) =>
        {
            var taskbarPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            ImportFrom("Import from Taskbar", taskbarPath);
            ResetInteractionState();
        };
        importMenu.Items.Add(importTaskbar);

        var importStartMenu = new MenuItem { Header = "Start Menu..." };
        importStartMenu.Click += (_, _) =>
        {
            var userStart = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
            var allStart = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
            ImportFrom("Import from Start Menu", new string[] { userStart, allStart });
            ResetInteractionState();
        };
        importMenu.Items.Add(importStartMenu);
        menu.Items.Add(importMenu);

        menu.Items.Add(new Separator());

        var lockItem = new MenuItem
        {
            Header = "Lock Icons",
            IsCheckable = true,
            IsChecked = _config.Settings.Locked
        };
        lockItem.Click += (_, _) =>
        {
            _config.Settings.Locked = !_config.Settings.Locked;
            _configService.Save(_config);
            ResetInteractionState();
        };
        menu.Items.Add(lockItem);

        menu.Items.Add(new Separator());

        var edgeSubMenu = new MenuItem { Header = "Edge" };
        var leftItem = new MenuItem { Header = "Left", IsChecked = _config.Settings.Edge == ScreenEdge.Left };
        leftItem.Click += (_, _) => { _edgeDetector.SwitchEdge(ScreenEdge.Left); ResetInteractionState(); };
        edgeSubMenu.Items.Add(leftItem);
        var rightItem = new MenuItem { Header = "Right", IsChecked = _config.Settings.Edge == ScreenEdge.Right };
        rightItem.Click += (_, _) => { _edgeDetector.SwitchEdge(ScreenEdge.Right); ResetInteractionState(); };
        edgeSubMenu.Items.Add(rightItem);
        menu.Items.Add(edgeSubMenu);

        var pinItem = new MenuItem
        {
            Header = "Pin",
            IsCheckable = true,
            IsChecked = _config.Settings.Pinned
        };
        pinItem.Click += (_, _) => { _edgeDetector.TogglePinned(); ResetInteractionState(); };
        menu.Items.Add(pinItem);

        var startupItem = new MenuItem
        {
            Header = "Launch on Startup",
            IsCheckable = true,
            IsChecked = StartupService.IsEnabled()
        };
        startupItem.Click += (_, _) =>
        {
            StartupService.SetEnabled(!StartupService.IsEnabled());
            ResetInteractionState();
        };
        menu.Items.Add(startupItem);

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
            RebuildGrid();
        }
    }

    private void ImportFrom(string title, string searchPath)
    {
        var existing = _config.Shortcuts.Select(s => s.Path);
        var dialog = new ImportShortcutsWindow(title, searchPath, existing) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedShortcuts.Count > 0)
        {
            foreach (var item in dialog.SelectedShortcuts)
            {
                AssignNextFreeSlot(item);
                _config.Shortcuts.Add(item);
            }
            _configService.Save(_config);
            RebuildGrid();
        }
    }

    private void ImportFrom(string title, string[] searchPaths)
    {
        var existing = _config.Shortcuts.Select(s => s.Path);
        var dialog = new ImportShortcutsWindow(title, searchPaths, existing) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedShortcuts.Count > 0)
        {
            foreach (var item in dialog.SelectedShortcuts)
            {
                AssignNextFreeSlot(item);
                _config.Shortcuts.Add(item);
            }
            _configService.Save(_config);
            RebuildGrid();
        }
    }

    private void AssignNextFreeSlot(ShortcutItem item)
    {
        var usedSlots = new HashSet<int>(_config.Shortcuts
            .Where(s => s.Slot >= 0 && s.Type != ShortcutType.Separator)
            .Select(s => s.Slot));
        int slot = 0;
        while (usedSlots.Contains(slot)) slot++;
        item.Slot = slot;
        usedSlots.Add(slot);
    }

    private void EditShortcut(ShortcutItem item)
    {
        var dialog = new EditShortcutWindow(item);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            var idx = _config.Shortcuts.IndexOf(item);
            if (idx >= 0)
            {
                dialog.Result.Slot = item.Slot;
                _config.Shortcuts[idx] = dialog.Result;
            }
            _iconExtractor.InvalidateCache(item.Path);
            if (item.IconPath != null) _iconExtractor.InvalidateCache(item.IconPath);
            _configService.Save(_config);
            RebuildGrid();
        }
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        UnregisterAppBar();
        base.OnClosed(e);
    }
}

public class SlotView
{
    public FrameworkElement Element { get; set; } = null!;
    public ShortcutItem Item { get; set; } = null!;
}
