// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Runtime.InteropServices;
using System.Text;

namespace Merno.Core;

/// <summary>
/// Запуск программы с обычными правами пользователя (как из Проводника), хотя сам Merno работает от администратора.
/// Нужен для удаляторов из HKCU: эти записи может написать любая программа пользователя, и запускать их
/// с правами администратора — значит отдать ей эти права.
/// Берётся токен Проводника текущего сеанса (CreateProcessWithTokenW, служба «Вторичный вход в систему»).
/// </summary>
public static class Unelevated
{
    public static T AsUser<T>(Func<T> action)
    {
        IntPtr process = IntPtr.Zero, token = IntPtr.Zero;
        try
        {
            var shell = GetShellWindow();
            if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out uint pid) == 0) throw new IOException("Проводник пользователя недоступен.");
            process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero || !OpenProcessToken(process, TOKEN_DUPLICATE | TOKEN_QUERY, out token) ||
                !DuplicateTokenEx(token, TOKEN_QUERY | TOKEN_DUPLICATE | 4, IntPtr.Zero, SecurityImpersonation, 2, out var duplicate))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            using var handle = new Microsoft.Win32.SafeHandles.SafeAccessTokenHandle(duplicate);
            if (!GetTokenInformation(duplicate, 20, out int elevated, sizeof(int), out _) || elevated != 0)
                throw new IOException("Нет токена пользователя с обычными правами.");
            return System.Security.Principal.WindowsIdentity.RunImpersonated(handle, action);
        }
        finally { if (token != IntPtr.Zero) CloseHandle(token); if (process != IntPtr.Zero) CloseHandle(process); }
    }
    public static void WriteText(string path, string text) => AsUser(() => { AtomicFile.Write(path, text); return true; });
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint TOKEN_ASSIGN_PRIMARY = 0x1, TOKEN_DUPLICATE = 0x2, TOKEN_QUERY = 0x8, TOKEN_ADJUST_DEFAULT = 0x80, TOKEN_ADJUST_SESSIONID = 0x100;
    const int SecurityImpersonation = 2, TokenPrimary = 1;
    const uint WAIT_TIMEOUT = 0x102;
    public const int ErrorElevationRequired = 740;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attrs, int level, int type, out IntPtr newToken);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string app, StringBuilder cmd, uint flags,
                                               IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);
    [DllImport("userenv.dll", SetLastError = true)] static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);
    [DllImport("userenv.dll")] static extern bool DestroyEnvironmentBlock(IntPtr environment);

    public static bool Open(string target, string args = null)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Contains('"') || target.Contains('\r') || target.Contains('\n')) return false;
        if (File.Exists(target) && Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            // программы Windows, которым нужны права администратора (netplwiz, regedit, rstrui…), с обычными правами
            // не стартуют (ошибка 740) — их Merno запускает сам, только из System32/папки Windows (Core/SystemTools.cs)
            if (SystemTools.NeedsAdmin(target)) return SystemTools.StartElevated(target, args);
            return RunAndWait(target, args, 0, out _);
        }
        if (!string.IsNullOrWhiteSpace(args)) return false;
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        return RunAndWait(explorer, "\"" + target + "\"", 0, out _);
    }

    /// <summary>Запустить и дождаться завершения (не дольше timeoutMs). false — не запустилось, error — код ошибки Windows.</summary>
    public static bool RunAndWait(string exe, string args, int timeoutMs, out int error)
    {
        error = 0;
        IntPtr proc = IntPtr.Zero, tok = IntPtr.Zero, dup = IntPtr.Zero, environment = IntPtr.Zero;
        try
        {
            var shell = GetShellWindow();
            if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out uint pid) == 0 || pid == 0) { error = -1; return false; }
            proc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (proc == IntPtr.Zero) { error = Marshal.GetLastWin32Error(); return false; }
            if (!OpenProcessToken(proc, TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY, out tok)) { error = Marshal.GetLastWin32Error(); return false; }
            if (!DuplicateTokenEx(tok, TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID,
                                  IntPtr.Zero, SecurityImpersonation, TokenPrimary, out dup)) { error = Marshal.GetLastWin32Error(); return false; }
            if (!GetTokenInformation(dup, 20, out int elevated, sizeof(int), out _) || elevated != 0) { error = ErrorElevationRequired; return false; }
            if (!CreateEnvironmentBlock(out environment, dup, false)) { error = Marshal.GetLastWin32Error(); return false; }
            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            var cmd = new StringBuilder("\"" + exe + "\"" + (string.IsNullOrWhiteSpace(args) ? "" : " " + args));
            string cwd = Path.GetDirectoryName(exe);
            if (string.IsNullOrEmpty(cwd) || !Directory.Exists(cwd)) cwd = Environment.SystemDirectory;
            if (!CreateProcessWithTokenW(dup, 0, exe, cmd, 0x400, environment, cwd, ref si, out var pi)) { error = Marshal.GetLastWin32Error(); return false; }
            CloseHandle(pi.hThread);
            WaitForSingleObject(pi.hProcess, (uint)Math.Max(0, timeoutMs));
            CloseHandle(pi.hProcess);
            return true;
        }
        catch { error = -1; return false; }
        finally
        {
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (dup != IntPtr.Zero) CloseHandle(dup);
            if (tok != IntPtr.Zero) CloseHandle(tok);
            if (proc != IntPtr.Zero) CloseHandle(proc);
        }
    }
}
