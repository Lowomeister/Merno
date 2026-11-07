// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using Merno.Core;
namespace Merno.Tests;
public class DefenderStateTests
{
    [Theory]
    [InlineData("False|False", true)]
    [InlineData(" false|false\r\n", true)]
    [InlineData("False|True", false)]
    [InlineData("True|False", false)]
    [InlineData("", false)]
    [InlineData("error", false)]
    [InlineData(null, false)]
    public void RegistryPolicyCannotSubstituteForActualDefenderState(string status, bool expected) =>
        Assert.Equal(expected, Ctx.DefenderIsDisabled(status));
}
