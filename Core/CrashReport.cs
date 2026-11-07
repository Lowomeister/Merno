// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Merno.Core;

/// <summary>
/// Отчёт о необработанной ошибке. Merno сам ничего не отправляет: отчёт (без журнала и личных данных —
/// тип и текст исключения, место в коде Merno, версии Merno и Windows) кладётся в папку данных, а при
/// следующем запуске Merno предлагает скопировать его и написать в Telegram (MainWindow.About.cs, OfferCrashReport).
/// Одну и ту же ошибку предлагаем не чаще раза в сутки.
/// Системные сбои (нарушение доступа в чужой DLL) ловятся отдельно через SetUnhandledExceptionFilter:
/// для них AppDomain.UnhandledException не вызывается.
/// </summary>
public static class CrashReport
{
    internal static string CompactException(string text) => Sanitize(string.Join("\n", (text ?? "").Split('\n')
        .Where(line => !line.TrimStart().StartsWith("at System.Windows.", StringComparison.Ordinal) &&
                       !line.TrimStart().StartsWith("at MS.Internal.", StringComparison.Ordinal)).Take(40)), 1500);
    /// <summary>Отчёт вставляется в сообщение Telegram (до 4096 символов) — оставляем запас.</summary>
    private const int MaxText = 2500;
    /// <summary>Больше отложенных отчётов не копим (цикл падений не должен забить диск).</summary>
    private const int MaxPending = 10;
    /// <summary>Фатальный сбой уже обработан — второй обработчик не нужен (managed + native).</summary>
    private static int _reported;
    private static int _nativeInstalled;
    private static readonly NativeFilterDelegate _nativeFilter = NativeFilter;
    private static readonly IntPtr _nativeFilterPtr = Marshal.GetFunctionPointerForDelegate(_nativeFilter);

    private static string MarkerFile => Path.Combine(Log.DataDir, "crash-last.txt");

    /// <summary>Можно ли писать файлы. Читается при запуске — в момент сбоя настройки не читаем. В режиме снимков выключено.</summary>
    private static volatile bool _enabled;

    /// <summary>
    /// Ловит и «настоящие» сбои Windows (например, нарушение доступа в чужой DLL): для них
    /// AppDomain.UnhandledException не вызывается, процесс просто закрывается через WER.
    /// В падающем процессе делаем минимум: пишем строку «NATIVE|код|адрес|модуль» в файл и выходим.
    /// Отчёт собирается при следующем запуске (TakePending).
    /// </summary>
    public static void InstallNativeHandler(bool force = false)
    {
        if (!force) { try { _enabled = !AppSettings.ReadOnly; } catch { } }
        if (!force && Interlocked.Exchange(ref _nativeInstalled, 1) != 0) return;
        Interlocked.Exchange(ref _nativeInstalled, 1);
        try { SetUnhandledExceptionFilter(_nativeFilterPtr); } catch { }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int NativeFilterDelegate(IntPtr exceptionInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr SetUnhandledExceptionFilter(IntPtr filter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetModuleHandleEx(uint flags, IntPtr moduleName, out IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileName(IntPtr module, StringBuilder name, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionPointers
    {
        public IntPtr ExceptionRecord;
        public IntPtr ContextRecord;
    }

    /// <summary>Вызывается Windows до WER. Возвращаем 0 — пусть WER тоже получит сбой и закроет процесс.</summary>
    private static int NativeFilter(IntPtr info)
    {
        try
        {
            if (Interlocked.Exchange(ref _reported, 1) != 0) return 0;
            if (!_enabled) return 0;   // режим снимков ничего не пишет

            uint code = 0;
            IntPtr address = IntPtr.Zero;
            if (info != IntPtr.Zero)
            {
                var ep = Marshal.PtrToStructure<ExceptionPointers>(info);
                if (ep.ExceptionRecord != IntPtr.Zero)
                {
                    code = (uint)Marshal.ReadInt32(ep.ExceptionRecord);
                    // EXCEPTION_RECORD.ExceptionAddress: 0x10 на x64, 0x0C на x86
                    address = Marshal.ReadIntPtr(ep.ExceptionRecord, IntPtr.Size == 8 ? 0x10 : 0x0C);
                }
            }

            string module = ModuleAt(address);
            File.WriteAllText(Path.Combine(Log.DataDir, "crash-pending-" + DateTime.UtcNow.Ticks + ".txt"),
                "NATIVE|" + code.ToString("X8") + "|" + address.ToString("X") + "|" + module);
        }
        catch { /* отчёт о сбое не должен мешать Windows закрыть процесс */ }
        return 0;
    }

    /// <summary>Имя модуля, в котором произошёл сбой (например, crashy-payload.dll).</summary>
    private static string ModuleAt(IntPtr address)
    {
        try
        {
            if (address == IntPtr.Zero) return "";
            // GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT
            if (!GetModuleHandleEx(0x00000006, address, out IntPtr module) || module == IntPtr.Zero) return "";
            var sb = new StringBuilder(1024);
            uint n = GetModuleFileName(module, sb, (uint)sb.Capacity);
            return n > 0 ? sb.ToString(0, (int)n) : "";
        }
        catch { return ""; }
    }

    internal static string ComposeNative(uint code, IntPtr address, string module)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Отчёт о сбое Merno.");
        sb.AppendLine();
        sb.AppendLine("Что: " + NativeCodeName(code) + " (0x" + code.ToString("X8") + ")");
        if (!string.IsNullOrEmpty(module)) sb.AppendLine("Модуль: " + module);
        sb.AppendLine("Адрес: 0x" + address.ToString("X"));
        sb.AppendLine("Версия: Merno " + App.Version);
        sb.AppendLine("Windows: " + Feedback.WindowsVersion());
        return Sanitize(sb.ToString(), MaxText);
    }

    private static string NativeCodeName(uint code) => code switch
    {
        0xC0000005 => "нарушение доступа к памяти",
        0xC00000FD => "переполнение стека",
        0xC0000409 => "повреждение стека",
        0x80000003 => "точка останова",
        _ => "необработанное исключение Windows"
    };

    /// <summary>Отпечаток системного сбоя: код + модуль. Адрес не берём — он меняется от запуска к запуску.</summary>
    internal static string NativeSignature(uint code, string module)
    {
        string data = "native|" + code.ToString("X8") + "|" + Path.GetFileName(module ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }

    /// <summary>
    /// Merno закрывается из-за ошибки (fatal) — кладёт отчёт в папку данных, его предложат скопировать при
    /// следующем запуске. Ничего не отправляет. Ошибки, после которых Merno работает дальше (интерфейс, фоновые
    /// задачи), отчёт не пишут: они уже в errors.log, а его собирает «Скопировать сведения». Иначе окно
    /// «В прошлый раз…» появлялось бы почти при каждом запуске.
    /// </summary>
    public static void Handle(Exception ex, string source, bool fatal = false)
    {
        try
        {
            if (ex == null || !fatal) return;
            if (AppSettings.ReadOnly || MainWindow.ShotDir != null) return;   // режим снимков ничего не пишет
            if (fatal) Interlocked.Exchange(ref _reported, 1);
            string signature = Signature(ex);
            string[] files = PendingFiles();
            if (files.Length >= MaxPending) return;
            // та же ошибка уже ждёт — второй файл не нужен
            foreach (var f in files)
                try { if (File.ReadLines(f).FirstOrDefault() == "SIG:" + signature) return; } catch { }
            WritePending(signature, Compose(ex, source));
        }
        catch { /* отчёт о сбое не должен ронять программу */ }
    }

    private static string[] PendingFiles()
    {
        try { return Directory.GetFiles(Log.DataDir, "crash-pending-*.txt"); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Текст отчёта: без журнала и без путей пользователя.</summary>
    internal static string Compose(Exception ex, string source)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Отчёт о сбое Merno.");
        sb.AppendLine();
        sb.AppendLine("Что: " + ex.GetType().Name + ": " + ex.Message);
        string where = FirstMernoFrame(ex);
        if (where.Length > 0) sb.AppendLine("Где: " + where);
        if (!string.IsNullOrEmpty(source)) sb.AppendLine("Источник: " + source);
        sb.AppendLine("Версия: Merno " + App.Version);
        sb.AppendLine("Windows: " + Feedback.WindowsVersion());
        sb.AppendLine();
        sb.AppendLine("Подробности:");
        sb.AppendLine(ex.ToString());
        return Sanitize(sb.ToString(), MaxText);
    }

    /// <summary>Первый кадр стека из кода Merno — по нему видно, какой раздел сломался.</summary>
    internal static string FirstMernoFrame(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            foreach (var line in (e.StackTrace ?? "").Split('\n'))
            {
                string l = line.Trim();
                if (l.StartsWith("at Merno", StringComparison.Ordinal))
                    return l[3..].Split(" in ")[0].Trim();
            }
        }
        return "";
    }

    /// <summary>Отпечаток ошибки: тип + обезличенный текст + место. По нему одну и ту же ошибку не предлагаем при каждом запуске.</summary>
    internal static string Signature(Exception ex)
    {
        string norm = Regex.Replace(ex.Message ?? "", @"\d+", "#");
        string data = ex.GetType().FullName + "|" + norm + "|" + FirstMernoFrame(ex);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }

    /// <summary>Убирает из текста имя пользователя, имя компьютера и пути к его папкам.</summary>
    internal static string Sanitize(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
                .Replace(profile.Replace('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        text = Regex.Replace(text, @"(?:\\\\\?\\)?[A-Za-z]:[\\/]Users[\\/][^\\/\r\n]+(?=[\\/])", "%USERPROFILE%", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?i)(?:https?://|file:///)[^\s<>""']+", "%URL%");
        text = Regex.Replace(text, @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "%EMAIL%");
        text = Regex.Replace(text, @"\\\\[^\\\s]+\\[^\s""<>]+", "%NETWORK_PATH%");
        text = Regex.Replace(text, @"(?<![\w\\/:])[A-ZА-Я0-9_-]+\\[A-Za-zА-Яа-я0-9_.-]+(?=\s|$)", "%ACCOUNT%");
        text = Regex.Replace(text, @"(?i)OneDrive - [^\\/\r\n]+(?=[\\/])", "OneDrive");
        // отдельно стоящее имя пользователя (в путях оно уже заменено)
        text = MaskWord(text, Environment.UserName, "%USER%");
        text = MaskWord(text, Environment.MachineName, "%PC%");
        // пути к исходникам в стеке (в release их обычно нет, но на всякий случай)
        text = Regex.Replace(text, @" in [A-Za-z]:\\[^\r\n:]+:line \d+", "");
        // управляющие символы, которые испортят текст при вставке в сообщение Telegram
        text = Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");
        if (text.Length > max) text = text[..max] + "…";
        return text;
    }

    /// <summary>Отчёт — на диск до следующего запуска: тогда Merno предложит его скопировать.</summary>
    private static string WritePending(string signature, string text)
    {
        try
        {
            Directory.CreateDirectory(Log.DataDir);
            string path = Path.Combine(Log.DataDir, "crash-pending-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            File.WriteAllText(path, "SIG:" + signature + "\n" + text, new UTF8Encoding(false));
            return path;
        }
        catch { return null; }
    }

    /// <summary>
    /// Отдельно стоящее имя пользователя или компьютера → метка. Короткое имя («Ян», «PC») совпало бы с обычными словами —
    /// не трогаем (пути уже заменены). Имя «Merno» тоже не трогаем: имя пользователя, совпадающее с названием программы,
    /// не должно портить текст (C:\ProgramData\Merno не превращается в C:\ProgramData\%USER%), а скрывать в названии нечего.
    /// </summary>
    internal static string MaskWord(string text, string word, string token)
    {
        if (string.IsNullOrEmpty(word) || word.Length < 3 || word.Equals("Merno", StringComparison.OrdinalIgnoreCase)) return text;
        return Regex.Replace(text, @"(?<![\w])" + Regex.Escape(word) + @"(?![\w])", token, RegexOptions.IgnoreCase);
    }

    /// <summary>Разбирает файл crash-pending-*.txt: (отпечаток, текст отчёта).</summary>
    internal static (string Signature, string Text) ReadPending(string raw)
    {
        string signature = "";
        // системный сбой: в момент падения записана только строка NATIVE|код|адрес|модуль — отчёт собираем сейчас
        var nat = Regex.Match(raw, @"^NATIVE\|([0-9A-F]{8})\|([0-9A-F]{1,16})\|([^\r\n]*)$");
        if (nat.Success)
        {
            uint code = Convert.ToUInt32(nat.Groups[1].Value, 16);
            var addr = new IntPtr(Convert.ToInt64(nat.Groups[2].Value, 16));
            signature = NativeSignature(code, nat.Groups[3].Value);
            raw = ComposeNative(code, addr, nat.Groups[3].Value);
        }
        else if (raw.StartsWith("SIG:", StringComparison.Ordinal))
        {
            int nl = raw.IndexOf('\n');
            if (nl > 0)
            {
                signature = raw[4..nl].Trim();
                raw = raw[(nl + 1)..];
            }
        }
        if (signature.Length == 0) signature = HashText(raw);
        return (signature, Sanitize(raw, MaxText));
    }

    /// <summary>
    /// Отчёт прошлого запуска, который стоит предложить: самый свежий, кроме уже предложенных за сутки.
    /// Все файлы удаляются, что бы человек ни выбрал. null — предлагать нечего (и в режиме снимков).
    /// </summary>
    public static string TakePending()
    {
        try
        {
            if (AppSettings.ReadOnly || MainWindow.ShotDir != null) return null;
            string[] files = PendingFiles();
            if (files.Length == 0) return null;
            string offer = null;
            // самый свежий первым
            foreach (var f in files.OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    if (offer == null)
                    {
                        var (signature, text) = ReadPending(File.ReadAllText(f, Encoding.UTF8));
                        if (ShouldOffer(signature)) { Remember(signature); offer = text; }
                    }
                }
                catch { }
                TryDelete(f);
            }
            return offer;
        }
        catch { return null; }
    }

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? "")));

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    /// <summary>Ту же ошибку не предлагаем чаще раза в сутки (защита от цикла падений при запуске).</summary>
    private static bool ShouldOffer(string signature)
    {
        try
        {
            if (!File.Exists(MarkerFile)) return true;
            var parts = File.ReadAllText(MarkerFile).Split('|');
            if (parts.Length == 2 && parts[0] == signature && long.TryParse(parts[1], out long ticks))
                return DateTime.UtcNow.Ticks - ticks > TimeSpan.FromHours(24).Ticks;
        }
        catch { }
        return true;
    }

    private static void Remember(string signature)
    {
        try { File.WriteAllText(MarkerFile, signature + "|" + DateTime.UtcNow.Ticks); } catch { }
    }
}
