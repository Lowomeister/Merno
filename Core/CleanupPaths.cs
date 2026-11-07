// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Merno.Core;

/// <summary>Пути очистки не берутся из TEMP, APPDATA или перенаправлений папок в HKCU.</summary>
internal static class CleanupPaths
{
    private static readonly Lazy<string> ProfilePath = new(ReadProfile);
    internal static string Profile => ProfilePath.Value;

    [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserProfileDirectory(IntPtr token, StringBuilder path, ref uint size);

    private static string ReadProfile()
    {
        using var identity = WindowsIdentity.GetCurrent();
        uint size = 0;
        GetUserProfileDirectory(identity.Token, null, ref size);
        if (size == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var path = new StringBuilder(checked((int)size));
        if (!GetUserProfileDirectory(identity.Token, path, ref size))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return Normalize(path.ToString()) ?? throw new IOException("Не удалось определить папку профиля для очистки.");
    }

    internal static string Expand(string template, string profile, string windows, string programData)
    {
        // Только известные подстановки. ExpandEnvironmentVariables здесь запрещён.
        var result = template
            .Replace("%LOCALAPPDATA%", Path.Combine(profile, "AppData", "Local"), StringComparison.OrdinalIgnoreCase)
            .Replace("%APPDATA%", Path.Combine(profile, "AppData", "Roaming"), StringComparison.OrdinalIgnoreCase)
            .Replace("%TEMP%", Path.Combine(profile, "AppData", "Local", "Temp"), StringComparison.OrdinalIgnoreCase)
            .Replace("%SystemRoot%", windows, StringComparison.OrdinalIgnoreCase)
            .Replace("%ProgramData%", programData, StringComparison.OrdinalIgnoreCase);
        if (result.Contains('%')) throw new ArgumentException("Неизвестная переменная в пути очистки.", nameof(template));
        return result;
    }

    internal static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Replace('/', '\\');
        // Никаких UNC, устройств, относительных путей и alternate data streams.
        if (path.Length < 4 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' || path[3..].Contains(':')) return null;
        if (path.Split('\\').Skip(1).Any(p => p.Length > 0 && p is not "." and not ".." && (p.EndsWith('.') || p.EndsWith(' ')))) return null;
        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\');
            return full.Length > 3 ? full : null;
        }
        catch { return null; }
    }

    internal static bool Allowed(string path, IEnumerable<string> allowedRoots)
    {
        string full = Normalize(path);
        return full != null && allowedRoots.Any(p => string.Equals(full, Normalize(p), StringComparison.OrdinalIgnoreCase));
    }

    internal static string ApprovedRoot(string path, IEnumerable<string> allowedRoots)
    {
        if (!Allowed(path, allowedRoots)) return null;
        string final = SafeFs.RootFinal(path);
        // RootFinal отвергает ссылку в корне. Сравнение с лексическим путём отвергает и junction в родителе.
        string physical = final?.StartsWith(@"\\?\", StringComparison.Ordinal) == true ? final[4..] : final;
        return string.Equals(Normalize(path), Normalize(physical), StringComparison.OrdinalIgnoreCase) && final != null ? final : null;
    }
}
