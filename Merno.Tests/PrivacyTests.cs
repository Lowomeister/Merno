// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;
using Xunit;

namespace Merno.Tests;

/// <summary>Личное на экране и в журнале: MAC скрыт до «Показать», в журнале нет пути к профилю.</summary>
public class PrivacyTests
{
    [Theory]
    [InlineData("A4-5E-60-12-3A-7F", "••-••-••-••-3A-7F")]
    [InlineData("A4:5E:60:12:3A:7F", "••-••-••-••-3A-7F")]
    [InlineData("12345678", "••••5678")]
    [InlineData("", "")]
    public void MacIsMasked(string mac, string masked) => Assert.Equal(masked, MainWindow.MaskSecret(mac));

    [Fact]
    public void ProfilePathIsHiddenInLogText()
    {
        var t = CrashReport.Sanitize(@"Не удалось удалить C:\Users\Вася Пупкин\AppData\Local\Temp\x.tmp", int.MaxValue);
        Assert.DoesNotContain("Вася", t);
        Assert.Contains(@"%USERPROFILE%\AppData", t);
    }

    [Fact]
    public void LongLogIsNotCut() => Assert.Equal(5000, CrashReport.Sanitize(new string('a', 5000), int.MaxValue).Length);
}
