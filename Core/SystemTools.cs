// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Merno.Core;

/// <summary>
/// Программы Windows, которым по манифесту нужны права администратора (requireAdministrator / highestAvailable):
/// «Конфигурация системы», «Редактор реестра», «Учётные записи», «Восстановление системы» и т. п.
/// С обычными правами (Unelevated, токен Проводника) Windows их не запускает — ошибка 740, окна UAC при таком
/// запуске не бывает. Merno и так работает от администратора, поэтому такие программы он запускает сам, но только
/// из System32 или папки Windows по полному пути: подменить там файл обычный пользователь не может.
/// </summary>
internal static class SystemTools
{
    private static readonly ConcurrentDictionary<string, bool> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Файл лежит прямо в System32 или в папке Windows (не во вложенной папке).</summary>
    internal static bool InSystemFolder(string path, string system, string windows)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
            string dir = Path.GetDirectoryName(Path.GetFullPath(path))?.TrimEnd('\\');
            return dir != null && (dir.Equals(system?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                                   dir.Equals(windows?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>В тексте манифеста запрошены права администратора.</summary>
    internal static bool ManifestNeedsAdmin(string text) =>
        !string.IsNullOrEmpty(text) &&
        Regex.IsMatch(text, @"requestedExecutionLevel[^>]*level\s*=\s*[""'](requireAdministrator|highestAvailable)[""']", RegexOptions.IgnoreCase);

    /// <summary>Программа Windows из системной папки, которой нужны права администратора.</summary>
    public static bool NeedsAdmin(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (!InSystemFolder(exe, Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows))) return false;
        if (!File.Exists(exe)) return false;
        // манифест — в ресурсах exe; читаем файл как текст (системные exe — сотни КБ), результат запоминаем
        return Cache.GetOrAdd(exe, p =>
        {
            try { return ManifestNeedsAdmin(Encoding.ASCII.GetString(File.ReadAllBytes(p))); }
            catch { return false; }
        });
    }

    /// <summary>Запустить с правами Merno: полный путь, рабочая папка System32, очищенное окружение (как Sh.Run).</summary>
    public static bool StartElevated(string exe, string args)
    {
        if (!NeedsAdmin(exe)) return false;
        try
        {
            var psi = new ProcessStartInfo(exe, args ?? "") { UseShellExecute = false, WorkingDirectory = Environment.SystemDirectory };
            ProcessEnvironment.Configure(psi);
            using var p = Process.Start(psi);
            return p != null;
        }
        catch (Exception ex) { Log.Warn("Не удалось запустить " + Path.GetFileName(exe) + ": " + ex.Message); return false; }
    }
}
