// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Merno.Core;

/// <summary>Запись в списке «Установленные приложения», от которой не осталось файлов.</summary>
public class LeftoverEntry
{
    public string Hive, Path, Name;
}

/// <summary>
/// Остатки удалённых программ — пункт «Очистки».
///  1) Записи в списке программ Windows (раздел Uninstall), у которых нет ни программы удаления, ни папки программы.
///     Так бывает, если папку стёрли руками или удалили лаунчер раньше игры (Тарков): Windows продолжает «видеть» программу.
///     Перед удалением записи сохраняются в резервную копию — их можно вернуть в «Откате».
///  2) Папки с данными известных игр и лаунчеров, если самой программы на компьютере уже нет.
/// Осторожно: записи MSI, системные и записи на съёмных/сетевых дисках не трогаем.
/// </summary>
public static class Leftovers
{
    static readonly string[] Roots =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    // программа (как её ищем в списке установленных) → её папки с данными
    static readonly (string Detect, string[] Dirs)[] Known =
    {
        ("Escape from Tarkov|BsgLauncher|Battlestate", new[] { @"%APPDATA%\Battlestate Games", @"%LOCALAPPDATA%\Battlestate Games", @"%TEMP%\Battlestate Games" }),
        (@"^Battle\.net", new[] { @"%APPDATA%\Battle.net", @"%LOCALAPPDATA%\Battle.net", @"%ProgramData%\Battle.net" }),
        ("^Epic Games Launcher", new[] { @"%LOCALAPPDATA%\EpicGamesLauncher" }),
        ("^EA app|^EA Desktop|^Origin$", new[] { @"%LOCALAPPDATA%\Electronic Arts\EA Desktop", @"%ProgramData%\EA Desktop" }),
        ("^Ubisoft Connect|^Uplay", new[] { @"%LOCALAPPDATA%\Ubisoft Game Launcher" }),
        ("^Discord$", new[] { @"%APPDATA%\discord", @"%LOCALAPPDATA%\Discord" }),
    };

    /// <summary>Что найдено: записи без программы, папки удалённых программ, названия установленных программ.</summary>
    public static (List<LeftoverEntry> Entries, List<string> Dirs) Scan()
    {
        var entries = new List<LeftoverEntry>();
        var installed = new List<string>();
        foreach (var hive in new[] { "HKLM", "HKCU" })
            foreach (var root in Roots)
            {
                if (hive == "HKCU" && root.Contains("WOW6432Node")) continue;
                try
                {
                    using var b = Reg.Root(hive);
                    using var k = b.OpenSubKey(root);
                    if (k == null) continue;
                    foreach (var sub in k.GetSubKeyNames())
                    {
                        try
                        {
                            using var e = k.OpenSubKey(sub);
                            if (e == null || e.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                            if (IsOrphan(e)) entries.Add(new LeftoverEntry { Hive = hive, Path = root + "\\" + sub, Name = name.Trim() });
                            else installed.Add(name.Trim());
                        }
                        catch { }
                    }
                }
                catch { }
            }

        var dirs = new List<string>();
        foreach (var (detect, list) in Known)
        {
            var rx = new Regex(detect, RegexOptions.IgnoreCase);
            if (installed.Any(n => rx.IsMatch(n))) continue;   // программа стоит — её данные нужны
            foreach (var d in list)
            {
                var p = CleanupPaths.Expand(d, CleanupPaths.Profile,
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
                if (CleanupPaths.ApprovedRoot(p, new[] { p }) != null) dirs.Add(p);
            }
        }
        return (entries, dirs);
    }

    /// <summary>Записи без программы: нет файла удаления, нет папки программы и значка. Только на постоянных дисках.</summary>
    static bool IsOrphan(RegistryKey e)
    {
        if (e.GetValue("SystemComponent") is int sc && sc == 1) return false;
        if (e.GetValue("WindowsInstaller") is int wi && wi == 1) return false;   // MSI: Windows сама ведёт учёт
        if (e.GetValue("ParentKeyName") != null) return false;                     // обновление другой программы
        if (e.GetValue("UninstallString") is not string un || string.IsNullOrWhiteSpace(un)) return false;
        if (un.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) return false;
        // пакеты-«обёртки» (Visual C++, .NET и т. п.) хранят удаление в Package Cache; эту папку иногда чистят другие
        // программы — запись при этом нужна (по ней «Компоненты» видят, что VC++ стоит), не трогаем
        if (un.Contains("Package Cache", StringComparison.OrdinalIgnoreCase) || e.GetValue("BundleVersion") != null || e.GetValue("BundleProviderKey") != null) return false;
        if (e.GetValue("DisplayName") is string dn && (dn.StartsWith("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase) || dn.StartsWith("Microsoft .NET", StringComparison.OrdinalIgnoreCase))) return false;
        var exe = ExePath(un);
        if (exe == null || !OnFixedDrive(exe) || File.Exists(exe)) return false;
        if (e.GetValue("InstallLocation") is string loc && !string.IsNullOrWhiteSpace(loc))
        {
            loc = Environment.ExpandEnvironmentVariables(loc.Trim().Trim('"'));
            if (!OnFixedDrive(loc) || Directory.Exists(loc)) return false;
        }
        if (e.GetValue("DisplayIcon") is string icon && !string.IsNullOrWhiteSpace(icon))
        {
            var ip = ExePath(icon.Split(',')[0]);
            if (ip != null && (!OnFixedDrive(ip) || File.Exists(ip))) return false;   // сетевой или съёмный — запись не трогаем, по сети не ходим
        }
        return true;
    }

    /// <summary>Путь к exe из командной строки: «"C:\…\unins000.exe" /SILENT» или «C:\…\Uninstall.exe --x».</summary>
    static string ExePath(string cmd)
    {
        try
        {
            var s = Environment.ExpandEnvironmentVariables(cmd.Trim());
            string p;
            if (s.StartsWith('"')) { int q = s.IndexOf('"', 1); if (q < 0) return null; p = s[1..q]; }
            else { int x = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); if (x < 0) return null; p = s[..(x + 4)]; }
            return p.Length > 3 && p[1] == ':' && p[2] == '\\' ? p : null;
        }
        catch { return null; }
    }

    /// <summary>Путь на подключённом постоянном диске (съёмный или сетевой мог просто отключиться — запись не трогаем).</summary>
    static bool OnFixedDrive(string path)
    {
        try
        {
            if (path.Length < 3 || path[1] != ':') return false;
            var d = new DriveInfo(path[..1]);
            return d.IsReady && d.DriveType == DriveType.Fixed;
        }
        catch { return false; }
    }

    static long DirSize(string dir)
    {
        if (CleanupPaths.ApprovedRoot(dir, new[] { dir }) == null) return 0;
        long sum = 0;
        try
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in Directory.EnumerateFiles(dir, "*", opts)) { try { sum += new FileInfo(f).Length; } catch { } }
        }
        catch { }
        return sum;
    }

    /// <summary>Сколько займут папки удалённых программ; count — сколько всего найдено (записи + папки).</summary>
    public static long Measure(out int count)
    {
        var (entries, dirs) = Scan();
        count = entries.Count + dirs.Count;
        return dirs.Sum(DirSize);
    }

    public static void Clean(Action<string> progress)
    {
        var (entries, dirs) = Scan();
        if (entries.Count > 0)
        {
            progress?.Invoke("Убираю записи удалённых программ…");
            var names = entries.Select(e => e.Name).Distinct().ToList();
            using var backupSession = Backup.Session();
            Backup.Current.Title = "Убраны записи удалённых программ: " + string.Join(", ", names.Take(3)) + (names.Count > 3 ? " и др." : "");
            foreach (var e in entries)
            {
                try
                {
                    using var b = Reg.Root(e.Hive);
                    using var k = b.OpenSubKey(e.Path);
                    if (k == null) throw new IOException("Не удалось прочитать исходную запись программы.");
                    foreach (var v in k.GetValueNames()) Backup.Record(e.Hive, e.Path, v, k);
                }
                catch (Exception ex) { throw new IOException("Не удалось сохранить исходные записи программ; удаление отменено.", ex); }
            }
            try { Backup.Save(); }
            catch (Exception ex) { Log.Warn("Остатки программ: не удалось сохранить резервную копию, записи оставлены без изменений: " + ex.Message); Backup.End(); entries.Clear(); }
            Backup.End();
            foreach (var e in entries)
            {
                try
                {
                    using var b = Reg.Root(e.Hive);
                    b.DeleteSubKeyTree(e.Path, false);
                    Log.Ok($"  Убрана запись удалённой программы: {e.Name}");
                }
                catch (Exception ex) { Log.Warn($"  {e.Name}: {ex.Message}"); }
            }
        }
        foreach (var d in dirs)
        {
            progress?.Invoke($"Удаляю остатки: {Path.GetFileName(d)}…");
            // папку в ProgramData мог создать любой пользователь — удаляем через открытые файлы, не выходя за саму папку (SafeFs)
            string root = CleanupPaths.ApprovedRoot(d, new[] { d });
            if (root == null) { Log.Skip($"  {d}: пропущено (это ссылка на другую папку или нет доступа)"); continue; }
            SafeFs.DeleteTree(d, root, false);
            if (SafeFs.DeleteRoot(d, root)) Log.Ok($"  Удалена папка с остатками программы: {d}");
            else Log.Warn($"  {d}: удалено не всё, часть файлов занята другими программами");
        }
    }
}
