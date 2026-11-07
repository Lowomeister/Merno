// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.Json;

namespace Merno.Core;

public class DeviceProblem { public string Name, Class, Problem, HwId, Maker; public int Code; }
public class DriverInfo { public string Device, DeviceId, Class, Version, Date, Provider, Inf; }
public class DriverUpdate { public string Id, Title, Class, Date, Size, Raw; }

/// <summary>Драйверы: проблемные устройства, установленные драйверы и обновления через Центр обновления Windows.</summary>
public static class Drivers
{
    static List<JsonElement> Json(string output)
    {
        var res = new List<JsonElement>();
        var s = output?.Trim();
        if (string.IsNullOrEmpty(s) || (s[0] != '[' && s[0] != '{')) return res;
        try
        {
            using var doc = JsonDocument.Parse(s);
            if (doc.RootElement.ValueKind == JsonValueKind.Array) foreach (var e in doc.RootElement.EnumerateArray()) res.Add(e.Clone());
            else res.Add(doc.RootElement.Clone());
        }
        catch (Exception ex) { Log.Warn("Драйверы: не удалось получить список: "
 + ex.Message); }
        return res;
    }
    static string Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";

    // ---------- человеческие названия вместо кодов оборудования ----------
    static readonly System.Text.RegularExpressions.Regex TechRx = new(
        @"\\|VEN_|DEV_|VID_|PID_|^(ACPI|PCI|USB|HID|HDAUDIO|ROOT|SWD|BTH|BTHENUM|DISPLAY|SCSI|STORAGE|INTELAUDIO)\b|\{[0-9A-Fa-f-]{8,}\}",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex GenericRx = new(
        @"^(PCI[ -]?(Device|устройство)|Unknown( device)?|Неизвестное устройство|Базовое системное устройство|Base System Device|USB Device|Устройство USB|Запоминающее устройство|PCI Simple Communications Controller|PCI Memory Controller|SM Bus Controller|Other PCI Bridge Device)\.?$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Название похоже на код оборудования, а не на имя.</summary>
    public static bool IsTechName(string s) => string.IsNullOrWhiteSpace(s) || TechRx.IsMatch(s.Trim()) || GenericRx.IsMatch(s.Trim());

    static string ClassLabel(string cls, string id)
    {
        string c = (cls ?? "").Trim().ToLowerInvariant();
        string i = (id ?? "").TrimStart().ToUpperInvariant();
        if (c.Length == 0)
            c = i.StartsWith("HDAUDIO") || i.StartsWith("INTELAUDIO") ? "media" : i.StartsWith("USB") ? "usb" : i.StartsWith("ACPI") ? "system"
              : i.StartsWith("BTH") ? "bluetooth" : i.StartsWith("HID") ? "hidclass" : "";
        return c switch
        {
            "media" or "audioendpoint" => "Звуковое устройство",
            "net" => "Сетевой адаптер",
            "display" => "Видеокарта",
            "usb" => "Устройство USB",
            "system" or "firmware" or "softwarecomponent" or "extension" or "processor" => "Системное устройство",
            "bluetooth" => "Устройство Bluetooth",
            "hidclass" or "keyboard" or "mouse" => "Устройство ввода",
            "camera" or "image" => "Камера",
            "monitor" => "Монитор",
            "printer" or "printqueue" => "Принтер",
            "diskdrive" or "scsiadapter" or "hdc" => "Диск",
            _ => "Неизвестное устройство"
        };
    }

    static readonly string[] VendorJunk = { "(R)", "(TM)", "®", "™", "Corporation", "Corp.", "Corp", "Semiconductor", "Co., Ltd.", "Co.,Ltd.", "Co.,Ltd", "Co. Ltd.", "Co., Ltd",
        "Inc.", "Inc", "Incorporated", "Technologies", "Technology", "Limited", "Ltd.", "Ltd", "GmbH", "International", "Electronics", "Microelectronics" };

    /// <summary>«Realtek Semiconductor Corp.» → «Realtek». Пусто — производитель неизвестен или это сама Windows.</summary>
    public static string NiceVendor(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        string s = v.Trim();
        if (s.StartsWith("(") || s.Contains("Standard", StringComparison.OrdinalIgnoreCase) || s.Contains("Стандарт", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) || s.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase) || TechRx.IsMatch(s)) return "";
        foreach (var j in VendorJunk) s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<![\w])" + System.Text.RegularExpressions.Regex.Escape(j) + @"(?![\w])", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s.Replace(",", " "), @"\s+", " ").Trim(' ', '.', '-', ',');
        if (s.Length == 0) return "";
        string l = s.ToLowerInvariant();
        if (l.StartsWith("advanced micro devices") || l == "amd") return "AMD";
        if (l == "intel") return "Intel";
        if (l == "nvidia") return "NVIDIA";
        if (l.StartsWith("realtek")) return "Realtek";
        if (l.StartsWith("qualcomm")) return "Qualcomm";
        if (l.StartsWith("mediatek")) return "MediaTek";
        if (l.StartsWith("asustek")) return "ASUS";
        return s;
    }

    /// <summary>Понятное название устройства: своё имя, если оно нормальное, иначе по классу и производителю.</summary>
    public static string NiceDeviceName(string name, string cls, string vendor, string deviceId)
    {
        if (!IsTechName(name)) return name.Trim();
        string label = ClassLabel(cls, string.IsNullOrWhiteSpace(deviceId) ? name : deviceId);
        string v = NiceVendor(vendor);
        return v.Length > 0 ? $"{label} ({v})" : label;
    }

    static string UpdateKind(string cls) => (cls ?? "").Trim().ToLowerInvariant() switch
    {
        "media" or "audioendpoint" => "драйвер звука",
        "net" => "драйвер сети",
        "display" => "драйвер видеокарты",
        "bluetooth" => "драйвер Bluetooth",
        "usb" => "драйвер USB",
        "firmware" => "обновление прошивки",
        "camera" or "image" => "драйвер камеры",
        "hidclass" or "keyboard" or "mouse" => "драйвер устройства ввода",
        "monitor" => "драйвер монитора",
        "printer" or "printqueue" => "драйвер принтера",
        "scsiadapter" or "hdc" or "diskdrive" => "драйвер диска",
        "system" or "extension" or "softwarecomponent" or "processor" or "" => "системный драйвер",
        _ => null
    };

    /// <summary>«Realtek Semiconductor Corp. - Extension - 10.0.22621.1» → «Realtek — системный драйвер».</summary>
    public static string NiceUpdateTitle(string title, string cls)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Драйвер";
        var parts = title.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        // версия в конце: «10.0.22621.1», «v1.2.3»
        if (parts.Count > 1 && System.Text.RegularExpressions.Regex.IsMatch(parts[^1], @"^v?\d+(\.\d+){1,}\S*$")) parts.RemoveAt(parts.Count - 1);
        string vendor = parts.Count > 0 ? NiceVendor(parts[0]) : "";
        string kind = UpdateKind(cls);
        string mid = parts.Count > 1 ? parts[1] : null;
        // середина — не класс, а название изделия («Intel(R) Wi-Fi 6 AX201») — тогда оставляем его
        if (mid != null && UpdateKind(mid) == null && !TechRx.IsMatch(mid) && !System.Text.RegularExpressions.Regex.IsMatch(mid, @"^(HIDClass|SCSIAdapter|Ports|Sensor|Printer|PrintQueue|Net|Media|Display|Bluetooth|Camera|Image|Monitor|System|Extension|Firmware|USB|Processor|SoftwareComponent|HDC|DiskDrive|Keyboard|Mouse|Biometric|WPD|Volume|MEDIA|AudioEndpoint)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return vendor.Length > 0 && !mid.StartsWith(vendor, StringComparison.OrdinalIgnoreCase) ? $"{vendor}: {mid}" : mid;
        if ((kind == null || string.IsNullOrWhiteSpace(cls)) && mid != null) kind = UpdateKind(mid) ?? kind;
        kind ??= "драйвер";
        if (vendor.Length == 0) return char.ToUpperInvariant(kind[0]) + kind[1..];
        return $"{vendor}: {kind}";
    }

    /// <summary>Название обновления Windows без кодов KB и «для систем x64».</summary>
    public static string CleanWinTitle(string t)
    {
        if (string.IsNullOrWhiteSpace(t)) return t;
        var rx = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        string s = System.Text.RegularExpressions.Regex.Replace(t, @"\s*[-–]?\s*\(?\bKB\s?\d{6,8}\b\)?", "", rx);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+(for|для)\s+(систем\s+на\s+базе\s+процессоров\s+)?(x64|x86|ARM64)(-based Systems)?", "", rx);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\((Version|Версия)\s[\d.]+\)", "", rx);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\(\s*\)", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(\s*[—–-]\s*){2,}", ", ");   // «— —» после вырезанного KB
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '–', '—', ',');
        return s.Length > 0 ? s : t;
    }

    // Коды ошибок Диспетчера устройств, понятные человеку
    static string ProblemText(int code) => code switch
    {
        28 => "Драйвер не установлен",
        1 or 18 => "Драйвер работает неправильно, переустановите его",
        10 => "Устройство не запускается",
        22 => "Устройство отключено",
        31 or 39 => "Драйвер повреждён или не установлен",
        43 => "Устройство сломалось или работает с ошибкой",
        45 => "Устройство сейчас не подключено",
        52 => "Windows не может проверить цифровую подпись драйвера",
        _ => "Устройство работает с ошибкой"
    };

    /// <summary>Устройства с жёлтым значком в Диспетчере устройств.</summary>
    public static List<DeviceProblem> Problems()
    {
        var (_, o) = Sh.PS(@"Get-CimInstance Win32_PnPEntity | Where-Object { $_.ConfigManagerErrorCode -ne 0 -and $_.ConfigManagerErrorCode -ne 45 -and $_.ConfigManagerErrorCode -ne 22 } |
            Select-Object Name, DeviceID, Manufacturer, PNPClass, ConfigManagerErrorCode | ConvertTo-Json -Compress", 90_000);
        return Json(o).Select(e =>
        {
            int code = int.TryParse(Str(e, "ConfigManagerErrorCode"), out var c) ? c : 0;
            var (hw, maker) = HwId(Str(e, "DeviceID"));
            return new DeviceProblem { Name = NiceDeviceName(Str(e, "Name"), Str(e, "PNPClass"), Str(e, "Manufacturer"), Str(e, "DeviceID")), Class = Str(e, "PNPClass"), Code = code, Problem = ProblemText(code), HwId = hw, Maker = maker };
        }).ToList();
    }

    // Самые частые производители по коду PCI/USB. Остальное человек найдёт поиском по коду устройства.
    static readonly Dictionary<string, string> Makers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["8086"] = "Intel", ["8087"] = "Intel", ["10DE"] = "NVIDIA", ["1002"] = "AMD", ["1022"] = "AMD",
        ["10EC"] = "Realtek", ["0BDA"] = "Realtek", ["14E4"] = "Broadcom", ["0A5C"] = "Broadcom",
        ["168C"] = "Qualcomm Atheros", ["17CB"] = "Qualcomm", ["0CF3"] = "Qualcomm Atheros", ["14C3"] = "MediaTek", ["0E8D"] = "MediaTek",
        ["1B21"] = "ASMedia", ["1106"] = "VIA", ["197B"] = "JMicron", ["1217"] = "O2Micro", ["1180"] = "Ricoh",
        ["1969"] = "Qualcomm Atheros (Killer)", ["1912"] = "Renesas", ["144D"] = "Samsung", ["15B7"] = "SanDisk / WD", ["1C5C"] = "SK hynix",
        ["046D"] = "Logitech", ["1532"] = "Razer", ["1038"] = "SteelSeries", ["0951"] = "HyperX", ["1B1C"] = "Corsair", ["04F3"] = "Elan", ["06CB"] = "Synaptics",
        ["80EE"] = "VirtualBox", ["15AD"] = "VMware", ["1AF4"] = "Red Hat VirtIO", ["1414"] = "Microsoft Hyper-V",
    };

    /// <summary>Код устройства для поиска драйвера («PCI\VEN_8086&amp;DEV_A370» → «VEN_8086&amp;DEV_A370») и производитель по нему.</summary>
    internal static (string Id, string Maker) HwId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return ("", "");
        var m = System.Text.RegularExpressions.Regex.Match(deviceId, @"(?:VEN|VID)_([0-9A-F]{4})&(?:DEV|PID)_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) return (m.Value.ToUpperInvariant(), Makers.TryGetValue(m.Groups[1].Value, out var mk) ? mk : "");
        // ACPI\INT33A1, HID\ELAN0001 — код без производителя в таблице, но поиск по нему тоже работает
        var a = System.Text.RegularExpressions.Regex.Match(deviceId, @"^(?:ACPI|HID)\\([A-Z0-9]{4,8})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return a.Success ? (a.Groups[1].Value.ToUpperInvariant(), "") : ("", "");
    }

    /// <summary>Поиск драйвера по коду устройства (без Яндекса).</summary>
    public static string HwSearchUrl(string hwId) => "https://www.google.com/search?q=" + Uri.EscapeDataString(hwId + " driver");

    /// <summary>Драйверы главных устройств: видеокарта, сеть, звук, Bluetooth.</summary>
    public static List<DriverInfo> Installed() => InstalledOrNull() ?? new();

    /// <summary>Ответ ConvertTo-Json годится: пусто при коде 0 (ничего не нашлось) или JSON.</summary>
    internal static bool ListAnswerOk(int code, string output)
    {
        string t = (output ?? "").Trim();
        if (t.Length == 0) return code == 0;
        return t[0] == '[' || t[0] == '{';
    }

    /// <summary>null — список получить не удалось (PowerShell не ответил или вернул не JSON). Пустой список — драйверов
    /// не от Microsoft нет: это не ошибка.</summary>
    public static List<DriverInfo> InstalledOrNull()
    {
        var (code, o) = Sh.PS(@"Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceClass -in 'DISPLAY','NET','MEDIA','BLUETOOTH' -and $_.DeviceName -and $_.DriverProviderName -ne 'Microsoft' } |
            Select-Object DeviceName, DeviceID, DeviceClass, DriverVersion, @{n='Date';e={ if ($_.DriverDate) { $_.DriverDate.ToString('dd.MM.yyyy') } else { '' } }}, DriverProviderName, InfName |
            Sort-Object DeviceClass, DeviceName, DeviceID -Unique | ConvertTo-Json -Compress", 90_000);
        if (!ListAnswerOk(code, o)) return null;
        string Cls(string c) => c switch { "DISPLAY" => "Видеокарта", "NET" => "Сеть", "MEDIA" => "Звук", "BLUETOOTH" => "Bluetooth", _ => c };
        return Json(o).Select(e => new DriverInfo
        {
            Device = Str(e, "DeviceName"), DeviceId = Str(e, "DeviceID"), Class = Cls(Str(e, "DeviceClass")), Version = Str(e, "DriverVersion"),
            Date = Str(e, "Date"), Provider = Str(e, "DriverProviderName"), Inf = Str(e, "InfName")
        }).ToList();
    }

    /// <summary>Материнская плата (или модель ноутбука) и ссылка на поиск драйверов на сайте производителя.</summary>
    public static (string Title, string Url) Board(bool laptop)
    {
        var (_, o) = Sh.PS(@"$b = Get-CimInstance Win32_BaseBoard | Select-Object -First 1
$p = Get-CimInstance Win32_ComputerSystemProduct | Select-Object -First 1
'B:' + $b.Manufacturer + '|' + $b.Product
'P:' + $p.Vendor + '|' + $p.Name", 30_000);
        string Line(string pre) => o.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(pre))?[pre.Length..] ?? "|";
        var b = Line("B:").Split('|'); var p = Line("P:").Split('|');
        // у ноутбуков драйверы ищут по модели ноутбука, у ПК — по плате
        string maker = (laptop ? p[0] : b[0]).Trim(), model = (laptop ? p.ElementAtOrDefault(1) : b.ElementAtOrDefault(1))?.Trim() ?? "";
        bool junk(string s) => string.IsNullOrWhiteSpace(s) || s.Contains("O.E.M", StringComparison.OrdinalIgnoreCase) || s.Contains("To be filled", StringComparison.OrdinalIgnoreCase)
                               || s.Equals("Default string", StringComparison.OrdinalIgnoreCase) || s.Equals("System Product Name", StringComparison.OrdinalIgnoreCase);
        if (junk(model)) { maker = b[0].Trim(); model = b.ElementAtOrDefault(1)?.Trim() ?? ""; }
        if (junk(model)) return (null, null);
        string m = maker.ToLowerInvariant();
        string site = m.Contains("asus") ? "asus.com" : m.Contains("micro-star") || m.Contains("msi") ? "msi.com" : m.Contains("gigabyte") ? "gigabyte.com"
                    : m.Contains("asrock") ? "asrock.com" : m.Contains("lenovo") ? "lenovo.com" : m.StartsWith("hp") || m.Contains("hewlett") ? "hp.com"
                    : m.Contains("dell") ? "dell.com" : m.Contains("acer") ? "acer.com" : m.Contains("huawei") ? "consumer.huawei.com" : null;
        string nice = (maker.Replace("Micro-Star International Co., Ltd.", "MSI").Replace("ASUSTeK COMPUTER INC.", "ASUS").Replace("Gigabyte Technology Co., Ltd.", "Gigabyte")
                      + " " + model).Trim();
        string q = Uri.EscapeDataString($"{nice} drivers download" + (site != null ? $" site:{site}" : ""));
        return (nice, "https://www.google.com/search?q=" + q);
    }

    /// <summary>Wheel — руль, педали, КПП: показываются отдельной карточкой.</summary>
    public sealed record GearApp(string Brand, string App, string Url, bool Wheel = false);

    static string Find(string q) => "https://www.google.com/search?q=" + Uri.EscapeDataString(q);

    // рули: у Logitech тот же VID, что у мышей, — руль узнаём по коду модели (PID)
    static readonly string[] LogiWheelPids = { "C24F", "C260", "C261", "C262", "C266", "C267", "C268", "C26D", "C26E", "C272", "C294", "C298", "C299", "C29A", "C29B", "C29C" };
    static readonly GearApp LogiWheel = new("Logitech", "Logitech G HUB (руль)", "https://www.logitechg.com/innovation/g-hub.html", true);
    static readonly (string Vid, GearApp App)[] WheelByVid =
    {
        ("346E", new("MOZA Racing", "MOZA Pit House", Find("MOZA Pit House download site:mozaracing.com"), true)),
        ("0EB7", new("Fanatec", "Драйвер Fanatec", Find("Fanatec wheel driver download site:fanatec.com"), true)),
        ("044F", new("Thrustmaster", "Драйвер Thrustmaster", "https://support.thrustmaster.com/", true)),
        ("2433", new("Asetek SimSports", "Asetek RaceHub", Find("Asetek RaceHub download"), true)),
    };
    // у этих брендов чипы общие (STMicro и т. п.) — узнаём по названию устройства
    static readonly (string Name, GearApp App)[] WheelByName =
    {
        ("Simagic", new("Simagic", "SimPro Manager", Find("Simagic SimPro Manager download"), true)),
        ("Simucube", new("Simucube", "Simucube Tuner", Find("Simucube Tuner download site:simucube.com"), true)),
        ("Cammus", new("Cammus", "Программа Cammus", Find("Cammus wheel software download"), true)),
        ("PXN", new("PXN", "Программа PXN", Find("PXN wheel driver download"), true)),
        ("Ardor Silverstone", new("Ardor Gaming", "Программа руля Ardor", Find("Ardor Gaming Silverstone driver download"), true)),
        ("Heusinkveld", new("Heusinkveld", "SmartControl", Find("Heusinkveld SmartControl download"), true)),
        ("Moza", new("MOZA Racing", "MOZA Pit House", Find("MOZA Pit House download site:mozaracing.com"), true)),
    };

    // код производителя USB (VID) → фирменная программа. HP (03F0) не берём: под ним и принтеры
    static readonly (string Vid, GearApp App)[] GearByVid =
    {
        ("046D", new("Logitech", "Logitech G HUB", "https://www.logitechg.com/innovation/g-hub.html")),
        ("1532", new("Razer", "Razer Synapse", "https://www.razer.com/synapse-4")),
        ("1038", new("SteelSeries", "SteelSeries GG", "https://steelseries.com/gg")),
        ("1B1C", new("Corsair", "Corsair iCUE", "https://www.corsair.com/icue")),
        ("0951", new("HyperX", "HyperX NGENUITY", "https://hyperx.com/pages/ngenuity")),
        ("0B05", new("ASUS ROG", "Armoury Crate", "https://www.asus.com/supportonly/armoury%20crate/helpdesk_download/")),
        ("31E3", new("Wooting", "Wootility", "https://wooting.io/wootility")),
        ("09DA", new("Bloody (A4Tech)", "Bloody", "https://www.bloody.com/en/download.php")),
        ("1E7D", new("ROCCAT", "ROCCAT Swarm", "https://support.roccat.com/")),
    };
    // недорогие бренды часто на чужих чипах — узнаём по названию устройства
    static readonly (string Name, GearApp App)[] GearByName =
    {
        ("Dark Project", new("Dark Project", "Программа Dark Project", "https://darkproject.com/support/")),
        ("AULA", new("AULA", "Программа AULA", "https://www.google.com/search?q=" + Uri.EscapeDataString("AULA keyboard software download"))),
        ("Ardor", new("Ardor Gaming", "Программа Ardor Gaming", "https://www.google.com/search?q=" + Uri.EscapeDataString("Ardor Gaming software download"))),
        ("Glorious", new("Glorious", "Glorious CORE", "https://www.gloriousgaming.com/pages/glorious-core")),
        ("Redragon", new("Redragon", "Программа Redragon", "https://www.redragonzone.com/pages/download")),
        ("Zowie", new("ZOWIE", "ZOWIE (настройка на самой мышке)", "https://zowie.benq.com/ru-ru/support/download.html")),
    };

    /// <summary>Фирменные программы для подключённых мышей, клавиатур, наушников и рулей.</summary>
    public static List<GearApp> Gear()
    {
        var (_, o) = Sh.PS(@"Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -match '^(USB|HID)\\' -or $_.Class -in 'Mouse','Keyboard','HIDClass','AudioEndpoint','MEDIA' } |
  ForEach-Object { $_.InstanceId + '|' + $_.FriendlyName }", 60_000);
        var res = new List<GearApp>();
        foreach (var line in o.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string vid = m.Groups[1].Value.ToUpperInvariant(), pid = m.Groups[2].Value.ToUpperInvariant();
                if (vid == "046D" && LogiWheelPids.Contains(pid)) res.Add(LogiWheel);
                foreach (var (v, app) in GearByVid.Concat(WheelByVid))
                    if (vid == v) res.Add(app);
            }
            foreach (var (name, app) in GearByName.Concat(WheelByName))
                if (line.Contains(name, StringComparison.OrdinalIgnoreCase)) res.Add(app);
        }
        return res.DistinctBy(a => a.App).ToList();
    }

    /// <summary>Удалить сторонний драйвер (oemNN.inf) из системы. Устройство откатится на драйвер Windows. null — успех.</summary>
    public static string Remove(DriverInfo d)
    {
        if (string.IsNullOrEmpty(d.Inf) || !System.Text.RegularExpressions.Regex.IsMatch(d.Inf, @"^oem\d+\.inf$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return "Это драйвер Windows, его удалять не нужно.";
        Log.Write($"Удаляю драйвер: {d.Device} ({d.Inf}, {d.Provider} {d.Version})");
        var (code, o) = Sh.Run(Sh.Sys32("pnputil.exe"), $"/delete-driver {d.Inf} /uninstall /force", 180_000);
        // 3010 — готово, нужна перезагрузка
        if (code == 0 || code == 3010) { Log.Ok($"Драйвер удалён: {d.Device}"); return null; }
        Log.Warn($"Драйвер «{d.Device}» удалить не получилось: {o}");
        return "Windows не дала удалить драйвер. Попробуйте в безопасном режиме («Инструменты»).";
    }

    /// <summary>Поиск обновлений драйверов в Центре обновления Windows (может занять 1–3 минуты).</summary>
    public static (List<DriverUpdate> List, string Error) FindUpdates()
    {
        var (code, o) = Sh.PS(@"try {
  $s = New-Object -ComObject Microsoft.Update.Session
  $r = $s.CreateUpdateSearcher().Search(""IsInstalled=0 and Type='Driver' and IsHidden=0"")
  @($r.Updates | ForEach-Object { [pscustomobject]@{
      Id = $_.Identity.UpdateID; Title = $_.Title; Class = $_.DriverClass
      Date = $(if ($_.DriverVerDate) { ([datetime]$_.DriverVerDate).ToString('dd.MM.yyyy') } else { '' })
      Size = [math]::Round($_.MaxDownloadSize / 1MB, 1) } }) | ConvertTo-Json -Compress
} catch { 'ERR:' + $_.Exception.Message }", 300_000);
        if (o.StartsWith("ERR:")) return (new(), o[4..].Trim());
        if (code != 0 || (o.Length > 0 && o[0] != '[' && o[0] != '{')) return (new(), o == Sh.TimedOut ? "Windows долго не отвечает. Попробуйте позже" : (o.Length > 0 ? o : "Не удалось найти драйверы. Попробуйте позже"));
        var list = Json(o).Select(e => new DriverUpdate
        {
            Id = Str(e, "Id"), Raw = Str(e, "Title"), Title = NiceUpdateTitle(Str(e, "Title"), Str(e, "Class")), Class = Str(e, "Class"), Date = Str(e, "Date"),
            Size = double.TryParse(Str(e, "Size"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var mb) && mb > 0 ? $"{mb:0.#} МБ" : ""
        }).Where(u => !string.IsNullOrEmpty(u.Id)).ToList();
        return (list, null);
    }

    /// <summary>Скачать и установить выбранные обновления драйверов. Возвращает (сколько установлено, нужна ли перезагрузка, ошибка).</summary>
    public static (int Installed, bool Reboot, string Error) Install(IEnumerable<string> ids)
    {
        using var operation = SystemOperation.TryEnter();
        if (operation == null) return (0, false, "Очистка или другая установка ещё выполняется. Повторите позже.");
        // номера обновлений Windows — GUID; всё остальное в скрипт не пускаем (как в WinUpdate.InstallAll)
        var idList = string.Join(",", ids.Where(i => i != null && System.Text.RegularExpressions.Regex.IsMatch(i, "^[0-9a-fA-F-]{36}$")).Select(i => "'" + i + "'"));
        if (idList.Length == 0) return (0, false, null);
        Log.Write("Устанавливаю драйверы…");
        var (_, o) = Sh.PS($@"try {{
  $want = @({idList})
  $s = New-Object -ComObject Microsoft.Update.Session
  $r = $s.CreateUpdateSearcher().Search(""IsInstalled=0 and Type='Driver' and IsHidden=0"")
  $c = New-Object -ComObject Microsoft.Update.UpdateColl
  foreach ($u in $r.Updates) {{ if ($want -contains $u.Identity.UpdateID) {{ if (-not $u.EulaAccepted) {{ $u.AcceptEula() }}; [void]$c.Add($u) }} }}
  if ($c.Count -eq 0) {{ 'RES:0:0'; return }}
  $d = $s.CreateUpdateDownloader(); $d.Updates = $c; [void]$d.Download()
  $i = $s.CreateUpdateInstaller(); $i.Updates = $c; $res = $i.Install()
  $ok = 0; for ($k = 0; $k -lt $c.Count; $k++) {{ if ($res.GetUpdateResult($k).ResultCode -eq 2) {{ $ok++ }} }}
  'RES:' + $ok + ':' + [int]$res.RebootRequired
}} catch {{ 'ERR:' + $_.Exception.Message }}", 3_600_000);
        var line = o.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("RES:") || l.StartsWith("ERR:")) ?? "";
        if (line.Length == 0) line = "ERR:" + (o == Sh.TimedOut ? "Windows долго не отвечает. Попробуйте позже" : "Не удалось установить драйверы. Попробуйте позже");
        if (line.StartsWith("ERR:")) { Log.Warn("Драйверы: " + line[4..]); return (0, false, line[4..].Trim()); }
        var p = line.Split(':');
        int n = p.Length > 1 && int.TryParse(p[1], out var x) ? x : 0;
        bool reboot = p.Length > 2 && p[2] == "1";
        Log.Ok($"Установлено драйверов: {n}" + (reboot ? ", нужна перезагрузка" : ""));
        return (n, reboot, null);
    }

    /// <summary>Сайт драйверов производителя видеокарты.</summary>
    public static (string Vendor, string Url) GpuVendor(string gpu)
    {
        gpu ??= "";
        if (gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return ("NVIDIA", "https://www.nvidia.com/ru-ru/drivers/");
        if (gpu.Contains("AMD", StringComparison.OrdinalIgnoreCase) || gpu.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return ("AMD", "https://www.amd.com/ru/support/download/drivers.html");
        if (gpu.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return ("Intel", "https://www.intel.com/content/www/us/en/support/detect.html");
        return (null, null);
    }

    // ---------- драйвер NVIDIA под игры ----------
    // Политика: по умолчанию советуем СВЕЖИЙ стабильный драйвер. Старый 566.36 «ради FPS» —
    // только в расширенном режиме, последним и с честными минусами: нет исправлений безопасности, новые игры
    // и программы (OBS и др.) перестают с ним работать, прибавка FPS — по отзывам, не по замерам.
    // Самое полезное — список проблемных версий Bad: о нём предупреждаем всегда.
    // Версии:
    //  • 566.36 (декабрь 2024) — по отзывам игроков даёт больше FPS в CS2/Dota 2; поддерживает GTX 900 и новее, кроме RTX 50;
    //  • 617.42 (октябрь 2026, WHQL) — стабильный для новых игр;
    //  • 582.78 (октябрь 2026) — самый свежий для GTX 900/10 (Maxwell, Pascal): игровые драйверы для них больше не выходят,
    //    только исправления безопасности (последний игровой — 581.80).
    // С ноября 2026 новые игровые драйверы NVIDIA только для Windows 11 — 617.42 работает и на Windows 10.
    // Рекомендуемый берётся у NVIDIA сам (RefreshNvidia, LatestFor): самый свежий, вышедший не меньше StableAfterDays дней назад,
    // кроме проблемных для этой карты. NvNew — запасной, если NVIDIA не ответила и сохранённого списка нет.
    // Обновлять руками — 566.36 (FPS), 582.78 (GTX 900/10) и список проблемных версий Bad.
    public const string NvFps = "566.36", NvNew = "617.42", NvLegacyLast = "582.78";

    /// <summary>Проверенные драйверы: если стоит один из них, совет «драйвер старше года» не показываем.
    /// 566.36 сюда не входит: он старый, и честно сказать об этом лучше, чем молчать.
    /// Свежие драйверы, которые вернула NVIDIA (RefreshNvidia), тоже считаются проверенными.</summary>
    public static readonly HashSet<string> GoodDrivers = new() { NvNew, NvLegacyLast, "581.80", "591.86" };
    public static bool IsGood(string nvVersion) => nvVersion != null && (GoodDrivers.Contains(nvVersion) || (_nvLatest?.Any(r => r.Version == nvVersion) ?? false));

    /// <summary>Версии с известными проблемами. Gpus — для каких карт (regex), null — для всех.</summary>
    public sealed record BadDriver(string Version, string Gpus, string Why);
    public static readonly List<BadDriver> Bad = new()
    {
        // чёрный экран и зависания видео на RTX 50 (NVIDIA исправила это в бета-версии 621.12)
        new("621.01", @"RTX\s*50\d\d", "С RTX 50 этот драйвер даёт чёрный экран и зависания видео. NVIDIA признала ошибку. Поставьте другой из списка ниже."),
        new("595.59", null, "NVIDIA отозвала этот драйвер из-за ошибок. Поставьте другой из списка ниже."),
    };

    /// <summary>Чем плох этот драйвер для этой видеокарты (null — всё в порядке).</summary>
    public static string BadFor(string version, string gpu) =>
        Bad.FirstOrDefault(b => b.Version == version &&
            (b.Gpus == null || System.Text.RegularExpressions.Regex.IsMatch(gpu ?? "", b.Gpus, System.Text.RegularExpressions.RegexOptions.IgnoreCase)))?.Why;

    // ---------- свежие драйверы от NVIDIA ----------
    public sealed record NvRelease(string Version, DateTime Date);
    static List<NvRelease> _nvLatest;
    static DateTime _nvAt;
    // Тот же запрос, что делает страница загрузки драйверов NVIDIA (GeForce RTX 40, Windows 10/11 64-bit, WHQL, DCH).
    // Игровые драйверы для RTX 20/30/40/50 и GTX 16 выходят общими — номер версии один.
    const string NvApi = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php" +
                         "?func=DriverManualLookup&psid=127&pfid=995&osID=57&languageCode=1033&beta=0&isWHQL=1&dltype=-1&dch=1&upCRD=0&qnf=0&sort1=0&numberOfResults=5";
    static readonly System.Net.Http.HttpClient NvHttp = new() { Timeout = TimeSpan.FromSeconds(12) };

    /// <summary>Узнать у NVIDIA последние драйверы (раз в 6 часов). Нет интернета — берём сохранённый список.</summary>
    static DateTime _nvFailAt = DateTime.MinValue;

    public static void RefreshNvidia()
    {
        if (_nvLatest != null && DateTime.Now - _nvAt < TimeSpan.FromHours(6)) return;
        // сайт недавно не ответил — не ждём его снова при каждом открытии «Драйверов» (15 минут)
        if (DateTime.Now - _nvFailAt < TimeSpan.FromMinutes(15)) return;
        string cache = Path.Combine(Log.DataDir, "nvidia-drivers.json");
        try
        {
            var json = NvHttp.GetStringAsync(NvApi).GetAwaiter().GetResult();
            var list = ParseNv(json);
            if (list.Count > 0)
            {
                _nvLatest = list; _nvAt = DateTime.Now;
                try { Directory.CreateDirectory(Log.DataDir); File.WriteAllText(cache, json); } catch { }
                Log.Skip($"Драйверы NVIDIA: самый свежий {list[0].Version} от {list[0].Date:dd.MM.yyyy}");
                return;
            }
        }
        catch (Exception ex) { _nvFailAt = DateTime.Now; Log.Skip("Драйверы NVIDIA: сайт не ответил: " + ex.Message); }
        try { if (File.Exists(cache)) { var l = ParseNv(File.ReadAllText(cache)); if (l.Count > 0) { _nvLatest = l; _nvAt = DateTime.Now; } } } catch { }
    }

    static List<NvRelease> ParseNv(string json)
    {
        var res = new List<NvRelease>();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("IDS", out var ids) || ids.ValueKind != System.Text.Json.JsonValueKind.Array) return res;
        foreach (var it in ids.EnumerateArray())
        {
            if (!it.TryGetProperty("downloadInfo", out var di)) continue;
            string v = di.TryGetProperty("Version", out var vv) ? vv.GetString() : null;
            // номер идёт в адрес загрузки — берём только вид «621.01»
            if (v == null || !System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{3}\.\d{2}$")) continue;
            DateTime.TryParseExact(di.TryGetProperty("ReleaseDateTime", out var dd) ? dd.GetString() : "", "ddd MMM d, yyyy",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date);
            res.Add(new NvRelease(v, date));
        }
        return res;
    }

    /// <summary>Сколько дней драйвер должен «отлежаться», чтобы мы назвали его стабильным: ошибки свежих выпусков
    /// обычно всплывают в первые недели (так было с 621.01 — чёрный экран нашли через 3 дня).</summary>
    const int StableAfterDays = 14;

    /// <summary>Рекомендуемый драйвер для этой карты: самый свежий от NVIDIA, вышедший не меньше 2 недель назад
    /// и не из списка проблемных; таких нет — самый свежий непроблемный; NVIDIA не ответила — NvNew.</summary>
    static NvRelease LatestFor(string gpu)
    {
        var ok = _nvLatest?.Where(r => BadFor(r.Version, gpu) == null).ToList();
        return ok?.FirstOrDefault(r => r.Date != default && r.Date <= DateTime.Today.AddDays(-StableAfterDays))
            ?? ok?.FirstOrDefault()
            ?? new NvRelease(NvNew, new DateTime(2026, 10, 6));
    }



    public sealed record GameDriver(string Version, string Title, string Text, string Url);

    /// <summary>Версия драйвера NVIDIA из версии Windows: 32.0.15.6636 → 566.36.</summary>
    public static string NvidiaVersion(string winVersion)
    {
        var p = (winVersion ?? "").Split('.');
        if (p.Length < 4 || p[2].Length == 0 || p[3].Length != 4) return null;
        var d = p[2][^1] + p[3];          // «5» + «6636»
        return d[..3] + "." + d[3..];     // 566.36
    }

    static string NvUrl(string v, bool laptop) =>
        $"https://us.download.nvidia.com/Windows/{v}/{v}-{(laptop ? "notebook" : "desktop")}-win10-win11-64bit-international-dch-whql.exe";

    /// <summary>Что сказать тому, у кого стоит старый 566.36 (null — у вас не он).</summary>
    public static string OldFpsNote(string current, string gpu)
    {
        if (current != NvFps) return null;
        var nw = LatestFor(gpu ?? "");
        return "Драйвер декабря 2024 года: с тех пор для него нет исправлений безопасности, а новые игры и программы (например, свежие версии OBS) " +
               $"могут с ним не работать. Если ставили его ради FPS в CS2 или Dota 2 и всё устраивает, его можно оставить. Иначе обновитесь до {nw.Version}.";
    }

    /// <summary>
    /// Игровые драйверы для видеокарты NVIDIA, первым — рекомендуемый (свежий стабильный).
    /// expert — расширенный режим: тогда последним ещё и старый 566.36 «ради FPS» с честными минусами.
    /// Пусто — карта не NVIDIA или слишком старая/неизвестная.
    /// </summary>
    public static List<GameDriver> GameDrivers(string gpu, bool laptop, bool expert = false)
    {
        var res = new List<GameDriver>();
        gpu ??= "";
        if (!gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) && !gpu.Contains("GeForce", StringComparison.OrdinalIgnoreCase)) return res;
        bool M(string re) => System.Text.RegularExpressions.Regex.IsMatch(gpu, re, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var fps = new GameDriver(NvFps, "Для опытных: старый драйвер ради FPS",
            "Декабрь 2024 года. Часть игроков отмечает больше кадров в CS2 и Dota 2 (по отзывам, не по замерам). " +
            "Минусы: с тех пор для него нет исправлений безопасности, новые игры и программы (например, свежие версии OBS) могут не работать. Ставьте, только если знаете зачем.",
            NvUrl(NvFps, laptop));
        var nw = LatestFor(gpu);
        string when = nw.Date == default ? "" : nw.Date.ToString("dd.MM.yyyy");
        if (M(@"RTX\s*50\d\d"))
        {   // RTX 50 вышли позже 566.36 — со старым драйвером не работают
            res.Add(new GameDriver(nw.Version, "Рекомендуем: свежий стабильный", $"Свежий драйвер NVIDIA{(when != "" ? " от " + when : "")}: поддерживает новые игры и программы.", NvUrl(nw.Version, laptop)));
        }
        else if (M(@"RTX\s*[234]0\d\d|GTX\s*16\d\d|TITAN\s*RTX"))
        {
            res.Add(new GameDriver(nw.Version, "Рекомендуем: свежий стабильный", $"Свежий драйвер NVIDIA{(when != "" ? " от " + when : "")}: поддерживает новые игры и программы, есть исправления безопасности.", NvUrl(nw.Version, laptop)));
            if (expert) res.Add(fps);
        }
        else if (M(@"GTX\s*(9[5-8]0|10[5-8]0)|GT\s*10[13]0|TITAN\s*X|TITAN\s*V\b|GTX\s*745|GTX\s*750"))
        {
            res.Add(new GameDriver(NvLegacyLast, "Рекомендуем: самый свежий для вашей видеокарты",
                "NVIDIA больше не выпускает игровые драйверы для GTX 900 и GTX 10, только исправления безопасности. Это самый свежий из них, с которым запускаются новые игры.", NvUrl(NvLegacyLast, laptop)));
            if (expert) res.Add(fps);
        }
        return res;
    }
}
