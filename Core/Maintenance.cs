// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Merno.Core;

// ===================================================== ОЧИСТКА
public class CleanTarget
{
    public string Id, Name, Hint;
    public string[] Paths = Array.Empty<string>();
    public string Filter = "*";
    public bool Default, Recycle, Dism, StopWU, Leftovers;
    public long Size = -2; // -2 — не считали
    /// <summary>Временные папки: не трогаем свежие (моложе 2 часов) и занятые файлы — ими пользуются запущенные программы.
    /// Иначе Merno показывал бы «можно освободить 2 ГБ», а удалить эти файлы было бы нельзя (запись экрана, установщик…).</summary>
    public bool SkipBusy;
    public int MinAgeHours;
    /// <summary>Сколько найдено штук (для остатков программ: записи + папки). Нужен, когда места они почти не занимают.</summary>
    public int Count;
}

public static class Cleaner
{
    public static List<CleanTarget> Targets() => Targets(CleanupPaths.Profile,
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

    internal static List<CleanTarget> Targets(string profile, string windows, string programData)
    {
        string E(string template) => CleanupPaths.Expand(template, profile, windows, programData);
        return new()
        {
            new() { Id="tmp_user", Name="Временные файлы программ", Default=true, SkipBusy=true, Paths=new[]{ E(@"%TEMP%") },
                    Hint="Мусор, который программы забыли за собой убрать. Открытые программы закрывать не нужно." },
            new() { Id="tmp_win", Name="Временные файлы Windows", Default=true, SkipBusy=true, Paths=new[]{ E(@"%SystemRoot%\Temp") },
                    Hint="Открытые программы закрывать не нужно." },
            new() { Id="wu", Name="Скачанные обновления Windows", Default=true, StopWU=true, Paths=new[]{ E(@"%SystemRoot%\SoftwareDistribution\Download") },
                    Hint="Файлы обновлений, которые Windows уже скачала. Если понадобятся, она скачает их снова." },
            new() { Id="do", Name="Запасные файлы обновлений", Default=true,
                    Hint="Копии обновлений, которыми Windows делится с другими компьютерами. Удалять их безопасно.",
                    Paths=new[]{ E(@"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"), E(@"%SystemRoot%\SoftwareDistribution\DeliveryOptimization") } },
            new() { Id="dumps", Name="Отчёты о сбоях программ", Default=false, MinAgeHours=168,
                    Hint="Только старше 7 дней. Могут понадобиться для поиска причин сбоев.",
                    Paths=new[]{ E(@"%LOCALAPPDATA%\CrashDumps"), E(@"%ProgramData%\Microsoft\Windows\WER\ReportArchive"), E(@"%ProgramData%\Microsoft\Windows\WER\ReportQueue"), E(@"%SystemRoot%\Minidump"), E(@"%SystemRoot%\LiveKernelReports") } },
            new() { Id="thumbs", Name="Миниатюры картинок (создадутся заново)", Default=true, Paths=new[]{ E(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer") }, Filter="thumbcache_*.db" },
            new() { Id="leftovers", Name="Остатки удалённых программ", Default=false, Leftovers=true,
                    Hint="Могут содержать настройки и сохранения. Удалённые папки нельзя вернуть через откат." },
            new() { Id="logs", Name="Старые журналы Windows", Default=false, MinAgeHours=168,
                    Hint="Только старше 7 дней. Могут понадобиться для диагностики Windows.", Paths=new[]{ E(@"%SystemRoot%\Logs\CBS"), E(@"%SystemRoot%\Panther") }, Filter="*.log" },
            new() { Id="shader", Name="Временные файлы видеокарты", Default=false,
                    Paths=new[]{ E(@"%LOCALAPPDATA%\D3DSCache"), E(@"%LOCALAPPDATA%\NVIDIA\DXCache"), E(@"%LOCALAPPDATA%\NVIDIA\GLCache"), E(@"%LOCALAPPDATA%\AMD\DxCache"), E(@"%LOCALAPPDATA%\AMD\DxcCache"), E(@"%LOCALAPPDATA%\AMD\VkCache") },
                    Hint="Помогает, если игры начали дёргаться после обновления драйвера. Первые минуты игры могут идти рывками." },
            new() { Id="recycle", Name="Корзина (удаляется навсегда)", Default=false, Recycle=true },
            new() { Id="winsxs", Name="Остатки старых обновлений Windows", Default=true, Dism=true,
                    Hint="Windows хранит старые копии своих файлов после обновлений. Их можно убрать." },
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

    internal static bool Safe(string path, IEnumerable<string> allowedRoots) =>
        CleanupPaths.ApprovedRoot(path, allowedRoots) != null;

    // По ID заново берём неизменённую политику. Путь/маска/флаги из строки интерфейса не дают расширить очистку.
    internal static CleanTarget CanonicalTarget(CleanTarget requested, IEnumerable<CleanTarget> catalog) =>
        requested == null ? null : catalog.FirstOrDefault(t => t.Id == requested.Id);

    // ---- состояние служб (номер состояния в выводе sc не зависит от языка: 4 — RUNNING) ----
    static bool ServiceRunning(string svc)
    {
        var (code, o) = Sh.Run(Sh.Sys32("sc.exe"), $"query {svc}", 15_000);
        if (code != 0) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(o ?? "", @":\s*4\s+\w");
    }

    /// <summary>Merno сам ставит обновления Windows или драйверы в фоне — кэш обновлений трогать нельзя
    /// (TrustedInstaller на этапе скачивания ещё не запущен, поэтому одной проверки службы мало).</summary>
    public static volatile bool UpdatesInstalling;

    /// <summary>Почему сейчас нельзя чистить кэш обновлений (null — можно). Текст продолжает «пропущено: …».</summary>
    static string WuBusyReason()
    {
        if (UpdatesInstalling) return "Merno сейчас устанавливает обновления, повторите очистку позже";
        if (Reg.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
            return "сначала перезагрузите компьютер";
        if (ServiceRunning("TrustedInstaller"))
            return "Windows сейчас ставит обновления, повторите позже";
        return null;
    }

    /// <summary>Файл занят или только что менялся — им пользуется запущенная программа, удалять нельзя.</summary>
    internal static bool Busy(FileInfo f, CleanTarget target, DateTime utcNow)
    {
        try
        {
            if (f.LastWriteTimeUtc > utcNow.AddHours(-Math.Max(target.MinAgeHours, target.SkipBusy ? 2 : 0))) return true;
            if (!target.SkipBusy) return false;
            if (f.Length < 1L << 20) return false;   // маленькие не проверяем — быстрее
            using var s = f.Open(FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch { return true; }
    }

    internal static IEnumerable<FileInfo> Files(string dir, CleanTarget target, IEnumerable<string> allowedRoots)
    {
        if (!Safe(dir, allowedRoots)) yield break;
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, MatchType = MatchType.Simple };
        IEnumerator<string> it;
        try { it = Directory.EnumerateFiles(dir, target.Filter, opts).GetEnumerator(); } catch { yield break; }
        using (it)
        {
            while (true)
            {
                // перебор может упасть на середине (папку удалили, диск занят) — тогда просто заканчиваем, а не роняем очистку
                string f;
                try { if (!it.MoveNext()) break; f = it.Current; } catch { break; }
                FileInfo fi = null;
                try { fi = new FileInfo(f); } catch { }
                if (fi != null && !Busy(fi, target, DateTime.UtcNow)) yield return fi;
            }
        }
    }

    public static long Measure(CleanTarget t)
    {
        var requested = t;
        var catalog = Targets();
        t = CanonicalTarget(t, catalog);
        if (t == null) return 0;
        if (t.Dism) return -1;
        if (t.Leftovers) { long s = Leftovers.Measure(out var n); requested.Count = n; return s; }
        if (t.Recycle)
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
        }
        long sum = 0;
        foreach (var p in t.Paths) foreach (var f in Files(p, t, catalog.SelectMany(c => c.Paths))) { try { sum += f.Length; } catch { } }
        return sum;
    }

    internal static void CleanFiles(CleanTarget target, IReadOnlyCollection<string> allowedRoots)
    {
        foreach (var path in target.Paths)
        {
            string root = CleanupPaths.ApprovedRoot(path, allowedRoots);
            if (root == null) continue;
            foreach (var file in Files(path, target, allowedRoots))
            {
                file.Refresh();
                if (!Busy(file, target, DateTime.UtcNow)) SafeFs.DeleteUnder(file.FullName, root);
            }
            // Не повторяем удаление всего дерева: там могли остаться свежие/занятые или новые файлы.
            if (target.Filter == "*") SafeFs.PruneEmpty(path, root);
        }
    }

    static long FreeSpace() => new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)).AvailableFreeSpace;

    /// <summary>Название пункта для строки состояния: «временные файлы Windows» (без пояснения в скобках).</summary>
    static string StepName(CleanTarget t)
    {
        var n = t.Name ?? "";
        int b = n.IndexOf(" (", StringComparison.Ordinal);
        if (b > 0) n = n[..b];
        return n.Length > 1 && !(char.IsUpper(n[0]) && char.IsUpper(n[1])) ? char.ToLower(n[0]) + n[1..] : n;
    }

    /// <summary>Очищает пункт. progress получает текущий шаг для строки состояния (вызывается из фонового потока).</summary>
    public static long Clean(CleanTarget t, Action<string> progress = null)
    {
        var catalog = Targets();
        t = CanonicalTarget(t, catalog);
        if (t == null) return 0;
        // С установкой обновлений Windows и драйверов конфликтуют только файлы обновлений и компоненты Windows.
        // Временные файлы, эскизы и т.п. чистятся и во время установки. Занято: пункт пропускается, без ошибки.
        using var operation = t.StopWU || t.Dism ? SystemOperation.TryEnter() : null;
        if ((t.StopWU || t.Dism) && operation == null)
        {
            Log.Skip($"{t.Name}: пропущено: идёт установка обновлений или драйверов. Повторите после неё.");
            return 0;
        }
        void P(string s) { try { progress?.Invoke(s); } catch { } }
        bool wuWasRunning = false, bitsWasRunning = false;
        if (t.StopWU)
        {
            var why = WuBusyReason();
            if (why != null) { Log.Skip($"{t.Name}: пропущено: {why}"); return 0; }
        }
        long before = FreeSpace();
        if (t.StopWU)
        {
            wuWasRunning = ServiceRunning("wuauserv");
            bitsWasRunning = ServiceRunning("bits");
            if (wuWasRunning || bitsWasRunning) P("Чищу файлы обновлений…");
            if (wuWasRunning) Sh.Run(Sh.Sys32("net.exe"), "stop wuauserv /y", 60_000);
            if (bitsWasRunning) Sh.Run(Sh.Sys32("net.exe"), "stop bits /y", 60_000);
        }
        try
        {
        if (t.Dism)
        {
            P("Чищу остатки старых обновлений. Это может занять до получаса…");
            Log.Skip("  Чищу остатки старых обновлений…");
            Sh.Run(Sh.Sys32("dism.exe"), "/Online /Cleanup-Image /StartComponentCleanup /Quiet", 1_800_000);
        }
        else if (t.Recycle) { P("Чищу корзину…"); SHEmptyRecycleBin(IntPtr.Zero, null, 0x7); }
        else if (t.Leftovers) { P("Ищу остатки удалённых программ…"); Leftovers.Clean(P); }
        else
        {
            P(t.StopWU ? "Чищу файлы обновлений…" : $"Чищу: {StepName(t)}…");
            CleanFiles(t, catalog.SelectMany(c => c.Paths).ToArray());
        }
        }
        finally
        {
            // службы обновлений запускаем обратно в любом случае: если очистка упала на середине,
            // без этого Windows перестанет обновляться до перезагрузки
            if (t.StopWU)
            {   // запускаем обратно только то, что работало до очистки
                if (wuWasRunning || bitsWasRunning) P("Заканчиваю очистку…");
                if (bitsWasRunning) Sh.Run(Sh.Sys32("net.exe"), "start bits", 60_000);
                if (wuWasRunning) Sh.Run(Sh.Sys32("net.exe"), "start wuauserv", 60_000);
            }
        }
        long freed = Math.Max(0, FreeSpace() - before);
        Log.Ok($"{t.Name}: освобождено {Sh.Size(freed)}");
        return freed;
    }
}

// ===================================================== АВТОЗАГРУЗКА
public enum StartKind { Registry, Folder, Task, Store }

public class StartupItem
{
    public string Name, Command, Scope, Hive, ApprovedPath;
    /// <summary>Понятное имя: русское для известных записей, название приложения Store или описание из exe. null — показываем Name.</summary>
    public string Title;
    public StartKind Kind;
    public string TaskPath;
    public string RunKey;     // для записей реестра — путь ключа Run   // для задач планировщика — полный путь задачи; для приложений Store — путь ключа
    public bool Enabled, Protected;
    /// <summary>Можно убрать из автозагрузки совсем (свои записи пользователя).</summary>
    public bool Removable;
    /// <summary>Настоящий файл программы (exe, lnk, dll) — для значка и «Показать в Проводнике». null — файла нет.</summary>
    public string FilePath;
    /// <summary>Откуда брать значок: файл (exe, ico, png) или «путь,номер». null — рисуем букву.</summary>
    public string IconPath;
    /// <summary>Кто сделал программу (из свойств exe). null — неизвестно.</summary>
    public string Company;
}

public static class Startup
{
    static readonly string Protect = @"SecurityHealth|RtkAud|Realtek|igfx|\bIntel(?!li)|NvBackend|\bAMD|Radeon|Synaptics|\bETD|\bElan(?!ce)|Waves|Dolby|Nahimic|MSI_|\bASUS|Armoury|Lenovo|Vanguard|vgtray|FACEIT|BattlEye|EasyAntiCheat|Defender";   // короткие слова — целиком: «AMD» не должно совпадать с «ProgramData»
    // OneDriveSetup в Protect не нужен: это установщик OneDrive при первом входе, а не драйвер и не защита
    const string RunCU = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppData = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";
    const string Packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    /// <summary>Русские названия известных программ: в exe у них английское описание (или файла уже нет). Ключ: имя файла.</summary>
    static readonly Dictionary<string, string> KnownFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SecurityHealthSystray.exe"] = "Значок «Безопасность Windows»",
        ["OneDriveSetup.exe"] = "Установка OneDrive",
        ["MicrosoftEdgeUpdate.exe"] = "Обновление Microsoft Edge",
    };
    /// <summary>Названия приложений Store, если в пакете неудачное или его не прочитать. Ключ: семейство пакета.</summary>
    static readonly Dictionary<string, string> KnownStore = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.GamingApp_8wekyb3d8bbwe"] = "Xbox",                                  // в пакете написано «XBOX»
        ["9426MICRO-STARINTERNATION.MSICenter_kzh8wxbdkxb8p"] = "MSI Center",
    };

    /// <summary>Известное русское название записи. Файлу оно даётся, только если тот лежит в папке Windows или Program Files:
    /// строку в HKCU Run может записать любая программа, и чужой exe с тем же именем не должен выглядеть системным.</summary>
    internal static string KnownTitle(StartupItem it)
    {
        try
        {
            if (it.Kind == StartKind.Store) return it.Command != null && KnownStore.TryGetValue(it.Command, out var s) ? s : null;
            var file = it.FilePath ?? ExeOf(it.Command);   // у OneDriveSetup файла часто уже нет, имя берём из команды
            if (string.IsNullOrWhiteSpace(file) || !Path.IsPathRooted(file) || !KnownFiles.TryGetValue(Path.GetFileName(file), out var t)) return null;
            var full = Path.GetFullPath(file);
            foreach (var f in new[] { Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                var root = Environment.GetFolderPath(f);
                if (root.Length > 0 && full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return t;
            }
        }
        catch { }
        return null;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int SHLoadIndirectString(string src, StringBuilder buf, int size, IntPtr reserved);

    /// <summary>Название приложения Microsoft Store, как в меню «Пуск» и на языке Windows («MSI Center», «Терминал Windows»).
    /// null — не нашли, тогда остаётся имя из пакета.</summary>
    static string StoreTitle(string family)
    {
        try
        {
            int us = family.LastIndexOf('_');
            if (us <= 0) return null;
            string name = family[..us], pub = family[(us + 1)..];
            using var repo = Registry.CurrentUser.OpenSubKey(Packages);
            if (repo == null) return null;
            foreach (var full in repo.GetSubKeyNames())
            {
                if (!full.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) || !full.EndsWith("_" + pub, StringComparison.OrdinalIgnoreCase)) continue;
                using var k = repo.OpenSubKey(full);
                if (k?.GetValue("PackageRootFolder") is not string dir || !SafeFs.IsLocal(dir) || !Directory.Exists(dir)) continue;   // старая версия пакета, её уже нет
                if (!InProtectedPackageRoot(dir, full, family)) continue;   // HKCU пишет любая программа: ресурсы из папки пользователя не разбираем
                var t = PackageText(full, name, k.GetValue("DisplayName") as string);
                if (t != null) return t;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Папка пакета — ровно та, что создаёт система: Program Files\WindowsApps\&lt;полное имя&gt; или
    /// Windows\SystemApps\&lt;семейство&gt; (там создают папки только система и TrustedInstaller). Иначе не читаем ресурсы
    /// от администратора: ветку Repository в HKCU может подделать обычная программа и подсунуть свой файл ресурсов,
    /// а так подделка может указать только на настоящую защищённую папку этого же пакета.
    /// Остаточный риск: пакет, зарегистрированный из папки пользователя в режиме разработчика, сюда не проходит,
    /// но откуда именно SHLoadIndirectString берёт папку пакета, не проверено.</summary>
    internal static bool InProtectedPackageRoot(string dir, string full, string family)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(full) || string.IsNullOrWhiteSpace(family) ||
                full.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || family.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                full.Contains("..") || family.Contains("..") || !Path.IsPathFullyQualified(dir) || dir.StartsWith(@"\\")) return false;
            var path = Path.GetFullPath(dir).TrimEnd('\\');
            var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps");
            return (apps.Length > 12 && string.Equals(path, Path.Combine(apps, full), StringComparison.OrdinalIgnoreCase)) ||
                   (sys.Length > 11 && string.Equals(path, Path.Combine(sys, family), StringComparison.OrdinalIgnoreCase));
        }
        catch { }
        return false;
    }

    /// <summary>DisplayName пакета: готовый текст или ссылка на строку в его ресурсах («ms-resource:…», «@{пакет?ms-resource:…}»).
    /// Открываем ресурсы только этого же пакета: значение лежит в HKCU, и чужую ссылку (например, «@файл.dll,-1») не читаем.</summary>
    internal static string PackageText(string full, string name, string s)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        const string res = "ms-resource:";
        if (s.StartsWith(res, StringComparison.OrdinalIgnoreCase))
        {   // короткая ссылка «ms-resource:AppName» → «@{полное имя?ms-resource://Имя/Resources/AppName}»
            var key = s[res.Length..];
            if (!key.StartsWith("//"))
            {
                if (!key.StartsWith("/")) key = "/" + key;
                if (!key.Contains("resources", StringComparison.OrdinalIgnoreCase)) key = "/Resources" + key;
                key = "//" + name + key;
            }
            s = "@{" + full + "?" + res + key + "}";
        }
        if (s.StartsWith("@"))
        {
            if (!s.StartsWith("@{" + full + "?" + res, StringComparison.OrdinalIgnoreCase) || !s.EndsWith("}")) return null;
            var sb = new StringBuilder(512);
            if (SHLoadIndirectString(s, sb, sb.Capacity, IntPtr.Zero) != 0) return null;
            s = sb.ToString().Trim();
        }
        return s.Length >= 2 && !s.StartsWith(res, StringComparison.OrdinalIgnoreCase) ? s : null;
    }

    static bool IsEnabled(string hive, string approvedPath, string name)
    {
        var v = Reg.Get(hive, approvedPath, name) as byte[];
        return v == null || v.Length == 0 || v[0] % 2 == 0;
    }
    static bool IsProtected(string s) => System.Text.RegularExpressions.Regex.IsMatch(s ?? "", Protect, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Путь к exe из строки запуска: "C:\..\a.exe" --min → C:\..\a.exe</summary>
    public static string ExeOf(string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return null;
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.StartsWith("\"")) { int e = cmd.IndexOf('"', 1); return e > 1 ? cmd[1..e] : null; }
        int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? cmd[..(i + 4)] : cmd.Split(' ')[0];
    }
    static string Describe(string cmd)
    {
        try
        {
            var exe = ExeOf(cmd);
            if (exe == null || !SafeFs.IsLocal(exe) || !File.Exists(exe)) return null;
            var d = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
            var t = (d.FileDescription ?? "").Trim();
            if (t.Length < 3) t = (d.ProductName ?? "").Trim();
            return t.Length >= 3 ? t : null;
        }
        catch { return null; }
    }

    public static List<StartupItem> List()
    {
        var res = new List<StartupItem>();
        // 1. Реестр: Run для пользователя и для всех
        var keys = new (string Hive, string Run, string Approved, string Scope)[]
        {
            ("HKCU", RunCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "текущий пользователь"),
            ("HKLM", RunCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "все пользователи"),
            ("HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", "все пользователи"),
        };
        foreach (var k in keys)
        {
            try
            {
                using var root = Reg.Root(k.Hive);
                using var key = root.OpenSubKey(k.Run);
                if (key == null) continue;
                foreach (var n in key.GetValueNames().Where(x => x.Length > 0))
                {
                    var cmd = key.GetValue(n)?.ToString() ?? "";
                    res.Add(new StartupItem
                    {
                        Name = n, Command = cmd, Scope = k.Scope, Hive = k.Hive, ApprovedPath = k.Approved, Kind = StartKind.Registry,
                        Enabled = IsEnabled(k.Hive, k.Approved, n), Protected = IsProtected(n + " " + cmd), Title = Describe(cmd),
                        RunKey = k.Run
                    });
                }
            }
            catch { }
        }
        // 2. Папки автозагрузки
        var folders = new (string Dir, string Hive, string Scope)[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.Startup), "HKCU", "папка автозагрузки"),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "HKLM", "папка автозагрузки (все)"),
        };
        foreach (var f in folders)
        {
            if (!Directory.Exists(f.Dir)) continue;
            string[] files;
            try { files = Directory.GetFiles(f.Dir); } catch { continue; }
            foreach (var file in files.Where(x => !x.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                string n = Path.GetFileName(file);
                const string ap = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
                res.Add(new StartupItem { Name = n, Title = Path.GetFileNameWithoutExtension(n), Command = file, Scope = f.Scope, Hive = f.Hive, ApprovedPath = ap,
                                          Kind = StartKind.Folder, Enabled = IsEnabled(f.Hive, ap, n) });
            }
        }
        // 3. Приложения из Microsoft Store (Discord, Spotify, Teams…) — их не видно в реестре Run
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(AppData);
            if (root != null)
                foreach (var pkg in root.GetSubKeyNames())
                {
                    using var pk = root.OpenSubKey(pkg);
                    if (pk == null) continue;
                    foreach (var task in pk.GetSubKeyNames())
                    {
                        using var tk = pk.OpenSubKey(task);
                        if (tk?.GetValue("State") is not int state) continue;
                        // название из пакета; не нашли — из имени пакета: SpotifyAB.SpotifyMusic_zpdnekdrzrea0 → SpotifyMusic
                        var fam = pkg.Split('_')[0]; var nice = fam.Contains('.') ? fam[(fam.LastIndexOf('.') + 1)..] : fam;
                        res.Add(new StartupItem { Name = task, Title = StoreTitle(pkg) ?? nice, Command = pkg, Scope = "приложение Microsoft Store", Kind = StartKind.Store,
                                                  TaskPath = AppData + "\\" + pkg + "\\" + task, Enabled = state == 2 || state == 4 });
                    }
                }
        }
        catch { }
        // 4. Задачи планировщика, которые запускаются при входе в Windows (их часто ставят лаунчеры и обновлялки)
        try
        {
            var (_, o) = Sh.PS(@"Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\Microsoft\*' -and ($_.Triggers | Where-Object { $_.CimClass.CimClassName -in 'MSFT_TaskLogonTrigger','MSFT_TaskBootTrigger' }) } |
  ForEach-Object { [pscustomobject]@{ N = $_.TaskName; P = $_.TaskPath; S = [string]$_.State; C = ($_.Actions | Select-Object -First 1).Execute } } | ConvertTo-Json -Compress", 60_000);
            o = o.Trim();
            if (o.StartsWith("[") || o.StartsWith("{"))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(o);
                var arr = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new() { doc.RootElement };
                foreach (var e in arr)
                {
                    string g(string p) => e.TryGetProperty(p, out var v) && v.ValueKind != System.Text.Json.JsonValueKind.Null ? v.ToString() : "";
                    var cmd = g("C");
                    res.Add(new StartupItem { Name = g("N"), Title = Describe(cmd), Command = cmd, Scope = "задача планировщика", Kind = StartKind.Task,
                                              TaskPath = g("P") + g("N"), Enabled = g("S") != "Disabled", Protected = IsProtected(g("N") + " " + cmd) });
                }
            }
        }
        catch { }
        // убрать совсем можно всё, кроме системного и приложений Store (их можно только выключить)
        foreach (var x in res) x.Removable = !x.Protected && x.Kind != StartKind.Store;
        ResolveFiles(res);
        // известные записи — по-русски; после ResolveFiles, он сам дописывает Title из exe
        foreach (var x in res) { var t = KnownTitle(x); if (t != null) x.Title = t; }
        return res.OrderBy(x => x.Protected).ThenBy(x => x.Title ?? x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ---------- файл и значок для каждой записи ----------
    // Одного первого слова команды мало: без значка остались бы приложения Microsoft Store, команды без полного
    // пути («ctfmon»), запуск через rundll32/cmd/powershell и программы, у которых в exe нет своей картинки.

    /// <summary>Программы-«обёртки»: их значок ничего не говорит, настоящий файл — в аргументах.</summary>
    static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    { "rundll32.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "conhost.exe", "mshta.exe", "regsvr32.exe", "explorer.exe" };

    static readonly System.Text.RegularExpressions.Regex PathInArgs = new(
        @"([A-Za-z]:\\[^""<>|?*\r\n]+?\.(?:exe|dll|lnk|ico|bat|cmd|ps1|vbs|js))(?=[""\s,]|$)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Полный путь к файлу по имени без папки: «App Paths», System32, Windows, PATH.</summary>
    static string Locate(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return null;
        exe = exe.Trim().Trim('"');
        try
        {
            if (Path.IsPathRooted(exe))
            {
                if (!SafeFs.IsLocal(exe)) return null;   // строка из реестра: по сети не ходим
                if (File.Exists(exe)) return exe;
                return !Path.HasExtension(exe) && File.Exists(exe + ".exe") ? exe + ".exe" : null;
            }
            var name = Path.HasExtension(exe) ? exe : exe + ".exe";
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + name);
                if (k?.GetValue(null) is string ap)
                {
                    ap = Environment.ExpandEnvironmentVariables(ap.Trim().Trim('"'));
                    if (SafeFs.IsLocal(ap) && File.Exists(ap)) return ap;
                }
            }
            var dirs = new List<string> { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            foreach (var d in dirs)
            {
                try { var f = Path.Combine(Environment.ExpandEnvironmentVariables(d.Trim('"')), name); if (SafeFs.IsLocal(f) && File.Exists(f)) return f; } catch { }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Настоящий файл из строки запуска. Для rundll32/cmd/powershell — файл из аргументов.</summary>
    public static string FileOf(string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return null;
        var x = Environment.ExpandEnvironmentVariables(cmd.Trim());
        var first = Locate(ExeOf(x));
        if (first != null && !Hosts.Contains(Path.GetFileName(first))) return first;
        foreach (System.Text.RegularExpressions.Match m in PathInArgs.Matches(x))
        {
            var f = m.Groups[1].Value;
            if (first != null && f.Equals(first, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Hosts.Contains(Path.GetFileName(f)) && SafeFs.IsLocal(f) && File.Exists(f)) return f;
        }
        return first;
    }

    /// <summary>Записи «Программ и компонентов»: папка установки и значок.</summary>
    static List<(string Dir, string Icon)> UninstallIcons()
    {
        var res = new List<(string, string)>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var un = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (un == null) continue;
                foreach (var sub in un.GetSubKeyNames())
                {
                    try
                    {
                        using var k = un.OpenSubKey(sub);
                        if (k?.GetValue("DisplayIcon") is not string icon || string.IsNullOrWhiteSpace(icon)) continue;
                        icon = Environment.ExpandEnvironmentVariables(icon.Trim());
                        var iconFile = icon.Trim('"'); int c = iconFile.LastIndexOf(','); if (c > 2) iconFile = iconFile[..c].Trim().Trim('"');
                        if (!SafeFs.IsLocal(iconFile) || !File.Exists(iconFile)) continue;
                        var dir = (k.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                        if (string.IsNullOrEmpty(dir)) dir = Path.GetDirectoryName(iconFile);
                        if (!string.IsNullOrEmpty(dir)) res.Add((dir.TrimEnd('\\') + "\\", icon));
                    }
                    catch { }
                }
            }
            catch { }
        }
        return res;
    }

    /// <summary>Логотип приложения Microsoft Store по имени семейства пакета (Name_PublisherId).</summary>
    static string StoreLogo(string family)
    {
        try
        {
            int us = family.LastIndexOf('_');
            if (us <= 0) return null;
            string name = family[..us], pub = family[(us + 1)..];
            using var repo = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (repo == null) return null;
            foreach (var full in repo.GetSubKeyNames())
            {
                if (!full.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) || !full.EndsWith("_" + pub, StringComparison.OrdinalIgnoreCase)) continue;
                using var k = repo.OpenSubKey(full);
                var dir = k?.GetValue("PackageRootFolder") as string;
                var manifest = dir == null ? null : Path.Combine(dir, "AppxManifest.xml");
                if (manifest == null || !SafeFs.IsLocal(manifest) || !File.Exists(manifest)) continue;
                var xml = File.ReadAllText(manifest);
                // значок из списка приложений (квадратный) лучше, чем логотип магазина
                foreach (var pat in new[] { @"Square44x44Logo=""([^""]+)""", @"<Logo>([^<]+)</Logo>" })
                {
                    var m = System.Text.RegularExpressions.Regex.Match(xml, pat);
                    if (!m.Success) continue;
                    var logo = FileIcons.AppxLogo(dir, m.Groups[1].Value.Trim());
                    if (logo != null) return logo;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Находит для каждой записи настоящий файл и значок.</summary>
    static void ResolveFiles(List<StartupItem> items)
    {
        List<(string Dir, string Icon)> uninstall = null;
        foreach (var it in items)
        {
            try
            {
                if (it.Kind == StartKind.Store) { it.IconPath = StoreLogo(it.Command); continue; }
                it.FilePath = it.Kind == StartKind.Folder ? (File.Exists(it.Command) ? it.Command : null) : FileOf(it.Command);
                if (it.FilePath == null) continue;
                if (it.Title == null && it.FilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) it.Title = Describe("\"" + it.FilePath + "\"");
                try { var c = (System.Diagnostics.FileVersionInfo.GetVersionInfo(it.FilePath).CompanyName ?? "").Trim(); if (c.Length >= 2) it.Company = c; } catch { }
                it.IconPath = it.FilePath;
                // у exe/dll нет своей картинки (служебные помощники, обновлялки) — берём значок программы из «Программ и компонентов»
                var ext = Path.GetExtension(it.FilePath);
                if ((ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)) && !FileIcons.HasOwnIcon(it.FilePath))
                {
                    uninstall ??= UninstallIcons();
                    var best = uninstall.Where(u => it.FilePath.StartsWith(u.Dir, StringComparison.OrdinalIgnoreCase))
                                        .OrderByDescending(u => u.Dir.Length).FirstOrDefault();
                    if (best.Icon != null) it.IconPath = best.Icon;
                }
            }
            catch { }
        }
    }

    /// <summary>Включение/отключение как в Диспетчере задач — полностью обратимо.</summary>
    public static void SetEnabled(StartupItem it, bool enable)
    {
        switch (it.Kind)
        {
            case StartKind.Task:
                Sh.Run(Sh.Sys32("schtasks.exe"), $"/Change /TN \"{it.TaskPath}\" {(enable ? "/ENABLE" : "/DISABLE")}", 30_000);
                break;
            case StartKind.Store:
                using (var k = Registry.CurrentUser.OpenSubKey(it.TaskPath, true)) k?.SetValue("State", enable ? 2 : 1, RegistryValueKind.DWord);
                break;
            default:
                byte[] val = enable
                    ? new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                    : new byte[] { 3, 0, 0, 0 }.Concat(BitConverter.GetBytes(DateTime.Now.ToFileTime())).ToArray();
                if (!Reg.Set(it.Hive, it.ApprovedPath, it.Name, val, RegistryValueKind.Binary)) throw new IOException("Не удалось сохранить изменение автозагрузки.");
                break;
        }
        it.Enabled = enable;
        Log.Write($"Автозагрузка: «{it.Title ?? it.Name}»: {(enable ? "включена" : "выключена")}");
    }

    /// <summary>Добавить программу в автозагрузку текущего пользователя.</summary>
    public static string Add(string path, string args = null)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {   // ярлык — копируем в папку автозагрузки, так сохраняются его параметры
            File.Copy(path, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Path.GetFileName(path)), true);
        }
        else
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunCU, true);
            k.SetValue(name, $"\"{path}\"" + (string.IsNullOrWhiteSpace(args) ? "" : " " + args.Trim()), RegistryValueKind.String);
        }
        Log.Ok($"Добавлено в автозагрузку: {name}");
        return name;
    }

    /// <summary>Убрать запись из автозагрузки совсем (кроме защищённых и приложений Store).</summary>
    public static void Remove(StartupItem it)
    {
        if (!it.Removable) return;
        switch (it.Kind)
        {
            case StartKind.Folder:
                // папка автозагрузки в профиле доступна пользователю — удаляем только внутри неё, через SafeFs
                foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
                {
                    string root = !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? SafeFs.RootFinal(dir) : null;
                    if (root != null && SafeFs.DeleteUnder(it.Command, root)) break;
                }
                break;
            case StartKind.Task: Sh.Run(Sh.Sys32("schtasks.exe"), $"/Delete /TN \"{it.TaskPath}\" /F", 30_000); break;
            case StartKind.Registry:
                using (var root = Reg.Root(it.Hive)) using (var k = root.OpenSubKey(it.RunKey ?? RunCU, true)) k?.DeleteValue(it.Name, false);
                break;
        }
        Log.Write($"Убрано из автозагрузки: {it.Title ?? it.Name}");
    }
}

// ===================================================== ИНСТРУМЕНТЫ
public static class Tools
{
    /// <summary>on — следующий запуск в безопасном режиме (с сетью нет — минимальный), off — обычный. Затем перезагрузка.</summary>
    public static void SafeMode(bool on)
    {
        var bcd = Sh.Sys32("bcdedit.exe");
        (int Code, string Output) Run(string command) => Sh.Run(bcd, command, 20_000);
        string temporary = null;
        if (on)
        {
            SafeBoot.CleanupStored();
            if (File.Exists(SafeBoot.PendingFile)) throw new IOException("Прошлый запуск в безопасном режиме ещё не завершён. Сначала загрузите Windows в обычном режиме.");
            temporary = SafeBoot.Prepare(Run, SafeBoot.Remember);
        }
        else if (Run("/deletevalue {current} safeboot").Code != 0)
            throw new IOException("Не удалось вернуть обычный запуск Windows. Перезагрузка отменена.");
        var result = Sh.Run(Sh.Sys32("shutdown.exe"), "/r /t 5", 10_000);
        if (result.Code != 0)
        {
            if (temporary != null && Run($"/delete {temporary}").Code != 0)
                throw new IOException("Перезагрузка не началась, а подготовленный безопасный режим убрать не удалось. Проверьте параметры загрузки Windows.");
            throw new IOException("Не удалось начать перезагрузку Windows.");
        }
        Log.Ok(on ? "Windows один раз запустится в безопасном режиме, затем вернётся к обычному запуску" : "Обычный запуск Windows возвращён");
    }

    public static void Repair()
    {
        Log.Head("Проверяю и чиню Windows (от 10 до 30 минут)");
        var (c1, _) = Sh.Run(Sh.Sys32("dism.exe"), "/Online /Cleanup-Image /RestoreHealth", 3_600_000);
        Log.Write(c1 == 0 ? "Шаг 1 из 2: образ Windows проверен" : "Шаг 1 из 2: образ Windows проверить не удалось, продолжаю", c1 == 0 ? LogLevel.Ok : LogLevel.Warn);
        Log.Head("Шаг 2 из 2: проверяю системные файлы");
        var (c2, _) = Sh.Run(Sh.Sys32("sfc.exe"), "/scannow", 3_600_000);
        Log.Write(c2 == 0 ? "Готово. Windows в порядке" : "Исправить всё не получилось. Перезагрузите компьютер и запустите проверку ещё раз",
                  c2 == 0 ? LogLevel.Ok : LogLevel.Warn);
    }

    public static void OptimizeDisks()
    {
        Log.Head("Ускоряю диски");
        var (_, o) = Sh.PS(@"
Get-Volume | Where-Object { $_.DriveLetter -and $_.FileSystem -eq 'NTFS' -and $_.DriveType -eq 'Fixed' } | ForEach-Object {
  $dl = $_.DriveLetter
  # Windows сама выбирает: TRIM для SSD, дефрагментация для HDD
  try { Optimize-Volume -DriveLetter $dl -ErrorAction Stop; ""Диск ${dl}: готово"" }
  catch { ""Диск ${dl}: не удалось оптимизировать"" }
}", 7_200_000);
        foreach (var l in o.Split('\n').Where(x => x.Trim().Length > 0))
        { if (l.Contains("не удалось")) Log.Warn(l.Trim()); else Log.Ok(l.Trim()); }
    }

    public static void NetReset()
    {
        Log.Head("Чиню интернет");
        foreach (var command in new[] { ("ipconfig.exe", "/flushdns"), ("netsh.exe", "winsock reset"), ("netsh.exe", "int ip reset") })
        {
            var (code, output) = Sh.Run(Sh.Sys32(command.Item1), command.Item2);
            string skipped = null;
            bool ok = command.Item2 == "int ip reset" ? NetIpResetOk(code, output, out skipped) : code == 0;
            if (!ok) throw new IOException("Сброс сети завершился не полностью. Проверьте журнал и настройки VPN/адресов.");
            if (skipped != null) Log.Skip("netsh int ip reset: Windows не дала сбросить один служебный раздел (" + skipped + "). Так бывает всегда, сеть сброшена");
        }
        Log.Ok("Сеть сброшена. Перезагрузите компьютер.");
    }

    /// <summary>
    /// «netsh int ip reset» выходит с кодом 1 даже от администратора: один раздел Windows сбросить не даёт
    /// («Resetting , failed. Access is denied.» / «Сброс  - сбой. Отказано в доступе.»), остальное «OK!»
    /// (так на Windows 10 и 11). Успех: код 0, или код 1, когда строк «OK!» не меньше 10,
    /// а неудачная строка с тем же первым словом («Resetting»/«Сброс») не больше одной. skipped — эта строка.
    /// </summary>
    internal static bool NetIpResetOk(int code, string output, out string skipped)
    {
        skipped = null;
        if (code == 0) return true;
        if (code != 1 || string.IsNullOrEmpty(output)) return false;
        var lines = output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var ok = lines.Where(l => l.EndsWith("OK!", StringComparison.Ordinal)).ToList();
        if (ok.Count < 10) return false;
        string word = ok[0].Split(' ')[0];
        var failed = lines.Where(l => l.StartsWith(word + " ", StringComparison.Ordinal) && !l.EndsWith("OK!", StringComparison.Ordinal)).ToList();
        if (failed.Count > 1) return false;
        skipped = failed.Count == 1 ? failed[0] : "без подробностей";
        return true;
    }

    // ---- очистка кэша ожидания ОЗУ ----
    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Lo; public int Hi; }
    [StructLayout(LayoutKind.Sequential)] struct TOKEN_PRIVILEGES { public int Count; public LUID Luid; public int Attr; }
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr h, int acc, out IntPtr tok);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string s, string n, out LUID l);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr t, bool d, ref TOKEN_PRIVILEGES n, int l, IntPtr p, IntPtr r);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int cls, ref int info, int len);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    class MEMORYSTATUSEX { public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>(); public uint dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

    public static ulong AvailMB() { var m = new MEMORYSTATUSEX(); GlobalMemoryStatusEx(m); return m.ullAvailPhys >> 20; }

    public static void PurgeStandby()
    {
        ulong before = AvailMB();
        OpenProcessToken(GetCurrentProcess(), 0x28, out var tok);
        var tp = new TOKEN_PRIVILEGES { Count = 1, Attr = 2 };
        LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out tp.Luid);
        AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        CloseHandle(tok);
        int cmd = 4;
        int r = NtSetSystemInformation(80, ref cmd, 4);
        if (r == 0) Log.Ok($"Память освобождена: было свободно {before:N0} МБ, стало {AvailMB():N0} МБ");
        else Log.Warn($"Не удалось освободить память: Windows не разрешила (код 0x{r:X8})");
    }
}

// ===================================================== ОТЧЁТ
public static class Report
{
    public static string Write(SysInfo s, IEnumerable<Tweak> applied, long freed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Merno {App.Version}: что сделано на компьютере");
        sb.AppendLine($"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}");   // без имени компьютера: отчёт часто пересылают
        sb.AppendLine(new string('=', 64));
        sb.AppendLine();
        sb.AppendLine("СВЕДЕНИЯ О КОМПЬЮТЕРЕ");
        sb.AppendLine(s?.ToText() ?? "(сведения не собраны)" + Environment.NewLine);   // пустая строка перед следующим разделом
        sb.AppendLine("ВКЛЮЧЁННЫЕ НАСТРОЙКИ");
        var list = applied?.ToList() ?? new();
        if (list.Count == 0) sb.AppendLine("  (настройки не менялись)");
        foreach (var t in list) { sb.AppendLine($"  ✓ [{t.Cat}] {t.Name}"); if (!string.IsNullOrEmpty(t.Desc)) sb.AppendLine($"      {t.Desc}"); }
        if (freed > 0) { sb.AppendLine(); sb.AppendLine($"ОЧИСТКА: освобождено {Sh.Size(freed)}"); }
        sb.AppendLine();
        sb.AppendLine("КАК ОТМЕНИТЬ ИЗМЕНЕНИЯ");
        sb.AppendLine("  1. Запустите Merno → «Настройки» → «Откат изменений» → выберите запись → «Вернуть как было».");
        sb.AppendLine("  2. Если не помогло: там же нажмите «Восстановление системы» и выберите точку «Merno» с нужной датой (если её удалось создать).");
        sb.AppendLine("  Merno сохранил прежние настройки, так что вернуть можно всё, кроме удалённых приложений.");
        sb.AppendLine(new string('=', 64));
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"Отчёт Merno {DateTime.Now:yyyy-MM-dd HH-mm}.txt");
        Unelevated.WriteText(file, sb.ToString());
        Log.Ok($"Отчёт сохранён на рабочий стол: {Path.GetFileName(file)}");
        return file;
    }
}
