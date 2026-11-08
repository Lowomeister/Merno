// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO.Compression;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Merno.Core;

/// <summary>Сведения о выпуске из version.json.</summary>
public sealed class UpdateInfo
{
    public string Version;
    public string Url;
    public string Sha256;
    public long Size;
    public string SigExe;
    public string SigMeta;
    public string Notes;
    public string Format;
    public long Published, Expires;
    /// <summary>version.json пришёл по HTTPS с проверенным сертификатом (GitHub). Ставит Parse, не из JSON.</summary>
    public bool Pinned;
    public System.Version Parsed;
    /// <summary>Полный адрес файла комплекта обновления.</summary>
    public Uri ExeUri;
}

/// <summary>Результат проверки: Info != null — есть новая версия.</summary>
public sealed class UpdateCheck
{
    public bool Ok;
    public string Message;
    public UpdateInfo Info;
}

/// <summary>
/// Обновление Merno с GitHub Releases (github.com/Lowomeister/Merno). Данные о выпуске (version.json) и комплект обновления
/// подписаны закрытым ключом выпуска (RSA-3072, PKCS#1 v1.5, SHA-256). Комплект ставится, только если верна подпись данных
/// о выпуске (UpdateMetadata), совпадают размер и SHA-256 файла, верна подпись самого файла, а версия выше текущей
/// (защита от подмены и отката). Установщик из комплекта ставит новую версию в папку versions\ установленного Merno.
/// </summary>
public static class Updater
{
    /// <summary>Репозиторий с выпусками. version.json — у последнего выпуска, комплект — у выпуска с его номером.</summary>
    public const string Repo = "Lowomeister/Merno";
    // обычный HTTPS с проверкой сертификата Windows; подлинность обновления проверяет подпись RSA (Core/UpdateKey.cs)
    public const string LatestJson = "https://github.com/" + Repo + "/releases/latest/download/version.json";
    private const long MaxSize = 400L * 1024 * 1024;

    /// <summary>
    /// Комплект берём у выпуска с номером из подписанного version.json, а не у «последнего»: если между проверкой
    /// и скачиванием выйдет новая версия, хеш не совпадёт.
    /// </summary>
    internal static Uri KitUri(string version, string file) =>
        new("https://github.com/" + Repo + "/releases/download/" + Uri.EscapeDataString(version) + "/" + Uri.EscapeDataString(file));

    /// <summary>GitHub отдаёт файлы выпуска через переадресацию на свой CDN; с HTTPS на HTTP .NET не переходит.</summary>
    static SocketsHttpHandler Handler(TimeSpan connect) => new() { ConnectTimeout = connect, AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };

    /// <summary>
    /// Ошибка именно сертификата (а не «нет связи»): защищённое соединение с GitHub кто-то перехватил. Чаще всего это
    /// антивирус с проверкой HTTPS или прокси, реже подмена в сети. Говорим, что проверка не прошла, и не обходим её.
    /// </summary>
    public static bool IsCertFailure(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is System.Security.Authentication.AuthenticationException) return true;
            if (e is HttpRequestException h && h.HttpRequestError == HttpRequestError.SecureConnectionError) return true;
        }
        return false;
    }

    private static readonly HttpClient _json = new(Handler(TimeSpan.FromSeconds(8))) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 128 * 1024 };
    private static readonly HttpClient _file = new(Handler(TimeSpan.FromSeconds(10))) { Timeout = TimeSpan.FromMinutes(15) };

    public static bool KeyConfigured =>
        !string.IsNullOrWhiteSpace(UpdateKey.PublicXml) && UpdateKey.PublicXml != "PASTE" && UpdateKey.PublicXml.Contains("<Modulus>");

    public static System.Version Current => System.Version.TryParse(App.Version, out var v) ? v : new System.Version(0, 0);

    static Updater()
    {
        try
        {
            var ua = "Merno/" + App.Version;
            _json.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
            _file.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
            _json.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        }
        catch { }
    }

    /// <summary>Проверка version.json. Исключения наружу не выходят.</summary>
    public static async Task<UpdateCheck> CheckAsync()
    {
        if (!KeyConfigured)
        {
            Log.Skip("Обновления отключены: ключ проверки подписи не настроен (Core/UpdateKey.cs)");
            return new UpdateCheck { Message = "Автообновление в этой версии выключено" };
        }
        try
        {
            var text = await _json.GetStringAsync(new Uri(LatestJson)).ConfigureAwait(false);
            var info = Parse(text);
            if (info == null) return new UpdateCheck { Message = "Не удалось проверить. Попробуйте позже" };
            var meta = VerifyMeta(info);
            if (meta == MetaCheck.Invalid)
            {
                Log.Warn($"Обновление {info.Version} не прошло проверку подлинности и не будет установлено");
                return new UpdateCheck { Message = "Не удалось проверить. Попробуйте позже" };
            }
            if (meta == MetaCheck.ClockBehind)
                Log.Skip($"Обновления: выпуск {info.Version} подписан {Day(info.Published)}, а часы компьютера показывают более раннее время");
            if (meta == MetaCheck.Expired)
                Log.Skip($"Обновления: срок подписи данных о выпуске {info.Version} истёк {Day(info.Expires)} (давно не было новых выпусков)");
            if (!UpdateMetadata.Accept(meta, info.Pinned))
            {
                Log.Warn("Обновления: данные о выпуске не приняты");
                return new UpdateCheck { Message = "Не удалось проверить. Попробуйте позже" };
            }
            if (info.Parsed <= Current)
            {
                Log.Skip($"Обновления: установлена последняя версия ({App.Version}, на GitHub {info.Version})");
                return new UpdateCheck { Ok = true, Message = "У вас последняя версия" };
            }
            Log.Write($"Доступна новая версия Merno: {info.Version}");
            return new UpdateCheck { Ok = true, Info = info, Message = $"Вышла версия {info.Version}" };
        }
        catch (Exception ex) when (IsCertFailure(ex))
        {
            Log.Warn("Обновления: сертификат GitHub не прошёл проверку. Соединение перехвачено (антивирус с проверкой HTTPS, прокси или подмена в сети). Обновление не проверено");
            return new UpdateCheck { Message = "Не удалось проверить: соединение с GitHub перехвачено. Обычно это делает антивирус или прокси, проверьте их настройки" };
        }
        catch (TaskCanceledException) { Log.Skip("Обновления: GitHub не ответил за 10 секунд"); return new UpdateCheck { Message = "Не удалось проверить. Проверьте интернет" }; }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        { Log.Skip("Обновления: на GitHub ещё нет выпуска с version.json"); return new UpdateCheck { Message = "Выпусков пока нет. Попробуйте позже" }; }
        catch (HttpRequestException ex) when (ex.StatusCode != null)
        { Log.Skip($"Обновления: GitHub ответил {(int)ex.StatusCode}"); return new UpdateCheck { Message = "Не удалось проверить. Попробуйте позже" }; }
        catch (HttpRequestException ex) { Log.Skip("Обновления: нет связи с GitHub: " + ex.Message); return new UpdateCheck { Message = "Не удалось проверить. Проверьте интернет" }; }
        catch (Exception ex) { Log.Skip("Обновления: " + ex.Message); return new UpdateCheck { Message = "Не удалось проверить. Попробуйте позже" }; }
    }

    private static UpdateInfo Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            string S(string n) => r.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            var info = new UpdateInfo
            {
                Version = S("version"), Url = S("url"), Sha256 = S("sha256")?.Trim().ToLowerInvariant(),
                SigExe = S("sig_exe"), SigMeta = S("sig_meta"), Notes = S("notes") ?? "", Format = S("format")
            };
            if (r.TryGetProperty("published", out var published) && published.TryGetInt64(out var pub)) info.Published = pub;
            if (r.TryGetProperty("expires", out var expires) && expires.TryGetInt64(out var exp)) info.Expires = exp;
            info.Pinned = true;   // только HTTPS с проверкой сертификата: старый ответ посредник не подсунет
            if (r.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number && sz.TryGetInt64(out var n)) info.Size = n;
            if (string.IsNullOrEmpty(info.Version) || !System.Version.TryParse(info.Version, out info.Parsed)) return null;
            if (string.IsNullOrEmpty(info.Url) || info.Sha256 == null || info.Sha256.Length != 64 || info.Size <= 0 || info.Size > MaxSize) return null;
            if (string.IsNullOrEmpty(info.SigExe) || string.IsNullOrEmpty(info.SigMeta)) return null;
            if (info.Url.IndexOfAny(new[] { '/', '\\', '?', '#', ':' }) >= 0) return null;
            info.ExeUri = KitUri(info.Version, info.Url);
            if (info.Notes.Length > 300) return null;
            return info;
        }
        catch { return null; }
    }

    private static RSA LoadKey()
    {
        var rsa = RSA.Create();
        rsa.FromXmlString(UpdateKey.PublicXml);
        return rsa;
    }

    private static MetaCheck VerifyMeta(UpdateInfo i)
    {
        try
        {
            using var rsa = LoadKey();
            return UpdateMetadata.Check(i, rsa, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch (Exception ex) { Log.Skip("Проверка подписи version.json: " + ex.Message); return MetaCheck.Invalid; }
    }

    static string Day(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd.MM.yyyy");

    /// <summary>Не %TEMP%: туда может писать любая программа пользователя. Папка данных Merno закрыта от записи.</summary>
    private static readonly Lazy<string> UpdateDirectory = new(() => Log.NewWorkDir("update"));
    public static string DownloadDir => UpdateDirectory.Value;

    /// <summary>
    /// Скачивает и проверяет комплект обновления. progress — проценты 0..100, затем -1 («проверяю подпись»).
    /// Возвращает путь к проверенному файлу или null (error — текст для пользователя). Плохой файл удаляется.
    /// </summary>
    public static async Task<(string path, string error)> DownloadAndVerifyAsync(UpdateInfo info, IProgress<int> progress)
    {
        string file = null;
        using var total = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        try
        {
            file = Path.Combine(DownloadDir, $"Merno-{info.Parsed}.zip");
            Log.Write($"Скачиваю обновление {info.Version}: {info.ExeUri}");
            using (var resp = await _file.GetAsync(info.ExeUri, HttpCompletionOption.ResponseHeadersRead, total.Token).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                using var src = await resp.Content.ReadAsStreamAsync(total.Token).ConfigureAwait(false);
                using var dst = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
                int last = -1;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await DownloadTransfer.CopyAsync(src, dst, info.Size, TimeSpan.FromSeconds(60), total.Token, async got =>
                {
                    int pct = (int)(got * 100 / info.Size);
                    if (pct != last) { last = pct; progress?.Report(pct); }
                    await NetLimit.PaceAsync(got, sw).WaitAsync(total.Token).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            progress?.Report(-1);
            string bad = await Task.Run(() => Verify(file, info)).ConfigureAwait(false);
            if (bad != null)
            {
                Log.Warn($"Обновление {info.Version} скачалось с ошибкой и удалено. Попробуйте позже");
                Log.Skip($"Причина: {bad}");
                TryDelete(file);
                return (null, "sig");
            }
            Log.Ok($"Обновление {info.Version} скачано и проверено");
            return (file, null);
        }
        catch (Exception ex)
        {
            Log.Warn("Не удалось скачать обновление: " + ex.Message);
            if (file != null) TryDelete(file);
            return (null, "Не удалось скачать обновление. Проверьте интернет и попробуйте позже.");
        }
    }

    /// <summary>null — всё верно, иначе причина.</summary>
    private static string Verify(string file, UpdateInfo info)
    {
        try
        {
            var fi = new FileInfo(file);
            if (fi.Length != info.Size) return $"размер {fi.Length} вместо {info.Size}";
            byte[] hash;
            using (var fs = File.OpenRead(file)) hash = SHA256.HashData(fs);
            if (!string.Equals(Convert.ToHexString(hash), info.Sha256, StringComparison.OrdinalIgnoreCase)) return "SHA-256 не совпадает";
            if (!UpdateMetadata.Accept(VerifyMeta(info), info.Pinned)) return "подпись version.json неверна или срок истёк";
            using var rsa = LoadKey();
            // SignData на стороне выпуска = подпись SHA-256 от байтов файла, поэтому проверяем хеш
            if (!rsa.VerifyHash(hash, Convert.FromBase64String(info.SigExe), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return "подпись файла неверна";
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>
    /// Сам обновляется только установленный Merno: запущенный файл лежит в versions\ папки установки
    /// и прошёл проверку ProtectedCode. Портативная копия обновляется вручную.
    /// </summary>
    internal static bool CanSelfUpdate(string file)
    {
        string install = Path.GetDirectoryName(Autostart.InstalledExe);
        return file != null && file.StartsWith(Path.Combine(install, "versions") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            ProtectedCode.IsTrustedFile(file, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
    }
    internal const string ManualUpdateMessage = "Сам обновляется только установленный Merno. Скачайте новую версию на GitHub: Merno-Setup.exe установит её, и дальше обновления будут приходить сами. Портативную версию (Merno-Portable.zip) достаточно скачать и распаковать заново.";

    internal static void ExtractKit(string archive, string destination)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.Contains('\\') || entry.FullName.StartsWith('/') || entry.FullName.Split('/').Any(p => p is "." or "..") || entry.FullName.Contains(':'))
                throw new InvalidDataException("Небезопасный путь в комплекте.");
            total = checked(total + entry.Length);
            if (total > 1024L * 1024 * 1024) throw new InvalidDataException("Комплект слишком большой.");
        }
        zip.ExtractToDirectory(destination);
        if (!File.Exists(Path.Combine(destination, "Merno.Setup.exe"))) throw new InvalidDataException("В комплекте нет установщика.");
    }

    /// <summary>Версия выпуска строго новее запущенной. Нет номера или он не разбирается — не новее.</summary>
    internal static bool IsNewer(string version, System.Version current) =>
        System.Version.TryParse(version, out var v) && v > current;

    /// <summary>
    /// Ставит скачанный и проверенный комплект: ещё раз сверяет подпись, распаковывает его в рабочую папку и запускает
    /// из неё установщик. Установщик дождётся закрытия Merno, поставит новую версию в versions\ и откроет Merno.
    /// null — установщик запущен (вызывающий закрывает программу), "sig" — комплект не прошёл проверку, иначе текст ошибки.
    /// </summary>
    public static string Apply(string newFile, UpdateInfo info)
    {
        if (!CanSelfUpdate(Environment.ProcessPath)) return ManualUpdateMessage;
        try
        {
            using var source = new FileStream(newFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (info.Format != "installed-kit-v1" || Verify(newFile, info) != null) return "sig";
            // ещё раз, перед самой установкой: старую или ту же версию не ставим (защита от отката)
            if (!IsNewer(info.Version, Current)) { Log.Warn($"Обновление {info.Version} не новее запущенной {App.Version}, не ставим"); return "sig"; }
            string directory = Log.NewWorkDir("install");
            ExtractKit(newFile, directory);
            string installer = Path.Combine(directory, "Merno.Setup.exe");
            using var held = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
            var start = new ProcessStartInfo(installer) { UseShellExecute = false, WorkingDirectory = directory };
            // установщик Inno: только окно хода; спросит, только если Merno не закроется. Merno он откроет сам,
            // новую или (если не встало) прежнюю (tools/installer/Merno.iss)
            foreach (var a in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART" }) start.ArgumentList.Add(a);
            start.ArgumentList.Add("--wait-pid"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            ProcessEnvironment.Configure(start);
            using var process = Process.Start(start);
            if (process == null || process.WaitForExit(1000)) return "Установщик обновления не запустился. Прежняя версия сохранена.";
            // след для следующего запуска (TakeFailedUpdate). Установщик ждёт, пока мы закроемся, так что успеваем
            try { AtomicFile.Write(PendingFile, info.Version); }
            catch (Exception ex) { Log.Skip("Не удалось запомнить ставящееся обновление: " + ex.Message); }
            return null;
        }
        catch (Exception ex) { Log.Warn("Обновление не установлено: " + ex.Message); return "Не удалось установить комплект. Прежняя версия сохранена."; }
    }

    /// <summary>Какую версию ставило обновление. Пишет только Merno (Apply), в защищённую папку данных; установщик сюда не пишет.</summary>
    private static string PendingFile => Path.Combine(Log.DataDir, "update-pending.txt");

    /// <summary>
    /// При запуске: прошлый запуск ставил обновление (Apply). Если работает версия старше той, что ставили, обновление
    /// не встало: вернёт текст для строки состояния, иначе null. След удаляется в любом случае.
    /// Причину не называем: здесь её не узнать (Merno не закрылся, отмена, ошибка установщика).
    /// </summary>
    internal static string TakeFailedUpdate()
    {
        try
        {
            string file = PendingFile;
            if (!File.Exists(file)) return null;
            string text = File.ReadAllText(file).Trim();
            TryDelete(file);
            var failed = FailedUpdate(text, Current);
            if (failed == null) { if (PendingVersion(text) != null) Log.Ok($"Merno обновлён до версии {App.Version}"); return null; }
            Log.Warn($"Обновление до версии {text} не установилось. Работает версия {App.Version}");
            return failed;
        }
        catch (Exception ex) { Log.Skip("Проверка прошлого обновления: " + ex.Message); return null; }
    }

    static System.Version PendingVersion(string text) =>
        text != null && text.Length <= 40 && System.Text.RegularExpressions.Regex.IsMatch(text, @"\A\d{1,9}(\.\d{1,9}){2}\z") &&
        System.Version.TryParse(text, out var v) ? v : null;

    /// <summary>Текст «не установилось», если работает версия старше той, что ставили; иначе null.
    /// Сравнение то же, что в CheckAsync: System.Version по числам.</summary>
    internal static string FailedUpdate(string pending, System.Version current) =>
        PendingVersion(pending?.Trim()) is { } target && current < target
            ? $"Обновление до версии {pending.Trim()} не установилось. Работает прежняя версия, обновить можно ещё раз."
            : null;

    /// <summary>При запуске: убрать скачанные комплекты обновления.
    /// Файл может быть ещё занят закрывающимся процессом, поэтому несколько попыток.</summary>
    public static async Task CleanupAsync()
    {
        try
        {
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase)))
                Log.Ok($"Merno обновлён до версии {App.Version}");
            if (!UpdateDirectory.IsValueCreated) return;
            var olds = Directory.GetFiles(DownloadDir, "Merno-*.zip").ToList();
            for (int attempt = 0; attempt < 10 && olds.Count > 0; attempt++)
            {
                olds.RemoveAll(TryDelete);
                if (olds.Count > 0) await Task.Delay(1500).ConfigureAwait(false);
            }
            foreach (var f in olds) Log.Skip("Не удалось удалить старый файл: " + f);
        }
        catch (Exception ex) { Log.Skip("Очистка после обновления: " + ex.Message); }
    }

    private static bool TryDelete(string f)
    {
        try { if (File.Exists(f)) File.Delete(f); return !File.Exists(f); }
        catch { return false; }
    }
}
