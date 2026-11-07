// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text;
using Microsoft.Win32;

namespace Merno.Core;

/// <summary>
/// «Связаться с нами»: Merno ничего не отправляет сам. Человек пишет в Telegram-бот поддержки,
/// а сведения о Merno и Windows копирует кнопкой и вставляет в сообщение.
/// Приветствие бота ссылается на кнопку «Скопировать сведения»: её название менять только вместе с ним.
/// </summary>
public static class Feedback
{
    /// <summary>https, а не tg:// — откроется и без установленного Telegram.</summary>
    public const string TelegramUrl = "https://t.me/Lowomebot?start=merno";

    /// <summary>Сообщение Telegram — до 4096 символов; оставляем место на текст человека.</summary>
    public const int MaxInfo = 3000;

    /// <summary>Версия Windows одной строкой: «Windows 11 Pro 24H2 (сборка 26100.2314)».</summary>
    public static string WindowsVersion()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (k == null) return Environment.OSVersion.VersionString;
            string name = k.GetValue("ProductName")?.ToString() ?? "Windows";
            string disp = k.GetValue("DisplayVersion")?.ToString() ?? k.GetValue("ReleaseId")?.ToString() ?? "";
            string build = k.GetValue("CurrentBuild")?.ToString() ?? "";
            string ubr = k.GetValue("UBR")?.ToString() ?? "0";
            // в реестре Windows 11 до сих пор называется «Windows 10»
            if (int.TryParse(build, out int b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
            return $"{name} {disp} (сборка {build}.{ubr})".Replace("  ", " ");
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    /// <summary>Хвост файла (последние maxBytes байт) как текст; пусто, если файла нет.</summary>
    private static string Tail(string path, int maxBytes)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, fs.Length - maxBytes);
            fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[fs.Length - start];
            int read = 0;
            while (read < buf.Length) { int n = fs.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
            return Encoding.UTF8.GetString(buf, 0, read);
        }
        catch { return ""; }
    }

    /// <summary>Сведения для «Скопировать сведения»: версия Merno, Windows, компьютер одной строкой и последние ошибки.</summary>
    public static string CollectInfo(string pc) =>
        ComposeInfo(App.Version, WindowsVersion(), pc, Tail(Log.ErrorsFile, 16 * 1024));

    /// <summary>
    /// Текст не длиннее max и без личного (CrashReport.Sanitize). Ошибки обрезаются с начала:
    /// самые свежие — в конце файла, они важнее.
    /// </summary>
    internal static string ComposeInfo(string version, string windows, string pc, string errors, int max = MaxInfo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Merno " + version);
        sb.AppendLine("Windows: " + windows);
        if (!string.IsNullOrWhiteSpace(pc)) sb.AppendLine("Компьютер: " + pc.Trim());
        string head = CrashReport.Sanitize(sb.ToString(), max / 3);
        const string title = "\nПоследние ошибки:\n";

        string errs = CrashReport.Sanitize(errors ?? "", int.MaxValue).Trim();
        int budget = max - head.Length - title.Length;
        if (errs.Length == 0) errs = "нет";
        else if (budget < 2) errs = "…";
        else if (errs.Length > budget)
        {
            errs = errs[^(budget - 1)..];
            int nl = errs.IndexOf('\n');   // не начинать с обрывка строки
            if (nl >= 0 && nl < errs.Length - 1) errs = errs[(nl + 1)..];
            errs = "…" + errs;
        }
        return head + title + errs;
    }
}
