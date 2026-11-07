// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Xunit;

namespace Merno.Tests;

/// <summary>«Поиск»: «edge» должен находить сначала программу Microsoft Edge, а не настройку про него.</summary>
public class SearchRankTests
{
    [Theory]
    [InlineData("Edge", "edge", 0)]
    [InlineData("Microsoft Edge", "edge", 1)]
    [InlineData("Dota 2", "dota", 0)]
    [InlineData("Outlook (новый)", "outlook", 0)]
    [InlineData("Knowledge base", "edge", 2)]
    [InlineData("Chrome", "edge", 3)]
    public void WordStartBeatsMiddle(string title, string q, int rank) => Assert.Equal(rank, MainWindow.TitleRank(title, q));

    [Fact]
    public void InstalledBeforeTweaks() => Assert.True(MainWindow.KindRank("Установлена") < MainWindow.KindRank("Настройка Windows"));
}
