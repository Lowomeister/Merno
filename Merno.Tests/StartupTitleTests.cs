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
