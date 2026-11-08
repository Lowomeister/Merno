// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// «Связаться с нами» → «Скопировать сведения» (Core/Feedback.cs): текст вставляется в сообщение Telegram
/// (до 4096 символов), поэтому он короткий, без личного и с самыми свежими ошибками.
/// </summary>
public class FeedbackInfoTests
{
    [Fact]
    public void Ссылка_ведёт_в_поддержку_по_https()
    {
        var uri = new Uri(Feedback.TelegramUrl);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("t.me", uri.Host);
        Assert.Equal("?start=merno", uri.Query);
    }

    [Fact]
    public void Ссылка_на_GitHub_ведёт_в_обращения_проекта()
    {
        var uri = new Uri(Feedback.GitHubIssuesUrl);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("github.com", uri.Host);
        Assert.Equal("/Lowomeister/Merno/issues/new/choose", uri.AbsolutePath);
    }

    [Fact]
    public void Сведения_влезают_в_сообщение_и_хранят_свежие_ошибки()
    {
        var errors = string.Join("\n", Enumerable.Range(1, 2000).Select(i => $"ошибка номер {i}"));
        string text = Feedback.ComposeInfo("1.0.0", "Windows 11 Pro 24H2", "Ryzen 5 • 16 ГБ ОЗУ", errors);

        Assert.True(text.Length <= Feedback.MaxInfo, $"длина {text.Length}");
        Assert.StartsWith("Merno 1.0.0", text);
        Assert.Contains("Windows 11 Pro 24H2", text);
        Assert.Contains("Ryzen 5", text);
        Assert.EndsWith("ошибка номер 2000", text);
        Assert.DoesNotContain("ошибка номер 1\n", text);
        // обрезано по началу строки, а не посередине
        Assert.Contains("Последние ошибки:\n…ошибка номер ", text);
    }

    [Fact]
    public void Без_ошибок_пишет_нет()
    {
        string text = Feedback.ComposeInfo("1.0.0", "Windows 10", null, "  ");
        Assert.EndsWith("Последние ошибки:\nнет", text);
        Assert.DoesNotContain("Компьютер:", text);
    }

    [Fact]
    public void Пути_пользователя_не_попадают_в_сведения()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string errors = $@"Не найден файл {profile}\Documents\секрет.txt" + "\n" + @"C:\Users\Иван\AppData\x.log";
        string text = Feedback.ComposeInfo("1.0.0", "Windows 11", null, errors);

        Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Иван", text);
        Assert.Contains("%USERPROFILE%", text);
    }

    [Fact]
    public void Маленький_предел_не_ломает_сборку()
    {
        string text = Feedback.ComposeInfo("1.0.0", "Windows 11", "ПК", new string('x', 500), max: 30);
        Assert.Contains("Последние ошибки:", text);
    }

    [Fact]
    public void Отложенный_отчёт_читается_с_отпечатком()
    {
        var (sig, text) = CrashReport.ReadPending("SIG:ABC123\nОтчёт о сбое Merno.\nЧто: X");
        Assert.Equal("ABC123", sig);
        Assert.StartsWith("Отчёт о сбое Merno.", text);
    }

    [Fact]
    public void Системный_сбой_собирается_в_отчёт()
    {
        var (sig, text) = CrashReport.ReadPending(@"NATIVE|C0000005|7FF812345678|C:\Windows\System32\bad.dll");
        Assert.Equal(CrashReport.NativeSignature(0xC0000005, @"C:\Windows\System32\bad.dll"), sig);
        Assert.Contains("нарушение доступа к памяти", text);
        Assert.Contains("bad.dll", text);
    }
}
