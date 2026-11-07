// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Merno.Core;

/// <summary>
/// «Настройки → Ограничить скорость скачивания»: чтобы игры, видео и звонки не тормозили, пока что-то качается.
///  • обновления Windows и Microsoft Store — политика «Оптимизации доставки» (DOMax…DownloadBandwidth, КБ/с);
///  • winget — переключаем на скачивание через ту же «Оптимизацию доставки» (network.downloader = "do"), тогда лимит действует и на него;
///  • собственные скачивания Merno (обновление, Spotify, winget) — паузы в цикле копирования (Pace).
/// </summary>
public static class NetLimit
{
    /// <summary>Варианты в Мбит/с; 0 — без ограничения.</summary>
    public static readonly int[] Choices = { 0, 5, 10, 25, 50, 100 };

    /// <summary>Текущий лимит для скачиваний самого Merno (Мбит/с, 0 — нет). Ставится из настроек при запуске.</summary>
    public static volatile int Mbps;

    const string DoKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";

    /// <summary>Пауза после очередной порции, чтобы средняя скорость не превышала лимит. total — скачано байт с начала.</summary>
    public static void Pace(long total, Stopwatch sw)
    {
        int ms = WaitMs(total, sw);
        if (ms > 0) Thread.Sleep(ms);
    }

    public static Task PaceAsync(long total, Stopwatch sw)
    {
        int ms = WaitMs(total, sw);
        return ms > 0 ? Task.Delay(ms) : Task.CompletedTask;
    }

    static int WaitMs(long total, Stopwatch sw)
    {
        int mbps = Mbps;
        if (mbps <= 0) return 0;
        double needSec = total / (mbps * 125_000.0);   // 1 Мбит/с = 125 000 байт/с
        double ms = (needSec - sw.Elapsed.TotalSeconds) * 1000;
        return ms > 5 ? (int)Math.Min(ms, 2000) : 0;
    }

    /// <summary>Копировать поток с учётом лимита (для синхронных скачиваний).</summary>
    public static void Copy(Stream src, Stream dst, Action<long> progress = null)
    {
        var buf = new byte[1 << 16];
        var sw = Stopwatch.StartNew();
        long got = 0; int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0)
        {
            dst.Write(buf, 0, n); got += n;
            progress?.Invoke(got);
            Pace(got, sw);
        }
    }

    /// <summary>
    /// Применить лимит к Windows и winget. wingetByMerno — ставил ли Merno раньше "do" в настройки winget
    /// (тогда при снятии лимита убираем; свою настройку пользователя не трогаем). Возвращает новое значение этого флага.
    /// </summary>
    public static bool ApplySystem(int mbps, bool wingetByMerno)
    {
        Mbps = mbps;
        try
        {
            if (mbps > 0)
            {
                int kbps = mbps * 125;   // КБ/с
                using var k = Registry.LocalMachine.CreateSubKey(DoKey, true);
                k.SetValue("DOMaxForegroundDownloadBandwidth", kbps, RegistryValueKind.DWord);
                k.SetValue("DOMaxBackgroundDownloadBandwidth", kbps, RegistryValueKind.DWord);
                k.SetValue("DOMaxDownloadBandwidth", kbps, RegistryValueKind.DWord);   // то же для старых Windows 10 (до 2004)
                Log.Ok($"Скорость скачивания ограничена: {mbps} Мбит/с (обновления Windows, Microsoft Store, программы)");
            }
            else
            {
                using var k = Registry.LocalMachine.OpenSubKey(DoKey, true);
                if (k != null)
                    foreach (var v in new[] { "DOMaxForegroundDownloadBandwidth", "DOMaxBackgroundDownloadBandwidth", "DOMaxDownloadBandwidth" })
                        k.DeleteValue(v, false);
                Log.Ok("Ограничение скорости скачивания снято");
            }
        }
        catch (Exception ex) { Log.Warn("Ограничение скорости для Windows: " + ex.Message); }
        try { return SetWingetDownloader(mbps > 0, wingetByMerno); }
        catch (Exception ex) { Log.Skip("Ограничение скорости для winget: " + ex.Message); return wingetByMerno; }
    }

    static string WingetSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Packages\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\LocalState\settings.json");

    /// <summary>on — скачивание winget через «Оптимизацию доставки» (лимит действует). Возвращает, стоит ли теперь значение Merno.</summary>
    static bool SetWingetDownloader(bool on, bool byMerno)
        => Unelevated.AsUser(() => SetWingetDownloaderCore(on, byMerno));
    static bool SetWingetDownloaderCore(bool on, bool byMerno)
    {
        string path = WingetSettingsPath;
        if (!Directory.Exists(Path.GetDirectoryName(path))) return false;   // winget не установлен
        var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        JsonObject root = null;
        if (File.Exists(path))
        {
            try { root = JsonNode.Parse(File.ReadAllText(path), null, opts) as JsonObject; }
            catch { Log.Skip("Настройки winget не прочитались. Оставляю их без изменений"); return byMerno; }
        }
        root ??= new JsonObject { ["$schema"] = "https://aka.ms/winget-settings.schema.json" };
        var net = root["network"] as JsonObject;
        string cur = net?["downloader"]?.GetValue<string>();
        if (on)
        {
            if (cur == "do") return byMerno;        // уже так (сами или пользователь)
            if (cur != null && cur != "default") { Log.Skip($"winget: у вас выбран способ скачивания «{cur}». Оставляю его без изменений. Ограничение скорости для winget не действует"); return false; }
            if (net == null) root["network"] = net = new JsonObject();
            net["downloader"] = "do";
            Write(path, root);
            return true;
        }
        if (!byMerno || cur != "do") return false;  // ставил не Merno — оставляем как есть
        net.Remove("downloader");
        if (net.Count == 0) root.Remove("network");
        Write(path, root);
        return false;
    }

    static void Write(string path, JsonObject root) =>
        AtomicFile.Write(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}
