// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Diagnostics;
using System.Text;

namespace Merno.Core;

public enum LogLevel { Info, Ok, Warn, Skip, Head }

/// <summary>Журнал: пишет в файл и уведомляет интерфейс.</summary>
public static class Log
{
    public static readonly string DataDir;
    public static readonly string LogFile;
    public static string DataWarning { get; private set; }
    public static event Action<string, LogLevel> Written;
    private static readonly object _lock = new();

    /// <summary>Что заметили при подготовке папки данных (журнала ещё нет) — запишем, как только он появится.</summary>
    static readonly List<string> _startNotes = new();

    static Log()
    {
        string common = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Merno");
        // Ошибка защиты — ошибка запуска. В профиль пользователя не переходим.
        TrustedDataDirectory.Prepare(common, n => { DataWarning = n; _startNotes.Add(n); });
        DataDir = common;
        LogDir = System.IO.Path.Combine(DataDir, "logs");
        try { Directory.CreateDirectory(LogDir); } catch { LogDir = DataDir; }
        LogFile = System.IO.Path.Combine(LogDir, $"log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        TidyLogs();
        foreach (var n in _startNotes) Write(n, LogLevel.Warn);
        if (_tidyNote != null) Write(_tidyNote, LogLevel.Skip);
    }

    static string _tidyNote;

    /// <summary>Журналы — отдельной папкой: ProgramData\Merno\logs (настройки и копии для отката — в корне).</summary>
    public static string LogDir { get; private set; }
    /// <summary>Внутренние ошибки (App.xaml.cs) — рядом с журналами.</summary>
    public static string ErrorsFile => System.IO.Path.Combine(LogDir, "errors.log");

    /// <summary>Сколько журналов хранить: этот запуск и два предыдущих — хватит, чтобы разобрать ошибку.</summary>
    const int KeepLogs = 3;

    /// <summary>При запуске: старые журналы — в logs\, лишние (больше KeepLogs) — удалить, errors.log — не больше 256 КБ.
    /// Удаление — через SafeFs, как везде в ProgramData.</summary>
    static void TidyLogs()
    {
        try
        {
            string root = SafeFs.RootFinal(DataDir);
            if (root == null) return;
            // журналы прежних версий лежали прямо в корне папки данных — переносим их в папку logs
            var old = Directory.GetFiles(DataDir, "log-*.txt").Concat(LogDir != DataDir ? Directory.GetFiles(LogDir, "log-*.txt") : Array.Empty<string>())
                               .Where(f => !string.Equals(f, LogFile, StringComparison.OrdinalIgnoreCase))
                               .OrderByDescending(f => System.IO.Path.GetFileName(f), StringComparer.Ordinal).ToList();
            int keepOld = KeepLogs - 1;   // текущий файл ещё не создан — он третий
            foreach (var f in old.Skip(keepOld)) SafeFs.DeleteUnder(f, root);
            if (LogDir != DataDir)
            {
                foreach (var f in old.Take(keepOld).Where(f => string.Equals(System.IO.Path.GetDirectoryName(f), DataDir, StringComparison.OrdinalIgnoreCase)))
                    try { File.Move(f, System.IO.Path.Combine(LogDir, System.IO.Path.GetFileName(f)), true); } catch { }
                string oldErr = System.IO.Path.Combine(DataDir, "errors.log");
                if (File.Exists(oldErr) && !File.Exists(ErrorsFile)) try { File.Move(oldErr, ErrorsFile); } catch { }
            }
            // errors.log растёт со всех запусков — оставляем свежую половину
            var ei = new FileInfo(ErrorsFile);
            if (ei.Exists && ei.Length > 256 * 1024)
            {
                using var input = new FileStream(ErrorsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                input.Seek(-128 * 1024, SeekOrigin.End);
                using var reader = new StreamReader(input, Encoding.UTF8);
                reader.ReadLine(); // drop the partial first line, including a possibly split UTF-8 character
                string text = reader.ReadToEnd();
                AtomicFile.Write(ErrorsFile, "(начало обрезано)\n" + text);
            }
            if (old.Count > keepOld) _tidyNote = $"Удалены старые журналы: {old.Count - keepOld} (хранятся последние {KeepLogs})";
        }
        catch (Exception ex) { _startNotes.Add("Уборка журналов: " + ex.Message); }
    }

    /// <summary>Уникальная защищённая папка для установщиков и рабочих файлов.</summary>
    public static string NewWorkDir(string name) => TrustedDataDirectory.CreateWorkDirectory(DataDir, name);

    public static void Write(string text, LogLevel level = LogLevel.Info)
    {
        string prefix = level switch
        {
            LogLevel.Ok => "[+]", LogLevel.Warn => "[!]", LogLevel.Skip => "[-]", LogLevel.Head => "===", _ => "   "
        };
        // журнал видят на экране, на скриншотах и в «Скопировать сведения» — без имени пользователя, имени ПК и пути к его папке
        string line = $"{prefix} {CrashReport.Sanitize(text, int.MaxValue)}";
        lock (_lock)
        {
            try { File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}", Encoding.UTF8); } catch { }
        }
        Written?.Invoke(line, level);
    }

    public static void Ok(string t) => Write(t, LogLevel.Ok);
    public static void Warn(string t) => Write(t, LogLevel.Warn);
    public static void Skip(string t) => Write(t, LogLevel.Skip);
    public static void Head(string t) => Write(t, LogLevel.Head);
    public static void Error(Exception error)
    {
        string text = CrashReport.CompactException(error?.ToString());
        lock (_lock)
        {
            try { File.AppendAllText(ErrorsFile, $"[{DateTime.Now:dd.MM.yyyy HH:mm:ss}] {App.Version}\n{text}\n\n", Encoding.UTF8); }
            catch (Exception ex) { Warn("Не удалось сохранить отдельный отчёт об ошибке: " + ex.Message); }
        }
        Warn(text);
    }
}

/// <summary>Запуск внешних программ и PowerShell.</summary>
public static class Sh
{
    /// <summary>Вывод Run, если программа не уложилась во время и была остановлена (код -1).</summary>
    public const string TimedOut = "Программа не ответила вовремя и была остановлена.";

    private static Encoding _oem;
    // Нативные утилиты (powercfg, sc, net, dism…) пишут в OEM-кодировке консоли (cp866 в русской Windows).
    private static Encoding OemEncoding()
    {
        if (_oem != null) return _oem;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _oem = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch { _oem = Encoding.UTF8; }
        return _oem;
    }

    /// <summary>
    /// Имя без пути («powershell.exe») → полный путь в системной папке Windows.
    /// Иначе Windows сначала ищет программу рядом с Merno и в текущей папке (например, в «Загрузках»),
    /// и подброшенный туда файл запустился бы с правами администратора.
    /// </summary>
    public static string Resolve(string file)
    {
        return Resolve(file, new[] { Environment.SystemDirectory, System.IO.Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0"),
                                    Environment.GetFolderPath(Environment.SpecialFolder.Windows) });
    }

    internal static string Resolve(string file, IEnumerable<string> systemDirectories)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        if (System.IO.Path.IsPathFullyQualified(file)) return File.Exists(file) ? file : null;
        if (file.Contains('\\') || file.Contains('/') || file.Contains(':')) return null;
        string name = System.IO.Path.HasExtension(file) ? file : file + ".exe";
        foreach (var dir in systemDirectories)
        {
            var p = System.IO.Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <param name="quiet">не писать ошибку в журнал — для проверок «есть ли такое», где «нет» — обычный ответ</param>
    public static (int Code, string Output) Run(string file, string args, int timeoutMs = 600_000, bool quiet = false)
    {
        file = Resolve(file);
        if (file == null) return (-1, "Программа недоступна или её путь небезопасен.");
        try
        {
            bool isPs = file.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase);
            // winget при перенаправленном выводе пишет UTF-8, а не OEM-кодировку
            bool isWinget = System.IO.Path.GetFileName(file).StartsWith("winget", StringComparison.OrdinalIgnoreCase);
            var enc = isPs || isWinget ? new UTF8Encoding(false) : OemEncoding();
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.SystemDirectory,   // не «Загрузки» — оттуда могли бы подгрузиться чужие файлы
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardOutputEncoding = enc,
                StandardErrorEncoding = enc
            };
            ProcessEnvironment.Configure(psi);
            using var p = Process.Start(psi);
            if (p == null) return (-1, "не удалось запустить " + file);
            var outTask = BoundedOutput.ReadAsync(p.StandardOutput);
            var errTask = BoundedOutput.ReadAsync(p.StandardError);
            // исключения в задачах чтения всегда наблюдаем, чтобы не было UnobservedTaskException
            outTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            errTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                try { p.WaitForExit(2000); } catch { }
                try { Task.WaitAll(new Task[] { outTask, errTask }, 2000); } catch { }
                return (-1, TimedOut);
            }
            // Чтение потоков ограничено ожиданием ниже; дочерний процесс мог унаследовать pipe.
            string stdout = "", stderr = "";
            try { if (outTask.Wait(5000)) stdout = outTask.Result.Text; } catch { }
            try { if (errTask.Wait(5000)) stderr = errTask.Result.Text; } catch { }
            stdout = stdout.Trim().TrimStart('\uFEFF');
            stderr = stderr.Trim().TrimStart('\uFEFF');
            int code = p.ExitCode;
            if (!outTask.IsCompletedSuccessfully || !errTask.IsCompletedSuccessfully || outTask.Result.Truncated || errTask.Result.Truncated) return (-1, "Вывод процесса неполный или превышает допустимый размер.");
            if (code != 0 && stderr.Length > 0)
            {
                if (!quiet) Log.Write($"    {System.IO.Path.GetFileName(file)}: {(stderr.Length > 300 ? stderr[..300] + "…" : stderr)}");
                // вызывающему коду нужна причина ошибки, если обычного вывода нет
                if (stdout.Length == 0) stdout = stderr;
            }
            return (code, stdout);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    /// <summary>Выполнить PowerShell-скрипт (вывод в UTF-8).</summary>
    public static (int Code, string Output) PS(string script, int timeoutMs = 600_000)
    {
        string full = PowerShellScript(script, stream: false);
        string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return Run(Resolve("powershell.exe"), $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}", timeoutMs);
    }

    /// <summary>
    /// PowerShell с построчным выводом: каждая строка сразу отдаётся в onLine (фон-поток),
    /// поэтому долгая установка может показывать этап и текущий пункт, а не одно «работаю…».
    /// </summary>
    public static (int Code, string Output) PSStream(string script, Action<string> onLine, int timeoutMs = 600_000)
    {
        string full = PowerShellScript(script, stream: true);
        string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return RunStream(Resolve("powershell.exe"), $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}", onLine, timeoutMs);
    }

    /// <summary>Запуск программы с построчным выводом (UTF-8): каждая строка сразу уходит в onLine (фон-поток).
    /// Строки делятся и по «\r» — так затёртые «крутилки» не склеиваются с текстом.</summary>
    public static (int Code, string Output) RunStream(string file, string args, Action<string> onLine, int timeoutMs = 600_000)
    {
        var sb = new StringBuilder();
        file = Resolve(file);
        if (file == null) return (-1, "Программа недоступна или её путь небезопасен.");
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.SystemDirectory,   // не «Загрузки» — оттуда могли бы подгрузиться чужие файлы
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };
            ProcessEnvironment.Configure(psi);
            using var p = Process.Start(psi);
            if (p == null) return (-1, "не удалось запустить " + System.IO.Path.GetFileName(file));
            var outTask = BoundedOutput.ReadAsync(p.StandardOutput, onLine);
            var errTask = BoundedOutput.ReadAsync(p.StandardError, onLine);
            bool exited = p.WaitForExit(timeoutMs);
            if (!exited)
            {
                try { p.Kill(true); } catch { }
                try { p.WaitForExit(3000); } catch { }
            }
            try { Task.WaitAll(new[] { outTask, errTask }, 5000); } catch { }
            if (!outTask.IsCompletedSuccessfully || !errTask.IsCompletedSuccessfully) return (-1, "Не удалось дочитать вывод процесса.");
            string output = (outTask.Result.Text + "\n" + errTask.Result.Text).Trim();
            return !exited || outTask.Result.Truncated || errTask.Result.Truncated ? (-1, output) : (p.ExitCode, output);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    public static string Sys32(string exe) => System.IO.Path.Combine(Environment.SystemDirectory, exe);

    internal static string PowerShellScript(string script, bool stream) =>
        "$env:PSModulePath='" + Esc(ProcessEnvironment.ModulePath) + "';" +
        "$ErrorActionPreference='" + (stream ? "Continue" : "SilentlyContinue") + "';" +
        "$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + script;

    public static void Open(string target)
    {
        if (!Unelevated.Open(target)) Log.Warn("Не удалось открыть с обычными правами пользователя: " + target);
    }

    public static string Size(long b)
    {
        if (b < 0) return "неизвестно";
        if (b >= 1L << 30) return $"{b / (double)(1L << 30):0.00} ГБ";
        if (b >= 1L << 20) return $"{b / (double)(1L << 20):0} МБ";
        return $"{b / 1024.0:0} КБ";
    }

    /// <summary>Свободное место на диске одной строкой: «свободно 487 из 952 ГБ». Меньше 10 ГБ свободно — с одним знаком после запятой
    /// («свободно 3,4 из 59 ГБ»), чтобы не писать «0 из 59 ГБ». capital — с заглавной буквы, если строка начинает предложение.</summary>
    public static string FreeOf(long free, long total, bool capital = false)
    {
        const long Gb = 1L << 30;
        string f = free < 10 * Gb ? (Math.Floor(free / (double)Gb * 10) / 10).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) : (free >> 30).ToString();
        string text = $"свободно {f} из {total >> 30} ГБ";
        return capital ? char.ToUpper(text[0]) + text.Substring(1) : text;
    }

    /// <summary>Текст для строки в одинарных кавычках PowerShell. PowerShell считает кавычкой не только ', но и ‘ ’ ‚ ‛ —
    /// их тоже нужно удваивать, иначе имя файла с такой кавычкой выходит из строки и выполняется как код.</summary>
    public static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            sb.Append(c);
            if (c == '\'' || c == '‘' || c == '’' || c == '‚' || c == '‛') sb.Append(c);
        }
        return sb.ToString();
    }
}
