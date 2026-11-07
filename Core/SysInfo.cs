// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Management;
using System.Text;

namespace Merno.Core;

public class Issue
{
    public string Kind { get; set; }
    public string DeviceId { get; set; }
    public string DriverVersion { get; set; }
    public string Text { get; set; }
    public string Advice { get; set; }
    public int Severity { get; set; } // 0 — инфо/хорошо, 1 — совет, 2 — важно
    /// <summary>Где это исправить в Merno: раздел (как в ShowPageByName) и элемент, который подсветить. null — исправляется не в Merno.</summary>
    public string Page { get; set; }
    public string Target { get; set; }
}

/// <summary>Сведения о компьютере (WMI + реестр).</summary>
public class SysInfo
{
    public string Computer, Board, Bios, OS, Edition, DisplayVersion, Cpu, Gpu, GpuDriver, Screens, Disks, Volumes;
    /// <summary>Сетевые адаптеры: (название, MAC, IPv4). В ToText не попадает — по MAC можно узнать компьютер.</summary>
    public List<(string Name, string Mac, string Ip)> Nics = new();
    /// <summary>Значение SysDisk, когда тип диска определить не удалось.</summary>
    public const string UnknownDisk = "Неизвестно";
    public string SysDisk = UnknownDisk, SysBus = "", PowerPlan = "", Vbs = "Неизвестно", Hvci = "Неизвестно", SecureBoot = "Неизвестно", Tpm = "Неизвестно";
    public int Build, Ubr, Cores, Threads, RamGB, RamSticks, RamSpeed, RamMaxSpeed;
    /// <summary>Тип памяти (DDR4/DDR5…) и модели планок — по модели можно найти тайминги (сами тайминги Windows не отдаёт).</summary>
    public string RamType = "", RamModels = "";
    public DateTime InstallDate, LastBoot;
    public bool Laptop, IsVM, HasDGpu, IsNvidia, IsAmd;
    public bool IsWin11 => Build >= 22000;
    public bool IsHome => (Edition ?? "").StartsWith("Core", StringComparison.OrdinalIgnoreCase);
    /// <summary>Редакции с документированной поддержкой управляемых политик. Неизвестная редакция не считается Pro.</summary>
    public bool HasManagedPolicies => (Edition ?? "").StartsWith("Professional", StringComparison.OrdinalIgnoreCase)
        || (Edition ?? "").StartsWith("Enterprise", StringComparison.OrdinalIgnoreCase)
        || (Edition ?? "").StartsWith("Education", StringComparison.OrdinalIgnoreCase)
        || (Edition ?? "").StartsWith("IoTEnterprise", StringComparison.OrdinalIgnoreCase);
    public bool HasEnterprisePolicies => HasManagedPolicies && !(Edition ?? "").StartsWith("Professional", StringComparison.OrdinalIgnoreCase);
    public bool SupportsRecallPolicy => HasManagedPolicies && (Build > 26100 || Build == 26100 && Ubr >= 3915);
    public bool SupportsLegacyCopilotPolicy => HasManagedPolicies &&
        (Build is 19044 or 19045 && Ubr >= 3758 || Build == 22621 && Ubr >= 2361 || Build >= 22631);
    public DateTime? GpuDriverDate;
    /// <summary>Версии драйверов NVIDIA в привычном виде (566.36) — по одной на видеокарту.</summary>
    public List<string> GpuNvVersions = new();
    public List<GpuDriverState> GpuDriverStates = new();
    public List<Issue> Issues = new();

    // все полученные объекты WMI освобождаются в конце Collect
    [ThreadStatic] private static List<ManagementObject> _bag;

    private static IEnumerable<ManagementObject> Q(string query, string scope = @"root\cimv2")
    {
        try
        {
            using var s = new ManagementObjectSearcher(scope, query);
            using var col = s.Get();
            var list = col.Cast<ManagementObject>().ToList();
            _bag?.AddRange(list);
            return list;
        }
        catch { return Enumerable.Empty<ManagementObject>(); }
    }

    public static string Plural(int n, string one, string few, string many)
    {
        int m100 = Math.Abs(n) % 100, m10 = m100 % 10;
        if (m100 is >= 11 and <= 14) return many;
        return m10 == 1 ? one : m10 is >= 2 and <= 4 ? few : many;
    }

    static string Uptime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays} дн. {t.Hours} ч";
        if (t.TotalHours >= 1) return $"{t.Hours} ч {t.Minutes} мин";
        return $"{t.Minutes} мин";
    }
    private static string S(ManagementObject o, string p) { try { return o[p]?.ToString()?.Trim() ?? ""; } catch { return ""; } }
    private static long L(ManagementObject o, string p) { try { return Convert.ToInt64(o[p] ?? 0); } catch { return 0; } }
    /// <summary>Windows хранит версию драйвера в своём формате (32.0.16.1088). У NVIDIA привычный номер — последние 5 цифр: 610.88.</summary>
    private static string VendorDriver(string name, string ver)
    {
        if (string.IsNullOrEmpty(ver)) return "?";
        if (name != null && name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            var parts = ver.Split('.');
            if (parts.Length == 4)
            {
                var digits = parts[2] + parts[3].PadLeft(4, '0');
                if (digits.Length >= 5) { var d = digits[^5..]; return $"{d[..3]}.{d[3..]} (в Windows: {ver})"; }
            }
        }
        return ver;
    }
    private static DateTime? D(ManagementObject o, string p)
    {
        try { var v = o[p]?.ToString(); return string.IsNullOrEmpty(v) ? null : ManagementDateTimeConverter.ToDateTime(v); } catch { return null; }
    }

    public static SysInfo Collect()
    {
        var prev = _bag;
        _bag = new List<ManagementObject>();
        try { return CollectCore(); }
        finally
        {
            foreach (var o in _bag) { try { o.Dispose(); } catch { } }
            _bag = prev;
        }
    }

    static SysInfo CollectCore()
    {
        var s = new SysInfo();
        var cs = Q("SELECT * FROM Win32_ComputerSystem").FirstOrDefault();
        var os = Q("SELECT * FROM Win32_OperatingSystem").FirstOrDefault();
        var cpu = Q("SELECT * FROM Win32_Processor").FirstOrDefault();
        var bios = Q("SELECT * FROM Win32_BIOS").FirstOrDefault();
        var mb = Q("SELECT * FROM Win32_BaseBoard").FirstOrDefault();

        if (cs != null)
        {
            s.Computer = $"{S(cs, "Manufacturer")} {S(cs, "Model")}".Trim();
            s.RamGB = (int)Math.Round(L(cs, "TotalPhysicalMemory") / 1073741824.0);
            s.Laptop = L(cs, "PCSystemType") == 2;
            string model = S(cs, "Model") + " " + S(cs, "Manufacturer");
            s.IsVM = System.Text.RegularExpressions.Regex.IsMatch(model, "Virtual|VMware|KVM|QEMU|VirtualBox|innotek|Xen|HVM", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        if (os != null)
        {
            s.OS = S(os, "Caption").Replace("Майкрософт ", "").Replace("Microsoft ", "");
            int.TryParse(S(os, "BuildNumber"), out s.Build);
            s.InstallDate = D(os, "InstallDate") ?? DateTime.MinValue;
            s.LastBoot = D(os, "LastBootUpTime") ?? DateTime.Now;
        }
        if (cpu != null)
        {
            s.Cpu = S(cpu, "Name");
            s.Cores = (int)L(cpu, "NumberOfCores");
            s.Threads = (int)L(cpu, "NumberOfLogicalProcessors");
        }
        if (bios != null) s.Bios = $"{S(bios, "SMBIOSBIOSVersion")} ({D(bios, "ReleaseDate")?.ToString("yyyy-MM-dd") ?? "?"})";
        if (mb != null) s.Board = $"{S(mb, "Manufacturer")} {S(mb, "Product")}".Trim();

        var cv = Reg.Get("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID");
        s.Edition = cv?.ToString() ?? "";
        s.DisplayVersion = Reg.Get("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion")?.ToString() ?? "";
        var ubr = Reg.Get("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");
        int.TryParse(ubr?.ToString(), out s.Ubr);
        s.OS = $"{s.OS} {s.DisplayVersion} (сборка {s.Build}.{ubr})";

        // ОЗУ
        var ram = Q("SELECT * FROM Win32_PhysicalMemory").ToList();
        s.RamSticks = ram.Count;
        if (ram.Count > 0) { s.RamSpeed = (int)L(ram[0], "ConfiguredClockSpeed"); s.RamMaxSpeed = (int)L(ram[0], "Speed"); }
        s.RamType = ram.Select(r => L(r, "SMBIOSMemoryType") switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => "" })
                       .FirstOrDefault(t => t != "") ?? "";
        s.RamModels = string.Join("\n", ram
            .Select(r => $"{S(r, "Manufacturer")} {S(r, "PartNumber")}".Trim())
            .Where(m => m.Length > 0 && !m.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key));

        // Видеокарты
        var gpus = Q("SELECT * FROM Win32_VideoController")
            .Where(g => !System.Text.RegularExpressions.Regex.IsMatch(S(g, "Name"), "Basic Display|Remote|Virtual|Parsec|Meta|Mirage|DisplayLink|Indirect", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        s.Gpu = string.Join(" + ", gpus.Select(g => S(g, "Name")));
        s.GpuDriverStates = gpus.Select(g => new GpuDriverState { DeviceId = S(g, "PNPDeviceID"), Name = S(g, "Name"),
            Version = S(g, "DriverVersion"), Date = D(g, "DriverDate") }).ToList();
        s.GpuDriver = string.Join("; ", gpus.Select(g => $"{S(g, "Name")}: {VendorDriver(S(g, "Name"), S(g, "DriverVersion"))} от {D(g, "DriverDate")?.ToString("dd.MM.yyyy") ?? "?"}"));
        s.GpuNvVersions = gpus.Where(g => S(g, "Name").Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                              .Select(g => Drivers.NvidiaVersion(S(g, "DriverVersion"))).Where(v => v != null).ToList();
        s.GpuDriverDate = gpus.Select(g => D(g, "DriverDate")).Where(d => d.HasValue).OrderBy(d => d).FirstOrDefault();
        // видеокарта сообщает только один режим, даже если мониторов два, — берём каждый монитор отдельно
        var mons = Display.Monitors();
        s.Screens = mons.Count > 0
            ? string.Join("; ", mons.Select(m => $"{m.Width}×{m.Height} @ {m.Now}\u00A0Гц" + (m.Primary && mons.Count > 1 ? " (основной)" : "")))
            : string.Join("; ", gpus.Where(g => L(g, "CurrentRefreshRate") > 0)
                .Select(g => $"{L(g, "CurrentHorizontalResolution")}×{L(g, "CurrentVerticalResolution")} @ {L(g, "CurrentRefreshRate")}\u00A0Гц"));
        s.IsNvidia = gpus.Any(g => S(g, "Name").Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        s.IsAmd = gpus.Any(g => System.Text.RegularExpressions.Regex.IsMatch(S(g, "Name"), "Radeon RX|Radeon Pro|Radeon (HD|R[579]) ", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        s.HasDGpu = s.IsNvidia || s.IsAmd || gpus.Any(g => System.Text.RegularExpressions.Regex.IsMatch(S(g, "Name"), "Arc A|Arc B", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        // Батарея
        // ИБП по USB тоже виден как Win32_Battery — на настольных ПК (PCSystemType 1/3/4/5/7) его не учитываем
        long pcType = cs != null ? L(cs, "PCSystemType") : 0;
        if (pcType is not (1 or 3 or 4 or 5 or 7) && Q("SELECT * FROM Win32_Battery").Any()) s.Laptop = true;

        // Диски
        var pdisks = Q("SELECT * FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage").ToList();
        string Media(ManagementObject d) => L(d, "MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => (L(d, "BusType") == 17 ? "SSD" : UnknownDisk) };
        string Bus(ManagementObject d) => L(d, "BusType") switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", _ => "" };
        s.Disks = string.Join("; ", pdisks.Select(d => $"{S(d, "FriendlyName")} [{Media(d)} {Bus(d)}, {L(d, "Size") / 1_000_000_000} ГБ]"));
        try
        {
            char sysLetter = Environment.GetFolderPath(Environment.SpecialFolder.Windows)[0];
            var part = Q("SELECT * FROM MSFT_Partition", @"root\Microsoft\Windows\Storage")
                .FirstOrDefault(p => string.Equals(S(p, "DriveLetter"), sysLetter.ToString(), StringComparison.OrdinalIgnoreCase));
            if (part != null)
            {
                string num = S(part, "DiskNumber");
                var sd = pdisks.FirstOrDefault(d => S(d, "DeviceId") == num);
                if (sd != null) { s.SysDisk = Media(sd); s.SysBus = Bus(sd); }
            }
        }
        catch { }
        s.Volumes = string.Join("; ", DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => $"{d.Name.TrimEnd('\\')}: {Sh.FreeOf(d.AvailableFreeSpace, d.TotalSize)}"));

        // Безопасность
        var dg = Q("SELECT * FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard").FirstOrDefault();
        if (dg != null)
        {
            s.Vbs = L(dg, "VirtualizationBasedSecurityStatus") switch { 2 => "Включена", 1 => "Настроена, не запущена", _ => "Выключена" };
            try
            {
                var run = dg["SecurityServicesRunning"] as Array;
                s.Hvci = run != null && run.Cast<object>().Any(x => Convert.ToInt32(x) == 2) ? "Включена" : "Выключена";
            }
            catch { }
        }
        var sb = Reg.Get("HKLM", @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        s.SecureBoot = sb is int sbi ? (sbi == 1 ? "Включена" : "Выключена") : "Нет (старый режим загрузки)";
        try
        {
            // настоящие адаптеры (проводной и Wi-Fi), без виртуальных VPN/VirtualBox/Hyper-V
            foreach (var n in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.NetworkInterfaceType is not (System.Net.NetworkInformation.NetworkInterfaceType.Ethernet or System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 or System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet)) continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(n.Description, "Virtual|VPN|TAP-|Hyper-V|VMware|Loopback|WAN Miniport|Bluetooth|Wintun|WireGuard|ZeroTier|Radmin|Hamachi", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                var b = n.GetPhysicalAddress().GetAddressBytes();
                if (b.Length != 6) continue;
                string ip = n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                    ? n.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? ""
                    : "";
                s.Nics.Add((n.Description, string.Join("-", b.Select(x => x.ToString("X2"))), ip));
            }
        }
        catch (Exception ex) { Log.Skip("Сеть: " + ex.Message); }

        var tpm = Q("SELECT * FROM Win32_Tpm", @"root\cimv2\Security\MicrosoftTpm").FirstOrDefault();
        s.Tpm = tpm != null ? $"Есть ({S(tpm, "SpecVersion").Split(',').FirstOrDefault()})" : "Нет или выключен";

        var plan = Q("SELECT * FROM Win32_PowerPlan WHERE IsActive=TRUE", @"root\cimv2\power").FirstOrDefault();
        if (plan != null) s.PowerPlan = S(plan, "ElementName");
        else
        {
            var (_, pp) = Sh.Run(Sh.Sys32("powercfg.exe"), "/getactivescheme", 10_000);
            var m = System.Text.RegularExpressions.Regex.Match(pp, @"\((.+)\)\s*$");
            s.PowerPlan = m.Success ? m.Groups[1].Value : "";
        }

        s.BuildIssues();
        return s;
    }

    private void BuildIssues()
    {
        Issues.Clear();
        if (RamSticks == 1 && !Laptop && !IsVM)
            Issues.Add(new Issue { Severity = 2, Text = "Установлена одна планка оперативной памяти", Advice = "Из-за этого игры идут заметно медленнее. Поставьте вторую такую же планку." });
        if (RamMaxSpeed > 0 && RamSpeed > 0 && RamSpeed < RamMaxSpeed)
            Issues.Add(new Issue { Severity = 1, Text = $"Память работает на {RamSpeed} МГц вместо {RamMaxSpeed}", Advice = "Память может работать быстрее. Включите в BIOS профиль памяти XMP или EXPO (название зависит от платы). Это бесплатно. Если после этого компьютер сбоит, выключите профиль обратно." });
        else if (RamSpeed is > 0 and <= 2666 && !Laptop && Build > 0)
            Issues.Add(new Issue { Severity = 1, Text = $"Память работает на {RamSpeed} МГц", Advice = "Возможно, память может работать быстрее. Проверьте, есть ли в BIOS профиль памяти XMP или EXPO, и включите его." });
        if (RamGB > 0 && RamGB < 8)
            Issues.Add(new Issue { Severity = 2, Text = $"Мало оперативной памяти: {RamGB} ГБ", Advice = "Программы и браузер будут работать медленно. Нужно хотя бы 8 ГБ, для игр лучше 16 ГБ." });
        if (SysDisk == "HDD")
            Issues.Add(new Issue { Severity = 2, Text = "Windows стоит на медленном диске", Advice = "Если перенести Windows на SSD, компьютер будет включаться и открывать программы в разы быстрее." });
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed && d.TotalSize > 20L << 30))
            if (d.AvailableFreeSpace < d.TotalSize / 10)
                Issues.Add(new Issue { Severity = 2, Text = $"На диске {d.Name.TrimEnd('\\')} свободно меньше 10 % ({d.AvailableFreeSpace >> 30} ГБ)", Page = "PageClean", Target = "CleanList", Advice = "Запустите очистку и удалите ненужные программы и игры." });
        // проверенный драйвер (свежий от NVIDIA, последний для GTX 900/10) — совет не нужен; старый 566.36 — нужен (Drivers.GoodDrivers)
        foreach (var driver in GpuDriverStates.Where(driver => HasDGpu && driver.Date < DateTime.Now.AddMonths(-12) &&
            !(driver.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) && Drivers.IsGood(Drivers.NvidiaVersion(driver.Version)))))
            Issues.Add(new Issue { Kind = "gpu_driver_age", DeviceId = driver.DeviceId, DriverVersion = driver.Version, Severity = 1,
                Text = $"Драйвер видеокарты старше года: {driver.Name}", Page = "PageDrivers", Target = "DrvCardGpu",
                Advice = "Проверьте доступные версии в разделе «Драйверы». Если оставляете текущую версию, отметьте «Оставить эту версию»." });
        if ((DateTime.Now - LastBoot).TotalDays > 7)
            Issues.Add(new Issue { Severity = 1, Text = $"Компьютер не перезагружался\n{(int)(DateTime.Now - LastBoot).TotalDays} дн.", Advice = "Обычное «Завершение работы» не перезагружает Windows полностью. Раз в несколько дней нажимайте «Перезагрузка»." });
        if (Issues.Count == 0)
            Issues.Add(new Issue { Severity = 0, Text = "Проблем с железом не найдено", Advice = "Всё в порядке." });
    }

    public string OneLine() =>
        $"{Cpu} • {RamGB} ГБ ОЗУ • {(string.IsNullOrEmpty(Gpu) ? "встроенная графика" : Gpu)} • {SysDisk} • {(Laptop ? "Ноутбук" : "Настольный")}";

    public string ToText()
    {
        var sb = new StringBuilder();
        void A(string k, string v) => sb.AppendLine($"{k,-17}{v}");
        A("Компьютер:", Computer);
        A("Плата / BIOS:", $"{Board} / {Bios}");
        A("Система:", $"{OS}, {Edition}");
        A("Установлена:", InstallDate == DateTime.MinValue ? "?" : InstallDate.ToString("dd.MM.yyyy"));
        A("Без перезагрузки:", Uptime(DateTime.Now - LastBoot));
        A("Процессор:", $"{Cpu} ({Cores} {Plural(Cores, "ядро", "ядра", "ядер")} / {Threads} {Plural(Threads, "поток", "потока", "потоков")})");
        A("ОЗУ:", $"{RamGB} ГБ, планок: {RamSticks}, {RamSpeed} МГц");
        A("Видеокарта:", Gpu);
        A("Драйвер видеокарты:", GpuDriver);
        A("Экран:", Screens);
        A("Диски:", Disks);
        A("Тома:", Volumes);
        A("Системный диск:", $"{SysDisk} {SysBus}");
        A("Тип:", (Laptop ? "Ноутбук" : "Настольный компьютер") + (IsVM ? " (виртуальная машина)" : ""));
        A("План питания:", PowerPlan);
        A("Безопасность:", $"VBS: {Vbs}; целостность памяти: {Hvci}; Secure Boot: {SecureBoot}; TPM: {Tpm}");
        sb.AppendLine();
        sb.AppendLine("ЗАМЕЧАНИЯ:");
        foreach (var i in Issues) sb.AppendLine($"  • {i.Text.Replace('\n', ' ').TrimEnd('.')}. {i.Advice}");
        return sb.ToString();
    }
}
