// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// Отчёт о сбое (Core/CrashReport.cs): только тип ошибки и место, без имени пользователя и путей.
/// Запись файла и предложение скопировать отчёт при запуске проверяются вручную; здесь — чистая часть, которую легко сломать правкой.
/// </summary>
public class CrashReportTests
{
    [Fact]
    public void Отчёт_содержит_тип_ошибки_и_версию()
    {
        var ex = new InvalidOperationException("Тестовая ошибка");
        string text = CrashReport.Compose(ex, "Проверка");

        Assert.Contains("Отчёт о сбое Merno.", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("Тестовая ошибка", text);
        Assert.Contains("Проверка", text);
        Assert.Contains("Merno " + App.Version, text);
    }

    [Fact]
    public void Путь_пользователя_заменяется()
    {
        string text = CrashReport.Sanitize(@"Не удалось открыть C:\Users\Ivan\AppData\Local\Temp\file.txt", 1000);

        Assert.DoesNotContain(@"C:\Users\Ivan", text);
        Assert.Contains("%USERPROFILE%", text);
    }

    [Fact]
    public void Имя_пользователя_и_компьютера_не_уходят()
    {
        string user = Environment.UserName;
        string machine = Environment.MachineName;
        string text = CrashReport.Sanitize($"Пользователь {user} на {machine}", 1000);

        Assert.DoesNotContain(user, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(machine, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USER%", text);
        Assert.Contains("%PC%", text);
    }

    [Fact]
    public void Длинный_отчёт_обрезается()
    {
        string text = CrashReport.Sanitize(new string('я', 5000), 2500);

        Assert.Equal(2501, text.Length);   // 2500 символов + многоточие
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void Одинаковая_ошибка_даёт_одинаковый_отпечаток()
    {
        var a = new InvalidOperationException("Сломалось на шаге 42");
        var b = new InvalidOperationException("Сломалось на шаге 99");

        // цифры обезличиваются: одно и то же место с разными числами — одна ошибка
        Assert.Equal(CrashReport.Signature(a), CrashReport.Signature(b));
    }

    [Fact]
    public void Разные_ошибки_дают_разные_отпечатки()
    {
        Assert.NotEqual(
            CrashReport.Signature(new InvalidOperationException("A")),
            CrashReport.Signature(new ArgumentException("A")));
    }

    [Fact]
    public void Управляющие_символы_убираются()
    {
        string text = CrashReport.Sanitize("a\u0001b\u0007c", 1000);
        Assert.Equal("abc", text);
    }

    [Fact]
    public void Системный_сбой_описывается_кодом_и_модулем()
    {
        string text = CrashReport.ComposeNative(0xC0000005, new IntPtr(0x1009),
            @"C:\Users\Ivan\Downloads\crashy\crashy-payload.dll");

        Assert.Contains("нарушение доступа к памяти", text);
        Assert.Contains("0xC0000005", text);
        Assert.Contains("crashy-payload.dll", text);
        Assert.DoesNotContain(@"C:\Users\Ivan", text);
    }

    [Fact]
    public void Один_и_тот_же_системный_сбой_даёт_один_отпечаток()
    {
        // Путь к DLL может быть разным, адрес меняется от ASLR — отпечаток только по коду и имени модуля.
        Assert.Equal(
            CrashReport.NativeSignature(0xC0000005, @"C:\Users\Ivan\Downloads\crashy\crashy-payload.dll"),
            CrashReport.NativeSignature(0xC0000005, @"D:\Other\crashy-payload.dll"));
        Assert.NotEqual(
            CrashReport.NativeSignature(0xC0000005, "crashy-payload.dll"),
            CrashReport.NativeSignature(0xC00000FD, "crashy-payload.dll"));
    }

    [Fact]
    public void Имя_пользователя_merno_не_портит_название_программы()
    {
        // Имя пользователя, совпадающее с названием программы, не должно портить текст.
        const string line = @"Merno 1.0.0 запущен. Журнал: C:\ProgramData\Merno\logs";
        Assert.Equal(line, CrashReport.MaskWord(line, "merno", "%USER%"));
        Assert.Equal("Вход: %USER%, Merno", CrashReport.MaskWord("Вход: Ivan, Merno", "Ivan", "%USER%"));
        Assert.Equal("Ян открыл", CrashReport.MaskWord("Ян открыл", "Ян", "%USER%"));
    }
}
