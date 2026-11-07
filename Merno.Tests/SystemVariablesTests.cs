// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using Merno.Core;
namespace Merno.Tests;
public class SystemVariablesTests
{
    [Fact]
    public void ElevatedUninstallUsesOnlyProvidedSystemValues()
    {
        var system = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) { ["ProgramFiles"] = @"C:\Program Files" };
        Assert.Equal(@"C:\Program Files\App\uninstall.exe", SystemVariables.Expand(@"%programfiles%\App\uninstall.exe", system));
        Assert.Throws<IOException>(() => SystemVariables.Expand(@"%LOCALAPPDATA%\App\uninstall.exe", system));
        Assert.Throws<IOException>(() => SystemVariables.Expand(@"%untrusted%\uninstall.exe", system));
    }
}
