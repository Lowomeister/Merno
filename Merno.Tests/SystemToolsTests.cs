// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// «Быстрый доступ»: программы Windows с правами администратора в манифесте (netplwiz, regedit, rstrui…) не открывались —
/// ошибка 740 при запуске с обычными правами. Merno запускает их сам, но только из системной папки.
/// Тесты только читают файлы, ничего не запускают.
/// </summary>
public class SystemToolsTests
{
    [Theory]
    [InlineData("<requestedExecutionLevel level=\"requireAdministrator\" uiAccess=\"false\"/>", true)]
    [InlineData("<requestedExecutionLevel  uiAccess='false' level='highestAvailable' />", true)]
    [InlineData("<requestedExecutionLevel level=\"asInvoker\" uiAccess=\"false\"/>", false)]
    [InlineData("", false)]
    [InlineData("requireAdministrator", false)]
    public void Манифест_с_правами_администратора(string text, bool needs) =>
        Assert.Equal(needs, SystemTools.ManifestNeedsAdmin(text));

    [Theory]
    [InlineData(@"C:\Windows\System32\netplwiz.exe", true)]
    [InlineData(@"C:\Windows\regedit.exe", true)]
    [InlineData(@"C:\Windows\System32\wbem\wmic.exe", false)]
    [InlineData(@"C:\Users\Ivan\Downloads\regedit.exe", false)]
    [InlineData(@"regedit.exe", false)]
    [InlineData(@"C:\Windows\System32\..\..\Users\x\netplwiz.exe", false)]
    public void Только_системные_папки(string path, bool ok) =>
        Assert.Equal(ok, SystemTools.InSystemFolder(path, @"C:\Windows\System32", @"C:\Windows"));

    [Fact]
    public void Настоящие_программы_этого_компьютера()
    {
        string sys = Environment.SystemDirectory;
        foreach (var exe in new[] { "netplwiz.exe", "msconfig.exe", "rstrui.exe", "optionalfeatures.exe" })
        {
            var p = Path.Combine(sys, exe);
            if (File.Exists(p)) Assert.True(SystemTools.NeedsAdmin(p), exe);
        }
        foreach (var exe in new[] { "msinfo32.exe", "cleanmgr.exe", "resmon.exe" })
        {
            var p = Path.Combine(sys, exe);
            if (File.Exists(p)) Assert.False(SystemTools.NeedsAdmin(p), exe);
        }
    }
}
