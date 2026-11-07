// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>Свободное место на диске пишется везде одинаково (Sh.FreeOf): «Главная», «Очистка», «Мой компьютер».</summary>
public class FreeSpaceFormatTests
{
    const long Gb = 1L << 30;

    [Theory]
    [InlineData(487 * Gb + 900_000_000, 952 * Gb, false, "свободно 487 из 952 ГБ")]
    [InlineData(3 * Gb + 460_000_000, 59 * Gb, false, "свободно 3,4 из 59 ГБ")]
    [InlineData(200_000_000, 59 * Gb, true, "Свободно 0,1 из 59 ГБ")]
    [InlineData(10 * Gb, 100 * Gb, true, "Свободно 10 из 100 ГБ")]
    public void FreeSpaceHasOneFormat(long free, long total, bool capital, string expected) =>
        Assert.Equal(expected, Sh.FreeOf(free, total, capital));
}
