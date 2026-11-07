// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using Merno.Core;
namespace Merno.Tests;
public class AccessibilityFlagsTests
{
    [Theory]
    [InlineData("511", "507")]
    [InlineData("127", "123")]
    [InlineData("59", "59")]
    [InlineData("1", "1")]
    public void OnlyShortcutBitIsCleared(string original, string expected) => Assert.Equal(expected, RegOp.ClearFlags(original, 4));
    [Fact]
    public void UnknownFlagsCannotResetAccessibilityDefaults() => Assert.Throws<IOException>(() => RegOp.ClearFlags("unknown", 4));
}
