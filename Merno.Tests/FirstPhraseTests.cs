// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Xunit;

namespace Merno.Tests;

/// <summary>Шаг «Программы» в «Настройке»: под названием — только первая фраза описания.</summary>
public class FirstPhraseTests
{
    [Theory]
    [InlineData("Браузер от Google. Быстрый и удобный.", "Браузер от Google")]
    [InlineData("Архиватор: zip, rar, 7z", "Архиватор: zip, rar, 7z")]
    [InlineData("Версия 3.9.28. Только для Windows 11.", "Версия 3.9.28")]
    [InlineData("Вопрос? Ответ.", "Вопрос?")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void TakesFirstPhrase(string desc, string expected) => Assert.Equal(expected, MainWindow.FirstPhrase(desc));
}
