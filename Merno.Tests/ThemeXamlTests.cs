// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// Тема перекрашивается регуляркой по всему тексту Themes/Dark.xaml (Theme.MapXaml): «#» и 6–8 шестнадцатеричных цифр.
/// Если такое сочетание окажется не цветом (в тексте, комментарии, ключе), тема молча не применится или исказит строку.
/// </summary>
public class ThemeXamlTests
{
    private static string SourceRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Merno.csproj"))) return d.FullName;
        throw new DirectoryNotFoundException("Не найдена папка с Merno.csproj");
    }

    [Fact]
    public void Перекрашиваются_только_целые_значения_цветов()
    {
        string xaml = File.ReadAllText(Path.Combine(SourceRoot(), "Themes", "Dark.xaml"));
        var matches = Theme.Hex.Matches(xaml);
        Assert.NotEmpty(matches);
        var bad = matches.Where(m =>
        {
            char before = m.Index > 0 ? xaml[m.Index - 1] : ' ';
            int end = m.Index + m.Length;
            char after = end < xaml.Length ? xaml[end] : ' ';
            return !(before == '"' && after == '"' || before == '>' && after == '<');
        }).Select(m => m.Value + " (символ " + m.Index + ")");
        Assert.Empty(bad);
    }

    [Fact]
    public void Перекраска_не_трогает_значки_шрифта()
    {
        const string glyph = "<TextBlock Text=\"&#xEBE8;\" Foreground=\"#5CB8A7\"/>";
        string mapped = Theme.Get("black").MapXaml(glyph);
        Assert.Contains("&#xEBE8;", mapped);
    }
}
