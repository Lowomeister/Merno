// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Merno.Core;

namespace Merno.Tests;

public class UpdateSafetyTests
{
    [Theory]
    [InlineData(@"C:\Users\Example\Desktop\Merno.exe")]
    [InlineData(@"D:\Merno.exe")]
    [InlineData(null)]
    public void PortableCopyCannotSelfReplaceOrRunElevatedUpdate(string path) => Assert.False(Updater.CanSelfUpdate(path));

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.9", "1.0.0", false)]
    [InlineData("", "1.0.0", false)]
    [InlineData(null, "1.0.0", false)]
    [InlineData("1.0.x", "1.0.0", false)]
    public void ApplyInstallsOnlyStrictlyNewerVersion(string kit, string running, bool expected) =>
        Assert.Equal(expected, Updater.IsNewer(kit, System.Version.Parse(running)));

    [Fact]
    public void ApplyRefusesBeforeReadingOrCopyingUpdateOutsideInstalledLocation() =>
        Assert.Equal(Updater.ManualUpdateMessage, Updater.Apply("missing-update", new UpdateInfo()));

    [Fact]
    public void StagedFileSecurityDoesNotAllowUserOwnershipOrWriting()
    {
        var security = ProtectedCode.FileSecurity();
        Assert.True(security.AreAccessRulesProtected);
        Assert.True(ProtectedCode.TrustedAcl(security));
        var user = new SecurityIdentifier("S-1-5-21-1-2-3-1001");
        security.SetOwner(user);
        Assert.False(ProtectedCode.TrustedAcl(security));
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.WriteData, AccessControlType.Allow));
        Assert.False(ProtectedCode.TrustedAcl(security));
    }

    [Fact]
    public void FileCannotBeOutsideProtectedAnchorEvenIfItExists() =>
        Assert.False(ProtectedCode.IsTrustedFile(Path.Combine(Environment.SystemDirectory, "cmd.exe"), Autostart.InstalledExe));
}
