// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// Всё, что включают режимы «Обычный», «Игровой» и «Бережный», должно быть видно в «Настройках Windows»:
/// там строки строятся по разделам Catalog.Categories.
/// </summary>
public class TweakCatalogTests
{
    [Theory]
    [InlineData("rec")]
    [InlineData("gaming")]
    [InlineData("minimal")]
    public void Пункты_режима_есть_в_Настройках_Windows(string mode)
    {
        var all = Catalog.All();
        var ids = Catalog.Preset(mode, all).ToHashSet();
        Assert.NotEmpty(ids);
        var hidden = all.Where(t => ids.Contains(t.Id) && !Catalog.Categories.Contains(t.Cat)).Select(t => t.Id + " (" + t.Cat + ")");
        Assert.Empty(hidden);
    }

    [Fact]
    public void Номера_настроек_не_повторяются()
    {
        var dup = Catalog.All().GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(dup);
    }
}
