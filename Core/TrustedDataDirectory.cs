// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Security.AccessControl;
using System.Security.Principal;

namespace Merno.Core;

/// <summary>Не исправляет права на недоверенных данных: сохраняет их отдельно и создаёт пустую защищённую папку.</summary>
internal static class TrustedDataDirectory
{
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Installer = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
    private static readonly SecurityIdentifier OwnerRights = new("S-1-3-4");

    internal static bool TrustedOwner(SecurityIdentifier owner) => owner != null &&
        (owner == Admins || owner == SystemSid || owner == Installer);

    internal static DirectorySecurity Security()
    {
        var security = new DirectorySecurity();
        security.SetOwner(Admins);
        security.SetAccessRuleProtection(true, false);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { Admins, SystemSid })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        // У новых файлов владельцем иногда становится SID пользователя. Без OWNER RIGHTS он мог бы
        // из неповышенного процесса переписать DACL, даже при отсутствии разрешения на запись.
        security.AddAccessRule(new FileSystemAccessRule(OwnerRights, FileSystemRights.ReadPermissions,
            inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    internal static bool TrustedSecurity(FileSystemSecurity security, bool root)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner) return false;
        if (root && (!TrustedOwner(owner) || !security.AreAccessRulesProtected)) return false;
        bool ownerLimited = false, adminsWrite = false, systemWrite = false;
        const FileSystemRights mutation = FileSystemRights.Write | FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = (SecurityIdentifier)rule.IdentityReference;
            bool applies = (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0;
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            if (sid == OwnerRights && applies && (security is not DirectorySecurity ||
                ((rule.InheritanceFlags & inherit) == inherit && rule.PropagationFlags == PropagationFlags.None)))
                ownerLimited = true;
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            // Generic rights могут встретиться в ACL, полученных не через FileSystemAccessRule.
            bool writes = (rule.FileSystemRights & mutation) != 0 || ((uint)rule.FileSystemRights & 0x50000000u) != 0;
            if (writes && !TrustedOwner(sid)) return false;
            if (applies && (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
            {
                if (sid == Admins) adminsWrite = true;
                if (sid == SystemSid) systemWrite = true;
            }
        }
        // Корень тоже обязан передавать OWNER RIGHTS новым файлам с владельцем-пользователем.
        return adminsWrite && systemWrite && ownerLimited;
    }

    internal static void RejectReparseAncestors(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir != null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Путь папки данных содержит ссылку: " + dir.FullName);
    }

    private static bool TrustedTree(string path, bool root)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
        bool directory = (attributes & FileAttributes.Directory) != 0;
        FileSystemSecurity security = directory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        if (!TrustedSecurity(security, root)) return false;
        // Рабочие папки прошлых запусков не проверяем вглубь: Merno их не читает, а программы, которым они
        // выданы как TEMP, создают там свои папки с правами пользователя (кэш winget). Иначе после каждой
        // установки через winget вся папка данных признавалась недоверенной и откладывалась.
        return !directory || Directory.EnumerateFileSystemEntries(path).All(p => (root && IsWorkDirectory(p)) || TrustedChild(p));
    }

    // Файл мог исчезнуть между перечислением и проверкой (уборка рабочих папок, замена через .tmp во второй копии):
    // исчезнувшее не может быть недоверенным.
    private static bool TrustedChild(string path)
    {
        try { return TrustedTree(path, false); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return true; }
    }

    private const string WorkPrefix = "work-";
    private static readonly HashSet<string> Created = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Рабочая папка в корне данных: имя work-*, настоящая папка (не ссылка).</summary>
    private static bool IsWorkDirectory(string path)
    {
        if (!IsWorkName(Path.GetFileName(path))) return false;
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return true; }   // уже удалена
        return (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
    }

    internal static bool IsWorkName(string name) => name != null && name.StartsWith(WorkPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Старая рабочая папка: не этого запуска и создана давно (в свежей ещё может работать установщик обновления).
    /// TEMP дочерних программ (process-temp) появляется при каждом запуске — их ждём 30 минут, а не 12 часов,
    /// иначе за день набирается много папок. 30 минут хватает установщику, запущенному прошлой копией.
    /// </summary>
    internal static bool IsStaleWork(string name, DateTime createdUtc, DateTime nowUtc, ICollection<string> keep)
    {
        if (!IsWorkName(name) || keep.Contains(name)) return false;
        var age = name.StartsWith(WorkPrefix + "process-temp-", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(12);
        return nowUtc - createdUtc > age;
    }

    /// <summary>Удаляет рабочие папки прошлых запусков. Вызывать после захвата единственного экземпляра.
    /// Удаление через SafeFs: внутри бывают папки, куда пишет обычный пользователь.</summary>
    internal static int RemoveStaleWorkDirectories(string root)
    {
        string rootFinal = SafeFs.RootFinal(root);
        if (rootFinal == null) return 0;
        string[] keep;
        lock (Created) keep = Created.ToArray();
        int removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(root, WorkPrefix + "*").ToList())
        {
            try
            {
                var info = new DirectoryInfo(dir);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    !IsStaleWork(info.Name, info.CreationTimeUtc, DateTime.UtcNow, keep)) continue;
                SafeFs.DeleteTree(dir, rootFinal, true);
                if (!Directory.Exists(dir)) removed++;
            }
            catch { }
        }
        return removed;
    }

    internal static void Prepare(string path, Action<string> notice, Action<string, DirectorySecurity> create = null)
    {
        path = Path.GetFullPath(path);
        // Родитель тоже не должен перенаправлять создание. Саму старую ссылку можно только отложить.
        RejectReparseAncestors(Path.GetDirectoryName(path));
        if (File.Exists(path) || Directory.Exists(path))
        {
            if (Directory.Exists(path) && TrustedTree(path, true)) return;
            string aside = path + ".untrusted-" + Guid.NewGuid().ToString("N");
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) Directory.Move(path, aside);
            else File.Move(path, aside);
            notice?.Invoke("Старая папка данных не прошла проверку безопасности. Данные сохранены отдельно: " + aside);
        }
        // ACL задаётся при создании, а не после создания доступной всем папки.
        if (create == null) new DirectoryInfo(path).Create(Security());
        else create(path, Security());
        if (!TrustedTree(path, true)) throw new UnauthorizedAccessException("Не удалось защитить папку данных Merno.");
    }

    internal static string CreateWorkDirectory(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Недопустимое имя рабочей папки.", nameof(name));
        RejectReparseAncestors(root);
        if (!TrustedSecurity(new DirectoryInfo(root).GetAccessControl(), true))
            throw new UnauthorizedAccessException("Папка данных Merno больше не защищена.");
        // Уникальная папка исключает повторное использование чужих файлов и конфликт двух операций.
        string path = Path.Combine(root, WorkPrefix + name + "-" + Guid.NewGuid().ToString("N"));
        lock (Created) Created.Add(Path.GetFileName(path));
        new DirectoryInfo(path).Create(Security());
        if (!TrustedTree(path, true)) throw new UnauthorizedAccessException("Не удалось защитить рабочую папку Merno.");
        return path;
    }
}
