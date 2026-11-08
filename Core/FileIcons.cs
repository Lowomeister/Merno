// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Merno.Core;

/// <summary>Значки программ прямо из их exe/ico/png — как в Проводнике. Кэшируются, безопасны для фонового потока.</summary>
public static class FileIcons
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint attr; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint n);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;

    static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Путь вида «C:\a.exe,0» или «"C:\a.exe"» → файл и номер значка.</summary>
    static (string File, int Index) Parse(string spec)
    {
        spec = Environment.ExpandEnvironmentVariables(spec.Trim().Trim('"'));
        int idx = 0, comma = spec.LastIndexOf(',');
        if (comma > 2 && int.TryParse(spec[(comma + 1)..].Trim(), out var i)) { idx = i; spec = spec[..comma].Trim().Trim('"'); }
        return (spec, idx);
    }

    public static ImageSource Get(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        lock (Cache) if (Cache.TryGetValue(spec, out var c)) return c;
        ImageSource img = null;
        try
        {
            var (file, idx) = Parse(spec);
            if (SafeFs.IsLocal(file) && File.Exists(file))   // путь из реестра: по сети не ходим
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg")
                {
                    var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(file); bmp.DecodePixelWidth = 48; bmp.EndInit();
                    img = bmp;
                }
                else
                {
                    IntPtr h = IntPtr.Zero;
                    var large = new IntPtr[1];
                    if (ExtractIconEx(file, idx, large, null, 1) > 0 && large[0] != IntPtr.Zero) h = large[0];
                    else { var info = new SHFILEINFO(); SHGetFileInfo(file, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_LARGEICON); h = info.hIcon; }
                    if (h != IntPtr.Zero)
                    {
                        img = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        DestroyIcon(h);
                    }
                }
                img?.Freeze();
            }
        }
        catch { img = null; }
        lock (Cache) Cache[spec] = img;
        return img;
    }

    /// <summary>Есть ли в exe/dll свои значки (у служебных помощников их часто нет — Windows рисует пустой «файл»).</summary>
    public static bool HasOwnIcon(string file)
    {
        if (!SafeFs.IsLocal(file)) return true;
        try { return ExtractIconEx(file, -1, null, null, 0) > 0; } catch { return true; }
    }

    /// <summary>Логотип приложения Windows из папки пакета (Assets\StoreLogo.scale-100.png и т.п.).</summary>
    public static string AppxLogo(string installDir, string logoRel)
    {
        try
        {
            if (string.IsNullOrEmpty(installDir) || string.IsNullOrEmpty(logoRel) || !SafeFs.IsLocal(installDir)) return null;
            var full = Path.Combine(installDir, logoRel);
            if (File.Exists(full)) return full;
            var dir = Path.GetDirectoryName(full); var name = Path.GetFileNameWithoutExtension(full); var ext = Path.GetExtension(full);
            if (!Directory.Exists(dir)) return null;
            // берём вариант с масштабом поближе к 100–200 %
            return Directory.GetFiles(dir, name + "*" + ext).OrderBy(f => f.Contains("scale-100") ? 0 : f.Contains("scale-200") ? 1 : f.Contains("targetsize-48") ? 0 : 2).FirstOrDefault();
        }
        catch { return null; }
    }
}
