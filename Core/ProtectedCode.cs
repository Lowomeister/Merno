// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Security.AccessControl;
using System.Security.Principal;

namespace Merno.Core;

internal static class ProtectedCode
{
    internal static bool TrustedAcl(FileSystemSecurity security)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner) return false;
        bool ownerLimited = false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (sid.Value == "S-1-3-4") ownerLimited = true;
            var mutation = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
                FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            if (rule.AccessControlType == AccessControlType.Allow &&
                ((rule.FileSystemRights & mutation) != 0 || ((uint)rule.FileSystemRights & 0x50000000u) != 0) &&
                !TrustedDataDirectory.TrustedOwner(sid)) return false;
        }
        return TrustedDataDirectory.TrustedOwner(owner) || ownerLimited;
    }

    internal static bool IsTrustedFile(string path, string anchor)
    {
        try
        {
            string file = CleanupPaths.Normalize(path), root = CleanupPaths.Normalize(anchor);
            if (file == null || root == null || !file.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) return false;
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || !TrustedAcl(new FileInfo(file).GetAccessControl())) return false;
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(file)); directory != null; directory = directory.Parent)
            {
                if (CleanupPaths.ApprovedRoot(directory.FullName, new[] { directory.FullName }) == null || !TrustedAcl(directory.GetAccessControl())) return false;
                if (string.Equals(directory.FullName.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch { }
        return false;
    }

    internal static FileSecurity FileSecurity()
    {
        var security = new FileSecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        return security;
    }

    internal static FileStream CreateSealedFile(string file) => new FileInfo(file).Create(FileMode.CreateNew,
        FileSystemRights.Write, FileShare.None, 81920, FileOptions.WriteThrough, FileSecurity());
}
