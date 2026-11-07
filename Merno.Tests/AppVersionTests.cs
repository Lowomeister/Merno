// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;
namespace Merno.Tests;
public class AppVersionTests
{
    [Theory]
    [InlineData("images-eds-ssl.xboxlive.com", true)]
    [InlineData("store.microsoft.com", true)]
    [InlineData("microsoft.com.evil.example", false)]
    [InlineData("evilmicrosoft.com", false)]
    [InlineData("127.0.0.1", false)]
    public void StoreImagesRequireAnExactOfficialDomainSuffix(string host, bool allowed) =>
        Assert.Equal(allowed, Apps.StoreImageHost(host));
    [Theory]
    [InlineData("3.9.24", "3.9.28", false)]
    [InlineData("3.9.28", "3.9.28", true)]
    [InlineData("3.9.28.0", "3.9.28", true)]
    [InlineData(null, "3.9.28", false)]
    [InlineData("unknown", "3.9.28", false)]
    public void DifferentInstalledVersionsDoNotMarkBothRows(string actual, string expected, bool matches) =>
        Assert.Equal(matches, Apps.VersionMatches(actual, expected));
}
