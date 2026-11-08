// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using Merno.Core;

namespace Merno.Tests;

/// <summary>Пути из реестра: Merno с правами администратора не должна ходить по сети (SMB) за значками и файлами.</summary>
public class LocalPathTests
{
    static DriveType Fake(char d) => char.ToUpperInvariant(d) switch
    {
        'C' => DriveType.Fixed,
        'E' => DriveType.Removable,
        'Z' => DriveType.Network,
        _ => DriveType.NoRootDirectory,
    };

    [Theory]
    [InlineData(@"C:\Program Files\App\app.exe")]
    [InlineData(@"c:/Windows/notepad.exe")]
    [InlineData(@"E:\Portable\tool.exe")]
    public void LocalDrivesAreAllowed(string path) => Assert.True(SafeFs.IsLocal(path, Fake));

    [Theory]
    [InlineData(@"\server\share\app.exe")]
    [InlineData(@"//server/share/app.exe")]
    [InlineData(@"\?\UNC\server\share\app.exe")]
    [InlineData(@"\.\pipe\x")]
    [InlineData(@"\?\C:\Windows\notepad.exe")]
    [InlineData(@"Z:\mapped\app.exe")]
    [InlineData(@"Q:\missing\app.exe")]
    [InlineData(@"app.exe")]
    [InlineData(@"\Windows\notepad.exe")]
    [InlineData(@"C:relative.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void NetworkAndUnclearPathsAreRejected(string path) => Assert.False(SafeFs.IsLocal(path, Fake));

    [Fact]
    public void DriveQueryFailureIsRejected() =>
        Assert.False(SafeFs.IsLocal(@"C:\a.exe", _ => throw new IOException()));

    [Fact]
    public void SystemFolderIsLocalOnThisComputer() =>
        Assert.True(SafeFs.IsLocal(Environment.SystemDirectory));
}
