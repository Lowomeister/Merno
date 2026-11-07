// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Security.AccessControl;
using System.Security.Principal;
using System.IO;
using Merno.Core;

namespace Merno.Tests;

public class TrustedDataDirectoryTests
{
    private static readonly SecurityIdentifier User = new("S-1-5-21-1-2-3-1001");

    [Fact]
    public void UntrustedDataIsPreservedAndProtectionFailureDoesNotFallBack()
    {
        string parent = Path.Combine(Path.GetTempPath(), "merno-security-test-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(parent, "data");
        Directory.CreateDirectory(root);
        // Явно недоверенная ACL (запись для всех), независимо от того, кто владелец папки.
        var security = new DirectoryInfo(root).GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.WriteData, AccessControlType.Allow));
        new DirectoryInfo(root).SetAccessControl(security);
        File.WriteAllText(Path.Combine(root, "backup-untrusted.json"), "untrusted");
        try
        {
            string warning = null;
            Assert.Throws<UnauthorizedAccessException>(() => TrustedDataDirectory.Prepare(root, n => warning = n,
                (_, _) => throw new UnauthorizedAccessException("protection failed")));
            Assert.False(Directory.Exists(root));
            string preserved = Assert.Single(Directory.GetDirectories(parent, "data.untrusted-*"));
            Assert.Equal("untrusted", File.ReadAllText(Path.Combine(preserved, "backup-untrusted.json")));
            Assert.Contains(preserved, warning);
        }
        finally { SafeFs.DeleteTree(parent, SafeFs.RootFinal(Path.GetTempPath()), true); }
    }

    [Theory]
    [InlineData("S-1-5-32-544", true)]
    [InlineData("S-1-5-18", true)]
    [InlineData("S-1-5-21-1-2-3-1001", false)]
    [InlineData("S-1-5-32-545", false)]
    public void UserOwnershipIsNotEvidenceOfElevation(string sid, bool expected) =>
        Assert.Equal(expected, TrustedDataDirectory.TrustedOwner(new SecurityIdentifier(sid)));

    [Fact]
    public void FreshSecurityIsProtectedAndTrusted()
    {
        var security = TrustedDataDirectory.Security();
        Assert.True(security.AreAccessRulesProtected);
        Assert.True(TrustedDataDirectory.TrustedSecurity(security, root: true));
    }

    [Fact]
    public void UserOwnedRootIsRejectedEvenWithRestrictivePermissions()
    {
        var security = TrustedDataDirectory.Security();
        security.SetOwner(User);
        Assert.False(TrustedDataDirectory.TrustedSecurity(security, root: true));
    }

    [Fact]
    public void UserOwnedChildRequiresOwnerRightsToPreventChangingDacl()
    {
        var security = TrustedDataDirectory.Security();
        security.SetOwner(User);
        Assert.True(TrustedDataDirectory.TrustedSecurity(security, root: false));
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.IdentityReference.Value == "S-1-3-4") security.RemoveAccessRuleSpecific(rule);
        Assert.False(TrustedDataDirectory.TrustedSecurity(security, root: false));
    }

    [Fact]
    public void AdministratorOwnedRootMustLimitOwnershipOfFutureChildren()
    {
        var security = TrustedDataDirectory.Security();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.IdentityReference.Value == "S-1-3-4") security.RemoveAccessRuleSpecific(rule);
        Assert.False(TrustedDataDirectory.TrustedSecurity(security, root: true));
    }

    [Fact]
    public void InheritOnlyWriteAccessCannotExposeNewChildren()
    {
        var security = TrustedDataDirectory.Security();
        security.AddAccessRule(new FileSystemAccessRule(User, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.InheritOnly, AccessControlType.Allow));
        Assert.False(TrustedDataDirectory.TrustedSecurity(security, root: true));
    }

    [Fact]
    public void OwnerRightsWithoutInheritanceDoesNotProtectNewFiles()
    {
        var security = TrustedDataDirectory.Security();
        var ownerRights = new SecurityIdentifier("S-1-3-4");
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.IdentityReference.Value == ownerRights.Value) security.RemoveAccessRuleSpecific(rule);
        security.AddAccessRule(new FileSystemAccessRule(ownerRights, FileSystemRights.ReadPermissions, AccessControlType.Allow));
        Assert.False(TrustedDataDirectory.TrustedSecurity(security, root: true));
    }

    [Theory]
    [InlineData(FileSystemRights.WriteData)]
    [InlineData(FileSystemRights.CreateDirectories)]
    [InlineData(FileSystemRights.Delete)]
    [InlineData(FileSystemRights.DeleteSubdirectoriesAndFiles)]
    [InlineData(FileSystemRights.ChangePermissions)]
    [InlineData(FileSystemRights.TakeOwnership)]
    public void UntrustedMutationPermissionIsRejected(FileSystemRights rights)
    {
        var security = TrustedDataDirectory.Security();
        security.AddAccessRule(new FileSystemAccessRule(User, rights, AccessControlType.Allow));
        Assert.False(TrustedDataDirectory.TrustedSecurity(security, root: true));
    }

    [Fact]
    public void NullDaclOrMissingAdministratorAccessIsRejected()
    {
        var security = new DirectorySecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        security.SetAccessRuleProtection(true, false);
        Assert.False(TrustedDataDirectory.TrustedSecurity(security, root: true));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("C:\\outside")]
    [InlineData("a\\b")]
    [InlineData("")]
    public void WorkDirectoryCannotEscapeRoot(string name) =>
        Assert.Throws<ArgumentException>(() => TrustedDataDirectory.CreateWorkDirectory("unused", name));

    // Временные папки дочерних программ (work-process-temp-*) не должны делать всю папку данных «недоверенной».
    [Theory]
    [InlineData("work-process-temp-ae71ea842d21442d92c9c1a1765c6bdd", true)]
    [InlineData("WORK-update-1", true)]
    [InlineData("work", false)]
    [InlineData("settings.json", false)]
    [InlineData("backup-20261106.json", false)]
    [InlineData("logs", false)]
    public void OnlyWorkDirectoriesAreSkippedByTrustCheck(string name, bool expected) =>
        Assert.Equal(expected, TrustedDataDirectory.IsWorkName(name));

    [Fact]
    public void StaleWorkKeepsCurrentRunAndFreshFolders()
    {
        var now = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        var keep = new List<string> { "work-update-current" };
        Assert.True(TrustedDataDirectory.IsStaleWork("work-update-old", now.AddDays(-2), now, keep));
        Assert.False(TrustedDataDirectory.IsStaleWork("work-update-current", now.AddDays(-2), now, keep));
        // свежая: в ней может ещё работать установщик обновления, запущенный прошлой копией
        Assert.False(TrustedDataDirectory.IsStaleWork("work-install-fresh", now.AddHours(-1), now, keep));
        Assert.False(TrustedDataDirectory.IsStaleWork("settings.json", now.AddDays(-30), now, keep));
    }

    [Fact]
    public void TempOfPastRunsIsRemovedSooner()
    {
        var now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var keep = new List<string> { "work-process-temp-current" };
        // TEMP дочерних программ прошлых запусков удаляется через 30 минут, а не копится весь день
        Assert.True(TrustedDataDirectory.IsStaleWork("work-process-temp-old", now.AddHours(-1), now, keep));
        Assert.False(TrustedDataDirectory.IsStaleWork("work-process-temp-fresh", now.AddMinutes(-10), now, keep));
        Assert.False(TrustedDataDirectory.IsStaleWork("work-process-temp-current", now.AddDays(-1), now, keep));
        // установка и обновление — через 12 часов
        Assert.False(TrustedDataDirectory.IsStaleWork("work-update-recent", now.AddHours(-1), now, keep));
    }
}
