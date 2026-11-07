// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

/// <summary>Обновления — только с GitHub Releases по HTTPS.</summary>
public class UpdateServerTests
{
    [Fact]
    public void VersionJsonComesFromLatestGitHubRelease()
    {
        var uri = new Uri(Updater.LatestJson);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("github.com", uri.Host);
        Assert.Equal("/Lowomeister/Merno/releases/latest/download/version.json", uri.AbsolutePath);
    }

    [Fact]
    public void KitIsTakenFromReleaseWithItsOwnNumber()
    {
        var uri = Updater.KitUri("1.0.1", "Merno-1.0.1.bin");
        Assert.Equal("https://github.com/Lowomeister/Merno/releases/download/1.0.1/Merno-1.0.1.bin", uri.AbsoluteUri);
    }

    [Fact]
    public void KitAddressCannotLeaveTheRepository()
    {
        // даже если в имя файла попадёт путь, он экранируется и остаётся внутри выпуска
        var uri = Updater.KitUri("1.0.1", "../../../evil/x.bin");
        Assert.Equal("github.com", uri.Host);
        Assert.StartsWith("/Lowomeister/Merno/releases/download/1.0.1/", uri.AbsolutePath);
    }
}
