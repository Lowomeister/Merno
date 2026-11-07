// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace Merno.Core;

/// <summary>
/// winget — то, через что Merno ставит программы. Он входит в «Установщик приложения» из Microsoft Store,
/// и на старых сборках Windows 10 (без Store, LTSC, давно не обновлялись) его нет или он слишком старый.
/// Тогда Merno ставит его сам: официальный выпуск Microsoft с GitHub (winget-cli) + библиотеки VCLibs и UI.Xaml.
/// Работает на Windows 10 1809 (сборка 17763) и новее; на более старых winget не встаёт в принципе.
/// Подлинность: ссылки только на github.com (HTTPS), а Add-AppxPackage ставит только пакеты с доверенной подписью
/// (Windows проверяет подпись, но не то, что это именно Microsoft). Файлы лежат в защищённой папке Merno, не в %TEMP%.
/// </summary>
public static class WingetSetup
{
    public enum State { Ok, Old, Missing, Unsupported }

    /// <summary>Ниже — winget не понимает команды, которые даёт Merno (--disable-interactivity и др.).</summary>
    public static readonly System.Version MinVersion = new(1, 6);
    const int MinBuild = 17763;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(40) };
    static WingetSetup() { try { Http.DefaultRequestHeaders.UserAgent.ParseAdd("Merno/" + App.Version); } catch { } }

    public static int Build => Environment.OSVersion.Version.Build;

    /// <summary>Версия из вывода winget: «v1.29.380» → 1.29.380; null — версии в тексте нет.</summary>
    public static System.Version ParseVersion(string output)
    {
        var m = System.Text.RegularExpressions.Regex.Match(output ?? "", @"v?(\d+\.\d+(\.\d+)?)");
        return m.Success && System.Version.TryParse(m.Groups[1].Value, out var v) ? v : null;
    }

    /// <summary>Достаточно ли свежий winget для команд Merno. Сравнение — System.Version, не текстом:
    /// иначе «1.9» окажется новее «1.29».</summary>
    public static bool IsSupported(System.Version v) => v != null && v >= MinVersion;

    /// <summary>Версия winget («v1.29.380» → 1.29.380); null — не запускается.</summary>
    public static System.Version Version()
    {
        try
        {
            var (code, o) = Sh.Run(Apps.Exe, "--version", 20_000);
            return code != 0 ? null : ParseVersion(o);
        }
        catch { return null; }
    }

    public static State Check()
    {
        // только для режима снимков: показать, как выглядит плашка без winget
        if (MainWindow.ShotDir != null && Environment.GetEnvironmentVariable("MERNO_SHOT_NOWINGET") == "1") return State.Missing;
        var v = Version();
        if (v != null) return IsSupported(v) ? State.Ok : State.Old;
        return Build < MinBuild ? State.Unsupported : State.Missing;
    }

    /// <summary>Скачать и поставить свежий winget. progress — текст для строки состояния.</summary>
    public static (bool Ok, string Error) Install(Action<string> progress)
    {
        if (Build < MinBuild) return (false, "На этой версии Windows 10 установка программ не работает. Нужна Windows 10 версии 1809 или новее.");
        Apps.ResetWinget();
        // не %TEMP%: туда любая программа пользователя могла бы подложить файлы, которые мы ставим с правами администратора
        string dir = null;
        try
        {
            dir = Log.NewWorkDir("winget");
            progress?.Invoke("Узнаю свежую версию…");
            var json = Http.GetStringAsync("https://api.github.com/repos/microsoft/winget-cli/releases/latest").GetAwaiter().GetResult();
            string bundleUrl = null, depsUrl = null, tag = "";
            using (var doc = JsonDocument.Parse(json))
            {
                tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : "";
                foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
                {
                    string n = a.GetProperty("name").GetString() ?? "", u = a.GetProperty("browser_download_url").GetString();
                    if (n.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) && n.StartsWith("Microsoft.DesktopAppInstaller")) bundleUrl = u;
                    else if (n.Equals("DesktopAppInstaller_Dependencies.zip", StringComparison.OrdinalIgnoreCase)) depsUrl = u;
                }
            }
            // только с GitHub — ссылки из ответа не должны уводить на чужие сайты
            bool Gh(string u) => u != null && Uri.TryCreate(u, UriKind.Absolute, out var x) && x.Scheme == "https" && x.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
            if (!Gh(bundleUrl) || !Gh(depsUrl)) return (false, "Установщик winget на GitHub не найден. Попробуйте позже.");

            string bundle = Path.Combine(dir, "AppInstaller.msixbundle"), deps = Path.Combine(dir, "deps.zip");
            Download(depsUrl, deps, "Скачиваю библиотеки", progress);
            Download(bundleUrl, bundle, $"Скачиваю winget {tag}", progress);

            progress?.Invoke("Распаковываю…");
            string depsDir = Path.Combine(dir, "deps");
            ZipFile.ExtractToDirectory(deps, depsDir);
            string arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
                System.Runtime.InteropServices.Architecture.X86 => "x86",
                _ => "x64"
            };
            var packages = Directory.GetFiles(depsDir, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f).Equals(".appx", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetExtension(f).Equals(".msix", StringComparison.OrdinalIgnoreCase)).ToArray();
            var appx = packages
                .Where(f => Path.GetDirectoryName(f)!.Split(Path.DirectorySeparatorChar).Any(p => p.Equals(arch, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (appx.Count == 0) return (false, "В выпуске winget нет библиотек для этой Windows. Установите «Установщик приложения» из Microsoft Store.");

            foreach (var package in appx.Append(bundle))
            {
                if (!BundleGuard.VerifyTrust(package)) throw new InvalidDataException("Подпись пакета winget не прошла проверку Windows.");
                // у X509CertificateLoader нет замены для чтения подписи из файла; сама подпись проверена VerifyTrust выше
#pragma warning disable SYSLIB0057
                using var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(package));
#pragma warning restore SYSLIB0057
                if (!certificate.SubjectName.EnumerateRelativeDistinguishedNames().Any(r => !r.HasMultipleElements && r.GetSingleElementType().Value == "2.5.4.10" && r.GetSingleElementValue() == "Microsoft Corporation"))
                    throw new InvalidDataException("Пакет winget подписан не Microsoft.");
            }
            progress?.Invoke("Ставлю winget…");
            string list = string.Join(",", appx.Select(f => "'" + Sh.Esc(f) + "'"));
            // ссылки берутся из ответа GitHub без подписи — ставим только пакеты, подписанные Microsoft
            var (installCode, o) = Sh.PS($@"$ErrorActionPreference = 'Stop'
try {{
  foreach ($d in @({list})) {{ try {{ Add-AppxPackage -Path $d -ErrorAction Stop }} catch {{ 'DEP:' + $_.Exception.Message }} }}
  Add-AppxPackage -Path '{Sh.Esc(bundle)}' -ForceApplicationShutdown -ErrorAction Stop
  'OK'
}} catch {{ 'ERR:' + $_.Exception.Message; exit 1 }}", 900_000);
            foreach (var l in (o ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.StartsWith("DEP:"))) Log.Skip("winget, библиотека: " + l[4..]);   // «уже стоит новее» — не ошибка
            var err = (o ?? "").Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("ERR:"));
            if (installCode != 0 || err != null || !(o ?? "").Split('\n').Any(l => l.Trim() == "OK"))
            {
                Log.Warn("Установка winget не подтверждена: " + (err ?? o));
                return (false, "Не удалось установить winget. Подробности записаны в журнал: «Настройки» → «Журнал».");
            }

            // ярлык winget появляется не сразу
            Apps.ResetWinget();
            for (int i = 0; i < 15 && Check() != State.Ok; i++)
            {
                Thread.Sleep(2000);
                Apps.ResetWinget();
            }
            if (Check() != State.Ok) return (false, "winget поставился, но пока не отвечает. Перезапустите Merno.");
            Log.Ok($"winget установлен: {Version()}");
            return (true, null);
        }
        catch (InvalidDataException ex)
        {
            // подпись не Microsoft, пакет не прошёл проверку Windows или архив повреждён — дело не в интернете
            Log.Warn("Установка winget: " + ex.Message);
            return (false, "Скачанные файлы winget не прошли проверку. Попробуйте ещё раз или установите «Установщик приложения» из Microsoft Store.");
        }
        catch (Exception ex)
        {
            Log.Warn("Установка winget: " + ex.Message);
            return (false, "Не удалось скачать winget. Проверьте интернет и попробуйте ещё раз.");
        }
        finally
        {
            try { var root = SafeFs.RootFinal(Log.DataDir); if (dir != null && root != null && Directory.Exists(dir)) SafeFs.DeleteTree(dir, root, true); } catch { }
        }
    }

    static void Download(string url, string file, string what, Action<string> progress)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var resp = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        using var src = resp.Content.ReadAsStream();
        using var dst = File.Create(file);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        DownloadTransfer.CopyAsync(src, dst, 400L * 1024 * 1024, TimeSpan.FromSeconds(60), timeout.Token, async got =>
        {
            await NetLimit.PaceAsync(got, sw).ConfigureAwait(false);
            progress?.Invoke($"{what}: {got >> 20} МБ");
        }).GetAwaiter().GetResult();
    }
}
