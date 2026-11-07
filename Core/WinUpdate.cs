// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.Json;

namespace Merno.Core;

public class WinPending { public string Title, Size, Id; }

/// <summary>Этап установки обновлений Windows — для подробного хода в интерфейсе.</summary>
public enum WinStage { Searching, Downloading, Installing, Done }

/// <summary>Один шаг установки: этап, номер обновления, всего обновлений и текущее название.</summary>
public sealed record WinProgress(WinStage Stage, int Index, int Total, string Title, string Note = null);

/// <summary>Состояние активации Windows (только просмотр) и обновления Windows (не драйверы).</summary>
public static class WinUpdate
{
    /// <summary>Статус активации. Active: true/false, null — не удалось определить.</summary>
    public static (bool? Active, string Text) Activation()
    {
        var (_, o) = Sh.PS(@"try {
  $p = @(Get-CimInstance SoftwareLicensingProduct -Filter ""PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'"")
  if ($p.Count -eq 0) { 'RES:NONE' } else {
    $ok = @($p | Where-Object { $_.LicenseStatus -eq 1 }).Count
    if ($ok -gt 0) { 'RES:1' } else { 'RES:' + [int]$p[0].LicenseStatus }
  }
} catch { 'ERR:' + $_.Exception.Message }", 60_000);
        var line = (o ?? "").Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("RES:") || l.StartsWith("ERR:")) ?? "";
        if (line.Length == 0 || line.StartsWith("ERR:")) return (null, "Не удалось проверить активацию");
        var v = line[4..];
        if (v == "1") return (true, "Windows активирована");
        if (v == "NONE") return (false, "Windows не активирована");
        // LicenseStatus: 2, 3 и 6 — срок на активацию ещё идёт, 5 — срок истёк
        return (false, v switch
        {
            "2" or "3" or "6" => "Windows ещё не активирована",
            "5" => "Windows не активирована, пробный срок закончился",
            _ => "Windows не активирована"
        });
    }

    public static void OpenActivationSettings() => Sh.Open("ms-settings:activation");

    /// <summary>Windows ждёт перезагрузку после обновлений или установки программ.</summary>
    public static bool RebootPending()
    {
        static bool KeyExists(string path)
        {
            try { using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path); return k != null; }
            catch { return false; }
        }
        if (KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")) return true;
        if (KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")) return true;
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            if (k?.GetValue("PendingFileRenameOperations") is string[] ops && ops.Length > 0) return true;
        }
        catch { }
        return false;
    }

    /// <summary>Поиск ожидающих обновлений Windows (без драйверов). Может занять несколько минут.</summary>
    public static (List<WinPending> List, string Error) FindPending()
    {
        var (code, o) = Sh.PS(@"try {
  $s = New-Object -ComObject Microsoft.Update.Session
  $r = $s.CreateUpdateSearcher().Search(""IsInstalled=0 and Type='Software' and IsHidden=0"")
  @($r.Updates | ForEach-Object { [pscustomobject]@{ Title = $_.Title; Size = [math]::Round($_.MaxDownloadSize / 1MB, 1); Id = [string]$_.Identity.UpdateID } }) | ConvertTo-Json -Compress
} catch { 'ERR:' + $_.Exception.Message }", 300_000);
        o = (o ?? "").Trim();
        if (o.StartsWith("ERR:")) return (new(), o[4..].Trim());
        if (o.Length == 0) return (new(), code != 0 ? "Не удалось проверить обновления" : null);
        if (o[0] != '[' && o[0] != '{') return (new(), o == Sh.TimedOut ? "Windows долго не отвечает" : o);
        var list = new List<WinPending>();
        try
        {
            using var doc = JsonDocument.Parse(o);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
            foreach (var e in items)
            {
                string title = e.TryGetProperty("Title", out var t) ? t.ToString() : "";
                double mb = 0;
                if (e.TryGetProperty("Size", out var sz) && sz.ValueKind == JsonValueKind.Number) mb = sz.GetDouble();
                if (title.Length > 0) list.Add(new WinPending { Title = Drivers.CleanWinTitle(title), Size = mb > 0 ? $"{mb:0.#} МБ" : "", Id = e.TryGetProperty("Id", out var id) ? id.ToString() : null });
            }
        }
        catch (Exception ex) { Log.Warn("Обновления Windows: " + ex.Message); return (new(), "не удалось получить список"); }
        return (list, null);
    }

    /// <summary>Скачать и установить все ожидающие обновления Windows (без драйверов).</summary>
    public static (int Installed, bool Reboot, string Error) InstallAll() => InstallAll(null);

    /// <summary>Метка NOTE из скрипта установки → понятная фраза для строки хода.</summary>
    static string NoteText(string note) => note?.Trim() switch
    {
        "лицензия" => "Windows не приняла условия лицензии",
        "скачивание" => "обновление не скачалось",
        _ => "обновление не установилось"
    };

    /// <summary>
    /// Скачать и установить все ожидающие обновления Windows по одному.
    /// report получает поиск, скачивание и установку каждого обновления — интерфейс показывает это как есть.
    /// </summary>
    public static (int Installed, bool Reboot, string Error) InstallAll(Action<WinProgress> report, ICollection<string> only = null)
    {
        using var operation = SystemOperation.TryEnter();
        if (operation == null) return (0, false, "Очистка или другая установка ещё выполняется. Повторите позже.");
        Log.Write("Устанавливаю обновления Windows…");
        report?.Invoke(new WinProgress(WinStage.Searching, 0, 0, null));
        int total = 0, current = 0;
        // какие обновления ставить: только отмеченные (пусто — все). В скрипт попадают только GUID — проверяем вид
        var ids = (only ?? Array.Empty<string>()).Where(x => System.Text.RegularExpressions.Regex.IsMatch(x ?? "", "^[0-9a-fA-F-]{36}$")).ToList();
        string onlyPs = "$only = @(" + string.Join(",", ids.Select(x => "'" + x + "'")) + ")\n";
        var (_, o) = Sh.PSStream(onlyPs + @"try {
  $s = New-Object -ComObject Microsoft.Update.Session
  'STAGE:search'
  $r = $s.CreateUpdateSearcher().Search(""IsInstalled=0 and Type='Software' and IsHidden=0"")
  $list = @($r.Updates | Where-Object { $only.Count -eq 0 -or $only -contains [string]$_.Identity.UpdateID })
  $total = $list.Count
  'TOTAL:' + $total
  if ($total -eq 0) { 'RES:0:0'; return }
  $ok = 0; $reboot = $false; $k = 0
  foreach ($u in $list) {
    $k++
    'ITEM:' + $k + '|' + $u.Title
    $c = New-Object -ComObject Microsoft.Update.UpdateColl
    $skip = $false
    try { if (-not $u.EulaAccepted) { $u.AcceptEula() }; [void]$c.Add($u) } catch { $skip = $true; 'NOTE:' + $k + '|лицензия' }
    if (-not $skip) {
      'STAGE:download|' + $k + '|' + $total
      try { $d = $s.CreateUpdateDownloader(); $d.Updates = $c; $dr = $d.Download() } catch { $skip = $true; 'NOTE:' + $k + '|скачивание' }
      if (-not $skip -and $dr.ResultCode -ne 2 -and $dr.ResultCode -ne 3) { $skip = $true; 'NOTE:' + $k + '|скачивание' }
    }
    if (-not $skip) {
      'STAGE:install|' + $k + '|' + $total
      try { $i = $s.CreateUpdateInstaller(); $i.Updates = $c; $ir = $i.Install() } catch { $skip = $true; 'NOTE:' + $k + '|установка' }
      if (-not $skip) {
        if ($ir.RebootRequired) { $reboot = $true }
        if ($ir.ResultCode -eq 2 -and $ir.GetUpdateResult(0).ResultCode -eq 2) { $ok++ } else { 'NOTE:' + $k + '|не установилось' }
      }
    }
    'DONE:' + $k + '|' + $total
  }
  'RES:' + $ok + ':' + [int]$reboot
} catch { 'ERR:' + $_.Exception.Message }", line =>
        {
            if (line.StartsWith("TOTAL:") && int.TryParse(line[6..], out var t)) { total = t; return; }
            if (line.StartsWith("ITEM:"))
            {
                var v = line[5..].Split('|');
                if (v.Length > 1) { int.TryParse(v[0], out current); report?.Invoke(new WinProgress(WinStage.Searching, current, total, Drivers.CleanWinTitle(v[1]))); }
                return;
            }
            if (line.StartsWith("STAGE:"))
            {
                var v = line[6..].Split('|');
                var stage = v[0] == "download" ? WinStage.Downloading : v[0] == "install" ? WinStage.Installing : WinStage.Searching;
                int idx = v.Length > 1 && int.TryParse(v[1], out var i) ? i : current;
                report?.Invoke(new WinProgress(stage, idx, total, null));
                return;
            }
            if (line.StartsWith("NOTE:"))
            {
                var v = line[5..].Split('|');
                if (v.Length > 1) report?.Invoke(new WinProgress(WinStage.Searching, current, total, null, NoteText(v[1])));
                return;
            }
            if (line.StartsWith("DONE:"))
            {
                var v = line[5..].Split('|');
                int idx = v.Length > 0 && int.TryParse(v[0], out var i) ? i : current;
                int all = v.Length > 1 && int.TryParse(v[1], out var a) ? a : total;
                report?.Invoke(new WinProgress(WinStage.Done, idx, all, null));
            }
        }, 7_200_000);
        var line = (o ?? "").Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("RES:") || l.StartsWith("ERR:")) ?? "";
        if (line.Length == 0 || line.StartsWith("ERR:"))
        {
            // текст ошибки Windows (часто на английском) — в журнал, человеку — понятная фраза
            bool slow = o == Sh.TimedOut;
            Log.Warn("Обновления Windows: " + (line.Length > 0 ? line[4..].Trim() : slow ? "нет ответа" : "нет итога установки"));
            return (0, false, slow ? "Windows долго не отвечает. Попробуйте позже" : "Не удалось установить обновления. Попробуйте позже");
        }
        var p = line.Split(':');
        int n = p.Length > 1 && int.TryParse(p[1], out var x) ? x : 0;
        bool reboot = p.Length > 2 && p[2] == "1";
        Log.Ok($"Установлено обновлений Windows: {n}" + (reboot ? ", нужна перезагрузка" : ""));
        return (n, reboot, null);
    }
}
