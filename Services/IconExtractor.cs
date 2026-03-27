using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using SidebarLauncher.Models;

namespace SidebarLauncher.Services;

public class IconExtractor
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex,
        IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(IntPtr pidl, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc,
        out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_PIDL = 0x000000008;
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

    // IShellLink COM interface for resolving .lnk metadata
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch,
            IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    public void InvalidateCache(string path)
    {
        _cache.Remove(path);
    }

    public ImageSource? GetIcon(ShortcutItem item)
    {
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
            else if (item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                result = GetShellPathIcon(item.Path);
            }
            else
            {
                result = item.Type switch
                {
                    ShortcutType.Folder => GetFolderIcon(),
                    ShortcutType.Url => GetShellIcon(".html"),
                    ShortcutType.Script => GetShellIcon(".bat"),
                    ShortcutType.Terminal => GetTerminalIcon(),
                    _ => GetFileIcon(item.Path)
                };
            }
        }
        catch { }

        _cache[keyPath] = result;
        return result;
    }

    /// <summary>
    /// Gets the icon for a file. For .lnk files, reads the shortcut's icon location
    /// metadata to get the correct icon without the overlay arrow.
    /// </summary>
    public static ImageSource? GetFileIcon(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                var ext = Path.GetExtension(filePath);
                return !string.IsNullOrEmpty(ext) ? GetShellIcon(ext) : null;
            }

            // For .lnk files, extract the icon the shortcut specifies
            if (Path.GetExtension(filePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var icon = GetLnkIcon(filePath);
                if (icon != null) return icon;
            }

            // For regular files, use ExtractAssociatedIcon
            using var ico = Icon.ExtractAssociatedIcon(filePath);
            if (ico != null)
                return IconToImageSource(ico);
        }
        catch { }

        var fallbackExt = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(fallbackExt) ? GetShellIcon(fallbackExt) : null;
    }

    /// <summary>
    /// Reads a .lnk file's icon metadata and extracts the exact icon it specifies.
    /// Priority: 1) GetIconLocation from .lnk, 2) target exe icon, 3) null
    /// </summary>
    private static ImageSource? GetLnkIcon(string lnkPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(lnkPath, 0);

            // Try the shortcut's explicit icon location first
            var iconPathBuf = new StringBuilder(260);
            link.GetIconLocation(iconPathBuf, iconPathBuf.Capacity, out int iconIndex);
            var iconPath = iconPathBuf.ToString();

            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                // Expand environment variables like %SystemRoot%
                iconPath = Environment.ExpandEnvironmentVariables(iconPath);

                if (File.Exists(iconPath))
                {
                    var result = ExtractIconByIndex(iconPath, iconIndex);
                    if (result != null) return result;
                }
            }

            // Fall back to the target executable
            var targetBuf = new StringBuilder(260);
            link.GetPath(targetBuf, targetBuf.Capacity, IntPtr.Zero, 0);
            var target = targetBuf.ToString();

            if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
            {
                using var ico = Icon.ExtractAssociatedIcon(target);
                if (ico != null)
                    return IconToImageSource(ico);
            }
        }
        catch { }

        // Final fallback: get the PIDL from the .lnk and use SHGetFileInfo with
        // SHGFI_PIDL to get the icon without the shortcut overlay arrow.
        // Handles shell objects like "This PC" that have no file path.
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(lnkPath, 0);
            link.GetIDList(out IntPtr pidl);
            if (pidl != IntPtr.Zero)
            {
                try
                {
                    var shinfo = new SHFILEINFO();
                    var result = SHGetFileInfo(pidl, 0, ref shinfo,
                        (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_PIDL);
                    if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                    {
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
                }
                finally
                {
                    CoTaskMemFree(pidl);
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Extracts a specific icon by index from an exe, dll, or ico file.
    /// </summary>
    private static ImageSource? ExtractIconByIndex(string filePath, int index)
    {
        var largeIcons = new IntPtr[1];
        var smallIcons = new IntPtr[1];

        try
        {
            uint count = ExtractIconEx(filePath, index, largeIcons, smallIcons, 1);
            if (count > 0 && largeIcons[0] != IntPtr.Zero)
            {
                using var icon = Icon.FromHandle(largeIcons[0]);
                return IconToImageSource(icon);
            }
        }
        finally
        {
            if (largeIcons[0] != IntPtr.Zero) DestroyIcon(largeIcons[0]);
            if (smallIcons[0] != IntPtr.Zero) DestroyIcon(smallIcons[0]);
        }

        return null;
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

    /// <summary>
    /// Gets the icon for a UWP/PWA app from its package manifest images.
    /// </summary>
    private static ImageSource? GetTerminalIcon()
    {
        var cmdPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        if (File.Exists(cmdPath))
        {
            try
            {
                using var ico = Icon.ExtractAssociatedIcon(cmdPath);
                if (ico != null) return IconToImageSource(ico);
            }
            catch { }
        }
        return GetShellIcon(".exe");
    }

    /// <summary>
    /// Gets the icon for any shell path (shell:AppsFolder\{AppID}, shell:MyComputerFolder, etc.)
    /// by parsing it to a PIDL and using SHGetFileInfo.
    /// </summary>
    public static ImageSource? GetShellPathIcon(string shellPath)
    {
        try
        {
            int hr = SHParseDisplayName(shellPath, IntPtr.Zero, out IntPtr pidl, 0, out _);
            if (hr != 0 || pidl == IntPtr.Zero) return null;

            try
            {
                var shinfo = new SHFILEINFO();
                var result = SHGetFileInfo(pidl, 0, ref shinfo,
                    (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_PIDL);
                if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
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
            }
            finally
            {
                CoTaskMemFree(pidl);
            }
        }
        catch { }

        return null;
    }

    public static ImageSource? GetAppPackageIcon(string appId)
    {
        try
        {
            var familyName = appId.Split('!')[0];

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"(Get-AppxPackage | Where-Object {{ $_.PackageFamilyName -eq '{familyName}' }} | Select-Object -First 1).InstallLocation\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var installLocation = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);

            if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation))
                return null;

            var manifestPath = System.IO.Path.Combine(installLocation, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) return null;

            var doc = XDocument.Load(manifestPath);
            XNamespace uapNs = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

            var visualElements = doc.Descendants(uapNs + "VisualElements").FirstOrDefault();
            var logoRelPath = visualElements?.Attribute("Square44x44Logo")?.Value;
            if (string.IsNullOrEmpty(logoRelPath)) return null;

            var logoDir = System.IO.Path.GetDirectoryName(System.IO.Path.Combine(installLocation, logoRelPath))!;
            var logoBase = System.IO.Path.GetFileNameWithoutExtension(logoRelPath);
            var logoExt = System.IO.Path.GetExtension(logoRelPath);

            string? bestFile = null;
            foreach (var suffix in new[] { ".targetsize-32", ".targetsize-48", ".targetsize-256", ".targetsize-24", "" })
            {
                var candidate = System.IO.Path.Combine(logoDir, logoBase + suffix + logoExt);
                if (File.Exists(candidate))
                {
                    bestFile = candidate;
                    break;
                }
                var unplated = System.IO.Path.Combine(logoDir, logoBase + suffix + "_altform-unplated" + logoExt);
                if (File.Exists(unplated))
                {
                    bestFile = unplated;
                    break;
                }
            }

            if (bestFile == null) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(bestFile, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 32;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { }

        return null;
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
