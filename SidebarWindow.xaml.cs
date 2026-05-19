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
    private int _rows;
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

        int cellSize = _config.Settings.BarWidth;
        int columns = Math.Max(1, _config.Settings.Columns);
        double totalBarWidth = cellSize * columns;
        Width = totalBarWidth;

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
            Left = (workArea.Right / dpiScale.DpiScaleX) - totalBarWidth;
            OuterBorder.CornerRadius = new CornerRadius(8, 0, 0, 8);
            OuterBorder.BorderThickness = new Thickness(1, 1, 0, 1);
        }

        if (!_isVisible)
            SlideTransform.X = _config.Settings.Edge == ScreenEdge.Left ? -totalBarWidth : totalBarWidth;

        double availableHeight = maxHeight;
        _rows = Math.Max(1, (int)(availableHeight / cellSize));
        _totalSlots = _rows * columns;
    }

    public void Relayout()
    {
        Reposition();
        RebuildGrid();
        _appBar?.SetPosition();
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
        int columns = Math.Max(1, _config.Settings.Columns);
        SlotCanvas.Width = bw * columns;
        SlotCanvas.Height = _rows * bw;

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

        // Render shortcuts at their slot positions (column-major: fill down then across)
        foreach (var shortcut in _config.Shortcuts)
        {
            if (shortcut.Type == ShortcutType.Separator) continue;
            if (shortcut.Slot < 0 || shortcut.Slot >= _totalSlots) continue;

            int col = shortcut.Slot / _rows;
            int row = shortcut.Slot % _rows;

            var sv = CreateSlotView(shortcut);
            _slotViews[shortcut.Slot] = sv;
            Canvas.SetLeft(sv.Element, col * bw);
            Canvas.SetTop(sv.Element, row * bw);
            SlotCanvas.Children.Add(sv.Element);
        }
    }

    public void RefreshShortcuts() => RebuildGrid();

    private void AssignSlots()
    {
        var usedSlots = new HashSet<int>(
            _config.Shortcuts
                .Where(s => s.Slot >= 0 && s.Type != ShortcutType.Separator)
                .Select(s => s.Slot));

        bool changed = false;
        foreach (var shortcut in _config.Shortcuts)
        {
            if (shortcut.Type == ShortcutType.Separator) continue;
            if (shortcut.Slot >= 0) continue;

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

        var grid = new Grid();
        var scaleTransform = new ScaleTransform(1, 1);
        grid.RenderTransform = scaleTransform;
        grid.RenderTransformOrigin = new Point(0.5, 0.5);

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
            var reset = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100));
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, reset);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, reset);
        };

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
            // Release scale animation
            var pressUp = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pressUp);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pressUp);

            if (!_isDragging)
            {
                var pos = e.GetPosition(this);
                var delta = pos - _dragStartPoint;
                if (Math.Abs(delta.X) <= 6 && Math.Abs(delta.Y) <= 6)
                {
                    // Shift+Click on a folder shortcut → open Windows Terminal tab at that path
                    bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                    if (shiftHeld && item.Type == ShortcutType.Folder)
                        ShellLauncher.OpenTerminalAt(item.Path);
                    else
                        ShellLauncher.Launch(item);
                }
            }
        };

        // Drag start
        border.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStartPoint = e.GetPosition(this);
            // Press-down scale animation
            var pressDown = new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(80))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pressDown);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pressDown);

            if (!_config.Settings.Locked)
            {
                _dragSource = _slotViews.Values.FirstOrDefault(sv => sv.Item == item);
            }
        };

        border.PreviewMouseMove += (_, e) =>
        {
            if (_dragSource != null && !_config.Settings.Locked &&
                e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                var pos = e.GetPosition(this);
                var delta = pos - _dragStartPoint;
                if (Math.Abs(delta.X) > 6 || Math.Abs(delta.Y) > 6)
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
        double totalBarWidth = _config.Settings.BarWidth * Math.Max(1, _config.Settings.Columns);
        double target = _config.Settings.Edge == ScreenEdge.Left
            ? -totalBarWidth : totalBarWidth;

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
        int bw = _config.Settings.BarWidth;
        int row = Math.Clamp((int)(pos.Y / bw), 0, _rows - 1);
        int col = Math.Clamp((int)(pos.X / bw), 0, Math.Max(1, _config.Settings.Columns) - 1);
        int slot = col * _rows + row;
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
                int bw = _config.Settings.BarWidth;
                int highlightCol = slot / _rows;
                int highlightRow = slot % _rows;
                Canvas.SetLeft(_highlightRect, highlightCol * bw);
                Canvas.SetTop(_highlightRect, highlightRow * bw);
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
                int sourceSlot = sv.Item.Slot;
                if (sourceSlot != targetSlot)
                {
                    var occupant = _config.Shortcuts.FirstOrDefault(
                        s => s.Slot == targetSlot && s != sv.Item && s.Type != ShortcutType.Separator);

                    // Remove dragged item from its slot so it becomes the gap
                    sv.Item.Slot = -1;

                    // Build set of occupied slots (excluding dragged item)
                    var occupied = new HashSet<int>(
                        _config.Shortcuts
                            .Where(s => s != sv.Item && s.Type != ShortcutType.Separator && s.Slot >= 0)
                            .Select(s => s.Slot));

                    if (occupied.Contains(targetSlot))
                    {
                        int sourceCol = sourceSlot / _rows;
                        int targetCol = targetSlot / _rows;

                        if (sourceCol != targetCol)
                        {
                            // Cross-column drag: find nearest gap within target column, shift toward it
                            int colStart = targetCol * _rows;
                            int colEnd = colStart + _rows - 1;

                            // Search both directions from target for nearest gap in this column
                            int gapSlot = -1;
                            for (int dist = 1; dist < _rows; dist++)
                            {
                                int up = targetSlot - dist;
                                if (up >= colStart && !occupied.Contains(up)) { gapSlot = up; break; }
                                int down = targetSlot + dist;
                                if (down <= colEnd && !occupied.Contains(down)) { gapSlot = down; break; }
                            }

                            if (gapSlot >= 0)
                            {
                                int step = gapSlot < targetSlot ? -1 : 1;
                                int s = gapSlot - step;
                                while (s != targetSlot - step)
                                {
                                    if (occupied.Contains(s))
                                    {
                                        var item = _config.Shortcuts.FirstOrDefault(
                                            sc => sc.Slot == s && sc != sv.Item && sc.Type != ShortcutType.Separator);
                                        if (item != null)
                                            item.Slot = s + step;
                                    }
                                    s -= step;
                                }
                            }
                            else
                            {
                                // No gap in target column — swap with occupant
                                occupant.Slot = sourceSlot;
                            }
                        }
                        else
                        {
                            // Same-column drag: shift in the direction the dragged icon came from
                            int step = sourceSlot > targetSlot ? 1 : -1;

                            int gapSlot = targetSlot;
                            while (gapSlot >= 0 && gapSlot < _totalSlots && occupied.Contains(gapSlot))
                                gapSlot += step;

                            if (gapSlot >= 0 && gapSlot < _totalSlots)
                            {
                                int s = gapSlot - step;
                                while (s != targetSlot - step)
                                {
                                    if (occupied.Contains(s))
                                    {
                                        var item = _config.Shortcuts.FirstOrDefault(
                                            sc => sc.Slot == s && sc != sv.Item && sc.Type != ShortcutType.Separator);
                                        if (item != null)
                                            item.Slot = s + step;
                                    }
                                    s -= step;
                                }
                            }
                        }
                    }

                    sv.Item.Slot = targetSlot;
                }
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
        int bw = _config.Settings.BarWidth;
        int columns = Math.Max(1, _config.Settings.Columns);
        int clickRow = Math.Clamp((int)(pos.Y / bw), 0, _rows - 1);
        int clickCol = Math.Clamp((int)(pos.X / bw), 0, columns - 1);
        int slot = clickCol * _rows + clickRow;
        var clickedItem = _config.Shortcuts.FirstOrDefault(
            s => s.Slot == slot && s.Type != ShortcutType.Separator);

        var menu = new ContextMenu();

        if (clickedItem != null)
        {
            if (clickedItem.Type == ShortcutType.Folder)
            {
                var terminalItem = new MenuItem
                {
                    Header = "Open in Terminal",
                    InputGestureText = "Shift+Click",
                    FontWeight = FontWeights.Bold
                };
                terminalItem.Click += (_, _) =>
                {
                    ShellLauncher.OpenTerminalAt(clickedItem.Path);
                    ResetInteractionState();
                };
                menu.Items.Add(terminalItem);
                menu.Items.Add(new Separator());
            }

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

        // Push All Up / Push All Down — create a gap at the clicked slot
        {
            int colStart = (slot / _rows) * _rows;
            int colEnd = colStart + _rows - 1;
            var occupiedSlots = new HashSet<int>(
                _config.Shortcuts
                    .Where(s => s.Type != ShortcutType.Separator && s.Slot >= 0)
                    .Select(s => s.Slot));

            // Check for gap above (lower slot in same column)
            bool hasGapAbove = false;
            for (int s = slot - 1; s >= colStart; s--)
            {
                if (!occupiedSlots.Contains(s)) { hasGapAbove = true; break; }
            }

            var pushUp = new MenuItem { Header = "Push All Up", IsEnabled = hasGapAbove };
            pushUp.Click += (_, _) =>
            {
                // Find nearest gap above
                int gap = -1;
                for (int s = slot - 1; s >= colStart; s--)
                {
                    if (!occupiedSlots.Contains(s)) { gap = s; break; }
                }
                if (gap >= 0)
                {
                    // Shift icons from gap+1 to slot up by one (toward gap)
                    for (int s = gap + 1; s <= slot; s++)
                    {
                        var item = _config.Shortcuts.FirstOrDefault(
                            sc => sc.Slot == s && sc.Type != ShortcutType.Separator);
                        if (item != null)
                            item.Slot = s - 1;
                    }
                    _configService.Save(_config);
                    RebuildGrid();
                }
                ResetInteractionState();
            };
            menu.Items.Add(pushUp);

            // Check for gap below (higher slot in same column)
            bool hasGapBelow = false;
            for (int s = slot + 1; s <= colEnd; s++)
            {
                if (!occupiedSlots.Contains(s)) { hasGapBelow = true; break; }
            }

            var pushDown = new MenuItem { Header = "Push All Down", IsEnabled = hasGapBelow };
            pushDown.Click += (_, _) =>
            {
                // Find nearest gap below
                int gap = -1;
                for (int s = slot + 1; s <= colEnd; s++)
                {
                    if (!occupiedSlots.Contains(s)) { gap = s; break; }
                }
                if (gap >= 0)
                {
                    // Shift icons from slot to gap-1 down by one (toward gap)
                    for (int s = gap - 1; s >= slot; s--)
                    {
                        var item = _config.Shortcuts.FirstOrDefault(
                            sc => sc.Slot == s && sc.Type != ShortcutType.Separator);
                        if (item != null)
                            item.Slot = s + 1;
                    }
                    _configService.Save(_config);
                    RebuildGrid();
                }
                ResetInteractionState();
            };
            menu.Items.Add(pushDown);

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
            var entries = ImportShortcutsWindow.DiscoverInstalledApps(GetExistingPaths());
            if (entries.Count == 0)
            {
                MessageBox.Show("No additional Start Menu apps found.", "Import",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                ResetInteractionState();
                return;
            }
            var dialog = new ImportShortcutsWindow("Import from Start Menu", entries) { Owner = this };
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
            ResetInteractionState();
        };
        importMenu.Items.Add(importStartMenu);
        menu.Items.Add(importMenu);

        menu.Items.Add(new Separator());

        var columnsSubMenu = new MenuItem { Header = "Columns" };
        for (int c = 1; c <= 4; c++)
        {
            int cols = c;
            var colItem = new MenuItem { Header = cols.ToString(), IsChecked = _config.Settings.Columns == cols };
            colItem.Click += (_, _) =>
            {
                _config.Settings.Columns = cols;
                _configService.Save(_config);
                Reposition();
                RebuildGrid();
                if (_appBar != null) _appBar.SetPosition();
                ResetInteractionState();
            };
            columnsSubMenu.Items.Add(colItem);
        }
        menu.Items.Add(columnsSubMenu);

        var hoverDelaySubMenu = new MenuItem { Header = "Hover Delay" };
        var hoverDelayOptions = new (string Label, int Ms)[]
        {
            ("Instant",   0),
            ("250 ms",  250),
            ("500 ms",  500),
            ("750 ms",  750),
            ("1 second", 1000),
            ("1.5 seconds", 1500),
            ("2 seconds", 2000),
            ("3 seconds", 3000)
        };
        foreach (var (label, ms) in hoverDelayOptions)
        {
            int captured = ms;
            var item = new MenuItem
            {
                Header = label,
                IsChecked = _config.Settings.HoverShowDelayMs == captured
            };
            item.Click += (_, _) =>
            {
                _config.Settings.HoverShowDelayMs = captured;
                _configService.Save(_config);
                ResetInteractionState();
            };
            hoverDelaySubMenu.Items.Add(item);
        }
        menu.Items.Add(hoverDelaySubMenu);

        var edgeSubMenu = new MenuItem { Header = "Edge" };
        var leftItem = new MenuItem { Header = "Left", IsChecked = _config.Settings.Edge == ScreenEdge.Left };
        leftItem.Click += (_, _) => { _edgeDetector.SwitchEdge(ScreenEdge.Left); ResetInteractionState(); };
        edgeSubMenu.Items.Add(leftItem);
        var rightItem = new MenuItem { Header = "Right", IsChecked = _config.Settings.Edge == ScreenEdge.Right };
        rightItem.Click += (_, _) => { _edgeDetector.SwitchEdge(ScreenEdge.Right); ResetInteractionState(); };
        edgeSubMenu.Items.Add(rightItem);
        menu.Items.Add(edgeSubMenu);

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

    private IEnumerable<string> GetExistingPaths()
    {
        // Return both paths and names so import can deduplicate properly
        return _config.Shortcuts.Select(s => s.Path)
            .Concat(_config.Shortcuts.Select(s => s.Name));
    }

    private void ImportFrom(string title, string searchPath)
    {
        var existing = GetExistingPaths();
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
        var existing = GetExistingPaths();
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
