using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SidebarLauncher.Models;

namespace SidebarLauncher.Services;

public class AppBarService : IDisposable
{
    private IntPtr _hwnd;
    private bool _registered;
    private HwndSource? _hwndSource;
    private readonly Window _window;
    private readonly LauncherSettings _settings;

    private const int ABM_NEW = 0x00;
    private const int ABM_REMOVE = 0x01;
    private const int ABM_QUERYPOS = 0x02;
    private const int ABM_SETPOS = 0x03;

    private const int ABE_LEFT = 0;
    private const int ABE_RIGHT = 2;

    private const int ABN_STATECHANGE = 0x00;
    private const int ABN_POSCHANGED = 0x01;

    private static readonly int WM_APPBAR_CALLBACK;

    static AppBarService()
    {
        WM_APPBAR_CALLBACK = RegisterWindowMessage("SidebarLauncher_AppBarCallback");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string msg);

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern uint SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uCallbackMessage;
        public int uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public AppBarService(Window window, LauncherSettings settings)
    {
        _window = window;
        _settings = settings;
    }

    public void Register()
    {
        if (_registered) return;

        _hwndSource = PresentationSource.FromVisual(_window) as HwndSource;
        if (_hwndSource == null) return;

        _hwnd = _hwndSource.Handle;
        _hwndSource.AddHook(WndProc);

        var abd = NewAppBarData();
        abd.uCallbackMessage = WM_APPBAR_CALLBACK;
        SHAppBarMessage(ABM_NEW, ref abd);
        _registered = true;

        SetPosition();
    }

    public void Unregister()
    {
        if (!_registered) return;

        var abd = NewAppBarData();
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _registered = false;

        _hwndSource?.RemoveHook(WndProc);
    }

    public void SetPosition()
    {
        if (!_registered) return;

        var screen = System.Windows.Forms.Screen.PrimaryScreen!;
        var workArea = screen.Bounds; // Use full bounds, not WorkingArea
        var dpi = VisualTreeHelper.GetDpi(_window);
        int barWidthPx = (int)(_settings.BarWidth * dpi.DpiScaleX);

        var abd = NewAppBarData();
        abd.uEdge = _settings.Edge == ScreenEdge.Left ? ABE_LEFT : ABE_RIGHT;

        if (_settings.Edge == ScreenEdge.Left)
        {
            abd.rc.Left = workArea.Left;
            abd.rc.Top = workArea.Top;
            abd.rc.Right = workArea.Left + barWidthPx;
            abd.rc.Bottom = workArea.Bottom;
        }
        else
        {
            abd.rc.Left = workArea.Right - barWidthPx;
            abd.rc.Top = workArea.Top;
            abd.rc.Right = workArea.Right;
            abd.rc.Bottom = workArea.Bottom;
        }

        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        SHAppBarMessage(ABM_SETPOS, ref abd);

        // Position the window to match
        _window.Left = abd.rc.Left / dpi.DpiScaleX;
        _window.Top = abd.rc.Top / dpi.DpiScaleY;
        _window.Width = (abd.rc.Right - abd.rc.Left) / dpi.DpiScaleX;
        _window.Height = (abd.rc.Bottom - abd.rc.Top) / dpi.DpiScaleY;
    }

    private APPBARDATA NewAppBarData()
    {
        return new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = _hwnd
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APPBAR_CALLBACK)
        {
            switch (wParam.ToInt32())
            {
                case ABN_POSCHANGED:
                    SetPosition();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
    }
}
