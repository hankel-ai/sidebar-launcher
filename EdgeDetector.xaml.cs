using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SidebarLauncher.Models;
using SidebarLauncher.Services;

namespace SidebarLauncher;

public partial class EdgeDetector : Window
{
    private readonly LauncherConfig _config;
    private readonly ConfigService _configService;
    private SidebarWindow? _sidebar;
    private readonly DispatcherTimer _topmostTimer;

    public EdgeDetector(LauncherConfig config, ConfigService configService)
    {
        _config = config;
        _configService = configService;
        InitializeComponent();
        Loaded += OnLoaded;

        // Periodically reassert Topmost — Windows can demote it after
        // session switches, fullscreen apps, or RDP reconnections.
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _topmostTimer.Tick += (_, _) =>
        {
            if (!Topmost) Topmost = true;
            // Force Win32 z-order refresh even when property is already true
            Topmost = false;
            Topmost = true;
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionAtEdge();
        _topmostTimer.Start();

        // Reposition when display resolution/DPI changes (e.g. RDP from iPad)
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Reposition + reassert Topmost on RDP session unlock/reconnect
        SystemEvents.SessionSwitch += OnSessionSwitch;

        if (_config.Settings.Pinned)
            ShowSidebar();
    }

    public void PositionAtEdge()
    {
        var screen = GetTargetScreen();
        var workArea = screen.WorkingArea;
        var dpiScale = VisualTreeHelper.GetDpi(this);

        // Convert physical pixels to WPF DIPs
        double left, top, width, height;
        top = workArea.Top / dpiScale.DpiScaleY;
        height = workArea.Height / dpiScale.DpiScaleY;
        width = 2; // 2 DIP detection strip

        if (_config.Settings.Edge == ScreenEdge.Left)
            left = workArea.Left / dpiScale.DpiScaleX;
        else
            left = (workArea.Right / dpiScale.DpiScaleX) - width;

        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    private Screen GetTargetScreen()
    {
        var screens = Screen.AllScreens;
        var idx = _config.Settings.MonitorIndex;
        if (idx >= 0 && idx < screens.Length)
            return screens[idx];
        return Screen.PrimaryScreen!;
    }

    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ShowSidebar();
    }

    private void OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        // Show sidebar when user drags files to the edge
        ShowSidebar();
    }

    public void ShowSidebar()
    {
        if (_sidebar == null || !_sidebar.IsLoaded)
        {
            _sidebar = new SidebarWindow(_config, _configService, this);
            _sidebar.Closed += (_, _) => _sidebar = null;
        }

        _sidebar.SlideIn();
    }

    public void RefreshConfig()
    {
        PositionAtEdge();
        _sidebar?.RefreshShortcuts();
    }

    public void SwitchEdge(ScreenEdge newEdge)
    {
        _config.Settings.Edge = newEdge;
        _configService.Save(_config);
        PositionAtEdge();
        _sidebar?.Reposition();
    }

    public void TogglePinned()
    {
        _config.Settings.Pinned = !_config.Settings.Pinned;
        _configService.Save(_config);

        if (_config.Settings.Pinned)
        {
            ShowSidebar();
        }
        else
        {
            _sidebar?.UnregisterAppBar();
            _sidebar?.SlideOut();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Display resolution or DPI changed — reposition to new screen edge
        Dispatcher.BeginInvoke(PositionAtEdge);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock ||
            e.Reason == SessionSwitchReason.RemoteConnect ||
            e.Reason == SessionSwitchReason.ConsoleConnect)
        {
            // After RDP reconnect or unlock, reposition and force z-order
            Dispatcher.BeginInvoke(() =>
            {
                PositionAtEdge();
                Topmost = false;
                Topmost = true;
            });
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmostTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        base.OnClosed(e);
    }
}
