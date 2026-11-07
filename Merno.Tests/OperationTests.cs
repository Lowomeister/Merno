// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;

namespace Merno.Tests;

public class OperationTests
{
    [Fact]
    public void CleanupAndUpdateCannotAcquireSameLease()
    {
        using (var first = SystemOperation.TryEnter())
        {
            Assert.NotNull(first);
            Assert.Null(SystemOperation.TryEnter());
        }
        using var next = SystemOperation.TryEnter();
        Assert.NotNull(next);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void UninstallCannotClaimSuccessForPresentOrUnknownState(bool? present) => Assert.False(Uninstall.ConfirmRemoval(present).Ok);

    [Fact]
    public void PresetsExcludeIrreversibleAndHighestRiskActions()
    {
        foreach (var name in new[] { "gaming", "minimal", "recommended" })
        {
            var catalog = Catalog.All();
            foreach (var id in Catalog.Preset(name, catalog)) Assert.Contains(catalog, t => t.Id == id && t.CanRevert && t.Risk < 2);
        }
        Assert.True(Uninstall.ConfirmRemoval(false).Ok);
    }
}
