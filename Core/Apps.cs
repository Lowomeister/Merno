// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Merno.Core;

public class AppItem
{
    public string name { get; set; }
    public string category { get; set; }
    public string winget { get; set; }
    public string url { get; set; }
    public string desc { get; set; }
    public string author { get; set; }
    /// <summary>Начало DisplayName в списке установленных программ Windows.</summary>
    public string match { get; set; }
    public string detectVersion { get; set; }
    /// <summary>"runtime" — вкладка «Компоненты»; пусто — «Программы».</summary>
    public string tab { get; set; }
    /// <summary>Входит в «Установить всё нужное».</summary>
    public bool rec { get; set; }
    /// <summary>Источник winget: по умолчанию winget, для приложений Microsoft Store — msstore.</summary>
    public string source { get; set; }
    /// <summary>Для результатов поиска: версия из winget.</summary>
    public string version { get; set; }
    /// <summary>Проверено Merno — помечается звёздочкой в поиске.</summary>
    public bool trusted { get; set; }
    /// <summary>Показывать только с такой видеокартой: nvidia, amd, intel, igpu. Несколько — через «|».</summary>
    public string forGpu { get; set; }
    /// <summary>Показывать только с таким процессором: amd, intel.</summary>
    public string forCpu { get; set; }
    /// <summary>Минимум оперативной памяти, ГБ. 0 — без ограничения.</summary>
    public int minRam { get; set; }
    /// <summary>Скрывать на ноутбуках.</summary>
    public bool desktopOnly { get; set; }
    /// <summary>Инструмент для проверки или разгона, а не обязательная программа: показывается отдельно.</summary>
    public bool tuning { get; set; }
    /// <summary>Только для одной версии Windows: "10" или "11". Пусто — для обеих.</summary>
    public string forOs { get; set; }
    /// <summary>Особый способ установки вместо winget. "spotify" — установщик LoaderSpot.</summary>
    public string loader { get; set; }
}

/// <summary>Каталог полезных программ и установка через winget.</summary>
public static class Apps
{
    // winget — App Execution Alias; из повышенного процесса может не находиться по PATH.
    public static string Exe => _exe ??= ResolveWinget();
    private static string _exe;
    /// <summary>Пересчитать путь к winget (после того как Merno поставил его сам).</summary>
    public static void ResetWinget() => _exe = null;

    /// <summary>Версия пакета «Установщик приложений» из имени папки
    /// (Microsoft.DesktopAppInstaller_1.29.380.0_x64__8wekyb3d8bbwe). Сравнивать только как System.Version:
    /// по тексту «1.9» оказывается «новее» «1.29».</summary>
    internal static System.Version PackageVersion(string dir)
    {
        var m = Regex.Match(Path.GetFileName(dir ?? ""), @"DesktopAppInstaller_([\d.]+)_");
        return m.Success && System.Version.TryParse(m.Groups[1].Value, out var v) ? v : new System.Version(0, 0);
    }

    private static string ResolveWinget()
    {
        // Сначала сам пакет «Установщик приложений» в Program Files\WindowsApps — туда пишет только система.
        // Ярлык в профиле (%LOCALAPPDATA%\Microsoft\WindowsApps) может заменить любая программа пользователя,
        // а Merno запускает winget с правами администратора.
        try
        {
            var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            // Проверяем каждый вариант: повреждённый старший пакет не скрывает рабочий.
            foreach (var file in Directory.GetDirectories(apps, "Microsoft.DesktopAppInstaller_*__8wekyb3d8bbwe")
                .OrderByDescending(PackageVersion).Select(d => Path.Combine(d, "winget.exe")))
                if (ProtectedCode.IsTrustedFile(file, apps)) return file;
        }
        catch { }
        // Get-AppxPackage не требует перечислять защищённый корень WindowsApps.
        // Ответ команды сам по себе не даёт доверия: проверяем точный файл и ACL всех родителей.
        try
        {
            var (code, output) = Sh.PS("Get-AppxPackage -Name Microsoft.DesktopAppInstaller -ErrorAction Stop | " +
                "Where-Object { $_.PackageFamilyName -eq 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe' -and -not $_.IsFramework } | " +
                "Sort-Object { [version]$_.Version } -Descending | ForEach-Object { $_.InstallLocation }", 30_000);
            if (code == 0)
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
                foreach (var location in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var file = Path.Combine(location, "winget.exe");
                    if (ProtectedCode.IsTrustedFile(file, root)) return file;
                }
            }
        }
        catch (Exception ex) { Log.Skip("Не удалось найти пакет winget: " + ex.Message); }
        // Ни обычный файл, ни произвольная reparse point в профиле не доказывают доверие.
        // null — недоступен; Sh не запускает его и не подставляет выдуманный путь.
        return null;
    }

    public static List<AppItem> Load()
    {
        try
        {
            using var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("apps.json");
            using var rd = new StreamReader(st);
            return JsonSerializer.Deserialize<List<AppItem>>(rd.ReadToEnd()) ?? new();
        }
        catch (Exception ex) { Log.Warn("Список программ не загрузился: " + ex.Message); return new(); }
    }

    /// <summary>winget есть и достаточно свежий (старый не понимает команды Merno — для него предлагаем обновить).</summary>
    public static bool WingetAvailable() => WingetSetup.Check() == WingetSetup.State.Ok;

    static bool Has(string text, string what) => text != null && text.Contains(what, StringComparison.OrdinalIgnoreCase);

    /// <summary>Процессор AMD (Ryzen, Athlon, Threadripper, EPYC).</summary>
    public static bool AmdCpu(SysInfo s) =>
        Has(s?.Cpu, "AMD") || Has(s?.Cpu, "Ryzen") || Has(s?.Cpu, "Threadripper") || Has(s?.Cpu, "Athlon");

    /// <summary>Какая видеокарта стоит: nvidia, amd, intel, igpu. Пусто — не определить.</summary>
    public static string GpuKind(SysInfo s)
    {
        if (s == null) return null;
        if (s.IsNvidia) return "nvidia";
        if (s.IsAmd) return "amd";
        if (Has(s.Gpu, "Arc")) return "intel";
        if (!string.IsNullOrEmpty(s.Gpu) && !s.HasDGpu) return "igpu";
        return null;
    }

    /// <summary>Подходит ли программа этому компьютеру. Неизвестное железо всегда считаем подходящим:
    /// лучше показать лишнее, чем спрятать нужное.</summary>
    public static bool Fits(AppItem a, SysInfo s)
    {
        if (a == null || s == null) return true;
        if (!string.IsNullOrEmpty(a.forCpu))
        {
            bool amd = AmdCpu(s);
            if (a.forCpu.Contains("amd", StringComparison.OrdinalIgnoreCase) && !amd) return false;
            if (a.forCpu.Contains("intel", StringComparison.OrdinalIgnoreCase) && amd) return false;
        }
        if (!string.IsNullOrEmpty(a.forGpu))
        {
            string kind = GpuKind(s);
            if (kind != null)
            {
                bool ok = a.forGpu.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(k => k.Equals(kind, StringComparison.OrdinalIgnoreCase));
                if (!ok) return false;
            }
        }
        if (a.desktopOnly && s.Laptop) return false;
        if (a.minRam > 0 && s.RamGB > 0 && s.RamGB < a.minRam) return false;
        if (!string.IsNullOrEmpty(a.forOs))
        {
            bool win11 = Environment.OSVersion.Version.Build >= 22000;
            bool want11 = a.forOs.Contains("11", StringComparison.Ordinal);
            bool want10 = a.forOs.Contains("10", StringComparison.Ordinal);
            if (want11 && !want10 && !win11) return false;
            if (want10 && !want11 && win11) return false;
        }
        return true;
    }

    /// <summary>Что уже установлено. Смотрим список «Программы и компоненты» в реестре
    /// (winget list обрезает длинные Id и не видит программы, поставленные не через winget).</summary>
    /// <param name="onDisk">ещё искать на дисках и в ярлыках (программа осталась на D: после переустановки Windows и в «Программах» её нет).
    /// После удаления не включать: от программы могла остаться папка.</param>
    public static HashSet<string> Installed(IEnumerable<AppItem> apps, bool onDisk = true, Action onAppxFailed = null)
    {
        var names = new List<string>();
        var versions = new List<(string Name, string Version)>();
        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryHive.CurrentUser })
        foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
        {
            try
            {
                using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                using var un = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (un == null) continue;
                foreach (var sub in un.GetSubKeyNames())
                {
                    try { using var k = un.OpenSubKey(sub); if (k?.GetValue("DisplayName") is string dn && dn.Length > 0) {
                        dn = dn.Replace("®", "").Replace("™", ""); names.Add(dn);
                        versions.Add((dn, k.GetValue("DisplayVersion") as string));
                    } }
                    catch { }
                }
            }
            catch { }
        }
        var res = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // приложения Магазина не видны в реестре — проверяем через Get-AppxPackage (один вызов)
        HashSet<string> appx = null;
        if (apps.Any(a => a.match?.StartsWith("appx:") == true))
        {
            // явный маркер успеха: ошибка, таймаут или пустой ответ — это «не удалось узнать», а не «приложений нет»
            // без -ErrorAction Stop: сбой одного пакета не должен прятать весь список
            var (code, o) = Sh.PS("try { Get-AppxPackage | ForEach-Object { $_.Name }; 'MERNO_APPX_OK' } catch { 'MERNO_APPX_FAIL' }", 120_000);
            appx = AppxNames(code, o, out bool appxOk);
            if (!appxOk)
            {
                Log.Warn("Не удалось получить список приложений Windows");
                onAppxFailed?.Invoke();
            }
        }
        foreach (var a in apps)
            if (a.match?.StartsWith("appx:") == true)
            { if (a.match[5..].Split('|').Any(n => appx.Contains(n))) res.Add(a.name); }
            else if (a.match?.StartsWith("file:") == true)   // компонент без записи в «Программах» — проверяем по файлу
            {
                // только системные переменные: %SystemRoot% из HKCU\Environment может подменить пользователь
                try { if (File.Exists(SystemVariables.Expand(a.match[5..]))) res.Add(a.name); }
                catch (IOException) { Log.Warn($"{a.name}: неизвестная переменная в пути проверки"); }
            }
            else if (string.IsNullOrEmpty(a.detectVersion) ? NameMatches(names, a.match) :
                versions.Any(v => NameMatches(new[] { v.Name }, a.match) && VersionMatches(v.Version, a.detectVersion)))
                res.Add(a.name);
        ExtraDetect(apps, names, res);
        if (onDisk) DiskDetect(apps, res);
        return res;
    }

    /// <summary>Строже, чем NameMatches: папка или ярлык называется ровно как программа или «программа …»
    /// (иначе папка «GitHub Desktop» засчиталась бы как Git).</summary>
    internal static bool DiskNameMatches(IEnumerable<string> names, string match)
    {
        if (names == null || string.IsNullOrEmpty(match)) return false;
        return match.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Any(m => names.Any(n => n != null &&
                (string.Equals(n, m, StringComparison.OrdinalIgnoreCase) ||
                 n.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>Запасная проверка для программ без записи в «Программах»: ярлыки в «Пуске» и на рабочем столе,
    /// папки в корне локальных дисков, в Program Files и в Games. Папка засчитывается, только если в ней есть .exe.</summary>
    private static void DiskDetect(IEnumerable<AppItem> apps, HashSet<string> res)
    {
        try
        {
            var left = apps.Where(a => !res.Contains(a.name) && string.IsNullOrEmpty(a.detectVersion) && !string.IsNullOrEmpty(a.match)
                                       && !a.match.StartsWith("appx:") && !a.match.StartsWith("file:")).ToList();
            if (left.Count == 0) return;
            var found = new List<string>();   // названия ярлыков и папок
            foreach (var f in new[] { Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.StartMenu,
                                      Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.DesktopDirectory })
            {
                try
                {
                    var d = Environment.GetFolderPath(f);
                    if (Directory.Exists(d))
                        found.AddRange(Directory.EnumerateFiles(d, "*.lnk", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension));
                }
                catch { }
            }
            var roots = new List<string>();
            foreach (var drv in DriveInfo.GetDrives())
            {
                try
                {
                    if (drv.DriveType != DriveType.Fixed || !drv.IsReady) continue;
                    roots.Add(drv.RootDirectory.FullName);
                    foreach (var sub in new[] { "Program Files", "Program Files (x86)", "Games", "Игры" })
                        roots.Add(Path.Combine(drv.RootDirectory.FullName, sub));
                }
                catch { }
            }
            foreach (var r in roots)
            {
                try
                {
                    if (!Directory.Exists(r)) continue;
                    foreach (var dir in Directory.EnumerateDirectories(r))
                    {
                        string n = Path.GetFileName(dir);
                        if (!left.Any(a => DiskNameMatches(new[] { n }, a.match))) continue;   // .exe ищем только у подходящих папок
                        try { if (Directory.EnumerateFiles(dir, "*.exe").Any()) found.Add(n); } catch { }
                    }
                }
                catch { }
            }
            foreach (var a in left)
                if (DiskNameMatches(found, a.match)) res.Add(a.name);
        }
        catch (Exception ex) { Log.Skip("Поиск программ на дисках: " + ex.Message); }
    }

    /// <summary>Совпадает ли запись «Программ и компонентов» с match из каталога.
    /// match может содержать несколько вариантов через «|». Короткий кусок (до 12 символов) считается только
    /// началом названия — иначе «PhysX» находился бы в любом упоминании; длинный может стоять в любом месте.</summary>
    internal static bool NameMatches(IEnumerable<string> names, string match)
    {
        if (names == null || string.IsNullOrEmpty(match)) return false;
        return match.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Any(m => names.Any(n => n != null &&
                (n.Equals(m, StringComparison.OrdinalIgnoreCase) || n.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase) ||
                 n.StartsWith(m + "-", StringComparison.OrdinalIgnoreCase) || (m.Length > 12 && n.Contains(m, StringComparison.OrdinalIgnoreCase)))));
    }
    internal static bool VersionMatches(string actual, string expected) =>
        Version.TryParse(actual, out var a) && Version.TryParse(expected, out var e) &&
        a.Major == e.Major && a.Minor == e.Minor && a.Build == e.Build &&
        (e.Revision < 0 || a.Revision == e.Revision);

    /// <summary>Дополнительная проверка Java и PhysX: по названию в «Программах» их находят не всегда.
    /// PhysX на русской Windows называется «NVIDIA Системное программное обеспечение PhysX»; Java бывает
    /// не только Temurin, но и Oracle, Microsoft, Zulu, Corretto, Liberica — и просто распакованной папкой.</summary>
    private static void ExtraDetect(IEnumerable<AppItem> apps, List<string> names, HashSet<string> res)
    {
        try
        {
            var physx = apps.FirstOrDefault(a => string.Equals(a.winget, "Nvidia.PhysX", StringComparison.OrdinalIgnoreCase));
            if (physx != null && !res.Contains(physx.name))
            {
                var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVIDIA Corporation", "PhysX", "Common");
                bool loader64 = File.Exists(Path.Combine(common, "PhysXLoader64.dll"));
                bool loader32 = File.Exists(Path.Combine(common, "PhysXLoader.dll"));
                if (PhysXDetected(names, loader64, loader32)) res.Add(physx.name);
            }

            var javas = apps.Where(a => a.winget != null && JavaPick.IsMatch(a.winget) && !res.Contains(a.name)).ToList();
            if (javas.Count == 0) return;
            var have = JavaMajors(names);
            foreach (var a in javas)
            {
                int major = JavaMajorFromWinget(a.winget);
                if (major > 0 && have.Contains(major)) res.Add(a.name);
            }
        }
        catch (Exception ex) { Log.Skip("Проверка Java/PhysX: " + ex.Message); }
    }

    private static readonly System.Text.RegularExpressions.Regex JavaPick = new(@"^EclipseAdoptium\.Temurin\.(\d+)\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Номер версии Java из Id пакета Temurin: EclipseAdoptium.Temurin.17.JRE → 17; 0 — не Temurin.</summary>
    internal static int JavaMajorFromWinget(string winget)
    {
        var m = JavaPick.Match(winget ?? "");
        return m.Success && int.TryParse(m.Groups[1].Value, out var major) && major >= 5 && major <= 40 ? major : 0;
    }

    /// <summary>Установлен ли PhysX. На русской Windows он называется «NVIDIA Системное программное обеспечение PhysX»,
    /// поэтому ищем оба слова в любом порядке, а не начало «NVIDIA PhysX». Запасной признак — файлы PhysXLoader.</summary>
    internal static bool PhysXDetected(IEnumerable<string> names, bool loader64, bool loader32)
    {
        if (loader64 || loader32) return true;
        if (names == null) return false;
        return names.Any(n => n != null &&
            n.Contains("PhysX", StringComparison.OrdinalIgnoreCase) &&
            n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Номер версии Java из строки: «1.8.0_402» → 8, «21.0.2» → 21, «17» → 17.
    /// 0 — не версия Java (например, «JavaScript»).</summary>
    internal static int JavaMajorOf(string v)
    {
        var m = System.Text.RegularExpressions.Regex.Match(v ?? "", @"^\s*(?:1\.)?(\d{1,2})(?!\d)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n >= 5 && n <= 40 ? n : 0;
    }

    /// <summary>Версии Java только по названиям в «Программах» (без реестра и файлов) — чистая часть для тестов.
    /// «Java 8 Update 401», «Java(TM) SE Development Kit 21.0.2», «Microsoft Build of OpenJDK with Hotspot 21.0.5»,
    /// «Zulu JDK 17», «Amazon Corretto 21», «Liberica JDK 21», «Eclipse Temurin JRE with Hotspot 17.0.12».</summary>
    internal static HashSet<int> JavaMajorsFromNames(IEnumerable<string> names)
    {
        var res = new HashSet<int>();
        if (names == null) return res;
        var rx = new System.Text.RegularExpressions.Regex(
            @"(?:\bJava(?:\s*\(TM\))?(?:\s+SE)?(?:\s+(?:Development Kit|Runtime Environment))?|OpenJDK|Hotspot|Zulu(?:\s+(?:JDK|JRE))?|Corretto|Liberica(?:\s+(?:JDK|JRE))?|Semeru[^\d]*|Temurin[^\d]*)\s+(?:1\.)?(\d{1,2})(?!\d)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var n in names)
        {
            if (n == null) continue;
            if (n.Contains("JavaScript", StringComparison.OrdinalIgnoreCase) || n.Contains("Java Auto Updater", StringComparison.OrdinalIgnoreCase)) continue;
            var m = rx.Match(n);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var v) && v >= 5 && v <= 40) res.Add(v);
        }
        return res;
    }

    /// <summary>Какие версии Java есть на компьютере (8, 17, 21…): записи в «Программах», ключи реестра производителей и файлы release в папках Java.</summary>
    internal static HashSet<int> JavaMajors(IEnumerable<string> names)
    {
        var res = JavaMajorsFromNames(names);
        void Add(int n) { if (n > 0) res.Add(n); }

        // 2) ключи реестра, которые ставят установщики Java (имена подключей — версии)
        string[] keys =
        {
            @"SOFTWARE\JavaSoft\JDK", @"SOFTWARE\JavaSoft\JRE", @"SOFTWARE\JavaSoft\Java Runtime Environment", @"SOFTWARE\JavaSoft\Java Development Kit",
            @"SOFTWARE\Eclipse Adoptium\JRE", @"SOFTWARE\Eclipse Adoptium\JDK", @"SOFTWARE\Eclipse Foundation\JDK", @"SOFTWARE\AdoptOpenJDK\JRE", @"SOFTWARE\AdoptOpenJDK\JDK",
            @"SOFTWARE\Microsoft\JDK", @"SOFTWARE\Azul Systems\Zulu", @"SOFTWARE\BellSoft\Liberica", @"SOFTWARE\Amazon Corretto"
        };
        foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
        {
            try
            {
                using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view);
                foreach (var path in keys)
                {
                    try
                    {
                        using var k = root.OpenSubKey(path);
                        if (k == null) continue;
                        foreach (var sub in k.GetSubKeyNames()) Add(JavaMajorOf(System.Text.RegularExpressions.Regex.Replace(sub, @"^(?:zulu|liberica|jdk|jre)[-\s]*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
                    }
                    catch { }
                }
            }
            catch { }
        }

        // 3) распакованная Java: файл release с JAVA_VERSION="21.0.2" в папках производителей и в JAVA_HOME
        var dirs = new List<string>();
        foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            foreach (var vendor in new[] { "Java", "Eclipse Adoptium", "Eclipse Foundation", "AdoptOpenJDK", "Zulu", "Microsoft", "Amazon Corretto", "BellSoft", "Semeru" })
            {
                try
                {
                    var d = Path.Combine(pf, vendor);
                    if (Directory.Exists(d)) dirs.AddRange(Directory.GetDirectories(d));
                }
                catch { }
            }
        try { var jh = Environment.GetEnvironmentVariable("JAVA_HOME", EnvironmentVariableTarget.Machine); if (!string.IsNullOrWhiteSpace(jh)) dirs.Add(jh); } catch { }
        foreach (var d in dirs)
        {
            try
            {
                var rel = Path.Combine(d, "release");
                if (!File.Exists(rel) || new FileInfo(rel).Length > 64 * 1024) continue;
                var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(rel), @"JAVA_VERSION=""([^""]+)""");
                if (m.Success) Add(JavaMajorOf(m.Groups[1].Value));
            }
            catch { }
        }
        return res;
    }

    /// <summary>0x8A150011 — хеш установщика не совпал с каталогом winget.</summary>
    const int HashMismatch = -1978335215;

    public static (bool Ok, string Message) Install(AppItem a)
    {
        // Spotify из России через winget не ставится — берём установщик LoaderSpot
        if (a?.loader == "spotify")
        {
            var sp = InstallSpotify(a);
            if (sp.Ok) return sp;
            Log.Warn("Spotify через LoaderSpot: " + sp.Message + ". Пробую winget.");
        }
        if (string.IsNullOrEmpty(a.winget)) return (false, "Эту программу скачайте с сайта, нажав «Сайт».");
        Log.Write($"Устанавливаю {a.name}…");
        // WebView2 «уже установлен», а файлов нет: в реестре осталась старая запись. Убираем её (с копией), иначе установщик ничего не поставит.
        if (a.winget.Contains("Microsoft.EdgeWebView2Runtime", StringComparison.OrdinalIgnoreCase)) WebView2.RepairStaleRegistration();
        // в одной строке может быть несколько пакетов через «;» (например, x64 и x86)
        foreach (var id in a.winget.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string args = $"install --id {id} --exact --source {a.source ?? "winget"} --silent --accept-source-agreements --accept-package-agreements --disable-interactivity";
            var (code, outp) = WingetLive(a.name, args, 7_200_000);
            if (code == HashMismatch)
            {
                // 0x8A150011: производитель уже выложил новый установщик, а каталог winget помнит старый хеш (так бывает у Chrome).
                // Проверку не обходим — она защищает от подменённого файла. Обновляем каталог и пробуем ещё раз.
                Log.Write($"{a.name}: каталог winget отстал от сайта, обновляю каталог и пробую ещё раз…");
                Sh.Run(Exe, "source update --name winget", 180_000);
                (code, outp) = WingetLive(a.name, args, 7_200_000);
                if (code == HashMismatch)
                {
                    Log.Warn($"{a.name}: каталог winget ещё не знает новую версию с сайта (код {code})");
                    return (false, $"{a.name} только что обновился на сайте, а каталог winget ещё не успел. Попробуйте через несколько часов или скачайте с сайта, нажав «Сайт».");
                }
            }
            // 0 — установлено; 0x8A15002B — обновлений нет; 0x8A150061 — уже установлено (Merno не узнал её по названию);
            // 0x8A150109 — установлено, нужна перезагрузка
            if (code == 0 || code == -1978335189 || code == -1978335135) continue;
            if (code == -1978334967) { Log.Ok($"{a.name}: установлено, заработает после перезагрузки"); continue; }
            Log.Warn($"{a.name}: установить не получилось (код {code})");
            return (false, $"Не удалось установить {a.name}. Скачайте её с сайта, нажав «Сайт».");
        }
        Log.Ok($"Установлено: {a.name}.");
        return (true, $"{a.name} установлен.");
    }

    // ---------- Spotify через LoaderSpot ----------
    private const string LoaderSpotVersions = "https://loadspot.vercel.app/versions.json";

    /// <summary>Организации, которыми Spotify подписывает свои программы. Сравниваем поле O= целиком:
    /// при поиске «O=Spotify» в тексте подошло бы и «O=Spotify Tools LLC».</summary>
    private static readonly string[] SpotifyOrgs = { "Spotify AB", "Spotify Technology S.A.", "Spotify USA Inc." };

    private static bool IsSpotifySigner(string subject)
    {
        try
        {
            var dn = new System.Security.Cryptography.X509Certificates.X500DistinguishedName(subject);
            foreach (var rdn in dn.EnumerateRelativeDistinguishedNames())
            {
                if (rdn.HasMultipleElements || rdn.GetSingleElementType().Value != "2.5.4.10") continue;   // 2.5.4.10 — O (организация)
                var org = rdn.GetSingleElementValue();
                if (SpotifyOrgs.Any(o => string.Equals(o, org, StringComparison.Ordinal))) return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Ставит Spotify установщиком LoaderSpot: берём самый свежий x64-выпуск из versions.json,
    /// качаем во временную папку и запускаем без окон.
    /// </summary>
    private static (bool Ok, string Message) InstallSpotify(AppItem a)
    {
        string file = null;
        try
        {
            Log.Write("Ищу свежий Spotify в LoaderSpot…");
            var json = SpotHttp.GetStringAsync(LoaderSpotVersions).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            string arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
                          System.Runtime.InteropServices.Architecture.Arm64 ? "arm64"
                        : Environment.Is64BitOperatingSystem ? "x64" : "x86";
            string bestUrl = null, bestVer = null, bestDate = null;
            DateTime bestWhen = DateTime.MinValue;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.Value.TryGetProperty("win", out var win) || win.ValueKind != JsonValueKind.Object) continue;
                JsonElement? pick = null;
                if (win.TryGetProperty(arch, out var exact)) pick = exact;
                else foreach (var alt in new[] { "x64", "x86", "arm64" })
                    if (win.TryGetProperty(alt, out var any)) { pick = any; break; }
                if (pick is not JsonElement e || !e.TryGetProperty("url", out var u)) continue;
                string url = u.GetString();
                if (string.IsNullOrWhiteSpace(url)) continue;
                string date = e.TryGetProperty("date", out var d) ? d.GetString() : null;
                DateTime when = DateTime.MinValue;
                if (DateTime.TryParseExact(date, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var parsed)) when = parsed;
                else if (DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var loose)) when = loose;
                if (bestUrl != null && when <= bestWhen) continue;   // при равных датах доверяем порядку в versions.json
                bestWhen = when; bestUrl = url; bestDate = date;
                bestVer = prop.Value.TryGetProperty("fullversion", out var fv) ? fv.GetString() : prop.Name;
            }
            if (bestUrl == null) return (false, "в списке LoaderSpot нет установщика для Windows");
            // список чужой — берём только https (по http файл подменили бы по дороге ещё до проверки подписи)
            if (!Uri.TryCreate(bestUrl, UriKind.Absolute, out var bestUri) || bestUri.Scheme != Uri.UriSchemeHttps)
                return (false, "в списке LoaderSpot небезопасная ссылка на установщик");
            Log.Write($"Скачиваю Spotify {bestVer} ({bestDate})…");
            // не %TEMP%: оттуда любая программа пользователя могла бы подменить файл между проверкой подписи и запуском
            var dir = Log.NewWorkDir("spotify");
            file = Path.Combine(dir, "spotify_installer.exe");
            using var downloadTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            using (var resp = SpotHttp.GetAsync(bestUri, HttpCompletionOption.ResponseHeadersRead, downloadTimeout.Token).GetAwaiter().GetResult())
            {
                resp.EnsureSuccessStatusCode();
                using var src = resp.Content.ReadAsStream();
                using var dst = File.Create(file);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                DownloadTransfer.CopyAsync(src, dst, 400L * 1024 * 1024, TimeSpan.FromSeconds(60), downloadTimeout.Token,
                    got => NetLimit.PaceAsync(got, sw)).GetAwaiter().GetResult();
            }
            if (!File.Exists(file) || new FileInfo(file).Length < 1_000_000) return (false, "установщик скачался не полностью");
            // ссылка берётся из чужого списка, а запускаем мы с правами администратора — ставим только файл, подписанный самим Spotify
            // у X509CertificateLoader нет замены для чтения подписи из файла; саму подпись проверяет VerifyTrust ниже
#pragma warning disable SYSLIB0057
            using var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            if (!BundleGuard.VerifyTrust(file) || !IsSpotifySigner(certificate.Subject))
            {
                Log.Warn("Установщик Spotify не прошёл проверку подписи и удалён");
                try { File.Delete(file); } catch { }
                return (false, "установщик не подписан Spotify");
            }
            Log.Write("Запускаю установщик Spotify…");
            foreach (var args in new[] { "/silent", "/S" })
            {
                try
                {
                    // установщик Spotify от администратора не работает (молча выходит) — запускаем от имени пользователя;
                    // Spotify ставится в профиль, права ему не нужны
                    if (!Unelevated.RunAndWait(file, args, 900_000, out int err))
                    {
                        Log.Skip($"Spotify: не получилось запустить установщик без прав администратора (код {err}), запускаю с правами администратора");
                        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args)
                        { UseShellExecute = true });
                        p?.WaitForExit(900_000);
                    }
                }
                catch (Exception ex) { Log.Skip("Spotify, запуск установщика: " + ex.Message); }
                // установщик LoaderSpot иногда выходит раньше, чем заканчивает работу, — даём ему время
                for (int i = 0; i < 30; i++)
                {
                    if (Installed(new[] { a }).Contains(a.name)) break;
                    Thread.Sleep(2000);
                }
                if (Installed(new[] { a }).Contains(a.name))
                {
                    Log.Ok($"Установлено: {a.name} {bestVer}.");
                    return (true, $"{a.name} установлен.");
                }
            }
            return (false, "установщик LoaderSpot не поставил Spotify");
        }
        catch (Exception ex)
        {
            Log.Warn("Spotify через LoaderSpot: " + ex.Message);
            return (false, ex.Message);
        }
        finally
        {
            try { if (file != null && File.Exists(file)) File.Delete(file); } catch { }
        }
    }

    // ---------- ход установки ----------
    /// <summary>Что сейчас делает winget: «Discord: скачиваю — 23 МБ · 0:41». Приходит из фонового потока.
    /// winget при перенаправленном выводе не печатает проценты — поэтому этап, скачанные мегабайты и время.</summary>
    public static event Action<string> Progress;

    /// <summary>winget с отчётом о ходе работы (событие Progress раз в секунду).</summary>
    static (int Code, string Output) WingetLive(string name, string args, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var started = DateTime.Now.AddSeconds(-2);
        string stage = "готовлюсь";
        bool downloading = false;
        string tmp = Path.Combine(ProcessEnvironment.TempDirectory, "WinGet");
        void Report()
        {
            string t = $"{(int)sw.Elapsed.TotalMinutes}:{sw.Elapsed.Seconds:00}";
            string size = "";
            if (downloading)
            {
                try
                {
                    // самый свежий файл в папке загрузок winget — его размер растёт по ходу скачивания
                    long bytes = new DirectoryInfo(tmp).Exists
                        ? new DirectoryInfo(tmp).EnumerateFiles("*", SearchOption.AllDirectories)
                            .Where(f => f.LastWriteTime >= started && !f.DirectoryName.Contains(@"\cache", StringComparison.OrdinalIgnoreCase))
                            .Select(f => f.Length).DefaultIfEmpty(0).Max()
                        : 0;
                    if (bytes > 0) size = $" ({bytes / 1048576.0:0.#} МБ)";
                }
                catch { }
            }
            try { Progress?.Invoke($"{name}: {stage}{size} · {t}"); } catch { }
        }
        using var timer = new System.Threading.Timer(_ => Report(), null, 0, 1000);
        var r = Sh.RunStream(Exe, args, line =>
        {
            var l = line.Trim();
            if (l.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Скачивание", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Загрузка", StringComparison.OrdinalIgnoreCase))
            { stage = "скачиваю"; downloading = true; }
            else if (l.StartsWith("Starting package install", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Запуск установки", StringComparison.OrdinalIgnoreCase)
                     || l.StartsWith("Starting package uninstall", StringComparison.OrdinalIgnoreCase) || l.Contains("verified installer hash", StringComparison.OrdinalIgnoreCase)
                     || l.Contains("хэш установщика", StringComparison.OrdinalIgnoreCase))
            { stage = "устанавливаю (может занять пару минут)"; downloading = false; }
            else if (l.StartsWith("Found ", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Найдено", StringComparison.OrdinalIgnoreCase))
                stage = "нашёл, начинаю";
        }, timeoutMs);
        timer.Change(Timeout.Infinite, Timeout.Infinite);
        if (r.Code != 0 && r.Code != -1978335189 && r.Output?.Length > 0)
            Log.Skip($"    winget: {(r.Output.Length > 400 ? r.Output[^400..] : r.Output)}");
        return r;
    }

    // ---------- обновления установленных программ (winget upgrade) ----------
    public sealed record WingetUpdate(string Name, string Id, string From, string To);

    /// <summary>Что можно обновить: таблица «winget upgrade» (один вызов, ~5 с).
    /// Пустой список — обновлений нет; null — проверить не получилось (winget не ответил).</summary>
    public static List<WingetUpdate> Updates()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0) { Thread.Sleep(3000); ResetWinget(); }   // после обновления самого winget путь и пакет меняются
            if (!WingetAvailable()) continue;
            var (code, o) = Sh.RunStream(Exe, "upgrade --accept-source-agreements --disable-interactivity", null, 180_000);
            var res = ParseUpgrades(o, out bool table);
            if (table) return res;
            // таблицы нет: либо обновлений нет (код 0, обычный текст), либо winget сломался
            if (code == 0 && !string.IsNullOrWhiteSpace(o)) return res;
            Log.Skip($"Обновления программ: winget не ответил (код {code}): {(o?.Length > 300 ? o[..300] : o)}");
        }
        return null;
    }

    /// <summary>Разбор таблицы «winget upgrade». Колонки ищем по строке из «-----» и словам заголовка над ней —
    /// так не важен язык winget (Name/Имя, Id/ИД…). «Крутилки» перед таблицей уже отрезаны (RunStream делит по \r).</summary>
    public static List<WingetUpdate> ParseUpgrades(string o, out bool table)
    {
        var res = new List<WingetUpdate>();
        table = false;
        var lines = (o ?? "").Replace("\r\n", "\n").Split('\n', '\r');
        int d = Array.FindIndex(lines, l => l.Trim().Length > 20 && l.Trim().All(c => c == '-'));
        if (d < 1) return res;
        string head = lines[d - 1];
        // начала колонок — начала слов заголовка (слова разделены пробелами)
        var cols = new List<int>();
        for (int i = 0; i < head.Length; i++)
            if (!char.IsWhiteSpace(head[i]) && (i == 0 || char.IsWhiteSpace(head[i - 1]))) cols.Add(i);
        if (cols.Count < 4) return res;
        table = true;
        int cId = cols[1], cVer = cols[2], cAv = cols[3], cSrc = cols.Count > 4 ? cols[4] : -1;
        string Col(string l, int from, int to) => l.Length <= from ? "" : l.Substring(from, Math.Min(l.Length, to < 0 ? l.Length : to) - from).Trim();
        for (int i = d + 1; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Trim().Length == 0) break;   // конец таблицы; дальше — «требуют явного указания» и т. п.
            if (l.Length < cAv || !char.IsWhiteSpace(l[cId - 1])) continue;   // строка-итог («6 upgrades available.»)
            string id = Col(l, cId, cVer);
            if (!Regex.IsMatch(id, @"^[\w.\-+]+$")) continue;
            res.Add(new WingetUpdate(Col(l, 0, cId), id, Col(l, cVer, cAv), Col(l, cAv, cSrc)));
        }
        return res;
    }

    /// <summary>Обновляет сам winget (App Installer) — его ставим последним: после него winget какое-то время не отвечает.</summary>
    public static bool IsWingetItself(WingetUpdate u) => u.Id.Equals("Microsoft.AppInstaller", StringComparison.OrdinalIgnoreCase);

    /// <summary>Обновить одну программу через winget (тихо).</summary>
    public static bool Upgrade(WingetUpdate u)
    {
        if (!Regex.IsMatch(u.Id ?? "", @"^[\w.\-+]+$")) return false;
        Log.Write($"Обновляю {u.Name}: {u.From} → {u.To}…");
        var (code, _) = WingetLive(u.Name, $"upgrade --id {u.Id} --exact --silent --accept-source-agreements --accept-package-agreements --disable-interactivity", 7_200_000);
        bool ok = code == 0 || code == -1978335189;   // 0x8A15002B — уже последняя версия
        if (IsWingetItself(u))
        {
            // winget обновил сам себя: ждём, пока новый снова ответит
            ResetWinget();
            for (int i = 0; i < 15 && !WingetAvailable(); i++) { Thread.Sleep(2000); ResetWinget(); }
            ok = ok || WingetAvailable();   // процесс мог оборваться при замене пакета, но обновление прошло
        }
        if (ok) Log.Ok($"Обновлено: {u.Name}"); else Log.Warn($"{u.Name}: обновить не получилось (код {code})");
        return ok;
    }

    /// <summary>Установить или удалить — для пакетных действий.</summary>
    public static (bool Ok, string Message) InstallOrUninstall(AppItem a, bool install) =>
        install ? Install(a) : Uninstall(a);

    /// <summary>Удаляет компонент: через winget по Id, иначе — командой удаления из реестра.</summary>
    public static (bool Ok, string Message) Uninstall(AppItem a)
    {
        Log.Write($"Удаляю {a.name}…");
        // приложения Windows (из Магазина) удаляем как пакет — так же, как «Встроенные приложения»
        if (a.match?.StartsWith("appx:") == true)
        {
            var names = a.match[5..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                     .Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^[A-Za-z0-9.\-]+$")).ToList();
            if (names.Count == 0) return (false, "Удалите это в «Параметры» → «Приложения».");
            var list = string.Join(",", names.Select(n => "'" + n + "'"));
            var (_, o) = Sh.PS($"$ok=$true; foreach ($n in @({list})) {{ Get-AppxPackage -Name $n | ForEach-Object {{ try {{ Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop }} catch {{ $ok=$false }} }} }}; if ($ok) {{ 'OK' }} else {{ 'ERR' }}", 300_000);
            if (o.Contains("OK")) { Log.Ok($"Удалено: {a.name}."); return (true, null); }
            return (false, $"Не удалось удалить {a.name}. Удалите его в «Параметры» → «Приложения».");
        }
        if (!string.IsNullOrEmpty(a.winget) && WingetAvailable())
        {
            int removed = 0;
            foreach (var id in a.winget.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var (code, outp) = Sh.Run(Exe,
                    $"uninstall --id {id} --exact --silent --accept-source-agreements --disable-interactivity", 7_200_000);
                if (code == 0) { removed++; continue; }
                // -1978335212 (0x8A150014) — такой пакет не установлен (например, нет 32-битной версии)
                if (code == -1978335212) continue;
                Log.Warn($"{a.name}: winget не удалил (код {code}). Пробую удалить через «Программы и компоненты»");
                break;
            }
            // winget мог ответить «готово», а программа осталась (портативные пакеты, запущенный процесс) — проверяем
            if (removed > 0 && !Installed(new[] { a }, onDisk: false).Contains(a.name)) { Log.Ok($"Удалено: {a.name}."); return (true, null); }
            if (removed > 0) Log.Skip($"{a.name}: winget сообщил об удалении, но программа осталась на компьютере. Удаляю по записи в «Программах»");
        }
        // без winget или winget его не знает — ищем запись в «Программах и компонентах»
        if (string.IsNullOrEmpty(a.match) || a.match.Contains(':')) return (false, "Удалите это в «Параметры» → «Приложения».");
        var marks = a.match.Split('|');
        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryHive.CurrentUser })
        foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
        {
            try
            {
                using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                using var un = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (un == null) continue;
                foreach (var sub in un.GetSubKeyNames())
                {
                    using var k = un.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is not string dn || !marks.Any(m => dn.StartsWith(m, StringComparison.OrdinalIgnoreCase))) continue;
                    var cmd = (k.GetValue("QuietUninstallString") as string) ?? (k.GetValue("UninstallString") as string);
                    if (string.IsNullOrWhiteSpace(cmd)) continue;
                    // System = false: компонент удаляется осознанно, после предупреждения
                    // путь к записи нужен, чтобы после удаления проверить, что её больше нет (Uninstall.StillInstalled)
                    return Merno.Core.Uninstall.Remove(new InstalledApp
                    {
                        Name = dn, UninstallCmd = cmd, PerUser = hive == Microsoft.Win32.RegistryHive.CurrentUser,
                        RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + sub, RegistryHive = hive, RegistryView = view
                    });
                }
            }
            catch { }
        }
        return (false, "Удалите это в «Параметры» → «Приложения».");
    }

    // ---------- поиск по каталогу winget ----------
    private static bool Banned(string s) => s != null && Regex.IsMatch(s, "yandex|яндекс", RegexOptions.IgnoreCase);
    /// <summary>Запрос про Яндекс — показываем шуточный ответ вместо поиска.</summary>
    // «Алиса»/«Alice» сами по себе — только если это весь запрос; иначе нужен явный признак Яндекса
    public static bool IsYandex(string q) => q != null &&
        (Regex.IsMatch(q, @"yandex|яндекс|yndx|кинопоиск|яндкс|ya\.ru", RegexOptions.IgnoreCase) ||
         Regex.IsMatch(q.Trim(), @"^(алиса|алисы|alice)$", RegexOptions.IgnoreCase));

    // ---------- подборки по смыслу запроса: «браузер», «торрент», «плеер»… ----------
    record Pick(string Name, string Id, string Desc);
    static readonly (string Title, string[] Words, Pick[] Apps)[] Topics =
    {
        ("Браузеры", new[] { "браузер", "browser", "интернет" }, new Pick[] {
            new("Google Chrome", "Google.Chrome", "Самый популярный браузер, все расширения и синхронизация с Google."),
            new("Mozilla Firefox", "Mozilla.Firefox", "Независимый браузер с упором на приватность."),
            new("Brave", "Brave.Brave", "Браузер со встроенной блокировкой рекламы."),
            new("Opera GX", "Opera.OperaGX", "Браузер для геймеров: можно ограничить, сколько памяти и процессора он занимает."),
            new("Vivaldi", "Vivaldi.Vivaldi", "Очень гибко настраивается под себя.") }),
        ("Общение", new[] { "мессенджер", "чат", "общение", "звонк", "messenger", "chat" }, new Pick[] {
            new("Telegram Desktop", "Telegram.TelegramDesktop", "Мессенджер Telegram для компьютера."),
            new("Discord", "Discord.Discord", "Голосовой и текстовый чат для игр и сообществ."),
            new("Zoom", "Zoom.Zoom", "Видеозвонки и конференции.") }),
        ("Видеомонтаж", new[] { "монтаж", "видеоредактор", "редактор видео", "edit video" }, new Pick[] {
            new("Shotcut", "Meltytech.Shotcut", "Простой бесплатный видеоредактор."),
            new("Kdenlive", "KDE.Kdenlive", "Продвинутый бесплатный видеоредактор."),
            new("HandBrake", "HandBrake.HandBrake", "Сжатие и конвертация видео.") }),
        ("Видеоплееры", new[] { "плеер", "видео", "фильм", "кино", "player", "video" }, new Pick[] {
            new("VLC media player", "VideoLAN.VLC", "Открывает почти любое видео."),
            new("MPC-HC", "clsid2.mpc-hc", "Лёгкий и быстрый классический плеер."),
            new("PotPlayer", "Daum.PotPlayer", "Плеер с множеством настроек и хорошей картинкой.") }),
        ("Музыка и звук", new[] { "музык", "аудио", "music", "audio", "звук" }, new Pick[] {
            new("Spotify", "Spotify.Spotify", "Музыка и подкасты по подписке."),
            new("AIMP", "AIMP.AIMP", "Лёгкий плеер для своей музыкальной коллекции."),
            new("Audacity", "Audacity.Audacity", "Запись и монтаж звука.") }),
        ("Архиваторы", new[] { "архив", "zip", "rar", "распаков", "archive" }, new Pick[] {
            new("7-Zip", "7zip.7zip", "Бесплатный архиватор: ZIP, 7z, RAR и другие."),
            new("PeaZip", "Giorgiotani.Peazip", "Бесплатный архиватор с удобным окном."),
            new("WinRAR", "RARLab.WinRAR", "Классический архиватор (условно-бесплатный).") }),
        ("Торрент-клиенты", new[] { "торрент", "torrent", "скачать файлы" }, new Pick[] {
            new("qBittorrent", "qBittorrent.qBittorrent", "Бесплатный торрент-клиент без рекламы.") }),
        ("Документы и офис", new[] { "офис", "office", "ворд", "ms word", "excel", "документ", "таблиц" }, new Pick[] {
            new("LibreOffice", "TheDocumentFoundation.LibreOffice", "Бесплатная замена Word, Excel и PowerPoint."),
            new("ONLYOFFICE", "ONLYOFFICE.DesktopEditors", "Бесплатный офис, очень похож на Microsoft Office.") }),
        ("PDF", new[] { "pdf", "пдф" }, new Pick[] {
            new("SumatraPDF", "SumatraPDF.SumatraPDF", "Очень быстрый просмотрщик PDF и электронных книг."),
            new("Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit", "Официальная программа для PDF от Adobe.") }),
        ("Скриншоты и запись экрана", new[] { "скриншот", "скрин", "запись экрана", "стрим", "screenshot", "obs studio" }, new Pick[] {
            new("OBS Studio", "OBSProject.OBSStudio", "Запись экрана и стримы."),
            new("ShareX", "ShareX.ShareX", "Скриншоты, GIF и запись экрана."),
            new("Greenshot", "Greenshot.Greenshot", "Простые скриншоты с пометками.") }),
        ("Графика и фото", new[] { "фото", "графи", "рисова", "редактор фото", "photo", "фотошоп", "photoshop" }, new Pick[] {
            new("Paint.NET", "dotPDN.PaintDotNet", "Простой и быстрый редактор картинок."),
            new("GIMP", "GIMP.GIMP", "Бесплатная замена Photoshop."),
            new("Krita", "KDE.Krita", "Рисование и цифровая живопись."),
            new("Inkscape", "Inkscape.Inkscape", "Векторная графика.") }),
        ("Программирование", new[] { "код", "программир", "code", "разработ", "python", "питон", "пайтон", "node", "pycharm" }, new Pick[] {
            new("Visual Studio Code", "Microsoft.VisualStudioCode", "Самый популярный редактор кода."),
            new("Python 3.14", "Python.Python.3.14", "Язык программирования для скриптов, ботов и учёбы."),
            new("Node.js LTS", "OpenJS.NodeJS.LTS", "JavaScript вне браузера."),
            new("JetBrains Toolbox", "JetBrains.Toolbox", "Установщик PyCharm, IntelliJ IDEA и других сред."),
            new("Visual Studio Community 2026", "Microsoft.VisualStudio.Community", "Среда для C#, C++ и .NET."),
            new("Notepad++", "Notepad++.Notepad++", "Лёгкий редактор текста и кода."),
            new("Git", "Git.Git", "Система контроля версий."),
            new("Windows Terminal", "Microsoft.WindowsTerminal", "Удобная командная строка во вкладках.") }),
        ("Java для Minecraft", new[] { "java", "джава", "minecraft", "майнкрафт", "майнкрафт" }, new Pick[] {
            new("Java 8", "EclipseAdoptium.Temurin.8.JRE", "Для Minecraft 1.16.5 и старше. Более новые версии Minecraft её не используют."),
            new("Java 17", "EclipseAdoptium.Temurin.17.JRE", "Для Minecraft с 1.18 по 1.20.4."),
            new("Java 21", "EclipseAdoptium.Temurin.21.JRE", "Подходит для Minecraft с 1.20.5 по 1.21.11."),
            new("Java 25", "EclipseAdoptium.Temurin.25.JRE", "Для Minecraft 26.1 и новее. Старым версиям не подойдёт.") }),
        ("Базы данных", new[] { "sql", "база данных", "базы данных", "бд", "postgres", "mysql", "database" }, new Pick[] {            new("DBeaver", "dbeaver.dbeaver", "Работа с любыми базами данных."),
            new("SQL Server Management Studio", "Microsoft.SQLServerManagementStudio.22", "Управление базами Microsoft SQL Server."),
            new("MySQL Workbench", "Oracle.MySQLWorkbench", "Проектирование и запросы к MySQL."),
            new("PostgreSQL 18", "PostgreSQL.PostgreSQL.18", "Сервер PostgreSQL вместе с pgAdmin.") }),
        ("Железо и мониторинг", new[] { "температур", "мониторинг", "железо", "фпс", "fps", "разгон" }, new Pick[] {
            new("HWiNFO", "REALiX.HWiNFO", "Подробные сведения и датчики железа."),
            new("CPU-Z", "CPUID.CPU-Z", "Процессор, память и материнская плата."),
            new("GPU-Z", "TechPowerUp.GPU-Z", "Всё о видеокарте."),
            new("MSI Afterburner", "Guru3D.Afterburner", "Показывает кадры и температуру в играх, разгоняет видеокарту."),
            new("CrystalDiskInfo", "CrystalDewWorld.CrystalDiskInfo", "Здоровье дисков.") }),
        ("Удалённый доступ", new[] { "удалён", "удален", "remote", "доступ к компьютеру" }, new Pick[] {
            new("RustDesk", "RustDesk.RustDesk", "Бесплатное подключение к другому компьютеру."),
            new("AnyDesk", "AnyDesk.AnyDesk", "Популярная программа удалённого доступа.") }),
        ("Пароли", new[] { "парол", "password" }, new Pick[] {
            new("Bitwarden", "Bitwarden.Bitwarden", "Менеджер паролей с синхронизацией."),
            new("KeePassXC", "KeePassXCTeam.KeePassXC", "Пароли хранятся только у вас, без облака.") }),
        ("Заметки", new[] { "замет", "note", "notion", "obsidian" }, new Pick[] {
            new("Obsidian", "Obsidian.Obsidian", "Заметки и база знаний в обычных файлах."),
            new("Notion", "Notion.Notion", "Заметки, задачи и вики в одном месте.") }),
        ("Полезное для Windows", new[] { "утилит", "powertoys", "полезн" }, new Pick[] {
            new("PowerToys", "Microsoft.PowerToys", "Полезные мелочи для Windows от Microsoft."),
            new("Everything", "voidtools.Everything", "Мгновенный поиск файлов на компьютере.") }),
    };

    /// <summary>Все приложения, которые Merno считает проверенными (каталог + подборки).</summary>
    public static HashSet<string> TrustedIds()
    {
        var set = new HashSet<string>(Topics.SelectMany(t => t.Apps.Select(a => a.Id)), StringComparer.OrdinalIgnoreCase);
        foreach (var a in Load()) if (!string.IsNullOrEmpty(a.winget)) foreach (var id in a.winget.Split(';')) set.Add(id.Trim());
        return set;
    }

    /// <summary>Если запрос — тип программы («браузер»), вернуть подборку Merno.</summary>
    public static (string Title, List<AppItem> Apps) Topic(string q)
    {
        var low = (q ?? "").Trim().ToLowerInvariant();
        foreach (var t in Topics)
            if (t.Words.Any(w => low.Contains(w)))
                return (t.Title, t.Apps.Select(p => new AppItem { name = p.Name, winget = p.Id, desc = p.Desc, trusted = true, match = p.Name.Length >= 4 ? p.Name : null,
                                                                   url = $"https://winstall.app/apps/{p.Id}" }).ToList());
        return (null, null);
    }

    /// <summary>Поиск в официальном каталоге winget. Возвращает до 30 программ.</summary>
    public static List<AppItem> Search(string query)
    {
        var res = new List<AppItem>();
        var q = Regex.Replace(query ?? "", "[\"'`&|<>^%]", " ").Trim();
        if (q.Length < 2 || Banned(q)) return res;
        var (_, outp) = Sh.Run(Exe, $"search \"{q}\" --source winget --accept-source-agreements --disable-interactivity", 90_000);
        // у winget крутилка пишет «\r - \r» — берём текст после последнего \r в каждой строке
        var lines = outp.Replace("\r\n", "\n").Split('\n').Select(l => l.Contains('\r') ? l[(l.LastIndexOf('\r') + 1)..] : l).ToList();
        int dash = lines.FindIndex(l => Regex.IsMatch(l.Trim(), "^-{10,}$"));
        if (dash < 1) return res;
        var cols = Regex.Matches(lines[dash - 1], @"\S+").Select(m => m.Index).ToList();   // Имя | ИД | Версия | …
        if (cols.Count < 3) return res;
        foreach (var l in lines.Skip(dash + 1))
        {
            int cut = CharIndexAtWidth(l, cols[1]);   // winget выравнивает по ширине на экране, а не по числу символов
            if (cut >= l.Length) continue;
            string name = l[..cut].Trim();
            string rest = l[cut..].Trim();
            if (rest.Length == 0) continue;
            var parts = Regex.Split(rest, @"\s+");
            string id = parts[0], ver = parts.Length > 1 ? parts[1] : "";
            if (id.Length < 3 || id.Contains('…') || !id.Contains('.') || Banned(id) || Banned(name)) continue;
            res.Add(new AppItem { name = name.TrimEnd('…'), winget = id, version = ver, url = $"https://winstall.app/apps/{id}" });
        }
        return Rank(res, q);
    }

    /// <summary>Отсекает «хлам», найденный только по тегу, и ставит точные совпадения выше.</summary>
    private static List<AppItem> Rank(List<AppItem> all, string q)
    {
        static string N(string s) => Regex.Replace(s ?? "", @"[\s\.\-_]+", "").ToLowerInvariant();
        string nq = N(q);
        if (nq == "vscode" || nq == "code") nq = "visualstudiocode";   // у пакета Microsoft.VisualStudioCode нет «vscode» в имени/Id
        var junk = new Regex(@"experimental|nightly|unstable|canary|\.dev\b|insider|alpha", RegexOptions.IgnoreCase);
        int Score(AppItem a)
        {
            string n = N(a.name), idName = N(a.winget.Contains('.') ? a.winget[(a.winget.IndexOf('.') + 1)..] : a.winget), id = N(a.winget);
            if (n == nq || idName == nq) return 0;                       // точное совпадение имени
            if (n.StartsWith(nq) || idName.StartsWith(nq)) return 1;
            if (n.Contains(nq) || id.Contains(nq)) return 2;
            return 99;                                                    // совпало только по тегу/описанию — выкидываем
        }
        return all
            .Where(a => !a.name.EnumerateRunes().Any(r => IsWide(r.Value)) && !junk.IsMatch(a.winget) && !junk.IsMatch(a.name))
            .Select(a => (a, sc: Score(a))).Where(x => x.sc < 99)
            .OrderBy(x => x.sc).ThenBy(x => x.a.winget.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(x => x.a.name.Length)
            .Select(x => x.a).Take(15).ToList();
    }

    /// <summary>Домашняя страница пакета из «winget show» — для значка и кнопки «Сайт».</summary>
    public static string Homepage(string id)
    {
        var (_, outp) = Sh.Run(Exe, $"show --id {id} --exact --source winget --accept-source-agreements --disable-interactivity", 30_000);
        string best = null;
        foreach (var raw in outp.Replace("\r\n", "\n").Split('\n'))
        {
            var m = Regex.Match(raw, @"^\s*([^:]+):\s*(https?://\S+)");
            if (!m.Success) continue;
            var key = m.Groups[1].Value.ToLowerInvariant();
            if (key.Contains("homepage") || key.Contains("домашн")) return m.Groups[2].Value;
            if (best == null && (key.Contains("publisher url") || key.Contains("издател"))) best = m.Groups[2].Value;
        }
        return best;
    }
    private static readonly SemaphoreSlim ShowGate = new(3);

    // Индекс символа, с которого начинается экранная колонка col (иероглифы/эмодзи занимают 2 клетки).
    private static int CharIndexAtWidth(string l, int col)
    {
        int w = 0, i = 0;
        while (i < l.Length && w < col)
        {
            int len = char.IsHighSurrogate(l[i]) && i + 1 < l.Length && char.IsLowSurrogate(l[i + 1]) ? 2 : 1;
            int cp = len == 2 ? char.ConvertToUtf32(l[i], l[i + 1]) : l[i];
            w += IsWide(cp) ? 2 : 1;
            i += len;
        }
        return i;
    }
    private static bool IsWide(int c) =>
        c >= 0x1100 && (c <= 0x115F || c == 0x2329 || c == 0x232A || (c >= 0x2E80 && c <= 0xA4CF && c != 0x303F) ||
        (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE4F) ||
        (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6) || (c >= 0x1F300 && c <= 0x1FAFF) || (c >= 0x20000 && c <= 0x3FFFD));

    // ---------- иконки (значок сайта, кэш в C:\ProgramData\Merno\icons) ----------
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
    // установщик Spotify весит больше 150 МБ — короткий таймаут значков здесь не подходит
    private static readonly HttpClient SpotHttp = new() { Timeout = TimeSpan.FromMinutes(20) };
    public static async Task<string> IconAsync(AppItem a)
    {
        if (string.IsNullOrEmpty(a.url)) return null;
        // встроенных значков программ нет: берём из Microsoft Store или с сайта программы
        // и кладём в кэш C:\ProgramData\Merno\icons
        if (a.url.StartsWith("ms-windows-store:")) return await StoreIconAsync(a);
        if (!a.url.StartsWith("http")) return null;
        try
        {
            var dir = Path.Combine(Log.DataDir, "icons"); Directory.CreateDirectory(dir);
            bool fromSearch = a.url.Contains("winstall.app");
            var file = Path.Combine(dir, Regex.Replace(fromSearch ? a.winget : a.name, @"[^\w]+", "_") + ".png");
            if (File.Exists(file) && new FileInfo(file).Length > 100) return file;
            string site = a.url;
            if (fromSearch)
            {
                // для найденных программ узнаём сайт через winget show (не больше 3 одновременно)
                await ShowGate.WaitAsync();
                try { site = await Task.Run(() => Homepage(a.winget)); } finally { ShowGate.Release(); }
                if (string.IsNullOrEmpty(site) || site.Contains("github.com")) return null;   // у GitHub-проектов значок был бы общий
            }
            var bytes = await Http.GetByteArrayAsync("https://www.google.com/s2/favicons?sz=64&domain_url=" + Uri.EscapeDataString(site));
            if (bytes.Length < 100) return null;
            await File.WriteAllBytesAsync(file, bytes);
            return file;
        }
        catch { return null; }
    }
    /// <summary>Значок приложения из каталога Microsoft Store (тот же, что показывает сам Store).</summary>
    private static async Task<string> StoreIconAsync(AppItem a)
    {
        try
        {
            var dir = Path.Combine(Log.DataDir, "icons"); Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "store_" + Regex.Replace(a.winget ?? a.name, @"[^\w]+", "_") + ".png");
            if (File.Exists(file) && new FileInfo(file).Length > 100) return file;
            var json = await Http.GetStringAsync($"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={a.winget}&market=US&languages=en-us");
            using var doc = JsonDocument.Parse(json);
            string uri = null;
            foreach (var img in doc.RootElement.GetProperty("Products")[0].GetProperty("LocalizedProperties")[0].GetProperty("Images").EnumerateArray())
            {
                var purpose = img.GetProperty("ImagePurpose").GetString();
                if (purpose == "Logo" || (uri == null && (purpose == "Tile" || purpose == "BoxArt"))) uri = img.GetProperty("Uri").GetString();
                if (purpose == "Logo") break;
            }
            if (string.IsNullOrEmpty(uri)) return null;
            if (uri.StartsWith("//")) uri = "https:" + uri;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var imageUri) || imageUri.Scheme != Uri.UriSchemeHttps ||
                !StoreImageHost(imageUri.Host)) return null;
            var bytes = await Http.GetByteArrayAsync(uri + (uri.Contains('?') ? "&" : "?") + "w=64&h=64");
            if (bytes.Length < 100) return null;
            await File.WriteAllBytesAsync(file, bytes);
            return file;
        }
        catch { return null; }
    }
    internal static bool StoreImageHost(string host) => new[] { "microsoft.com", "s-microsoft.com", "microsoftstore.com", "xboxlive.com" }
        .Any(domain => string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    /// <summary>Есть ли на компьютере сам Microsoft Store (без него приложения не вернуть).</summary>
    /// <returns>true — есть, false — точно нет, null — не удалось проверить (ошибка или таймаут PowerShell).</returns>
    public static bool? StorePresent()
    {
        var (code, o) = Sh.PS("try { if (Get-AppxPackage -Name Microsoft.WindowsStore -ErrorAction Stop) { 'MERNO_STORE_YES' } else { 'MERNO_STORE_NO' } } catch { 'MERNO_STORE_FAIL' }", 60_000);
        return StoreAnswer(code, o);
    }

    /// <summary>Разбор ответа StorePresent: «нет» только по явному MERNO_STORE_NO, ошибка/таймаут/пустой вывод — null.</summary>
    internal static bool? StoreAnswer(int code, string output)
    {
        if (code != 0) return null;
        var last = (output ?? "").Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0);
        return last == "MERNO_STORE_YES" ? true : last == "MERNO_STORE_NO" ? false : null;
    }

    /// <summary>Имена пакетов из Get-AppxPackage. ok = false, если нет маркера успеха последней строкой или имён ноль
    /// (на живой Windows пакетов всегда много): тогда пустой список значит «не удалось узнать», а не «приложений нет».</summary>
    internal static HashSet<string> AppxNames(int code, string output, out bool ok)
    {
        var lines = (output ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        var names = new HashSet<string>(lines.Where(x => !x.StartsWith("MERNO_APPX_")), StringComparer.OrdinalIgnoreCase);
        ok = code == 0 && lines.Count > 0 && lines[^1] == "MERNO_APPX_OK" && names.Count > 0;
        return names;
    }

    /// <summary>Переустановить Microsoft Store штатной командой Windows.</summary>
    public static bool InstallStore()
    {
        Log.Write("Устанавливаю Microsoft Store…"
);
        Sh.Run(Sh.Sys32("wsreset.exe"), "-i", 600_000);
        // wsreset -i ставит Store в фоне — ждём до 3 минут
        for (int i = 0; i < 36; i++) { if (StorePresent() == true) return true; Thread.Sleep(5000); }
        return false;
    }
}
