// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Merno.Core;

/// <summary>
/// WebView2: перед установкой убираем «зависшую» запись в реестре.
/// Бывает, что в EdgeUpdate\Clients осталась версия (pv), а файлов этой версии на диске нет.
/// Тогда установщик WebView2 пишет «уже установлен» и ничего не ставит, а winget сообщает об ошибке.
/// Запись сначала сохраняем в .reg в защищённую папку данных и только после этого удаляем один этот ключ.
/// </summary>
public static class WebView2
{
    /// <summary>Код WebView2 Runtime в EdgeUpdate.</summary>
    const string ClientId = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    /// <summary>Только четыре числа через точку из цифр 0–9: без пробелов, «..», «\» и перевода строки в конце
    /// (в .NET «$» пропускает \n, а «\d» — цифры других алфавитов). Из pv строится путь к папке,
    /// поэтому всё, что не похоже на версию, считаем непонятным и не трогаем.</summary>
    static readonly Regex VersionShape = new(@"^[0-9]+(\.[0-9]+){3}\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// Запись «зависла»: pv — правильная версия (не 0.0.0.0), а msedgewebview2.exe этой версии нет.
    /// exeExists получает только уже проверенную версию. Всё непонятное — «не зависла», ничего не делаем.
    /// </summary>
    internal static bool IsStale(string pv, Func<string, bool> exeExists)
    {
        if (pv == null || exeExists == null || !VersionShape.IsMatch(pv)) return false;
        if (!Version.TryParse(pv, out var v)) return false;   // числа слишком большие
        if (v.Major == 0 && v.Minor == 0 && v.Build == 0 && v.Revision == 0) return false;
        return !exeExists(pv);
    }

    internal static string ExePath(string appDir, string pv) => Path.Combine(appDir, pv, "msedgewebview2.exe");

    /// <summary>Где искать запись и файлы: для всех пользователей и для текущего.</summary>
    sealed class Scope
    {
        public string Tag;          // machine / user — в имени файла копии
        public RegistryHive Hive;
        public RegistryView View;
        public string Clients;      // путь к Clients внутри куста
        public string RegPath;      // тот же ключ для reg.exe
        public string[] AppDirs;    // папки, где лежат версии WebView2
        public RegistryKey Open() => RegistryKey.OpenBaseKey(Hive, View);
        public bool ExeExists(string pv) => AppDirs.Any(d => File.Exists(ExePath(d, pv)));
    }

    static IEnumerable<Scope> Scopes()
    {
        // 32-битный вид реестра — это WOW6432Node. reg.exe получает явный путь, чтобы копия .reg возвращалась туда же.
        yield return new Scope
        {
            Tag = "machine",
            Hive = RegistryHive.LocalMachine,
            View = RegistryView.Registry32,
            Clients = @"SOFTWARE\Microsoft\EdgeUpdate\Clients",
            RegPath = (Environment.Is64BitOperatingSystem ? @"HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\"
                                                          : @"HKLM\SOFTWARE\Microsoft\EdgeUpdate\Clients\") + ClientId,
            AppDirs = AppDirs(Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles)
        };
        yield return new Scope
        {
            Tag = "user",
            Hive = RegistryHive.CurrentUser,
            View = RegistryView.Default,
            Clients = @"Software\Microsoft\EdgeUpdate\Clients",
            RegPath = @"HKCU\Software\Microsoft\EdgeUpdate\Clients\" + ClientId,
            AppDirs = AppDirs(Environment.SpecialFolder.LocalApplicationData)
        };
    }

    static string[] AppDirs(params Environment.SpecialFolder[] roots) => roots
        .Select(Environment.GetFolderPath)
        .Where(r => !string.IsNullOrEmpty(r) && Path.IsPathFullyQualified(r))
        .Select(r => Path.Combine(r, "Microsoft", "EdgeWebView", "Application"))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    static string ReadPv(Scope s)
    {
        using var root = s.Open();
        using var key = root.OpenSubKey(s.Clients + "\\" + ClientId);
        return key?.GetValue("pv", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    /// <summary>Перед установкой WebView2: найти зависшие записи и убрать их, сохранив копию. Исключений не бросает.</summary>
    public static void RepairStaleRegistration()
    {
        try
        {
            foreach (var s in Scopes())
            {
                try { Repair(s); }
                catch { Log.Warn("WebView2: проверить запись в реестре не получилось."); }
            }
        }
        catch { Log.Warn("WebView2: проверить запись в реестре не получилось."); }
    }

    static void Repair(Scope s)
    {
        if (s.AppDirs.Length == 0) return;   // не знаем, где искать файлы, — значит, и судить не можем
        string pv = ReadPv(s);
        if (!IsStale(pv, s.ExeExists)) return;

        string backup = BackupPath(s.Tag);
        var (code, _) = Sh.Run(Sh.Sys32("reg.exe"), $"export \"{s.RegPath}\" \"{backup}\" /y", 60_000);
        if (code != 0 || !File.Exists(backup) || new FileInfo(backup).Length == 0)
        {
            Log.Warn($"WebView2: в реестре осталась запись о версии {pv}, а файлов нет. Сохранить копию записи не получилось, поэтому её не трогаю.");
            return;
        }

        // пока сохраняли копию, EdgeUpdate мог сам всё исправить
        string again = ReadPv(s);
        if (again != pv || !IsStale(again, s.ExeExists))
        {
            Log.Skip("WebView2: запись в реестре изменилась сама, не трогаю её.");
            return;
        }

        string name = Path.GetFileName(backup);
        try
        {
            using var root = s.Open();
            using var clients = root.OpenSubKey(s.Clients, writable: true);
            if (clients == null) { Log.Skip("WebView2: запись в реестре уже пропала, ничего не убираю."); return; }
            clients.DeleteSubKeyTree(ClientId, throwOnMissingSubKey: false);
        }
        catch
        {
            Log.Warn($"WebView2: убрать старую запись о версии {pv} не получилось. Копия записи сохранена ({name}).");
            return;
        }
        Log.Ok($"WebView2: в реестре осталась запись о версии {pv}, а файлов нет. Копия записи сохранена ({name}), сама запись убрана. Ставлю заново.");
    }

    /// <summary>Копия — в защищённой папке данных Merno, имя не повторяется.</summary>
    static string BackupPath(string tag)
    {
        string stem = $"webview2-registry-{DateTime.Now:yyyyMMdd-HHmmss}-{tag}";
        string path = Path.Combine(Log.DataDir, stem + ".reg");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(Log.DataDir, $"{stem}-{i}.reg");
        return path;
    }
}
