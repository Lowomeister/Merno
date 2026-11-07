// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// «Приложения Windows»: ошибка PowerShell не должна превращаться в «Microsoft Store не найден» или «нет на компьютере».
/// </summary>
public class StoreDetectionTests
{
    [Theory]
    [InlineData(0, "MERNO_STORE_YES\r\n", true)]
    [InlineData(0, "MERNO_STORE_NO\n", false)]
    [InlineData(0, "MERNO_STORE_FAIL\n", null)]
    [InlineData(0, "", null)]
    [InlineData(-1, "timeout", null)]
    [InlineData(1, "MERNO_STORE_NO", null)]
    [InlineData(0, "warning text\nMERNO_STORE_YES\n\n", true)]
    public void StoreAnswerSaysNoOnlyOnExplicitNo(int code, string output, bool? expected) =>
        Assert.Equal(expected, Apps.StoreAnswer(code, output));

    [Fact]
    public void AppxListNeedsSuccessMarker()
    {
        var names = Apps.AppxNames(0, "Microsoft.WindowsStore\r\nMicrosoft.GamingApp\r\nMERNO_APPX_OK\r\n", out bool ok);
        Assert.True(ok);
        Assert.Contains("microsoft.gamingapp", names);   // сравнение без учёта регистра
        Assert.DoesNotContain("MERNO_APPX_OK", names);

        Apps.AppxNames(0, "", out ok);
        Assert.False(ok);
        Apps.AppxNames(0, "MERNO_APPX_FAIL\n", out ok);
        Assert.False(ok);
        Apps.AppxNames(-1, "timeout", out ok);
        Assert.False(ok);
        Apps.AppxNames(0, "Microsoft.WindowsStore\nMERNO_APPX_OK\nMicrosoft.Partial", out ok);   // маркер не последний: вывод обрезан
        Assert.False(ok);
        Assert.True(Apps.AppxNames(0, "MERNO_APPX_OK\n", out ok).Count == 0 && !ok);   // ноль пакетов может значить, что ошибки PowerShell скрыты, а не что ничего не установлено
    }
}
