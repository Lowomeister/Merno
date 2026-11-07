// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>
/// Сравнение версий. Строки нельзя сравнивать как текст: «1.9» при таком сравнении «новее» «1.29».
/// Поэтому все версии сравниваются как System.Version, а не строками.
/// </summary>
public class VersionTests
{
    /// <summary>Версии X.Y.Z сравниваются числами: 1.0.10 новее 1.0.9, а 1.1.0 новее 1.0.10.</summary>
    [Fact]
    public void Версия_1_0_10_новее_1_0_9()
    {
        Assert.True(System.Version.Parse("1.0.10") > System.Version.Parse("1.0.9"));
        Assert.True(System.Version.Parse("1.1.0") > System.Version.Parse("1.0.10"));
    }

    [Fact]
    public void Версия_пакета_1_29_новее_1_9()
    {
        var old = @"C:\Program Files\WindowsApps\Microsoft.DesktopAppInstaller_1.9.0.0_x64__8wekyb3d8bbwe";
        var fresh = @"C:\Program Files\WindowsApps\Microsoft.DesktopAppInstaller_1.29.380.0_x64__8wekyb3d8bbwe";

        var newest = new[] { old, fresh }.OrderByDescending(Apps.PackageVersion).First();

        Assert.Equal(fresh, newest);
        Assert.True(Apps.PackageVersion(fresh) > Apps.PackageVersion(old));
    }

    [Fact]
    public void Версия_пакета_2_0_новее_1_29()
    {
        var a = @"C:\WindowsApps\Microsoft.DesktopAppInstaller_1.29.380.0_x64__8wekyb3d8bbwe";
        var b = @"C:\WindowsApps\Microsoft.DesktopAppInstaller_2.0.0.0_x64__8wekyb3d8bbwe";

        Assert.True(Apps.PackageVersion(b) > Apps.PackageVersion(a));
    }

    [Fact]
    public void Неизвестное_имя_папки_даёт_нулевую_версию()
    {
        Assert.Equal(new System.Version(0, 0), Apps.PackageVersion(@"C:\WindowsApps\SomeOtherPackage_1.29.0.0"));
        Assert.Equal(new System.Version(0, 0), Apps.PackageVersion(null));
        Assert.Equal(new System.Version(0, 0), Apps.PackageVersion(""));
    }

    [Theory]
    [InlineData("v1.29.380", "1.29.380")]
    [InlineData("1.29.380", "1.29.380")]
    [InlineData("v1.9", "1.9")]
    [InlineData("winget 1.6.3133", "1.6.3133")]
    public void Версия_winget_читается_из_вывода(string output, string expected)
    {
        Assert.Equal(System.Version.Parse(expected), WingetSetup.ParseVersion(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("нет версии")]
    [InlineData("v")]
    public void Мусор_в_выводе_даёт_null(string output)
    {
        Assert.Null(WingetSetup.ParseVersion(output));
    }

    [Fact]
    public void Минимальная_версия_winget_1_6()
    {
        Assert.False(WingetSetup.IsSupported(null));
        Assert.False(WingetSetup.IsSupported(new System.Version(1, 5, 9)));
        Assert.True(WingetSetup.IsSupported(new System.Version(1, 6)));
        Assert.True(WingetSetup.IsSupported(new System.Version(1, 29, 380)));
        Assert.True(WingetSetup.IsSupported(new System.Version(2, 0)));
    }

    [Fact]
    public void Текстовое_сравнение_действительно_ломается()
    {
        // Почему нельзя сравнивать строки: "1.9" > "1.29" по алфавиту.
        Assert.True(string.CompareOrdinal("1.9", "1.29") > 0);
        Assert.True(new System.Version(1, 29) > new System.Version(1, 9));
    }
}
