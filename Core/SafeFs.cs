// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Merno.Core;

/// <summary>
/// Безопасное удаление для очистки (Merno работает с правами администратора).
/// Обычное File.Delete(путь) идёт по пути заново: если кто-то успел подменить папку ссылкой (junction),
/// удалится чужой файл. Здесь файл сначала открывается (сама ссылка, а не то, куда она ведёт),
/// проверяется его настоящее место, и удаляется именно открытый файл — подмена после открытия ничего не даёт.
/// </summary>
public static class SafeFs
{
    const uint DELETE = 0x00010000, FILE_READ_ATTRIBUTES = 0x80, FILE_WRITE_ATTRIBUTES = 0x100, SYNCHRONIZE = 0x00100000;
    const uint FILE_SHARE_ALL = 0x7, OPEN_EXISTING = 3;
    const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000, FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    const int FileBasicInfo = 0, FileDispositionInfo = 4, FileDispositionInfoEx = 21;
    const uint DISP_DELETE = 0x1, DISP_POSIX = 0x2, DISP_IGNORE_READONLY = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    struct FILE_BASIC_INFO { public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; public uint FileAttributes; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder buf, uint len, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetFileInformationByHandle(SafeFileHandle h, int cls, ref uint info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetFileInformationByHandle(SafeFileHandle h, int cls, ref byte info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetFileInformationByHandle(SafeFileHandle h, int cls, ref FILE_BASIC_INFO info, uint size);

    /// <summary>
    /// Путь на диске этого компьютера: «C:\…», не сетевой (\\сервер, //, \\?\UNC, \\.\) и не на подключённом сетевом диске.
    /// Пути из реестра и чужих файлов проверять этим до File.Exists и чтения: иначе Merno с правами администратора
    /// пойдёт по сети (вход по NTLM, зависание на таймауте, разбор чужого файла).
    /// </summary>
    public static bool IsLocal(string path) => IsLocal(path, d => new DriveInfo(d.ToString()).DriveType);

    internal static bool IsLocal(string path, Func<char, DriveType> driveType)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3) return false;
        if (!char.IsAsciiLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/')) return false;
        try { return driveType(path[0]) is not (DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown); }
        catch { return false; }
    }

    static SafeFileHandle Open(string path, uint access) =>
        CreateFileW(path, access, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);

    static string Final(SafeFileHandle h)
    {
        var sb = new StringBuilder(1024);
        uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
        if (n == 0) return null;
        if (n >= sb.Capacity) { sb = new StringBuilder((int)n + 1); n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0); if (n == 0) return null; }
        return sb.ToString().TrimEnd('\\');
    }

    /// <summary>Настоящий путь папки-корня очистки. null — папки нет, к ней нет доступа или она сама — ссылка (такую не чистим).</summary>
    public static string RootFinal(string dir)
    {
        try
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) return null;
            using var h = Open(dir, FILE_READ_ATTRIBUTES | SYNCHRONIZE);
            if (h.IsInvalid) return null;
            return Final(h);
        }
        catch { return null; }
    }

    /// <summary>Удалить файл или пустую папку, только если открытый объект действительно лежит внутри root (root — из RootFinal).
    /// Ссылка (junction/symlink) удаляется сама, без того, на что она указывает.</summary>
    public static bool DeleteUnder(string path, string rootFinal) =>
        !string.IsNullOrEmpty(rootFinal) && DeleteChecked(path, real => real.StartsWith(rootFinal + "\\", StringComparison.OrdinalIgnoreCase));

    /// <summary>Удалить саму (уже пустую) папку-корень, только если это та же папка, что дала RootFinal.</summary>
    public static bool DeleteRoot(string dir, string rootFinal) =>
        !string.IsNullOrEmpty(rootFinal) && DeleteChecked(dir, real => string.Equals(real, rootFinal, StringComparison.OrdinalIgnoreCase));

    static bool DeleteChecked(string path, Func<string, bool> placeOk)
    {
        try
        {
            using var h = Open(path, DELETE | FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES | SYNCHRONIZE);
            if (h.IsInvalid) return false;   // занят (открыт без права на удаление) или нет доступа — пропускаем
            var real = Final(h);
            if (real == null || !placeOk(real)) return false;
            uint flags = DISP_DELETE | DISP_POSIX | DISP_IGNORE_READONLY;
            if (SetFileInformationByHandle(h, FileDispositionInfoEx, ref flags, 4)) return true;
            // старая Windows 10 (до 1809): снимаем «только чтение» через тот же открытый файл и удаляем по-старому
            var basic = new FILE_BASIC_INFO { FileAttributes = 0x80 };   // FILE_ATTRIBUTE_NORMAL, время не меняем (0)
            SetFileInformationByHandle(h, FileBasicInfo, ref basic, (uint)Marshal.SizeOf<FILE_BASIC_INFO>());
            byte del = 1;
            return SetFileInformationByHandle(h, FileDispositionInfo, ref del, 1);
        }
        catch { return false; }
    }

    /// <summary>Удалить содержимое папки dir (и саму папку, если withSelf), не выходя за rootFinal. Ссылки удаляются как ссылки.</summary>
    public static void DeleteTree(string dir, string rootFinal, bool withSelf)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir).ToList(); } catch { entries = Array.Empty<string>(); }
        foreach (var e in entries)
        {
            FileAttributes a;
            try { a = File.GetAttributes(e); } catch { continue; }
            if ((a & FileAttributes.Directory) != 0 && (a & FileAttributes.ReparsePoint) == 0) DeleteTree(e, rootFinal, true);
            else DeleteUnder(e, rootFinal);
        }
        if (withSelf) DeleteUnder(dir, rootFinal);
    }

    /// <summary>Убрать опустевшие папки внутри dir (снизу вверх), не заходя в ссылки. Непустые папки не удаляются (Windows не даст).</summary>
    public static void PruneEmpty(string dir, string rootFinal)
    {
        IEnumerable<string> subs;
        try { subs = Directory.EnumerateDirectories(dir).ToList(); } catch { return; }
        foreach (var d in subs)
        {
            FileAttributes a;
            try { a = File.GetAttributes(d); } catch { continue; }
            if ((a & FileAttributes.ReparsePoint) != 0) continue;
            PruneEmpty(d, rootFinal);
            try { if (!Directory.EnumerateFileSystemEntries(d).Any()) DeleteUnder(d, rootFinal); } catch { }
        }
    }
}
