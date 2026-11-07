// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// Разбор таблицы «winget upgrade» (Core/Apps.cs, ParseUpgrades).
/// Каждый тест проверяет случай, на котором разбор ломался: «Все программы свежие» при том, что обновления есть
/// (вывод склеивался по \r, колонки съезжали, заголовок искали по английским словам).
/// </summary>
public class WingetParseTests
{
    // Колонки как в настоящем выводе winget: Name(0) Id(34) Version(60) Available(73) Source(86).
    private static readonly string HeadEn = Line((0, "Name"), (34, "Id"), (60, "Version"), (73, "Available"), (86, "Source"));
    private static readonly string HeadRu = Line((0, "Имя"), (34, "ИД"), (60, "Версия"), (73, "Доступная"), (86, "Источник"));
    private static readonly string Sep = new string('-', 102);

    /// <summary>Строка с текстом в заданных колонках — как выравнивает winget.</summary>
    private static string Line(params (int Col, string Text)[] parts)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (col, text) in parts.OrderBy(p => p.Col))
        {
            if (sb.Length < col) sb.Append(' ', col - sb.Length);
            sb.Append(text);
        }
        return sb.ToString();
    }

    private static string Row(string name, string id, string from, string to, string source = "winget")
        => Line((0, name), (34, id), (60, from), (73, to), (86, source));

    [Fact]
    public void Английская_таблица_разбирается_по_колонкам()
    {
        string output = string.Join("\n", HeadEn, Sep,
            Row("Mozilla Firefox", "Mozilla.Firefox", "121.0", "122.0"),
            Row("Microsoft Visual Studio Code", "Microsoft.VisualStudioCode", "1.85.0", "1.86.0"));

        var list = Apps.ParseUpgrades(output, out bool table);

        Assert.True(table);
        Assert.Equal(2, list.Count);
        Assert.Equal(new Apps.WingetUpdate("Mozilla Firefox", "Mozilla.Firefox", "121.0", "122.0"), list[0]);
        Assert.Equal("Microsoft.VisualStudioCode", list[1].Id);
        Assert.Equal("1.86.0", list[1].To);
    }

    [Fact]
    public void Русский_заголовок_не_мешает_разбору()
    {
        // Язык winget не важен: колонки ищутся по строке «-----» и словам над ней.
        string output = string.Join("\n", HeadRu, Sep,
            Row("Firefox", "Mozilla.Firefox", "121.0", "122.0"));

        var list = Apps.ParseUpgrades(output, out bool table);

        Assert.True(table);
        Assert.Single(list);
        Assert.Equal("Mozilla.Firefox", list[0].Id);
    }

    [Fact]
    public void Крутилки_и_CRLF_не_склеиваются_с_таблицей()
    {
        // RunStream уже делит по \r, но ParseUpgrades обязан пережить и «сырой» вывод:
        // иначе «-\» попадает в строку заголовка и колонки съезжают.
        string output = "-\r\\\r|\r/\r" + HeadEn + "\r\n" + Sep + "\r\n" +
                        Row("Mozilla Firefox", "Mozilla.Firefox", "121.0", "122.0") + "\r\n";

        var list = Apps.ParseUpgrades(output, out bool table);

        Assert.True(table);
        Assert.Single(list);
        Assert.Equal("Mozilla Firefox", list[0].Name);
    }

    [Fact]
    public void Строка_итога_после_таблицы_не_становится_программой()
    {
        string output = string.Join("\n", HeadEn, Sep,
            Row("Mozilla Firefox", "Mozilla.Firefox", "121.0", "122.0"),
            "",
            "6 upgrades available.",
            "To upgrade a package, run: winget upgrade --id <id>");

        var list = Apps.ParseUpgrades(output, out bool table);

        Assert.True(table);
        Assert.Single(list);
    }

    [Fact]
    public void Без_таблицы_возвращается_пустой_список_и_table_false()
    {
        var list = Apps.ParseUpgrades("No installed package found matching input criteria.", out bool table);

        Assert.False(table);
        Assert.Empty(list);
    }

    [Fact]
    public void Пустой_вывод_не_ломает_разбор()
    {
        Assert.Empty(Apps.ParseUpgrades(null, out bool table));
        Assert.False(table);
        Assert.Empty(Apps.ParseUpgrades("", out table));
        Assert.False(table);
    }

    [Fact]
    public void Мало_колонок_не_считается_таблицей()
    {
        // Таблица без нужных колонок не считается полной, даже если в ней есть Id и Version.
        string output = "Name   Id   Version\n--------------------------------\nFoo    Foo.Bar   1.0";

        var list = Apps.ParseUpgrades(output, out bool table);

        Assert.False(table);
        Assert.Empty(list);
    }

    [Fact]
    public void Таблица_без_строк_остаётся_таблицей()
    {
        // Важно: «таблица есть, обновлений нет» ≠ «проверить не удалось» (Updates() возвращает null).
        var list = Apps.ParseUpgrades(HeadEn + "\n" + Sep + "\n", out bool table);

        Assert.True(table);
        Assert.Empty(list);
    }

    [Fact]
    public void Строка_с_недопустимым_Id_пропускается()
    {
        string output = string.Join("\n", HeadEn, Sep,
            Row("Нормальная программа", "Vendor.App", "1.0", "2.0"),
            Row("Плохая строка", "not a valid id!", "1.0", "2.0"));

        var list = Apps.ParseUpgrades(output, out bool table);

        Assert.True(table);
        Assert.Single(list);
        Assert.Equal("Vendor.App", list[0].Id);
    }

    [Fact]
    public void Без_колонки_источника_остаток_строки_уходит_в_версию()
    {
        // У старых версий winget колонки Source может не быть.
        string head = Line((0, "Name"), (20, "Id"), (40, "Version"), (55, "Available"));
        string sep = "------------------------------------------------------------";
        string row = Line((0, "Foo"), (20, "Vendor.Foo"), (40, "1.0"), (55, "2.0"));

        var list = Apps.ParseUpgrades(head + "\n" + sep + "\n" + row, out bool table);

        Assert.True(table);
        Assert.Single(list);
        Assert.Equal("2.0", list[0].To);
    }

    [Theory]
    [InlineData("Vendor.App")]
    [InlineData("Vendor.App-x64")]
    [InlineData("Vendor.App_x64")]
    [InlineData("Vendor.App+beta")]
    [InlineData("Vendor.App.1.0")]
    public void Допустимые_Id_попадают_в_список(string id)
    {
        string output = string.Join("\n", HeadEn, Sep, Row("Программа", id, "1.0", "2.0"));

        var list = Apps.ParseUpgrades(output, out bool table);

        Assert.True(table);
        Assert.Single(list);
        Assert.Equal(id, list[0].Id);
    }
}
