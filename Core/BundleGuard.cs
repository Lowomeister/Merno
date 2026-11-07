// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Merno.Core;

/// <summary>
/// Merno собран одним exe: при запуске .NET распаковывает библиотеки WPF (wpfgfx_cor3.dll и др.) в %TEMP%\.net\Merno\…
/// и потом загружает их — а Merno работает с правами администратора. %TEMP% доступен любой программе пользователя:
/// она могла бы подменить библиотеку и получить права администратора.
/// Поэтому в самом начале запуска, до WPF: закрываем эти папки от записи и проверяем, что каждый файл подписан Microsoft.
/// Подмена — удаляем папку и перезапускаемся: .NET распакует чистые копии.
/// </summary>
public static class BundleGuard
{
    const string RecheckArg = "--bundle-recheck";

    public static void Check()
    {
        try
        {
            var dirs = ExtractDirs();
            if (dirs.Count == 0) return;
            bool bad = false;
            foreach (var d in dirs)
            {
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) { bad = true; continue; }
                Lock(d);
                foreach (var f in Directory.EnumerateFiles(d))
                    if (!SignedByMicrosoft(f)) { bad = true; break; }
            }
            if (!bad) return;

            var args = Environment.GetCommandLineArgs().Skip(1).ToList();
            if (args.Contains(RecheckArg, StringComparer.OrdinalIgnoreCase))
            {
                MessageBoxW(IntPtr.Zero, "Файлы, которые Merno распаковывает при запуске, повреждены или подменены, и исправить это не удалось.\n\n" +
                    "Проверьте компьютер антивирусом. Merno не будет запущен.", "Merno", 0x10);
                Environment.Exit(1);
            }
            foreach (var d in dirs) Remove(d);
            var psi = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, WorkingDirectory = Environment.SystemDirectory };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add(RecheckArg);
            Process.Start(psi);
            Environment.Exit(0);
        }
        catch { /* защита не должна мешать запуску */ }
    }

    /// <summary>Папки распаковки этого Merno: %TEMP%\.net\Merno\* (или DOTNET_BUNDLE_EXTRACT_BASE_DIR).</summary>
    static List<string> ExtractDirs()
    {
        var res = new List<string>();
        string baseDir = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
        if (string.IsNullOrWhiteSpace(baseDir)) baseDir = Path.Combine(Path.GetTempPath(), ".net");
        string app = Path.Combine(baseDir, "Merno");
        if (!Directory.Exists(app)) return res;
        if ((File.GetAttributes(app) & FileAttributes.ReparsePoint) == 0) Lock(app);
        res.AddRange(Directory.GetDirectories(app));
        return res;
    }

    /// <summary>Владелец — Администраторы; запись — только Администраторам и SYSTEM; пользователю — чтение.</summary>
    static void Lock(string dir)
    {
        try
        {
            var sec = new DirectorySecurity();
            sec.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            sec.SetAccessRuleProtection(true, false);
            var inh = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
            var me = WindowsIdentity.GetCurrent().User;
            if (me != null) sec.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.ReadAndExecute, inh, PropagationFlags.None, AccessControlType.Allow));
            FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(dir), sec);
            // у файлов могли остаться свои права (например, у подложенных) — возвращаем им права от папки
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                try
                {
                    var fs = new FileSecurity();
                    fs.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
                    fs.SetAccessRuleProtection(false, false);
                    FileSystemAclExtensions.SetAccessControl(new FileInfo(f), fs);
                }
                catch { }
            }
        }
        catch { }
    }

    static void Remove(string dir)
    {
        try
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(dir); return; }   // только ссылку
            string root = SafeFs.RootFinal(dir);
            if (root == null) return;
            SafeFs.DeleteTree(dir, root, false);
            SafeFs.DeleteRoot(dir, root);
        }
        catch { }
    }

    // ---- проверка подписи файла (WinVerifyTrust) и что подписал именно Microsoft ----
    static bool SignedByMicrosoft(string file)
    {
        if (!VerifyTrust(file)) return false;
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            foreach (var rdn in cert.SubjectName.EnumerateRelativeDistinguishedNames())
                if (!rdn.HasMultipleElements && rdn.GetSingleElementType().Value == "2.5.4.10"
                    && rdn.GetSingleElementValue() == "Microsoft Corporation") return true;
        }
        catch { }
        return false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WINTRUST_FILE_INFO { public int cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public int cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData; public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
        public IntPtr pFile; public uint dwStateAction; public IntPtr hWVTStateData, pwszURLReference; public uint dwProvFlags, dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static bool VerifyTrust(string file)
    {
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
        var fi = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = file };
        IntPtr pFi = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fi, pFi, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,              // WTD_UI_NONE
                fdwRevocationChecks = 0,     // WTD_REVOKE_NONE — без сети, запуск не тормозит
                dwUnionChoice = 1,           // WTD_CHOICE_FILE
                pFile = pFi,
                dwStateAction = 0,
                dwProvFlags = 0x1000         // WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFi);
            Marshal.FreeHGlobal(pFi);
        }
    }
}
