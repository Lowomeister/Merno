// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// «Драйверы»: ответ PowerShell со списком драйверов (Drivers.ListAnswerOk). Пусто при коде 0 — драйверов
/// не от Microsoft нет (норма для чистой Windows); текст ошибки или пусто при ошибке — список не получен, страница
/// показывает «Повторить», а не пустоту.
/// </summary>
public class DriverListTests
{
    [Theory]
    [InlineData(0, "", true)]
    [InlineData(0, "   ", true)]
    [InlineData(1, "", false)]
    [InlineData(-1, "", false)]
    [InlineData(0, "[{\"DeviceName\":\"NVIDIA GeForce RTX 4060\"}]", true)]
    [InlineData(0, "{\"DeviceName\":\"Realtek Audio\"}", true)]
    [InlineData(-1, "Вывод процесса неполный или превышает допустимый размер.", false)]
    [InlineData(1, "Get-CimInstance : Отказано в доступе", false)]
    public void Ответ_со_списком_драйверов(int code, string output, bool ok) =>
        Assert.Equal(ok, Drivers.ListAnswerOk(code, output));
}
