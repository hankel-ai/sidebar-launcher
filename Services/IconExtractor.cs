using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SidebarLauncher.Models;

namespace SidebarLauncher.Services;

public class IconExtractor
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public ImageSource? GetIcon(ShortcutItem item)
    {
        // Use custom icon path if specified
        var keyPath = item.IconPath ?? item.Path;
        if (_cache.TryGetValue(keyPath, out var cached))
            return cached;

        ImageSource? result = null;

        try
        {
            if (!string.IsNullOrEmpty(item.IconPath) && File.Exists(item.IconPath))
            {
                result = LoadImageFile(item.IconPath);
            }
            else
            {
                result = item.Type switch
                {
                    ShortcutType.Folder => GetFolderIcon(),
                    ShortcutType.Url => GetShellIcon(".html"),
                    ShortcutType.Script => GetShellIcon(".bat"),
                    _ => GetFileIcon(item.Path)
                };
            }
        }
        catch { }

        _cache[keyPath] = result;
        return result;
    }

    private static ImageSource? LoadImageFile(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource? GetFileIcon(string filePath)
    {
        // Try ExtractAssociatedIcon first (works for .exe and .lnk)
        try
        {
            if (File.Exists(filePath))
            {
                using var icon = Icon.ExtractAssociatedIcon(filePath);
                if (icon != null)
                    return IconToImageSource(icon);
            }
        }
        catch { }

        // Fallback: get icon by extension via shell
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext))
            return GetShellIcon(ext);

        return null;
    }

    private static ImageSource? GetFolderIcon()
    {
        var shinfo = new SHFILEINFO();
        SHGetFileInfo("folder", FILE_ATTRIBUTE_DIRECTORY, ref shinfo,
            (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);

        if (shinfo.hIcon == IntPtr.Zero) return null;

        try
        {
            using var icon = Icon.FromHandle(shinfo.hIcon);
            return IconToImageSource(icon);
        }
        finally
        {
            DestroyIcon(shinfo.hIcon);
        }
    }

    private static ImageSource? GetShellIcon(string extension)
    {
        var shinfo = new SHFILEINFO();
        SHGetFileInfo("file" + extension, FILE_ATTRIBUTE_NORMAL, ref shinfo,
            (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);

        if (shinfo.hIcon == IntPtr.Zero) return null;

        try
        {
            using var icon = Icon.FromHandle(shinfo.hIcon);
            return IconToImageSource(icon);
        }
        finally
        {
            DestroyIcon(shinfo.hIcon);
        }
    }

    private static ImageSource IconToImageSource(Icon icon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
