// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// Sh.Esc — единственное место, где строки попадают в PowerShell в одинарных кавычках.
/// Ошибка здесь = выполнение чужого кода с правами администратора. Sh.Esc должен удваивать все виды одинарных кавычек, включая ’.
/// </summary>
public class ShEscTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("C:\\Program Files\\Merno", "C:\\Program Files\\Merno")]
    [InlineData("обычный текст", "обычный текст")]
    public void Безопасные_строки_не_меняются(string input, string expected)
    {
        Assert.Equal(expected, Sh.Esc(input));
    }

    [Fact]
    public void Одинарная_кавычка_удваивается()
    {
        Assert.Equal("O''Brien", Sh.Esc("O'Brien"));
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('‘')]   // левая одиночная
    [InlineData('’')]   // правая одиночная
    [InlineData('‚')]   // низкая одиночная
    [InlineData('‛')]   // одиночная сверху
    public void Все_кавычки_PowerShell_удваиваются(char quote)
    {
        string input = $"до{quote}после";
        Assert.Equal($"до{quote}{quote}после", Sh.Esc(input));
    }

    [Fact]
    public void Смешанная_строка_не_выходит_из_кавычек()
    {
        // PowerShell считает кавычкой все пять символов; после Esc каждая удвоена,
        // поэтому строка остаётся одним литералом и не может стать командой.
        string input = "a'b‘c’d‚e‛f";
        string escaped = Sh.Esc(input);
        Assert.Equal("a''b‘‘c’’d‚‚e‛‛f", escaped);
        Assert.DoesNotContain("';", escaped);
    }

    [Fact]
    public void Попытка_внедрения_остаётся_текстом()
    {
        string input = "x'; Remove-Item C:\\Windows -Recurse; '";
        string escaped = Sh.Esc(input);
        Assert.Equal("x''; Remove-Item C:\\Windows -Recurse; ''", escaped);
        // В PowerShell внутри одинарных кавычек две подряд идущие кавычки — это одна кавычка-символ.
        // Значит, после Esc ни одна кавычка не может закрыть строку и стать командой.
        Assert.True(Все_кавычки_удвоены(escaped));
    }

    private static bool Все_кавычки_удвоены(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] is not ('\'' or '‘' or '’' or '‚' or '‛')) continue;
            if (i + 1 >= s.Length || s[i + 1] != s[i]) return false;
            i++;   // пропускаем парную кавычку
        }
        return true;
    }

    [Fact]
    public void Двойные_кавычки_и_обратные_слэши_не_трогаются()
    {
        // Esc рассчитан только на строку в одинарных кавычках; " и \ там обычные символы.
        Assert.Equal("a\"b\\c", Sh.Esc("a\"b\\c"));
    }
}
