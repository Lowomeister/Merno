// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace Merno.Core;

public class InstalledApp
{
    public string Name = "", Publisher = "", Version = "", Size = "", UninstallCmd = "", Source = "";
    /// <summary>Полное имя пакета для приложений Windows; null — обычная программа.</summary>
    public string AppxName;
    public string RegistryPath;
    public RegistryHive RegistryHive;
    public RegistryView RegistryView;
    /// <summary>Откуда брать значок: exe/ico с номером или png-логотип пакета.</summary>
    public string Icon;
    /// <summary>Имя пакета без версии (Microsoft.BingNews) — только для приложений Windows.</summary>
    public string Pkg;
    /// <summary>Дата установки из реестра (InstallDate, yyyyMMdd); у приложений Windows — нет.</summary>
    public DateTime? InstallDate;
    public long Bytes;
    public bool System;   // защищено от удаления
    /// <summary>Запись из HKCU: её может написать любая программа пользователя — программу удаления запускаем без прав администратора.</summary>
    public bool PerUser;
    /// <summary>Папка программы (InstallLocation или папка её файла удаления/значка) — по ней считаем настоящий размер.</summary>
    public string Dir;
}

/// <summary>Встроенное приложение Windows, которое можно удалить.</summary>
public class BuiltinApp
{
    public string[] Pkgs = Array.Empty<string>();
    public string Name, Desc;
    /// <summary>Удаляется готовой настройкой из Tweaks.cs (Edge, OneDrive) — только в расширенном режиме.</summary>
    public string TweakId;
    /// <summary>Сильное предупреждение (красным) перед удалением.</summary>
    public string Danger;
}

/// <summary>Список установленных программ и их удаление.</summary>
public static class Uninstall
{
    // Без этого Windows или сам Merno работают плохо — удалять не даём
    static readonly string[] Keep =
    {
        "Microsoft.WindowsStore", "Microsoft.DesktopAppInstaller", "Microsoft.VCLibs", "Microsoft.UI.Xaml", "Microsoft.NET", "Microsoft.Services.Store",
        "Microsoft.SecHealthUI", "Microsoft.Windows.ShellExperienceHost", "Microsoft.Windows.StartMenuExperienceHost", "MicrosoftWindows.Client",
        "Microsoft.WindowsAppRuntime", "Microsoft.WebpImageExtension", "Microsoft.HEIFImageExtension", "Microsoft.Winget", "Microsoft.StorePurchaseApp",
        "Microsoft.AAD.BrokerPlugin", "Microsoft.AccountsControl", "Microsoft.LockApp", "Microsoft.Windows.CloudExperienceHost", "Microsoft.Windows.Search",
        "windows.immersivecontrolpanel", "Microsoft.WindowsCalculator", "Microsoft.WindowsTerminal", "Microsoft.EdgeWebView2Runtime"
    };
    static bool IsKept(string s) => Keep.Any(k => s.StartsWith(k, StringComparison.OrdinalIgnoreCase));

    // Среды выполнения и драйверы — без них перестают работать игры и программы
    static readonly string[] KeepDesktop =
    {
        "Microsoft Visual C++", "Microsoft .NET", "Microsoft Windows Desktop Runtime", "Microsoft ASP.NET",
        "NVIDIA Graphics Driver", "NVIDIA PhysX", "Realtek", "Intel(R) Chipset", "AMD Chipset", "AMD Software"
    };
    static bool IsKeptDesktop(string s) => KeepDesktop.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase));

    public static string Size(long b) => b <= 0 ? "" : b >= 1L << 30 ? $"{b / (double)(1L << 30):0.#} ГБ" : $"{Math.Max(1, b >> 20)} МБ";

    // ---------- настоящий размер ----------
    // Размер в «Программах и компонентах» (EstimatedSize) пишет установщик, и часто он сильно меньше правды:
    // iRacing — «2 ГБ» при 50+ ГБ, Unity — без докачанных модулей. Поэтому считаем саму папку программы.

    /// <summary>Папка программы: InstallLocation, иначе папка значка или файла удаления. null — не нашли или она общая (опасно считать).</summary>
    static string AppDir(string loc, string icon, string uninstall)
    {
        foreach (var raw in new[] { loc, ExeDir(icon), ExeDir(uninstall) })
        {
            try
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var d = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'))).TrimEnd('\\');
                if (SafeFs.IsLocal(d) && Directory.Exists(d) && !SharedDir(d)) return d;   // путь из реестра: по сети не считаем
            }
            catch { }
        }
        return null;
    }

    static string ExeDir(string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd) || cmd.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) return null;
        var s = cmd.Trim();
        string p;
        if (s.StartsWith('"')) { int q = s.IndexOf('"', 1); if (q < 0) return null; p = s[1..q]; }
        else { int x = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); if (x < 0) return null; p = s[..(x + 4)]; }
        p = p.Split(',')[0];
        return p.Length > 3 && p[1] == ':' ? Path.GetDirectoryName(p) : null;
    }

    /// <summary>Общие папки: корень диска, Program Files, Windows, профиль, кэши установщиков — их размер не программы.</summary>
    static bool SharedDir(string d)
    {
        if (d.Length <= 3) return true;
        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };
        foreach (var r in roots)
            if (!string.IsNullOrEmpty(r) && string.Equals(d, r.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(win) && d.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        // кэши установщиков и временные папки — по целому имени папки, чтобы не задеть «Tempest» и т. п.
        return d.Split('\\').Any(p => p.Equals("Package Cache", StringComparison.OrdinalIgnoreCase) || p.Equals("Installer", StringComparison.OrdinalIgnoreCase)
                                   || p.Equals("Temp", StringComparison.OrdinalIgnoreCase) || p.Equals("Tmp", StringComparison.OrdinalIgnoreCase));
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Bytes, DateTime At)> _dirCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Забыть посчитанные размеры — «Обновить список» и каждое открытие «Удалить» считают заново
    /// (пользователь почистил кэш программы и ждёт новую цифру).</summary>
    public static void ClearSizeCache() => _dirCache.Clear();

    /// <summary>Сколько на самом деле весит папка программы (ссылки на другие папки не учитываем). Кэш — 10 минут.</summary>
    public static long DirBytes(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return 0;
        if (_dirCache.TryGetValue(dir, out var c) && DateTime.Now - c.At < TimeSpan.FromMinutes(10)) return c.Bytes;
        long sum = 0;
        try
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", opts)) { try { sum += f.Length; } catch { } }
        }
        catch { }
        _dirCache[dir] = (sum, DateTime.Now);
        return sum;
    }

    public static List<InstalledApp> List()
    {
        var res = new List<InstalledApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 1. Обычные программы — как «Программы и компоненты»
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
                    using var k = un.OpenSubKey(sub);
                    if (k == null) continue;
                    var name = k.GetValue("DisplayName") as string;
                    var cmd = (k.GetValue("UninstallString") as string) ?? "";
                    if (string.IsNullOrWhiteSpace(name) || cmd.Length == 0) continue;
                    if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    if (k.GetValue("ParentKeyName") != null || k.GetValue("ReleaseType") is string rt && rt.Contains("Update")) continue;
                    if (!seen.Add(name.Trim())) continue;
                    long bytes = k.GetValue("EstimatedSize") is int kb ? kb * 1024L : 0;
                    DateTime? date = DateTime.TryParseExact((k.GetValue("InstallDate") as string ?? "").Trim(), "yyyyMMdd",
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt) ? dt : null;
                    // некоторые установщики (Squirrel: Discord и др.) пишут дату с ошибкой, и она выходит в будущем: такую не показываем
                    if (date > DateTime.Today.AddDays(1)) date = null;
                    res.Add(new InstalledApp
                    {
                        RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + sub, RegistryHive = hive, RegistryView = view,
                        Name = name.Trim(), Publisher = (k.GetValue("Publisher") as string ?? "").Trim(), Version = k.GetValue("DisplayVersion") as string ?? "",
                        UninstallCmd = cmd, Bytes = bytes, Size = Size(bytes), Source = "Программа", InstallDate = date,
                        Icon = (k.GetValue("DisplayIcon") as string) is string di && di.Length > 0 ? di
                             : cmd.Contains("MsiExec", StringComparison.OrdinalIgnoreCase) ? null : cmd,
                        System = name.StartsWith("Merno", StringComparison.OrdinalIgnoreCase) || IsKept(name) || IsKeptDesktop(name),
                        PerUser = hive == RegistryHive.CurrentUser,
                        Dir = AppDir(k.GetValue("InstallLocation") as string, k.GetValue("DisplayIcon") as string, cmd)
                    });
                }
            }
            catch { }
        }
        // 2. Приложения Windows (Store)
        try
        {
            var (_, o) = Sh.PS(@"Get-AppxPackage | Where-Object { -not $_.IsFramework -and $_.SignatureKind -ne 'System' -and -not $_.NonRemovable } |
  ForEach-Object { $logo = ''
    try { $m = Join-Path $_.InstallLocation 'AppxManifest.xml'; if (Test-Path -LiteralPath $m) { $logo = [string]([xml][IO.File]::ReadAllText($m)).Package.Properties.Logo } } catch { }
    [pscustomobject]@{ Name = $_.Name; PackageFullName = $_.PackageFullName; Publisher = $_.Publisher; Version = [string]$_.Version; Dir = $_.InstallLocation; Logo = $logo } } |
  ConvertTo-Json -Compress", 180_000);
            o = o.Trim();
            if (o.StartsWith("[") || o.StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(o);
                var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
                foreach (var e in arr)
                {
                    string g(string p) => e.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
                    var n = g("Name");
                    // имя пакета SpotifyAB.SpotifyMusic → SpotifyMusic; у Microsoft.* — без префикса
                    var nice = NiceAppxName(n);
                    if (!seen.Add("appx:" + n)) continue;
                    var pub = g("Publisher"); var cn = Drivers.NiceVendor(System.Text.RegularExpressions.Regex.Match(pub, "CN=([^,]+)").Groups[1].Value.Trim('"'));
                    if (System.Text.RegularExpressions.Regex.IsMatch(pub, "CN=Microsoft", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) cn = "Microsoft";
                    else if (System.Text.RegularExpressions.Regex.IsMatch(cn, @"^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-|^[0-9A-Fa-f]{20,}$")) cn = "";
                    res.Add(new InstalledApp { Name = nice, Publisher = cn, Version = "", AppxName = g("PackageFullName"), Pkg = n, Source = "Приложение Windows", System = IsKept(n),
                                              Icon = FileIcons.AppxLogo(g("Dir"), g("Logo")) });
                }
            }
        }
        catch { }
        return res.OrderBy(a => a.System).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // Встроенные приложения, которые можно удалить без вреда для Windows. Только те, что есть в этом списке;
    // Магазин, App Installer, Калькулятор, Фотографии, Терминал, Блокнот, Ножницы, Параметры, Безопасность и фреймворки сюда не входят никогда.
    public static readonly BuiltinApp[] Builtin =
    {
        new() { Pkgs = new[] { "Microsoft.549981C3F5F10" }, Name = "Cortana", Desc = "Старый голосовой помощник. В России почти не работает." },
        new() { Pkgs = new[] { "Microsoft.Copilot", "Microsoft.Windows.Ai.Copilot.Provider" }, Name = "Copilot", Desc = "Чат-помощник с ИИ от Microsoft." },
        new() { Pkgs = new[] { "Microsoft.GamingApp", "Microsoft.XboxApp" }, Name = "Xbox", Desc = "Без него не запустятся игры из Game Pass." },
        new() { Pkgs = new[] { "Microsoft.XboxGamingOverlay", "Microsoft.XboxGameOverlay" }, Name = "Xbox Game Bar", Desc = "Панель по Win+G: запись экрана и счётчик кадров." },
        new() { Pkgs = new[] { "Microsoft.XboxSpeechToTextOverlay" }, Name = "Xbox: голос в текст", Desc = "Субтитры голосового чата в играх Xbox." },
        new() { Pkgs = new[] { "Microsoft.BingNews" }, Name = "Новости", Desc = "Лента новостей Microsoft Start." },
        new() { Pkgs = new[] { "Microsoft.BingWeather" }, Name = "Погода", Desc = "Прогноз погоды от MSN." },
        new() { Pkgs = new[] { "Microsoft.BingSearch" }, Name = "Поиск Bing", Desc = "Поиск Bing в меню «Пуск» и на панели задач." },
        new() { Pkgs = new[] { "Microsoft.Getstarted" }, Name = "Советы", Desc = "Подсказки «Начало работы» с Windows." },
        new() { Pkgs = new[] { "Microsoft.WindowsFeedbackHub" }, Name = "Центр отзывов", Desc = "Отправка отзывов и ошибок в Microsoft." },
        new() { Pkgs = new[] { "Microsoft.MixedReality.Portal" }, Name = "Портал смешанной реальности", Desc = "Нужен только для очков виртуальной реальности." },
        new() { Pkgs = new[] { "Microsoft.People" }, Name = "Люди", Desc = "Старая адресная книга Windows." },
        new() { Pkgs = new[] { "Microsoft.WindowsMaps" }, Name = "Карты", Desc = "Карты Microsoft (Bing)." },
        new() { Pkgs = new[] { "Microsoft.MicrosoftSolitaireCollection" }, Name = "Пасьянсы", Desc = "Солитер, Паук и другие пасьянсы с рекламой." },
        new() { Pkgs = new[] { "Clipchamp.Clipchamp" }, Name = "Clipchamp", Desc = "Онлайн-видеоредактор Microsoft." },
        new() { Pkgs = new[] { "microsoft.windowscommunicationsapps" }, Name = "Почта и Календарь", Desc = "Старые приложения «Почта» и «Календарь»." },
        new() { Pkgs = new[] { "Microsoft.SkypeApp" }, Name = "Skype", Desc = "Мессенджер Skype (Microsoft его закрыла)." },
        new() { Pkgs = new[] { "Microsoft.YourPhone" }, Name = "Связь с телефоном", Desc = "Уведомления и звонки с телефона на компьютере." },
        new() { Pkgs = new[] { "Microsoft.Microsoft3DViewer" }, Name = "Средство 3D-просмотра", Desc = "Просмотр 3D-моделей." },
        new() { Pkgs = new[] { "Microsoft.MSPaint" }, Name = "Paint 3D", Desc = "Редактор 3D-рисунков. Обычный Paint остаётся." },
        new() { Pkgs = new[] { "Microsoft.Office.OneNote" }, Name = "OneNote для Windows 10", Desc = "Старая версия заметок OneNote." },
        new() { Pkgs = new[] { "MicrosoftTeams", "MSTeams" }, Name = "Teams", Desc = "Чат и звонки Microsoft Teams." },
        new() { Pkgs = new[] { "Microsoft.OutlookForWindows" }, Name = "Outlook (новый)", Desc = "Почта Microsoft. Если пользуетесь почтой в браузере или другой программе, это приложение не нужно." },
        new() { Pkgs = new[] { "Microsoft.Todos" }, Name = "Microsoft To Do", Desc = "Списки дел Microsoft." },
        new() { Pkgs = new[] { "Microsoft.MicrosoftStickyNotes" }, Name = "Записки", Desc = "Заметки-стикеры на рабочем столе." },
        new() { Pkgs = new[] { "Microsoft.WindowsAlarms" }, Name = "Часы", Desc = "Будильник, таймер и секундомер." },
        new() { Pkgs = new[] { "Microsoft.WindowsSoundRecorder" }, Name = "Звукозапись", Desc = "Запись звука с микрофона." },
        new() { Pkgs = new[] { "MicrosoftCorporationII.QuickAssist" }, Name = "Быстрая помощь", Desc = "Удалённая помощь через интернет. Ею часто пользуются мошенники." },
        new() { Pkgs = new[] { "Microsoft.GetHelp" }, Name = "Техническая поддержка", Desc = "Чат поддержки Microsoft." },
        new() { Pkgs = new[] { "MicrosoftCorporationII.MicrosoftFamily" }, Name = "Семья Microsoft", Desc = "Родительский контроль через учётную запись Microsoft." },
        new() { Pkgs = new[] { "Microsoft.Windows.DevHome" }, Name = "Dev Home", Desc = "Панель для программистов." },
        new() { Pkgs = new[] { "Microsoft.MicrosoftJournal" }, Name = "Журнал Microsoft", Desc = "Рукописные заметки для пера." },
        new() { Pkgs = new[] { "Microsoft.BingFinance", "Microsoft.BingSports" }, Name = "Финансы и Спорт", Desc = "Старые приложения MSN." },
        new() { Pkgs = new[] { "Microsoft.Wallet" }, Name = "Кошелёк Microsoft", Desc = "Платежи Microsoft. В России не работает." },
        new() { Pkgs = new[] { "Microsoft.ZuneMusic" }, Name = "Groove / Медиаплеер", Desc = "Музыкальный плеер Microsoft. Видео и музыку можно открывать другим плеером." },
        new() { Pkgs = new[] { "Microsoft.ZuneVideo" }, Name = "Кино и ТВ", Desc = "Старый видеоплеер Microsoft." },
        new() { Pkgs = new[] { "Microsoft.MicrosoftOfficeHub" }, Name = "Microsoft 365 (Office)", Desc = "Реклама подписки Office. Сам Office останется." },
        new() { Pkgs = new[] { "Microsoft.PowerAutomateDesktop" }, Name = "Power Automate", Desc = "Автоматизация для опытных. Обычно не нужна." },
        new() { TweakId = "edge_remove", Name = "Microsoft Edge", Desc = "Браузер Microsoft. Пароли и закладки заранее перенесите в новый браузер.",
                Danger = "Могут перестать работать виджеты, новый Outlook и Teams. Сначала поставьте другой браузер. Вернуть Edge можно с сайта microsoft.com/edge." },
        new() { TweakId = "onedrive", Name = "OneDrive", Desc = "Облачное хранилище Microsoft.",
                Danger = "Файлы, которые лежат только в облаке, станут недоступны с этого компьютера. Сначала скачайте нужные." },
    };

    /// <summary>Имя пакета → название для человека: из списка встроенных, иначе «WindowsTerminal» → «Windows Terminal».</summary>
    public static string NiceAppxName(string pkg)
    {
        var b = Builtin.FirstOrDefault(x => x.Pkgs.Any(p => p.Equals(pkg, StringComparison.OrdinalIgnoreCase)));
        if (b != null) return b.Name;
        var s = pkg.Contains('.') ? pkg[(pkg.IndexOf('.') + 1)..].Replace('.', ' ') : pkg;
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", " ");
        return s.Trim();
    }

    /// <summary>Установленные пакеты встроенного приложения (по списку из List()).</summary>
    public static List<InstalledApp> PackagesOf(BuiltinApp b, IEnumerable<InstalledApp> installed) =>
        installed.Where(a => a.AppxName != null && a.Pkg != null && !a.System && !IsKept(a.Pkg)
                             && b.Pkgs.Any(p => a.Pkg.Equals(p, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>Удаляет встроенное приложение для текущего пользователя (все его пакеты).</summary>
    public static (bool Ok, string Error) RemoveBuiltin(BuiltinApp b, IEnumerable<InstalledApp> installed)
    {
        var pk = PackagesOf(b, installed);
        if (pk.Count == 0) return (false, "Приложение уже удалено.");
        string err = null;
        foreach (var a in pk) { var r = Remove(a); if (!r.Ok) err ??= r.Error; }
        return err == null ? (true, null) : (false, err);
    }

    /// <summary>Запущенные процессы, которые мешают удалить программу. Пусто — программа не запущена.</summary>
    public static List<Process> RunningFor(string name, string id = null, string match = null)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string s)
        {
            s = Norm(s);
            if (s.Length >= 4 && !GenericProcess.Contains(s)) tokens.Add(s);
        }
        Add(name);
        if (!string.IsNullOrEmpty(id))
            foreach (var part in id.Split(';', StringSplitOptions.RemoveEmptyEntries))
                Add(part.Contains('.') ? part[(part.LastIndexOf('.') + 1)..] : part);
        if (!string.IsNullOrEmpty(match) && !match.Contains(':'))
            foreach (var part in match.Split('|', StringSplitOptions.RemoveEmptyEntries)) Add(part);
        if (tokens.Count == 0) return new();

        // у известных программ имя процесса не совпадает с названием
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tokens)
            if (ProcessAliasesNorm.TryGetValue(t, out var alias))
                foreach (var a in alias.Split('|', StringSplitOptions.RemoveEmptyEntries)) aliases.Add(Norm(a));

        var res = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId) continue;
                var n = Norm(p.ProcessName);
                if (n.Length < 3) continue;
                bool hit = aliases.Contains(n) || tokens.Any(t => n == t);
                if (hit) res.Add(p); else p.Dispose();
            }
            catch { try { p.Dispose(); } catch { } }
        }
        return res.OrderBy(p => { try { return p.ProcessName; } catch { return ""; } }, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Закрывает процессы перед удалением. true — всё закрыто.</summary>
    public static bool CloseAll(IEnumerable<Process> processes)
    {
        bool ok = true;
        foreach (var p in processes ?? Enumerable.Empty<Process>())
        {
            try
            {
                if (p.HasExited) continue;
                // Есть окно: просим закрыться и ждём. Не закрылась (например, спрашивает «Сохранить изменения?»):
                // не убиваем, иначе пропадёт несохранённое; человек закроет сам.
                if (p.CloseMainWindow()) { if (!p.WaitForExit(10_000)) ok = false; continue; }
                // Окна нет (программа в трее, например Everything): человек уже согласился «Закрыть и продолжить», завершаем.
                // Только сам процесс, без дочерних: из программы в трее могут быть запущены игры и документы.
                p.Kill();
                if (!p.WaitForExit(5000)) ok = false;
            }
            catch (Exception ex)
            {
                bool gone = false;
                try { gone = p.HasExited; } catch { }
                if (!gone) { ok = false; Log.Warn("Не удалось закрыть программу перед удалением: " + ex.Message); }
            }
            finally { try { p.Dispose(); } catch { } }
        }
        return ok;
    }

    static string Norm(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c == '+') sb.Append("plus");   // Notepad++ и StartIsBack++ не должны совпадать с обычным notepad
        }
        return sb.ToString();
    }

    static readonly HashSet<string> GenericProcess = new(StringComparer.OrdinalIgnoreCase)
    { "app", "apps", "code", "desktop", "setup", "update", "updater", "runtime", "service", "services", "client", "microsoft", "windows", "program", "tools", "driver", "drivers", "java" };

    // название программы (без пробелов и знаков) → имя процесса (через «|» — несколько вариантов)
    static readonly Dictionary<string, string> ProcessAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["visualstudiocode"] = "Code",
        ["sublimetext"] = "sublime_text",
        ["notepad++"] = "notepad++",
        ["telegramdesktop"] = "Telegram",
        ["googlechrome"] = "chrome",
        ["mozillafirefox"] = "firefox",
        ["epicgameslauncher"] = "EpicGamesLauncher",
        ["eaapp"] = "EADesktop",
        ["battle.net"] = "Battle.net",
        ["ubisoftconnect"] = "UbisoftConnect|upc",
        ["vlcmediaplayer"] = "vlc",
        ["7-zip"] = "7zFM|7zG",
        ["dockerdesktop"] = "Docker Desktop",
        ["microsoftteams"] = "ms-teams|Teams",
        ["startallback"] = "StartAllBack",
        ["startisback++"] = "StartIsBack",
        ["spotify"] = "Spotify",
        ["steam"] = "steam",
        ["discord"] = "Discord",
        ["postman"] = "Postman",
        ["dbeaver"] = "dbeaver",
    };

    // ключи сравниваем в том же виде, что и имена процессов («Notepad++» → «notepadplusplus»)
    static readonly Dictionary<string, string> ProcessAliasesNorm =
        ProcessAliases.ToDictionary(kv => Norm(kv.Key), kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Запущенные процессы программы из каталога Merno.</summary>
    public static List<Process> RunningFor(AppItem a) => a == null ? new() : RunningFor(a.name, a.winget, a.match);

    /// <summary>Запущенные процессы установленной программы.</summary>
    public static List<Process> RunningFor(InstalledApp a) => a == null ? new() : RunningFor(a.Name, a.Pkg, null);

    /// <summary>Идёт ли ещё программа удаления, запущенная после started: копии Inno (unins*), NSIS (Au_, Un_*), InstallShield (_iu14D2N).</summary>
    static bool UninstallerRunning(DateTime started)
    {
        foreach (var pr in Process.GetProcesses())
        {
            try
            {
                var n = pr.ProcessName;
                bool like = n.StartsWith("unins", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Au_", StringComparison.OrdinalIgnoreCase)
                         || n.StartsWith("Un_", StringComparison.OrdinalIgnoreCase) || n.Equals("_iu14D2N", StringComparison.OrdinalIgnoreCase)
                         || n.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase);
                if (like && pr.StartTime >= started.AddSeconds(-2)) return true;
            }
            catch { }
            finally { pr.Dispose(); }
        }
        return false;
    }

    /// <summary>
    /// Портативная программа winget (OCCT и т. п.): сначала winget (по полному пути — у программы с правами администратора
    /// короткое имя «winget» может не находиться), потом добиваем сами: процессы из её папки, ярлыки в WinGet\Links,
    /// саму папку пакета и запись в «Программах»: winget может ответить «готово», а программа останется.
    /// </summary>
    static (bool Ok, string Error) RemovePortable(string name, string productCode, bool perUser)
    {
        // «.» или «..» вместо кода вывели бы удаление на папку выше (вся WinGet\Packages или WinGet)
        if (!System.Text.RegularExpressions.Regex.IsMatch(productCode ?? "", @"^[A-Za-z0-9][\w.\-]*$") || productCode.Contains(".."))
            return (false, $"Не удалось удалить {name}: странная запись в списке программ. Удалите её через «Параметры» → «Приложения».");
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string packages = Path.Combine(local, @"Microsoft\WinGet\Packages");
        string pkg = Path.Combine(packages, productCode);
        string links = Path.Combine(local, @"Microsoft\WinGet\Links");
        // запись из HKCU может написать любая программа пользователя: winget (от администратора) запускаем только
        // для пакета, который правда лежит в профиле, и только в области пользователя — иначе так удалили бы чужую программу
        if (perUser && !Directory.Exists(pkg))
            return (false, $"Не удалось удалить {name}: странная запись в списке программ. Удалите её через «Параметры» → «Приложения».");
        try { if (Apps.WingetAvailable()) Sh.Run(Apps.Exe, $"uninstall --product-code {productCode}{(perUser ? " --scope user" : "")} --silent --accept-source-agreements --disable-interactivity", 600_000); }
        catch (Exception ex) { Log.Skip($"{name}: ошибка winget ({ex.Message})"); }
        try
        {
            if (Directory.Exists(pkg))
            {
                // запущенные из этой папки программы закрываем — иначе файлы не удалить
                foreach (var pr in Process.GetProcesses())
                {
                    try { if (pr.MainModule?.FileName?.StartsWith(pkg + "\\", StringComparison.OrdinalIgnoreCase) == true) { pr.Kill(true); pr.WaitForExit(5000); } }
                    catch { }
                    finally { pr.Dispose(); }
                }
                // папки в профиле доступны пользователю — удаляем через SafeFs (защита от подмены ссылкой)
                string linksRoot = Directory.Exists(links) ? SafeFs.RootFinal(links) : null;
                if (linksRoot != null)
                    foreach (var l in new DirectoryInfo(links).EnumerateFiles())
                        try { if (l.LinkTarget?.StartsWith(pkg + "\\", StringComparison.OrdinalIgnoreCase) == true) SafeFs.DeleteUnder(l.FullName, linksRoot); } catch { }
                string pkgRoot = SafeFs.RootFinal(packages);
                if (pkgRoot != null) SafeFs.DeleteTree(pkg, pkgRoot, true);
            }
            using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true))
                k?.DeleteSubKeyTree(productCode, false);
        }
        catch (Exception ex) { Log.Warn($"{name}: {ex.Message}"); }
        if (Directory.Exists(pkg)) return (false, $"Не удалось удалить {name}: файлы заняты. Закройте программу и повторите.");
        Log.Ok($"Удалено: {name}");
        return (true, null);
    }

    /// <summary>Системная программа, которая выполняет чужие команды (powershell, cmd, rundll32…), или любой файл из папки Windows.
    /// Такие файлы подписаны Microsoft, поэтому проверка подписи их не отсекает.</summary>
    internal static bool IsHostExe(string exe)
    {
        try
        {
            string full = Path.GetFullPath(exe);
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
            if (full.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return true;
            string n = Path.GetFileNameWithoutExtension(full).ToLowerInvariant();
            return n is "powershell" or "pwsh" or "cmd" or "rundll32" or "mshta" or "wscript" or "cscript" or "regsvr32"
                     or "msiexec" or "conhost" or "explorer" or "schtasks" or "reg" or "wmic" or "bitsadmin" or "certutil" or "msbuild" or "installutil";
        }
        catch { return true; }
    }

    /// <summary>Итог удаления по тому, осталась ли программа: present — true (осталась), false (удалена), null (проверить не удалось).
    /// Успехом считается только «удалена».</summary>
    internal static (bool Ok, string Error) ConfirmRemoval(bool? present) => present == false ? (true, null) :
        (false, present == true ? "Программа осталась установленной. Удаление отменено или не завершилось." : "Не удалось подтвердить удаление. Проверьте список приложений Windows.");
    private static bool? StillInstalled(InstalledApp app)
    {
        try
        {
            if (app.AppxName != null)
            {
                var result = Sh.PS($"$ErrorActionPreference='Stop'; if(Get-AppxPackage -Name '{Sh.Esc(app.Pkg)}' -ErrorAction Stop) {{'PRESENT'}} else {{'ABSENT'}}", 30_000);
                return result.Code != 0 ? null : result.Output.Trim() switch { "PRESENT" => true, "ABSENT" => false, _ => null };
            }
            if (app.RegistryPath == null) return null;
            using var root = RegistryKey.OpenBaseKey(app.RegistryHive, app.RegistryView);
            using var entry = root.OpenSubKey(app.RegistryPath);
            return entry != null;
        }
        catch { return null; }
    }

    /// <summary>Запускает удаление. Для обычных программ откроется их собственная программа удаления.</summary>
    public static (bool Ok, string Error) Remove(InstalledApp a)
    {
        if (a.System) return (false, "Эта программа нужна Windows, играм или Merno. Удалять её нельзя.");
        Log.Write($"Удаляю: {a.Name}");
        try
        {
            if (a.AppxName != null)
            {
                var (_, o) = Sh.PS($"try {{ Remove-AppxPackage -Package '{Sh.Esc(a.AppxName)}' -ErrorAction Stop; 'OK' }} catch {{ 'ERR:' + $_.Exception.Message }}", 300_000);
                if (o.Contains("ERR:"))
                {
                    // текст ошибки Windows бывает на английском: в журнал, человеку — понятное сообщение
                    Log.Warn($"{a.Name}: {o[(o.IndexOf("ERR:") + 4)..].Trim()}");
                    return (false, "Windows не дала удалить приложение. Попробуйте через «Параметры» → «Приложения».");
                }
                return ConfirmRemoval(StillInstalled(a));
            }
            string rawCommand = (a.UninstallCmd ?? "").Trim();
            var cmd = a.PerUser ? Environment.ExpandEnvironmentVariables(rawCommand) : SystemVariables.Expand(rawCommand);
            if (cmd.Length == 0) return (false, "У программы нет команды удаления. Удалите её через «Параметры» → «Приложения».");
            // «портативные» программы winget (например, OCCT): вместо программы удаления — команда winget
            var pc = System.Text.RegularExpressions.Regex.Match(cmd, @"^winget(\.exe)?\s+uninstall\s+--product-code\s+""?([\w.\-]+)""?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (pc.Success) return RemovePortable(a.Name, pc.Groups[2].Value, a.PerUser);
            // MSI: «/I{GUID}» в строке удаления означает «изменить» — меняем на удаление
            if (cmd.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase))
                cmd = System.Text.RegularExpressions.Regex.Replace(cmd, @"/I(\{)", "/X$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string exe, args;
            if (cmd.StartsWith("\"")) { int e = cmd.IndexOf('"', 1); exe = cmd[1..e]; args = cmd[(e + 1)..].Trim(); }
            else
            {
                int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (i > 0) { exe = cmd[..(i + 4)]; args = cmd[(i + 4)..].Trim(); }
                else { var sp = cmd.IndexOf(' '); exe = sp > 0 ? cmd[..sp] : cmd; args = sp > 0 ? cmd[(sp + 1)..] : ""; }
            }
            exe = Sh.Resolve(exe);   // «MsiExec.exe» без пути → System32, а не файл с таким именем рядом с Merno
            // файла удаления нет на месте (программу уже стёрли вручную) или путь относительный: Sh.Resolve даёт null
            if (string.IsNullOrEmpty(exe))
            {
                Log.Warn($"{a.Name}: файл удаления не найден ({CrashReport.Sanitize(cmd, 200)})");
                return (false, "Не найден файл удаления программы. Удалите её через «Параметры» → «Приложения».");
            }
            var started = DateTime.Now;
            if (a.PerUser)
            {
                // запись из HKCU — запускаем с обычными правами пользователя (как это сделал бы Windows), не от администратора
                if (!Unelevated.RunAndWait(exe, args, 30 * 60_000, out int err))
                {
                    Log.Warn($"{a.Name}: запуск без повышения не удался ({err}).");
                    return (false, "Не удалось запустить удаление. Удалите программу через «Параметры» → «Приложения».");
                }
            }
            else
            {
                using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                if (p == null || !p.WaitForExit(60 * 60_000)) return (false, "Окно удаления открыто уже больше часа. Закройте его и попробуйте ещё раз.");
            }
            // Программы удаления Inno Setup (unins000) и NSIS копируют себя во временную папку и сразу завершаются —
            // ждём и эту копию, иначе при удалении нескольких программ окна открываются все разом (MSI — ошибка 1618)
            var deadline = started.AddMinutes(30);
            while (DateTime.Now < deadline && UninstallerRunning(started)) Thread.Sleep(1000);
            Log.Write($"Окно удаления «{a.Name}» закрыто");
            return ConfirmRemoval(StillInstalled(a));
        }
        catch (Exception ex)
        {
            // текст исключения бывает на английском: в журнал, человеку — понятное сообщение
            Log.Warn($"{a.Name}: {ex.Message}");
            return (false, "Не удалось запустить удаление. Удалите программу через «Параметры» → «Приложения».");
        }
    }
}
