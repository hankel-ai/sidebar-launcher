using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using SidebarLauncher.Models;
using SidebarLauncher.Services;

namespace SidebarLauncher;

public partial class EdgeDetector : Window
{
    private readonly LauncherConfig _config;
    private readonly ConfigService _configService;
    private SidebarWindow? _sidebar;
    private NotifyIcon? _trayIcon;

    public EdgeDetector(LauncherConfig config, ConfigService configService)
    {
        _config = config;
        _configService = configService;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionAtEdge();
        SetupTrayIcon();

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
            ShowSidebar();
        else
            _sidebar?.SlideOut();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Sidebar Launcher",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Sidebar", null, (_, _) => ShowSidebar());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _sidebar?.Close();
            System.Windows.Application.Current.Shutdown();
        });

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowSidebar();
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
