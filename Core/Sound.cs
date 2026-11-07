// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.Json;

namespace Merno.Core;

/// <summary>Устройство, которое отвечает за звук: колонки Realtek, звук видеокарты через HDMI и т. п.</summary>
public class SoundDevice
{
    public string Name { get; set; }
    /// <summary>InstanceId — по нему устройство выключается и включается обратно.</summary>
    public string Instance { get; set; }
    /// <summary>Состояние из Диспетчера устройств: OK, Error, Disabled, Unknown.</summary>
    public string Status { get; set; } = "OK";
    /// <summary>Выключено: в Диспетчере устройств (PnpDisabled) или все его входы/выходы отключены в окне «Звук».
    /// «Не подключено» (Unknown) — это другое, см. Present.</summary>
    public bool Disabled { get; set; }
    /// <summary>Выключено целиком в Диспетчере устройств (код проблемы 22).</summary>
    public bool PnpDisabled { get; set; }
    /// <summary>Входы и выходы этого устройства из окна «Звук» (по ним и включаем/отключаем).</summary>
    public List<AudioEndpoint> Endpoints { get; set; } = new();
    /// <summary>false — устройство сейчас не подключено: вынутая флешка с наушниками, Bluetooth-гарнитура выключена.</summary>
    public bool Present { get; set; } = true;
    public bool Error { get; set; }
    /// <summary>Изменение ждёт перезагрузки (код проблемы 14): Windows включит или выключит устройство после неё.</summary>
    public bool Pending { get; set; }
    /// <summary>Звук видеокарты. Нужен только для колонок в мониторе или телевизоре.</summary>
    public bool Video { get; set; }
}

/// <summary>Звуковые устройства: показать, выключить и включить обратно.
/// Ничего не удаляется — состояние устройства всегда возвращается.</summary>
public static class Sound
{
    // Звук видеокарты надёжнее искать по коду производителя в InstanceId: он не зависит от языка Windows.
    // VEN_10DE — NVIDIA, VEN_1002 — AMD (их HDMI-звук). Встроенный звук платы — обычно VEN_10EC (Realtek).
    static bool VideoById(string instance) =>
        instance != null && (instance.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase)
                          || instance.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase));

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
        catch (Exception ex) { Log.Warn("Звук: не удалось прочитать список устройств: " + ex.Message); }
        return res;
    }

    static string Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";

    /// <summary>Звуковые устройства компьютера. Выключенные тоже — иначе их не включить обратно.
    /// Служебные заглушки Windows вроде «Microsoft Streaming Service Proxy» не показываем: это не звук.</summary>
    public static List<SoundDevice> List()
    {
        // Статус «Error» Windows ставит и выключенным устройствам, поэтому смотрим код проблемы:
        // 22 — выключено, 14 — ждёт перезагрузки, 0 — работает, остальное — ошибка.
        var (_, o) = Sh.PS(@"Get-PnpDevice -Class MEDIA -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName } |
            Select-Object @{n='Name';e={$_.FriendlyName}}, InstanceId, @{n='Status';e={[string]$_.Status}},
                          @{n='Code';e={[int]$_.ConfigManagerErrorCode}}, @{n='Present';e={ if ($null -ne $_.Present) { [bool]$_.Present } else { [string]$_.Status -ne 'Unknown' } }} | ConvertTo-Json -Compress", 60_000);
        var list = Json(o).Select(e =>
        {
            string status = Str(e, "Status");
            string instance = Str(e, "InstanceId");
            int code = e.TryGetProperty("Code", out var c) && c.TryGetInt32(out var ci) ? ci : -1;
            bool present = e.TryGetProperty("Present", out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? p.GetBoolean() : !status.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
            bool disabled = code == 22 || status.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
            bool pending = code == 14;
            return new SoundDevice
            {
                Name = Str(e, "Name"),
                Instance = instance,
                Status = status.Length > 0 ? status : "Unknown",
                Disabled = disabled,
                Pending = pending,
                Error = present && !disabled && !pending && (code > 0 || status.Equals("Error", StringComparison.OrdinalIgnoreCase)),
                Present = present,
                Video = VideoById(instance) ||
                        Str(e, "Name").Contains("Display Audio", StringComparison.OrdinalIgnoreCase)
            };
        }).Where(d =>
            d.Name.Length > 0 && d.Instance.Length > 0
            // SW\{…} — программные заглушки DirectShow, а не звуковые карты
            && !d.Instance.StartsWith(@"SW\", StringComparison.OrdinalIgnoreCase)
            && !d.Name.StartsWith("Microsoft Streaming", StringComparison.OrdinalIgnoreCase))
          .ToList();

        // Входы и выходы из окна «Звук»: устройство может работать, а его «Микрофон» там отключён.
        // Сопоставляем по коду устройства (InstanceId); если его нет — по имени («Logi C270 HD WebCam»).
        var eps = AudioEndpoints.List();
        foreach (var d in list)
        {
            d.PnpDisabled = d.Disabled;
            d.Endpoints = eps.Where(e => e.Instance.Length > 0 ? e.Instance.Equals(d.Instance, StringComparison.OrdinalIgnoreCase)
                                                             : e.Adapter.Equals(d.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var live = d.Endpoints.Where(e => e.State != 4).ToList();   // 4 — старые записи об устройстве, которого нет
            if (!d.PnpDisabled && live.Count > 0 && live.All(e => e.Off)) { d.Disabled = true; d.Error = false; }
        }

        // Работающие устройства — сверху, потом выключенные, потом отключённые от компьютера
        return list.OrderBy(d => d.Present ? 0 : 1).ThenBy(d => d.Disabled ? 1 : 0).ThenBy(d => d.Video ? 1 : 0).ThenBy(d => d.Name).ToList();
    }

    public static List<SoundDevice> VideoDevices(List<SoundDevice> all) => all.Where(d => d.Video).ToList();

    public static SoundDevice Nvidia(List<SoundDevice> all) =>
        all.FirstOrDefault(d => d.Video && (d.Instance?.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) == true
                                            || d.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Сколько звуковых устройств сейчас работает. Ноль — значит звука в системе нет.</summary>
    public static int EnabledCount(List<SoundDevice> all) => all.Count(d => !d.Disabled && d.Present);

    /// <summary>Выключить или включить звук устройства. Reboot — Windows применит изменение после перезагрузки.
    /// Выключаем, как кнопка «Отключить» в окне «Звук»: входы и выходы устройства. Оно остаётся видно в окне «Звук»
    /// как «Отключено», вернуть можно и там, и в Merno. Целиком (Диспетчер устройств) выключаем, только если
    /// у устройства нет входов и выходов. Включаем и то и другое: устройство, выключенное раньше целиком, тоже вернётся.</summary>
    public static (bool Ok, bool Reboot, string Error) SetEnabled(SoundDevice d, bool enable)
    {
        if (d == null || string.IsNullOrEmpty(d.Instance)) return (false, false, "Устройство не найдено.");
        if (!d.Present) return (false, false, "Устройство не подключено к компьютеру.");
        Log.Write($"{(enable ? "Включаю" : "Выключаю")} звук: {d.Name}…");
        bool reboot = false;
        var orig = d;
        if (enable)
        {
            if (d.PnpDisabled)
            {
                var (ok, rb, err) = SetPnp(d, true);
                if (!ok || rb) return (ok, rb, err);   // SetPnp уже перечитал устройство: появились входы и выходы
            }
            foreach (var e in d.Endpoints.Where(e => e.Off)) AudioEndpoints.SetVisible(e.Id, true);
        }
        else
        {
            var live = d.Endpoints.Where(e => e.State != 4 && !e.Off).ToList();
            if (live.Count == 0) return SetPnp(d, false);
            foreach (var e in live) AudioEndpoints.SetVisible(e.Id, false);
        }
        var now = List().FirstOrDefault(x => x.Instance == d.Instance);
        if (now != null) Refresh(orig, now);
        if (now != null && now.Disabled == enable)
        {
            Log.Warn($"Звук: {d.Name}: Windows не изменила состояние");
            return (false, false, "Windows не изменила состояние устройства. Попробуйте в окне «Настройки звука Windows».");
        }
        Log.Ok(enable ? $"Звук включён: {d.Name}" : $"Звук выключен: {d.Name}");
        return (true, reboot, null);
    }

    /// <summary>Входы и выходы, которые можно включать по отдельности (без старых записей об отсутствующих устройствах).</summary>
    public static List<AudioEndpoint> Parts(SoundDevice d) =>
        d == null || d.PnpDisabled || !d.Present ? new() : d.Endpoints.Where(e => e.State != 4).OrderBy(e => e.Capture).ThenBy(e => e.Name).ToList();

    /// <summary>Название входа или выхода без имени устройства в скобках: «Микрофон (Realtek Audio)» → «Микрофон».</summary>
    public static string PartName(AudioEndpoint e)
    {
        string n = e.Name ?? "";
        if (e.Adapter.Length > 0 && n.EndsWith($"({e.Adapter})", StringComparison.OrdinalIgnoreCase))
            n = n[..^(e.Adapter.Length + 2)].Trim();
        return n.Length > 0 ? n : e.Capture ? "Запись" : "Воспроизведение";
    }

    /// <summary>Сколько выходов звука (колонки, наушники) сейчас работает на всём компьютере.</summary>
    public static int OutputsOn(List<SoundDevice> all) =>
        all.Where(d => d.Present && !d.PnpDisabled).SelectMany(d => d.Endpoints).Count(e => !e.Capture && e.State != 4 && !e.Off);

    /// <summary>Включить или отключить один вход/выход (например, только микрофон гарнитуры), как в окне «Звук».</summary>
    public static (bool Ok, string Error) SetPart(SoundDevice d, AudioEndpoint e, bool enable)
    {
        string name = $"{PartName(e)} ({d.Name})";
        Log.Write($"{(enable ? "Включаю" : "Отключаю")}: {name}…");
        AudioEndpoints.SetVisible(e.Id, enable);
        var now = List().FirstOrDefault(x => x.Instance == d.Instance);
        if (now != null) Refresh(d, now);
        var fresh = d.Endpoints.FirstOrDefault(x => x.Id == e.Id);
        if (fresh != null && fresh.Off == enable)
        {
            Log.Warn($"Звук: {name}: Windows не изменила состояние");
            return (false, "Windows не изменила состояние. Попробуйте в окне «Настройки звука Windows».");
        }
        Log.Ok(enable ? $"Включено: {name}" : $"Отключено: {name}");
        return (true, null);
    }

    /// <summary>Перенести свежее состояние в объект, который держит экран.</summary>
    static void Refresh(SoundDevice to, SoundDevice from)
    {
        if (ReferenceEquals(to, from)) return;
        to.Status = from.Status; to.Disabled = from.Disabled; to.PnpDisabled = from.PnpDisabled; to.Pending = from.Pending;
        to.Error = from.Error; to.Present = from.Present; to.Endpoints = from.Endpoints;
    }

    /// <summary>Устройство целиком (как в Диспетчере устройств). pnputil (Windows 10 2004+) честно отвечает
    /// «нужна перезагрузка» (код 3010), а Disable-PnpDevice в этом случае пишет «Общий сбой».</summary>
    static (bool Ok, bool Reboot, string Error) SetPnp(SoundDevice d, bool enable)
    {
        bool reboot = false;
        var (code, _) = Sh.Run(Sh.Sys32("pnputil.exe"), $"/{(enable ? "enable" : "disable")}-device \"{d.Instance}\"", 90_000);
        if (code == 3010) reboot = true;
        else if (code != 0)
        {
            // старый pnputil без этих команд — пробуем через PowerShell
            string id = Sh.Esc(d.Instance);
            string cmd = enable ? "Enable-PnpDevice" : "Disable-PnpDevice";
            var (_, o) = Sh.PS($@"try {{ {cmd} -InstanceId '{id}' -Confirm:$false -ErrorAction Stop; 'OK' }}
                catch {{ 'ERR:' + $_.Exception.Message }}", 90_000);
            string line = o.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("OK") || l.StartsWith("ERR:")) ?? "";
            if (!line.StartsWith("OK")) Log.Skip($"Звук: {d.Name}: pnputil {code}, PowerShell: {(line.StartsWith("ERR:") ? line[4..].Trim() : "нет ответа")}");
        }
        // По ответу команд судить нельзя — сверяемся с настоящим состоянием устройства
        var now = List().FirstOrDefault(x => x.Instance == d.Instance);
        if (now != null) Refresh(d, now);
        if (now != null && now.Pending) reboot = true;
        bool done = now == null || now.PnpDisabled != enable;
        if (!done && !reboot)
        {
            Log.Warn($"Звук: {d.Name}: Windows не изменила состояние устройства");
            return (false, false, "Windows не изменила состояние устройства. Попробуйте через Диспетчер устройств.");
        }
        if (reboot && !done) Log.Ok($"Звук: {d.Name}: {(enable ? "включится" : "выключится")} после перезагрузки");
        else if (!enable) Log.Ok($"Звук выключен: {d.Name}");
        return (true, reboot && !done, null);
    }

    /// <summary>Короткое пояснение под названием устройства.</summary>
    public static string Advice(SoundDevice d)
    {
        if (!d.Present) return "Не подключено к компьютеру. Подключите его, чтобы оно появилось в списке.";
        if (d.Pending) return "Изменение применится после перезагрузки компьютера.";
        if (d.Error) return "Устройство работает с ошибкой. Поможет переустановка драйвера звука или перезагрузка.";
        if (!d.Video) return d.Disabled
            ? "Включите, если по нему должен идти звук."
            : "Обычное звуковое устройство: колонки, наушники или микрофон.";
        return d.Disabled
            ? "Звук идёт на другое устройство. Включите, если нужен звук в мониторе или телевизоре."
            : "Звук через HDMI или DisplayPort видеокарты. Нужен, только если колонки встроены в монитор или звук идёт на телевизор.";
    }

    /// <summary>Короткая подпись состояния для строки.</summary>
    public static string State(SoundDevice d) =>
        !d.Present ? "Не подключено" : d.Pending ? "После перезагрузки" : d.Disabled ? "Выключено" : d.Error ? "Ошибка" : "Работает";
}
