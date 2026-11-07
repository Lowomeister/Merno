// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Microsoft.Win32;

namespace Merno.Core;

public record RegOp(string Hive, string Path, string Name, object Value, RegistryValueKind Kind = RegistryValueKind.DWord)
{
    public Func<SysInfo, bool> Cond { get; init; }
    public bool Applicable(SysInfo s) => Cond == null || s != null && Cond(s);
    public Func<SysInfo, object> ValueFor { get; init; }
    public object ExpectedValue(SysInfo s) => ValueFor == null ? Value : ValueFor(s);
    public int ClearBits { get; init; }
    internal static string ClearFlags(object current, int bits)
    {
        if (!int.TryParse(current?.ToString(), out int flags) || flags < 0) throw new IOException("Не удалось прочитать текущие параметры специальных возможностей.");
        return (flags & ~bits).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

public class Ctx
{
    public SysInfo S;
    public HashSet<string> Appx = new(StringComparer.OrdinalIgnoreCase);
    public bool AppxKnown;
    public bool DefenderDisabled;

    public void RefreshAppx()
    {
        var (appxCode, o) = Sh.PS("Get-AppxPackage -AllUsers -ErrorAction Stop | ForEach-Object { $_.Name }", 120_000);
        AppxKnown = appxCode == 0;
        Appx = new HashSet<string>(o.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
        var (code, status) = Sh.PS("$s=Get-MpComputerStatus -ErrorAction Stop; \"$($s.RealTimeProtectionEnabled)|$($s.IsTamperProtected)\"", 30_000);
        DefenderDisabled = code == 0 && DefenderIsDisabled(status);
    }
    internal static bool DefenderIsDisabled(string status) => string.Equals(status?.Trim(), "False|False", StringComparison.OrdinalIgnoreCase);
    public bool HasAppx(params string[] names) => !AppxKnown || names.Any(n => Appx.Contains(n));
}

public class Tweak
{
    public string Id, Cat, Name, Desc, Hint;
    public bool Rec, Game, Min, Reboot;
    /// <summary>false — твик удаляет приложения, автоматически вернуть нельзя.</summary>
    public bool CanRevert = true;
    public int Risk; // 0 — безопасно, 1 — осторожно, 2 — риск
    public List<RegOp> Reg = new();
    public List<(string Svc, int Start)> Svc = new();
    public List<string> Tasks = new();
    public Func<SysInfo, bool> Cond;
    public Func<Ctx, bool> Test;
    public Action<Ctx> Do;

    public bool Applicable(SysInfo s) => Cond == null || (s != null && Cond(s));

    public bool IsApplied(Ctx c)
    {
        try
        {
            if (!Applicable(c?.S)) return false;
            if (Test != null) return Test(c);
            if (Reg.Count == 0 && Svc.Count == 0) return false;
            foreach (var r in Reg.Where(r => r.Applicable(c?.S)))
            {
                object current = Core.Reg.Get(r.Hive, r.Path, r.Name);
                if (!Core.Reg.Equal(current, r.ClearBits == 0 ? r.ExpectedValue(c?.S) : RegOp.ClearFlags(current, r.ClearBits))) return false;
            }
            foreach (var (svc, start) in Svc)
            {
                var cur = Core.Reg.ServiceStart(svc);
                if (cur != null && cur != start) return false;
            }
            return true;
        }
        catch { return false; }
    }

    public void Apply(Ctx c)
    {
        if (!Applicable(c?.S)) throw new InvalidOperationException("Эта настройка не поддерживается текущей Windows.");
        if (IsApplied(c)) { Log.Skip($"Настройка «{Name}» уже применена"); return; }
        using var ownSession = Backup.Current == null ? Backup.Session() : null;
        Backup.CurrentTweak = Id;
        try { ApplyCore(c); }
        finally { Backup.CurrentTweak = null; }
    }

    void ApplyCore(Ctx c)
    {
        int failed = 0;
        foreach (var r in Reg.Where(r => r.Applicable(c?.S))) if (!Core.Reg.Set(r.Hive, r.Path, r.Name,
            r.ClearBits == 0 ? r.ExpectedValue(c?.S) : RegOp.ClearFlags(Core.Reg.Get(r.Hive, r.Path, r.Name), r.ClearBits), r.Kind)) failed++;
        foreach (var (svc, start) in Svc) if (!Core.Reg.SetService(svc, start)) failed++;
        foreach (var t in Tasks)
        {
            // XML-вид не зависит от языка Windows: запоминаем только задачи, которые были включены
            var (qc, xml) = Sh.Run(Sh.Sys32("schtasks.exe"), $"/Query /TN \"{t}\" /XML", 20_000, quiet: true);
            if (qc != 0) continue;
            bool wasEnabled = !xml.Contains("<Enabled>false</Enabled>", StringComparison.OrdinalIgnoreCase);
            if (!wasEnabled) continue;
            Backup.AddTask(t);
            var (code, _) = Sh.Run(Sh.Sys32("schtasks.exe"), $"/Change /TN \"{t}\" /Disable", 20_000);
            if (code != 0) failed++;
        }
        Do?.Invoke(c);
        if (failed > 0)
        {
            // частично записанное откатывается как обычно, но применённым твик не считаем
            throw new IOException($"Настройка «{Name}» применена не полностью: не удалось изменить {failed} {SysInfo.Plural(failed, "параметр", "параметра", "параметров")}. Прежние значения сохранены для отката.");
        }
        var cur = Backup.Current;
        if (cur != null) lock (cur) { if (!cur.Tweaks.Contains(Id)) cur.Tweaks.Add(Id); }
    }
}

public static class Catalog
{
    const string CU = "HKCU", LM = "HKLM";
    const string ADV = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string CDM = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    const string MM = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    internal static readonly string[] DebloatPackages = { "Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.BingSearch", "Microsoft.Getstarted",
                "Microsoft.MicrosoftSolitaireCollection", "Microsoft.People", "Microsoft.WindowsFeedbackHub", "Microsoft.WindowsMaps",
                "Microsoft.ZuneVideo", "Microsoft.MixedReality.Portal", "Microsoft.SkypeApp", "Microsoft.Microsoft3DViewer",
                "Microsoft.MicrosoftOfficeHub", "Microsoft.PowerAutomateDesktop", "Clipchamp.Clipchamp",
                "Microsoft.549981C3F5F10", "Microsoft.Windows.DevHome", "Microsoft.Copilot", "Microsoft.MicrosoftJournal",
                "*Disney*", "*TikTok*", "*Instagram*", "*Facebook*", "*CandyCrush*", "*king.com*", "*Netflix*",
                "*AmazonVideo*", "*Twitter*", "*LinkedIn*", "*Duolingo*" };
    const string DEF_RT = @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection";
    const string HP ="8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    const string CLASSIC = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    static RegOp D(string h, string p, string n, long v) => new(h, p, n, v);
    static RegOp Str(string h, string p, string n, string v) => new(h, p, n, v, RegistryValueKind.String);
    const string USB_SUB = "2a737441-1930-4402-8d77-b2bebba308a3", USB_SET = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
    const string PCIE_SUB = "501a4d13-42af-4429-9fd1-a8218c268e20", PCIE_SET = "ee12f906-d277-404b-b6da-e5fa1a576df5";

    /// <summary>Текущее значение параметра питания «от сети». Первое число вида 0x... в выводе — это AC (не зависит от языка).</summary>
    static int? PowerAc(string sub, string setting)
    {
        var (code, o) = Sh.Run(Sh.Sys32("powercfg.exe"), $"/query SCHEME_CURRENT {sub} {setting}", 10_000);
        if (code != 0) return null;
        var m = System.Text.RegularExpressions.Regex.Match(o, "0x([0-9a-fA-F]{8})");
        return m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) : null;
    }

    public static readonly string[] Categories =
        { "Производительность", "Игры", "Конфиденциальность", "Интерфейс", "Удобство", "Проводник", "Службы", "Обновления", "Приложения", "Защита Windows" };

    static string ActiveScheme()
    {
        var (_, o) = Sh.Run(Sh.Sys32("powercfg.exe"), "/getactivescheme", 10_000);
        var m = System.Text.RegularExpressions.Regex.Match(o, "[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}");
        return m.Success ? m.Value : null;
    }

    static void RemoveAppx(params string[] patterns) => RemoveAppxCore(true, patterns);
    /// <summary>Удаление только для текущего пользователя, без снятия подготовленных пакетов.</summary>
    static void RemoveAppxUser(params string[] patterns) => RemoveAppxCore(false, patterns);

    /// <summary>Есть ли хоть один из пакетов у текущего пользователя.</summary>
    static bool UserHasAppx(params string[] names)
    {
        var list = string.Join(",", names.Select(n => $"'{n}'"));
        var (code, o) = Sh.PS($"foreach ($a in @({list})) {{ if (Get-AppxPackage -Name $a -ErrorAction Stop) {{ 'YES'; break }} }}", 60_000);
        return code != 0 || o.Contains("YES");
    }

    static void RemoveAppxCore(bool allUsers, string[] patterns)
    {
        var list = string.Join(",", patterns.Select(p => $"'{p}'"));
        var script = allUsers ? $@"
$prov = Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue
foreach ($a in @({list})) {{
  Get-AppxPackage -AllUsers -Name $a -ErrorAction SilentlyContinue | ForEach-Object {{
    $n = $_.Name; $full = $_.PackageFullName
    try {{ Remove-AppxPackage -Package $full -AllUsers -ErrorAction Stop; ""OK $n"" }}
    catch {{ ""FAIL $n"" }} }}
  $prov | Where-Object DisplayName -like $a | ForEach-Object {{ Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction SilentlyContinue | Out-Null }}
}}" : $@"
foreach ($a in @({list})) {{
  Get-AppxPackage -Name $a -ErrorAction SilentlyContinue | ForEach-Object {{
    $n = $_.Name
    try {{ Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop; ""OK $n"" }} catch {{ ""FAIL $n"" }} }}
}}";
        var (code, o) = Sh.PS(script.Replace("-ErrorAction SilentlyContinue", "-ErrorAction Stop"), 900_000);
        foreach (var line in o.Split('\n').Select(l => l.Trim()))
        {
            if (line.StartsWith("OK ")) Log.Write("    удалено: " + line[3..]);
            else if (line.StartsWith("FAIL ")) Log.Warn("    не получилось удалить: " + line[5..]);
        }
        if (code != 0 || o.Split('\n').Any(line => line.TrimStart().StartsWith("FAIL ")) || UserHasAppx(patterns))
            throw new IOException("Не все приложения удалось удалить. Подробности записаны в журнал.");
    }

    public static List<Tweak> All() => new()
    {
        // ================= ПРОИЗВОДИТЕЛЬНОСТЬ =================
        new Tweak { Id="power_high", Cat="Производительность", Rec=true, Game=true,
            Name="Высокая производительность",
            Desc="Процессор не сбавляет частоту. Компьютер тратит больше электричества.",
            Hint="То же, что «Высокая производительность» в разделе «Электропитание». На ноутбуках не включается.",
            Cond = s => !s.Laptop,
            Test = c => { var a = ActiveScheme() ?? ""; return a.Equals(HP, StringComparison.OrdinalIgnoreCase) || a.StartsWith("e9a42b02", StringComparison.OrdinalIgnoreCase); },
            Do = c => {
                var cur = ActiveScheme();
                Backup.SetPowerScheme(cur);
                var (_, list) = Sh.Run(Sh.Sys32("powercfg.exe"), "/list");
                // без второго GUID duplicatescheme создаёт копию со СЛУЧАЙНЫМ GUID, и setactive HP не срабатывает
                if (!list.Contains(HP, StringComparison.OrdinalIgnoreCase)) Sh.Run(Sh.Sys32("powercfg.exe"), $"/duplicatescheme {HP} {HP}");
                var (rc, _) = Sh.Run(Sh.Sys32("powercfg.exe"), $"/setactive {HP}");
                if (rc != 0) Sh.Run(Sh.Sys32("powercfg.exe"), "/setactive SCHEME_MIN");
            } },
        new Tweak { Id="usb_pcie_power", Cat="Производительность", Rec=true, Game=true,
            Name="Отключить энергосбережение USB и PCI Express",
            Desc="Устройства USB и PCI Express (например, видеокарта) не уходят в режим экономии.",
            Hint="Компьютер будет тратить чуть больше электричества.",
            Cond = s => !s.Laptop,
            Test = c => PowerAc(USB_SUB, USB_SET) == 0 && PowerAc(PCIE_SUB, PCIE_SET) == 0,
            Do = c => {
                var scheme = ActiveScheme();
                foreach (var (sub, set) in new[] { (USB_SUB, USB_SET), (PCIE_SUB, PCIE_SET) })
                {
                    var old = PowerAc(sub, set);
                    if (!old.HasValue || scheme == null) throw new IOException("Не удалось сохранить исходную настройку питания.");
                    if (old.Value != 0)
                        Backup.AddPowerUndo($"/setacvalueindex {scheme} {sub} {set} {old.Value}");
                    Sh.Run(Sh.Sys32("powercfg.exe"), $"/setacvalueindex SCHEME_CURRENT {sub} {set} 0");
                }
                Sh.Run(Sh.Sys32("powercfg.exe"), "/setactive SCHEME_CURRENT");
            } },
        new Tweak { Id="power_throttling", Cat="Производительность", Rec=true, Game=true,
            Name="Не урезать скорость фоновых программ",
            Desc="Windows не замедляет программы, которые работают в фоне.",
            Cond = s => !s.Laptop && s.Build >= 16299,
            Reg = { D(LM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1) } },
        new Tweak { Id="hibernate_off", Cat="Производительность", Rec=true, Min=true,
            Name="Отключить гибернацию и быстрый запуск",
            Desc="Освободит несколько ГБ на диске C.",
            Hint="Компьютер может включаться немного дольше. Обычный спящий режим продолжит работать.",
            Cond = s => !s.Laptop,
            Test = c => !File.Exists(System.IO.Path.Combine(System.IO.Path.GetPathRoot(Environment.SystemDirectory), "hiberfil.sys")),
            Do = c => {
                if (File.Exists(System.IO.Path.Combine(System.IO.Path.GetPathRoot(Environment.SystemDirectory), "hiberfil.sys")))
                    Backup.MarkHibernate();
                Sh.Run(Sh.Sys32("powercfg.exe"), "/hibernate off"); } },
        new Tweak { Id="mmcss", Cat="Производительность", Risk=1,
            Name="Приоритет для игр и мультимедиа",
            Desc="Играм и видео достаётся больше времени процессора.",
            Hint="Действует только на игры и программы, которые сами просят Windows о таком приоритете. Прирост FPS не гарантируется, фоновые задачи могут работать медленнее.",
            Reg = { D(LM, MM, "SystemResponsiveness", 10),
                    D(LM, MM + @"\Tasks\Games", "GPU Priority", 8) with { Cond = s => false },
                    D(LM, MM + @"\Tasks\Games", "Priority", 6) with { Cond = s => false },
                    Str(LM, MM + @"\Tasks\Games", "Scheduling Category", "High") } },
        new Tweak { Id="ntfs_lastaccess", Cat="Производительность", Risk=1, Reboot=true,
            Name="Не записывать время открытия файлов",
            Desc="Windows не записывает, когда файл открывали в последний раз.",
            Hint="Некоторые программы резервного копирования и очистки опираются на эту дату. Ускорение не гарантируется.",
            Reg = { D(LM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", 0x80000001) } },
        new Tweak { Id="sysmain", Cat="Производительность", Risk=1,
            Name="Запускать SysMain только при необходимости",
            Desc="Служба SysMain заранее загружает частые программы в память. Она будет запускаться, только когда нужна.",
            Hint="Программы могут открываться чуть медленнее. Польза заметна не на каждом компьютере.",
            Cond = s => s.SysDisk == "SSD" && s.RamGB >= 16,
            Svc = { ("SysMain", 3) } },
        new Tweak { Id="search_index", Cat="Производительность", Risk=1,
            Name="Отключить индексацию поиска",
            Desc="Поиск в Пуске и Проводнике станет медленнее.",
            Svc = { ("WSearch", 4) } },
        new Tweak { Id="vbs_off", Cat="Производительность", Risk=2, Reboot=true,
            Name="Отключить «Целостность памяти»",
            Desc="Снижает защиту системы. Некоторые античиты могут перестать работать.",
            Reg = { D(LM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0),
                    D(LM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 0) },
            Do = c => {
                // если защита закреплена в UEFI (часто на ноутбуках и рабочих ПК), реестр её не выключит — говорим честно
                var (_, o) = Sh.PS("(Get-CimInstance -Namespace root\\Microsoft\\Windows\\DeviceGuard -ClassName Win32_DeviceGuard).VirtualizationBasedSecurityStatus", 30_000);
                var locked = Reg.Get(LM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "Locked");
                if (locked != null && Reg.Equal(locked, 1))
                    Log.Warn("«Целостность памяти» закреплена в UEFI. Merno не сможет её выключить. Выключите её в BIOS или в «Безопасность Windows» → «Безопасность устройства».");
                else if (o.Trim() == "2") Log.Write("    после перезагрузки «Целостность памяти» выключится");
            } },

        // ================= ИГРЫ =================
        new Tweak { Id="gamedvr_off", Cat="Игры", Rec=true, Game=true, Min=true,
            Name="Отключить фоновую запись игр",
            Desc="Windows перестанет записывать игры в фоне. Запись и снимки через Xbox Game Bar тоже выключатся.",
            Reg = { D(CU, @"System\GameConfigStore", "GameDVR_Enabled", 0),
                    D(CU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0),
                    D(CU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 0),
                    D(LM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0)
                        with { Cond = s => s.HasManagedPolicies && s.Build >= 10240 && !s.IsWin11 } } },
        new Tweak { Id="gamemode", Cat="Игры", Rec=true, Game=true,
            Name="Включить игровой режим",
            Desc="Во время игры Windows откладывает установку обновлений и отдаёт игре больше ресурсов.",
            Cond = s => s.Build >= 15063,
            Reg = { D(CU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1), D(CU, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1) } },
        new Tweak { Id="hags", Cat="Игры", Risk=1, Reboot=true,
            Name="Аппаратное планирование видеокарты",
            Desc="Меняет способ планирования работы видеокарты.",
            Hint="Работает, если видеокарта и драйвер это поддерживают: тогда в параметрах графики Windows есть такой пункт. Прирост FPS и снижение задержки не гарантируются.",
            Cond = s => s.Build >= 19041 && !s.IsVM,
            Reg = { D(LM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2) } },
        new Tweak { Id="windowed_opt", Cat="Игры", Rec=true, Game=true,
            Name="Оптимизация игр в оконном режиме",
            Desc="Оптимизирует вывод DirectX 10/11 в окне и без рамки.",
            Hint="Результат зависит от игры. После изменения перезапустите игру.",
            Cond = s => s.Build >= 22621,
            Test = c => (Reg.Get(CU, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings")?.ToString() ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("SwapEffectUpgradeEnable=1", StringComparer.Ordinal),
            Do = c => {
                var cur = Reg.Get(CU, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings")?.ToString() ?? "";
                cur = System.Text.RegularExpressions.Regex.Replace(cur, @"(?:^|;)SwapEffectUpgradeEnable=\d+(?=;|$)", "").Trim(';');
                if (!Reg.Set(CU, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings",
                    (cur.Length > 0 ? cur + ";" : "") + "SwapEffectUpgradeEnable=1;", RegistryValueKind.String))
                    throw new IOException("Не удалось сохранить оптимизацию игр в оконном режиме.");
            } },
        new Tweak { Id="mouse_accel", Cat="Игры", Game=true,
            Name="Отключить ускорение мыши",
            Desc="Курсор движется точно за рукой.",
            Hint="Заработает после повторного входа в Windows.",
            Reg = { Str(CU, @"Control Panel\Mouse", "MouseSpeed", "0"), Str(CU, @"Control Panel\Mouse", "MouseThreshold1", "0"), Str(CU, @"Control Panel\Mouse", "MouseThreshold2", "0") } },
        new Tweak { Id="sticky_keys", Cat="Игры", Rec=true, Game=true,
            Name="Не включать залипание клавиш после пяти нажатий Shift",
            // снимаем только бит HOTKEYACTIVE (0x4), включено ли залипание и остальные флаги не трогаем
            // https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-stickykeys
            Reg = { Str(CU, @"Control Panel\Accessibility\StickyKeys", "Flags", "506") with { ClearBits = 4 },
                    Str(CU, @"Control Panel\Accessibility\ToggleKeys", "Flags", "58") with { ClearBits = 4 },
                    Str(CU, @"Control Panel\Accessibility\Keyboard Response", "Flags", "122") with { ClearBits = 4 } } },

        // ================= КОНФИДЕНЦИАЛЬНОСТЬ =================
        new Tweak { Id="telemetry", Cat="Конфиденциальность", Rec=true, Game=true, Min=true,
            Name="Минимальная отправка данных в Microsoft",
            Desc="Windows будет отправлять в Microsoft как можно меньше сведений о работе компьютера.",
            Hint="В Pro обязательные сведения всё равно отправляются. Полностью отключить отправку можно только в редакциях «Корпоративная» и «Для образования».",
            Cond = s => s.HasManagedPolicies && s.Build >= 10240,
            Reg = { D(LM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0)
                        with { ValueFor = s => s?.HasEnterprisePolicies == true ? 0 : 1 } },
            Svc = { ("DiagTrack", 4), ("dmwappushservice", 4) },
            Tasks = { @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
                      @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                      @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
                      @"\Microsoft\Windows\Feedback\Siuf\DmClient",
                      @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload" } },
        new Tweak { Id="ads", Cat="Конфиденциальность", Rec=true, Game=true, Min=true,
            Name="Убрать рекламу и «рекомендации»",
            Desc="В Пуске, на экране блокировки и в Проводнике.",
            Hint="Какие именно предложения пропадут, зависит от версии Windows.",
            Reg = { D(CU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0),
                    D(CU, CDM, "SilentInstalledAppsEnabled", 0), D(CU, CDM, "SystemPaneSuggestionsEnabled", 0),
                    D(CU, CDM, "SoftLandingEnabled", 0), D(CU, CDM, "PreInstalledAppsEnabled", 0),
                    D(CU, CDM, "OemPreInstalledAppsEnabled", 0), D(CU, CDM, "RotatingLockScreenOverlayEnabled", 0),
                    D(CU, CDM, "SubscribedContent-338387Enabled", 0), D(CU, CDM, "SubscribedContent-338388Enabled", 0),
                    D(CU, CDM, "SubscribedContent-338389Enabled", 0), D(CU, CDM, "SubscribedContent-310093Enabled", 0),
                    D(CU, CDM, "SubscribedContent-353694Enabled", 0), D(CU, CDM, "SubscribedContent-353696Enabled", 0),
                    D(CU, ADV, "Start_IrisRecommendations", 0), D(CU, ADV, "ShowSyncProviderNotifications", 0),
                    D(CU, @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0),
                    D(LM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1)
                        with { Cond = s => s.HasEnterprisePolicies && s.Build >= 14393 },
                    D(CU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0) } },
        new Tweak { Id="bing", Cat="Конфиденциальность", Rec=true, Game=true, Min=true,
            Name="Не показывать веб-результаты в поиске Windows",
            Desc="Поиск в Пуске ищет только на компьютере, без результатов из интернета.",
            Hint="Работает только в редакциях «Корпоративная» и «Для образования». Интернет в браузерах и других программах не отключается.",
            Cond = s => s.HasEnterprisePolicies && s.Build >= 17134,
            Reg = { D(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0),
                    // эти значения больше не записываются (Cond = false), но остаются в каталоге, чтобы откат мог их вернуть
                    D(CU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1) with { Cond = s => false },
                    D(CU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0) with { Cond = s => false } } },
        new Tweak { Id="activity", Cat="Конфиденциальность", Rec=true,
            Name="Не публиковать журнал действий Windows",
            Desc="Ограничивает журнал действий и его синхронизацию.",
            Cond = s => s.HasManagedPolicies && s.Build >= 16299,
            Reg = { D(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0),
                    D(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
                    D(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0) } },
        new Tweak { Id="copilot", Cat="Конфиденциальность", Rec=true,
            Name="Отключить встроенный Copilot и снимки Recall",
            Desc="Выключает встроенную панель Copilot и снимки экрана Recall, если они есть в вашей Windows.",
            Hint="Отдельное приложение Copilot это не отключает. Recall бывает только в свежих версиях Windows 11.",
            Cond = s => s.SupportsLegacyCopilotPolicy || s.SupportsRecallPolicy,
            Reg = { D(CU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1)
                        with { Cond = s => s.SupportsLegacyCopilotPolicy },
                    // в HKLM не записываем (политика документирована только для HKCU), но держим в каталоге, чтобы откат мог её вернуть
                    D(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1)
                        with { Cond = s => false },
                    D(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1)
                        with { Cond = s => s.SupportsRecallPolicy },
                    D(CU, ADV, "ShowCopilotButton", 0) with { Cond = s => false } } },
        new Tweak { Id="delivery_opt", Cat="Конфиденциальность", Rec=true, Game=true,
            Name="Не раздавать обновления другим компьютерам",
            Desc="Обновления скачиваются только с серверов Microsoft, ваш интернет не тратится на раздачу.",
            Cond = s => s.HasManagedPolicies && s.Build >= 10240,
            Reg = { D(LM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0) } },

        // ================= ИНТЕРФЕЙС =================
        new Tweak { Id="classic_menu", Cat="Интерфейс", Rec=false,
            Name="Классическое меню правой кнопки мыши",
            Desc="Без «Показать дополнительные параметры».",
            Hint="Заработает после перезагрузки.",
            Cond = s => s.IsWin11,
            Test = c => Reg.KeyExists(CU, CLASSIC),
            Do = c => {
                if (!Reg.Set(CU, CLASSIC, "", "", RegistryValueKind.String)) throw new IOException("Не удалось изменить контекстное меню.");
            } },
        new Tweak { Id="menu_delay", Cat="Интерфейс", Rec=true, Min=true,
            Name="Быстрее открывать меню",
            Desc="Открывать вложенные меню без задержки.",
            Reg = { Str(CU, @"Control Panel\Desktop", "MenuShowDelay", "100") } },
        new Tweak { Id="visual_fx", Cat="Интерфейс",
            Name="Уменьшить анимации окон и панели задач",
            Desc="Окна сворачиваются и разворачиваются без анимации, панель задач тоже без неё.",
            Hint="Заработает после повторного входа в Windows.",
            Reg = { D(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3) with { Cond = s => false },
                    new RegOp(CU, @"Control Panel\Desktop", "UserPreferencesMask", new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 }, RegistryValueKind.Binary) with { Cond = s => false },
                    Str(CU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0"),
                    D(CU, ADV, "TaskbarAnimations", 0) } },
        new Tweak { Id="transparency", Cat="Интерфейс",
            Name="Отключить прозрачность",
            Reg = { D(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0) } },
        new Tweak { Id="end_task", Cat="Интерфейс", Rec=true,
            Name="Пункт «Завершить задачу» на панели задач",
            Desc="Закрыть зависшую программу правым щелчком.",
            Cond = s => s.IsWin11 && s.Build >= 22631,
            Reg = { D(CU, ADV + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1) } },

        // ================= УДОБСТВО =================
        // Уже есть в других разделах (не дублируем): file_ext, this_pc, mouse_accel.
        new Tweak { Id="conv_start_left", Cat="Удобство",
            Name="Кнопка «Пуск» слева",
            Desc="Как в Windows 10.",
            Cond = s => s.IsWin11,
            Reg = { D(CU, ADV, "TaskbarAl", 0) } },
        new Tweak { Id="conv_desktop_pc", Cat="Удобство",
            Name="Значок «Этот компьютер» на рабочем столе",
            Reg = { D(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0),
                    D(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0) } },
        new Tweak { Id="conv_dark_theme", Cat="Удобство",
            Name="Тёмная тема Windows",
            Reg = { D(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0),
                    D(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) } },
        new Tweak { Id="conv_hide_search", Cat="Удобство",
            Name="Скрыть поиск на панели задач",
            Desc="Искать можно, открыв Пуск.",
            Reg = { D(CU, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 0) } },
        new Tweak { Id="conv_hide_taskview", Cat="Удобство",
            Name="Скрыть кнопку «Представление задач»",
            Desc="Win+Tab продолжит работать.",
            Reg = { D(CU, ADV, "ShowTaskViewButton", 0) } },

        // ================= ПРОВОДНИК =================
        new Tweak { Id="file_ext", Cat="Проводник", Rec=true, Min=true,
            Name="Показывать расширения файлов",
            Hint="Будет видно «документ.pdf» и «программа.exe». Проще заметить вирус под видом фото.",
            Reg = { D(CU, ADV, "HideFileExt", 0) } },
        new Tweak { Id="this_pc", Cat="Проводник", Rec=true,
            Name="Проводник открывается на «Этот компьютер»",
            Reg = { D(CU, ADV, "LaunchTo", 1) } },
        new Tweak { Id="no_recent", Cat="Проводник",
            Name="Не показывать недавние файлы",
            Reg = { D(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 0),
                    D(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", 0),
                    D(CU, ADV, "Start_TrackDocs", 0) } },
        new Tweak { Id="checkboxes", Cat="Проводник",
            Name="Галочки для выделения файлов",
            Reg = { D(CU, ADV, "AutoCheckSelect", 1) } },
        new Tweak { Id="hidden_files", Cat="Проводник",
            Name="Показывать скрытые файлы",
            Reg = { D(CU, ADV, "Hidden", 1) } },

        // ================= СЛУЖБЫ =================
        new Tweak { Id="svc_misc", Cat="Службы", Rec=true, Game=true,
            Name="Не держать включёнными редкие функции",
            Desc="Службы карт, факса, геолокации, отчётов об ошибках, родительского контроля и ещё несколько запускаются, только когда нужны.",
            Svc = { ("MapsBroker", 3), ("RetailDemo", 4), ("Fax", 3), ("WerSvc", 3), ("lfsvc", 3), ("WMPNetworkSvc", 3), ("PhoneSvc", 3), ("WpcMonSvc", 3) } },
        new Tweak { Id="remote_reg", Cat="Службы", Rec=true, Game=true, Min=true,
            Name="Отключить удалённый доступ к реестру",
            Desc="Другие компьютеры в сети не смогут читать и менять реестр этого компьютера.",
            Svc = { ("RemoteRegistry", 4) } },
        new Tweak { Id="print_off", Cat="Службы", Risk=1,
            Name="Отключить печать",
            Desc="Только если нет принтера. Печать в PDF тоже перестанет работать.",
            Svc = { ("Spooler", 4) } },

        // ================= ОБНОВЛЕНИЯ =================
        new Tweak { Id="no_auto_reboot", Cat="Обновления", Rec=true, Game=true, Min=true,
            Name="Не перезагружать при плановой установке обновлений",
            Desc="Пока вы в системе, Windows не перезагрузит компьютер сама после установки обновлений.",
            Hint="Действует, когда в политиках Windows настроена установка обновлений по расписанию. Перезагрузку, которую вы назначили сами, Windows выполнит.",
            Cond = s => s.HasManagedPolicies && s.Build >= 10240 &&
                Reg.Equal(Reg.Get(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions"), 4),
            Reg = { D(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1) } },
        new Tweak { Id="no_driver_wu", Cat="Обновления", Risk=1,
            Name="Не обновлять драйверы через Центр обновления Windows",
            Desc="Обновлять драйверы придётся самому.",
            Cond = s => s.HasManagedPolicies && s.Build >= 14393,
            Reg = { D(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1) } },
        new Tweak { Id="defer_feature", Cat="Обновления", Risk=1,
            Name="Откладывать крупные обновления на год",
            Desc="Обновления безопасности будут приходить как обычно.",
            Hint="Крупные обновления придут через год после выхода, когда в них уже исправят ошибки.",
            Cond = s => s.HasManagedPolicies && s.Build >= 14393,
            Reg = { D(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdates", 1),
                    D(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferFeatureUpdatesPeriodInDays", 365) } },

        // ================= ЗАЩИТА WINDOWS (только вручную, мастер не трогает) =================
        new Tweak { Id="defender_off", Cat="Защита Windows", Risk=2, Reboot=true,
            Name="Отключить Защитник Windows",
            Hint="Компьютер останется без защиты от вирусов. Если Windows не даст выключить, Merno откроет нужное окно. Выключите в нём «Защиту от подделки» и нажмите ещё раз.",
            Test = c => c.DefenderDisabled,
            Do = c => {
                var (_, st) = Sh.PS("$s = Get-MpComputerStatus -ErrorAction Stop; \"$($s.RealTimeProtectionEnabled)|$($s.IsTamperProtected)\"", 30_000);
                var parts = st.Trim().Split('|');
                if (parts.Length != 2)
                {
                    throw new IOException("Не удалось определить состояние Защитника Windows. Изменения не внесены.");
                }
                bool wasOff = parts[0].Equals("False", StringComparison.OrdinalIgnoreCase);
                bool tamper = parts[1].Equals("True", StringComparison.OrdinalIgnoreCase);
                if (tamper)
                {
                    c.DefenderDisabled = false;
                    Sh.Open("windowsdefender://threatsettings");
                    throw new IOException("Включена «Защита от подделки», поэтому Windows не даёт выключить Защитник. Выключите её в открывшемся окне и нажмите ещё раз. Пока ничего не изменено.");
                }
                if (!bool.TryParse(parts[0], out _) || !bool.TryParse(parts[1], out _))
                    throw new IOException("Windows вернула неизвестное состояние Защитника. Изменения не внесены.");
                Backup.RecordDefender();
                // политики пишем и когда уже выключен — чтобы Защитник не включился сам
                foreach (var r in new[] {
                    D(LM, DEF_RT, "DisableRealtimeMonitoring", 1), D(LM, DEF_RT, "DisableBehaviorMonitoring", 1),
                    D(LM, DEF_RT, "DisableOnAccessProtection", 1), D(LM, DEF_RT, "DisableScanOnRealtimeEnable", 1),
                    D(LM, DEF_RT, "DisableIOAVProtection", 1),
                    D(LM, @"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SpynetReporting", 0),
                    D(LM, @"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SubmitSamplesConsent", 2) })
                    if (!Reg.Set(r.Hive, r.Path, r.Name, r.Value, r.Kind)) throw new IOException("Не удалось сохранить политику Защитника. Исходное состояние сохранено для отката.");
                if (wasOff) { c.DefenderDisabled = true; Log.Ok("Защитник Windows уже выключен."); return; }
                Sh.PS("Set-MpPreference -DisableRealtimeMonitoring $true -DisableBehaviorMonitoring $true -DisableIOAVProtection $true -MAPSReporting 0 -SubmitSamplesConsent 2 -ErrorAction SilentlyContinue", 60_000);
                var (_, now) = Sh.PS("(Get-MpComputerStatus).RealTimeProtectionEnabled", 30_000);
                c.DefenderDisabled = now.Trim().Equals("False", StringComparison.OrdinalIgnoreCase);
                if (c.DefenderDisabled) Log.Ok("Защитник Windows выключен.");
                else throw new IOException("Windows не дала выключить Защитник. Исходное состояние сохранено для отката.");
            } },
        new Tweak { Id="uac_quiet", Cat="Защита Windows", Risk=2,
            Name="Не спрашивать разрешение при запуске программ",
            Desc="Вирус тоже сможет менять всё без спроса.",
            Hint="Пропадёт окно «Разрешить этому приложению вносить изменения на вашем устройстве?».",
            Reg = { D(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 0),
                    D(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop", 0) } },
        new Tweak { Id="smartscreen_off", Cat="Защита Windows", Risk=2,
            Name="Отключить предупреждения SmartScreen",
            Desc="Отключает проверки репутации файлов Проводника и сайтов Edge.",
            Hint="Меняет политики Проводника и Microsoft Edge. Антивирус и проверки других браузеров этой настройкой не отключаются.",
            Cond = s => s.HasManagedPolicies && s.Build >= 15063,
            Reg = { D(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 0),
                    Str(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", "Off") with { Cond = s => false },
                    D(CU, @"Software\Microsoft\Windows\CurrentVersion\AppHost", "EnableWebContentEvaluation", 0) with { Cond = s => false },
                    D(LM, @"SOFTWARE\Policies\Microsoft\Edge", "SmartScreenEnabled", 0) } },
        new Tweak { Id="edge_remove", Cat="Защита Windows", Risk=2, CanRevert=false,
            Name="Удалить Microsoft Edge полностью",
            Desc="Сначала поставьте другой браузер.",
            Hint="Пароли и закладки заранее перенесите в новый браузер. Могут перестать работать виджеты, новый Outlook и Teams. После крупного обновления Windows Edge может вернуться, тогда удалите его ещё раз. Вернуть Edge можно с сайта microsoft.com/edge.",
            Test = c => !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"))
                     && !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe")),
            Do = c => {
                // системные папки берём у Windows и передаём в скрипт готовыми: переменные окружения ($env:ProgramFiles и т. п.)
                // пользователь может переопределить, и setup.exe запустился бы с правами администратора из его папки
                static string Q(string s) => "'" + Sh.Esc(s) + "'";
                string head = $"$pf86 = {Q(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))}; " +
                              $"$pf = {Q(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))}; " +
                              $"$sr = {Q(Environment.GetFolderPath(Environment.SpecialFolder.Windows))}; " +
                              $"$pd = {Q(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))}\n";
                var (code, o) = Sh.PS(head + @"$ErrorActionPreference='SilentlyContinue'
Get-Process msedge,MicrosoftEdgeUpdate,widgets | Stop-Process -Force
New-Item 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate' -Force | Out-Null
Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate' -Name DoNotUpdateToEdgeWithChromium -Value 1 -Type DWord
$stub = ""$sr\SystemApps\Microsoft.MicrosoftEdge_8wekyb3d8bbwe""
New-Item $stub -ItemType Directory -Force | Out-Null
$madeStub = -not (Test-Path ""$stub\MicrosoftEdge.exe"")
if ($madeStub) { New-Item ""$stub\MicrosoftEdge.exe"" -ItemType File -Force | Out-Null }
New-Item 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdateDev' -Force | Out-Null
Set-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdateDev' -Name AllowUninstall -Value 1 -Type DWord
foreach ($root in ""$pf86\Microsoft\Edge\Application"", ""$pf\Microsoft\Edge\Application"") {
  Get-ChildItem ""$root\*\Installer\setup.exe"" | ForEach-Object { Start-Process $_.FullName '--uninstall --system-level --force-uninstall --verbose-logging' -Wait }
}
if ($madeStub) { Remove-Item ""$stub\MicrosoftEdge.exe"" -Force }
Get-AppxPackage -AllUsers *MicrosoftEdge* | Where-Object { $_.Name -notlike '*WebView*' -and $_.NonRemovable -eq $false } | Remove-AppxPackage -AllUsers
Remove-Item ""$env:PUBLIC\Desktop\Microsoft Edge.lnk"", ""$pd\Microsoft\Windows\Start Menu\Programs\Microsoft Edge.lnk"", ""$env:USERPROFILE\Desktop\Microsoft Edge.lnk"" -Force
Remove-Item ""$env:APPDATA\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Microsoft Edge.lnk"" -Force
if ((Test-Path ""$pf86\Microsoft\Edge\Application\msedge.exe"") -or (Test-Path ""$pf\Microsoft\Edge\Application\msedge.exe"")) { 'FAIL' } else { 'OK' }", 600_000);
                if (code != 0 || !o.TrimEnd().EndsWith("OK", StringComparison.Ordinal) || o.Contains("FAIL")) throw new IOException("Удаление Edge не подтверждено. Проверьте журнал и повторите после перезагрузки.");
                else Log.Ok("Edge удалён.");
            } },

        // ================= ПРИЛОЖЕНИЯ =================
        new Tweak { Id="debloat", Cat="Приложения", Rec=false, Risk=1, Game=true, CanRevert=false,
            Name="Удалить лишние приложения",
            Desc="Copilot, Cortana, Skype, Clipchamp, Новости, Погода, Карты, Пасьянсы, TikTok, Netflix и подобное.",
            Hint="Ещё удаляются: Поиск Bing, Люди, Кино и ТВ, Центр отзывов, Советы, Средство 3D-просмотра, Портал смешанной реальности, Microsoft 365 (сам Office останется), " +
                 "Power Automate, Dev Home, Журнал Microsoft, а также предустановленные Instagram, Facebook, X (Twitter), LinkedIn, Disney+, Prime Video, Duolingo и игры King (Candy Crush). " +
                 "Калькулятор, Фотографии, Блокнот и Магазин останутся. Удалённое придётся ставить заново, и не всё есть в Microsoft Store.",
            // удаляем только у текущего пользователя, поэтому и проверяем только его (у -AllUsers пакеты остаются «подготовленными»)
            Test = c => !UserHasAppx(DebloatPackages),
            Do = c => RemoveAppxUser(DebloatPackages) },
        new Tweak { Id="widgets", Cat="Приложения", Rec=false, Game=true, CanRevert=false,
            Name="Удалить виджеты",
            Desc="Панель новостей и погоды.",
            Cond = s => s.IsWin11,
            Test = c => !c.HasAppx("MicrosoftWindows.Client.WebExperience"),
            Do = c => RemoveAppx("MicrosoftWindows.Client.WebExperience") },
        new Tweak { Id="xbox_bar", Cat="Приложения", CanRevert=false,
            Name="Удалить Xbox Game Bar",
            Desc="Панель Win+G. Xbox и Game Pass останутся.",
            Test = c => !c.HasAppx("Microsoft.XboxGamingOverlay"),
            Do = c => RemoveAppx("Microsoft.XboxGamingOverlay") },
        new Tweak { Id="onedrive", Cat="Приложения", Risk=1, CanRevert=false,
            Name="Удалить OneDrive",
            Desc="Файлы только в облаке станут недоступны.",
            Hint="Сначала скачайте нужные файлы на компьютер.",
            Test = c => !File.Exists(Path.Combine(CleanupPaths.Profile, @"AppData\Local\Microsoft\OneDrive\OneDrive.exe"))
                        && !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft OneDrive\OneDrive.exe")),
            Do = c => {
                Sh.Run(Sh.Sys32("taskkill.exe"), "/f /im OneDrive.exe", 10_000);
                // SysWOW64 — у Windows (SystemX86), не из %SystemRoot%: переменную пользователь может переопределить
                foreach (var p in new[] { Sh.Sys32("OneDriveSetup.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "OneDriveSetup.exe") })
                    if (File.Exists(p)) { Sh.Run(p, "/uninstall", 300_000); break; }
                Sh.Run(Apps.Exe, "uninstall --id Microsoft.OneDrive --silent --accept-source-agreements", 300_000);
                if (File.Exists(Path.Combine(CleanupPaths.Profile, @"AppData\Local\Microsoft\OneDrive\OneDrive.exe")) ||
                    File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft OneDrive\OneDrive.exe")))
                    throw new IOException("OneDrive остался на компьютере. Удаление не завершено.");
            } },
    };

    /// <summary>«Значения нет» — так Windows и стоит по умолчанию.</summary>
    internal static readonly object Absent = new();

    /// <summary>
    /// Значения Windows по умолчанию для настроек вне Policies — чтобы выключить настройку, включённую до Merno
    /// или в самой Windows (копии «как было» тогда нет). Только то, что одинаково на всех компьютерах:
    /// VBS, HAGS и залипание клавиш зависят от железа и прежних настроек — их здесь нет намеренно.
    /// </summary>
    static readonly Dictionary<string, object> WinDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        [K(LM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff")] = Absent,
        [K(LM, MM, "SystemResponsiveness")] = 20L,
        [K(LM, MM + @"\Tasks\Games", "Scheduling Category")] = "Medium",
        [K(LM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate")] = 0x80000002L,
        [K(CU, @"System\GameConfigStore", "GameDVR_Enabled")] = 1L,
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled")] = Absent,
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled")] = Absent,
        [K(CU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled")] = Absent,
        [K(CU, @"Software\Microsoft\GameBar", "AllowAutoGameMode")] = Absent,
        [K(CU, @"Control Panel\Mouse", "MouseSpeed")] = "1",
        [K(CU, @"Control Panel\Mouse", "MouseThreshold1")] = "6",
        [K(CU, @"Control Panel\Mouse", "MouseThreshold2")] = "10",
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled")] = 1L,
        [K(CU, CDM, "SilentInstalledAppsEnabled")] = 1L,
        [K(CU, CDM, "SystemPaneSuggestionsEnabled")] = 1L,
        [K(CU, CDM, "SoftLandingEnabled")] = 1L,
        [K(CU, CDM, "PreInstalledAppsEnabled")] = 1L,
        [K(CU, CDM, "OemPreInstalledAppsEnabled")] = 1L,
        [K(CU, CDM, "RotatingLockScreenOverlayEnabled")] = 1L,
        [K(CU, CDM, "SubscribedContent-338387Enabled")] = 1L,
        [K(CU, CDM, "SubscribedContent-338388Enabled")] = 1L,
        [K(CU, CDM, "SubscribedContent-338389Enabled")] = 1L,
        [K(CU, CDM, "SubscribedContent-310093Enabled")] = 1L,
        [K(CU, CDM, "SubscribedContent-353694Enabled")] = 1L,
        [K(CU, CDM, "SubscribedContent-353696Enabled")] = 1L,
        [K(CU, ADV, "Start_IrisRecommendations")] = Absent,
        [K(CU, ADV, "ShowSyncProviderNotifications")] = Absent,
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled")] = Absent,
        [K(CU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod")] = Absent,
        [K(CU, @"Control Panel\Desktop", "MenuShowDelay")] = "400",
        [K(CU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate")] = "1",
        [K(CU, ADV, "TaskbarAnimations")] = 1L,
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency")] = 1L,
        [K(CU, ADV + @"\TaskbarDeveloperSettings", "TaskbarEndTask")] = Absent,
        [K(CU, ADV, "TaskbarAl")] = Absent,
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}")] = Absent,
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}")] = Absent,
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme")] = 1L,
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme")] = 1L,
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode")] = Absent,
        [K(CU, ADV, "ShowTaskViewButton")] = Absent,
        [K(CU, ADV, "HideFileExt")] = 1L,
        [K(CU, ADV, "LaunchTo")] = Absent,
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent")] = Absent,
        [K(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent")] = Absent,
        [K(CU, ADV, "Start_TrackDocs")] = Absent,
        [K(CU, ADV, "AutoCheckSelect")] = 0L,
        [K(CU, ADV, "Hidden")] = 2L,
    };
    static string K(string hive, string path, string name) => hive + "|" + path + "|" + name;

    /// <summary>
    /// Как вернуть настройку к виду Windows по умолчанию: (параметр, значение; Absent — удалить значение).
    /// null — нельзя: у настройки есть службы, задачи или своё действие, либо значение по умолчанию неизвестно.
    /// Политики (…\Policies\…) по умолчанию не заданы — их значение просто удаляется.
    /// </summary>
    public static List<(RegOp Op, object Value)> DefaultsPlan(Tweak t, SysInfo s)
    {
        if (t == null || !t.CanRevert || t.Do != null || t.Svc.Count > 0 || t.Tasks.Count > 0) return null;
        var plan = new List<(RegOp, object)>();
        foreach (var op in t.Reg.Where(o => o.Applicable(s)))
        {
            if (op.ClearBits != 0) return null;
            if (op.Path.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase)) plan.Add((op, Absent));
            else if (WinDefaults.TryGetValue(K(op.Hive, op.Path, op.Name), out var v)) plan.Add((op, v));
            else return null;
        }
        return plan.Count > 0 ? plan : null;
    }

    public static IEnumerable<string> Preset(string name, List<Tweak> all) => name switch
    {
        "gaming" => all.Where(t => t.Game && t.CanRevert && t.Risk < 2).Select(t => t.Id),
        "minimal" => all.Where(t => t.Min && t.CanRevert && t.Risk < 2).Select(t => t.Id),
        _ => all.Where(t => t.Rec && t.CanRevert && t.Risk < 2).Select(t => t.Id)
    };
}
