// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// «Автозагрузка»: понятные названия. Строки в HKCU может записать любая программа,
/// поэтому русское «системное» название и разбор ресурсов пакета даются только тому, что лежит в защищённых папках.
/// </summary>
public class StartupTitleTests
{
    static string Sys(string file) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), file);

    [Theory]
    [InlineData("GoogleChromeAutoLaunch_27E7DFBEDA387BCDDF97FC8C07B06710", "Google Chrome")]
    [InlineData("v2rayNAutoRun_58c393267ca660edfc87e8f73d442bb7", "v2rayN")]
    [InlineData("OneDrive Reporting Task-S-1-5-21-1111111111-2222222222-3333333333-1001", "OneDrive Reporting")]
    [InlineData("MicrosoftEdgeUpdateTaskMachineCore{A1B2C3D4-1111-2222-3333-444455556666}", "Microsoft Edge Update Task Machine Core")]
    [InlineData("MSICenter", "MSI Center")]
    [InlineData("MyAppAutostart", "My App")]
    [InlineData("Steam", "Steam")]
    [InlineData("Discord", "Discord")]
    [InlineData("Radmin VPN", "Radmin VPN")]
    [InlineData("TaskbarX", "TaskbarX")]
    [InlineData("MicrosoftOfficeHub", "Microsoft Office Hub")]
    [InlineData("OneDriveLauncher", "OneDrive Launcher")]
    [InlineData("GeForceExperience", "GeForce Experience")]
    [InlineData("WinRAR", "WinRAR")]
    public void RawKeyBecomesReadableName(string key, string expected) => Assert.Equal(expected, Startup.NameFromKey(key));

    [Theory]
    [InlineData("AutoRun")]
    [InlineData("_0123456789abcdef0123")]
    [InlineData("")]
    [InlineData(null)]
    public void NothingReadableLeftGivesNull(string key) => Assert.Null(Startup.NameFromKey(key));

    [Theory]
    [InlineData("Discord - https://discord.com/", "Discord")]
    [InlineData("Some Tool (https://example.org)", "Some Tool")]
    [InlineData("Radmin VPN", "Radmin VPN")]
    [InlineData("Помощник www.example.ru", "Помощник")]
    [InlineData("OneDriveLauncher", "OneDrive Launcher")]
    [InlineData("v2rayN", "v2rayN")]
    [InlineData("Steam", "Steam")]
    public void DescriptionLosesWebAddress(string text, string expected) => Assert.Equal(expected, Startup.CleanDescription(text));

    [Fact]
    public void KnownFileGetsRussianTitleOnlyFromSystemFolders()
    {
        Assert.Equal("Значок «Безопасность Windows»",
            Startup.KnownTitle(new StartupItem { Kind = StartKind.Registry, Command = Sys("SecurityHealthSystray.exe"), FilePath = Sys("SecurityHealthSystray.exe") }));
        // файла уже нет: путь берётся из команды
        Assert.Equal("Установка OneDrive", Startup.KnownTitle(new StartupItem { Kind = StartKind.Registry, Command = "\"" + Sys("OneDriveSetup.exe") + "\" /thfirstsetup" }));

        var fake = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "SecurityHealthSystray.exe");
        Assert.Null(Startup.KnownTitle(new StartupItem { Kind = StartKind.Registry, Command = fake, FilePath = fake }));
        Assert.Null(Startup.KnownTitle(new StartupItem { Kind = StartKind.Registry, Command = "SecurityHealthSystray.exe" }));   // относительный путь
        Assert.Null(Startup.KnownTitle(new StartupItem { Kind = StartKind.Registry, Command = Sys("notepad.exe"), FilePath = Sys("notepad.exe") }));
    }

    [Fact]
    public void KnownStoreTitleByFamily()
    {
        Assert.Equal("Xbox", Startup.KnownTitle(new StartupItem { Kind = StartKind.Store, Command = "Microsoft.GamingApp_8wekyb3d8bbwe" }));
        Assert.Null(Startup.KnownTitle(new StartupItem { Kind = StartKind.Store, Command = "Some.Other_abc" }));
    }

    [Fact]
    public void PackageResourcesOnlyFromProtectedFolders()
    {
        var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        var sysApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps");
        const string full = "Microsoft.GamingApp_1.0.0.0_x64__8wekyb3d8bbwe", family = "Microsoft.GamingApp_8wekyb3d8bbwe";
        Assert.True(Startup.InProtectedPackageRoot(Path.Combine(apps, full), full, family));
        Assert.True(Startup.InProtectedPackageRoot(Path.Combine(sysApps, family) + "\\", full, family));
        Assert.False(Startup.InProtectedPackageRoot(Path.Combine(apps, "Other.App_1.0.0.0_x64__zzz"), full, family));   // папка другого пакета
        Assert.False(Startup.InProtectedPackageRoot(Path.Combine(sysApps, "Other.App_zzz"), full, family));
        Assert.False(Startup.InProtectedPackageRoot(apps, full, family));                                   // сама корневая папка
        Assert.False(Startup.InProtectedPackageRoot(Path.Combine(apps, full, "nested"), full, family));    // глубже папки пакета
        Assert.False(Startup.InProtectedPackageRoot(Path.Combine(apps, "..", "..", "Users", "Public", full), full, family));
        Assert.False(Startup.InProtectedPackageRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), full), full, family));
        Assert.False(Startup.InProtectedPackageRoot(@"\\server\share\WindowsApps\" + full, full, family));
        Assert.False(Startup.InProtectedPackageRoot(@"Program Files\WindowsApps\" + full, full, family));
        Assert.False(Startup.InProtectedPackageRoot(Path.Combine(apps, full), @"..\x", family));
        Assert.False(Startup.InProtectedPackageRoot(null, full, family));
    }

    [Fact]
    public void PackageTextRejectsForeignReferences()
    {
        const string full = "Vendor.App_1.0.0.0_x64__abc123", name = "Vendor.App";
        Assert.Equal("Vendor App", Startup.PackageText(full, name, " Vendor App "));
        Assert.Null(Startup.PackageText(full, name, @"@C:\Users\Public\evil.dll,-1"));
        Assert.Null(Startup.PackageText(full, name, "@{Other.App_1.0.0.0_x64__zzz?ms-resource://Other.App/Resources/Name}"));
        Assert.Null(Startup.PackageText(full, name, "@{" + full + "?ms-resource://Vendor.App/Resources/Name"));   // нет закрывающей скобки
        Assert.Null(Startup.PackageText(full, name, ""));
        Assert.Null(Startup.PackageText(full, name, "X"));
    }
}
