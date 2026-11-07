// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// WebView2 «уже установлен», а не ставится: в EdgeUpdate\Clients осталась версия (pv), а файлов этой версии нет.
/// Merno убирает такую запись перед установкой, но только когда уверен. Здесь проверяется только решение
/// «зависла запись или нет»: реестр и диск не трогаем, наличие файла подменяем.
/// </summary>
public class WebView2Tests
{
    [Fact]
    public void Версия_есть_а_файла_нет_запись_зависла()
    {
        string asked = null;

        Assert.True(WebView2.IsStale("156.0.4314.58", v => { asked = v; return false; }));
        Assert.Equal("156.0.4314.58", asked);
    }

    [Fact]
    public void Файл_этой_версии_есть_запись_не_трогаем()
    {
        Assert.False(WebView2.IsStale("156.0.4314.58", _ => true));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("00.0.0.0")]
    [InlineData("0.00.000.0")]
    public void Нулевая_версия_не_зависла(string pv)
    {
        bool asked = false;

        Assert.False(WebView2.IsStale(pv, _ => { asked = true; return false; }));
        Assert.False(asked);
    }

    /// <summary>Из pv строится путь к папке: всё, что не похоже на версию, не трогаем и путь не строим.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("156.0.4314")]
    [InlineData("156.0.4314.58.1")]
    [InlineData("156.0.4314.58\n")]
    [InlineData(" 156.0.4314.58")]
    [InlineData("156.0.4314.58 ")]
    [InlineData("１５６.０.４３１４.５８")]
    [InlineData("156.0.4314.58\\..")]
    [InlineData("..\\156.0.4314.58")]
    [InlineData("156.0.4314.-58")]
    [InlineData("156,0,4314,58")]
    [InlineData("v156.0.4314.58")]
    [InlineData("99999999999.0.0.1")]
    public void Непонятная_версия_не_зависла(string pv)
    {
        bool asked = false;

        Assert.False(WebView2.IsStale(pv, _ => { asked = true; return false; }));
        Assert.False(asked);
    }

    [Fact]
    public void Без_проверки_файла_ничего_не_решаем()
    {
        Assert.False(WebView2.IsStale("156.0.4314.58", null));
    }

    [Fact]
    public void Путь_к_файлу_версии()
    {
        Assert.Equal(@"C:\Apps\EdgeWebView\Application\156.0.4314.58\msedgewebview2.exe",
                     WebView2.ExePath(@"C:\Apps\EdgeWebView\Application", "156.0.4314.58"));
    }
}
