// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.Json;

namespace Merno.Core;

public class TempReading
{
    public string Name { get; set; }
    /// <summary>Температура в градусах. null — узнать не удалось.</summary>
    public int? C { get; set; }
    /// <summary>Дополнительная строка: загрузка, обороты вентилятора.</summary>
    public string Extra { get; set; }
    /// <summary>Пояснение, если температуру узнать не получилось.</summary>
    public string Why { get; set; }
    /// <summary>Видеокарта (обновляется часто), иначе диск.</summary>
    public bool Gpu { get; set; }
}

public class TempsResult
{
    public List<TempReading> Items { get; set; } = new();
    public bool Any => Items.Any(i => i.C.HasValue);
}

/// <summary>Температура видеокарты и дисков.
///
/// Честно о том, что тут можно: надёжного общего API в Windows нет.
///  • видеокарта NVIDIA — nvidia-smi, точные данные, есть всегда вместе с драйвером;
///  • процессор и материнская плата — не показываем: без своего драйвера их не прочитать,
///    а зона ACPI (MSAcpi_ThermalZoneTemperature) — датчик платы, на многих ПК застывает на одном числе;
///  • диски — счётчик надёжности из Storage; на части накопителей пусто;
///  • видеокарты AMD и Intel — без сторонней программы (LibreHardwareMonitor) данных нет.
/// Поэтому пустой ответ — это нормально, а не ошибка: так и пишем пользователю.</summary>
public static class Temps
{
    static string NvidiaSmi()
    {
        foreach (var p in new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        })
            if (File.Exists(p)) return p;
        return null;
    }

    /// <summary>Прочитать датчики. disks=false — только видеокарта (быстро, для обновления в реальном времени).</summary>
    public static TempsResult Read(bool gpu = true, bool disks = true)
    {
        var r = new TempsResult();
        if (gpu) try { Gpu(r); } catch (Exception ex) { Log.Skip("Температура видеокарты: " + ex.Message); }
        if (disks) try { Disks(r); } catch (Exception ex) { Log.Skip("Температура дисков: " + ex.Message); }
        return r;
    }

    static void Gpu(TempsResult r)
    {
        string smi = NvidiaSmi();
        if (smi == null) return;
        var (code, o) = Sh.Run(smi,
            "--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,fan.speed --format=csv,noheader,nounits", 15_000);
        if (code != 0 || string.IsNullOrWhiteSpace(o)) return;
        foreach (var line in o.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var p = line.Split(',').Select(x => x.Trim()).ToArray();
            if (p.Length < 2) continue;
            int? c = int.TryParse(p[1], out var v) && v > 0 && v < 130 ? v : null;
            var extra = new List<string>();
            if (p.Length > 2 && int.TryParse(p[2], out var load)) extra.Add($"загрузка {load} %");
            if (p.Length > 4 && long.TryParse(p[3], out var used) && long.TryParse(p[4], out var total) && total > 0)
                extra.Add($"память {used / 1024.0:0.#} из {total / 1024.0:0.#} ГБ");
            // 0 оборотов — вентилятор стоит (режим тишины), писать «0 %» незачем
            if (p.Length > 5 && int.TryParse(p[5], out var fan) && fan > 0) extra.Add($"вентилятор {fan} %");
            r.Items.Add(new TempReading
            {
                Name = p[0], C = c, Gpu = true,
                Extra = string.Join(" · ", extra),
                Why = c.HasValue ? null : "драйвер NVIDIA не отдаёт температуру"
            });
        }
    }

    static void Disks(TempsResult r)
    {
        var (_, o) = Sh.PS(@"$d = @()
try {
  $d = @(Get-PhysicalDisk -ErrorAction Stop | ForEach-Object {
    $n = $_.FriendlyName
    try { $t = ($_ | Get-StorageReliabilityCounter -ErrorAction Stop).Temperature } catch { $t = $null }
    if ($t -and $t -gt 0 -and $t -lt 100) { [pscustomobject]@{ n = $n; t = [int]$t } }
  })
} catch {}
[pscustomobject]@{ disks = $d } | ConvertTo-Json -Compress -Depth 4", 60_000);
        var s = o?.Trim();
        if (string.IsNullOrEmpty(s) || s[0] != '{') return;
        using var doc = JsonDocument.Parse(s);
        var root = doc.RootElement;

        if (root.TryGetProperty("disks", out var disks) && disks.ValueKind == JsonValueKind.Array)
            foreach (var d in disks.EnumerateArray())
            {
                string name = d.TryGetProperty("n", out var n) ? n.ToString() : "Диск";
                int? t = d.TryGetProperty("t", out var te) && te.TryGetInt32(out var tv) ? tv : null;
                r.Items.Add(new TempReading { Name = name, C = t, Why = t == null ? "накопитель не отдаёт температуру" : null });
            }
    }

    /// <summary>Цвет и подпись для готового значения: до 70 — норма, до 85 — тепло, выше — горячо.</summary>
    public static string Level(int? c) =>
        c == null ? null : c < 70 ? "норма" : c < 85 ? "тепло" : "горячо";

    public static string Text(int? c) => c == null ? "нет данных" : $"{c} °C";
}
