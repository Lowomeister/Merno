// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace Merno.Core;

/// <summary>
/// Запуск Merno вместе с Windows — задание «Merno» в Планировщике (при входе, с наивысшими правами),
/// поэтому окна UAC при входе нет. Задание создаётся из XML: так можно снять ограничение «3 суток работы»
/// и запуск «только от сети», которые schtasks /SC ONLOGON ставит по умолчанию.
/// </summary>
public static class Autostart
{
    private const string TaskName = "Merno";
    private static string Schtasks => Sh.Sys32("schtasks.exe");
    private static string Exe => Environment.ProcessPath;

    /// <summary>
    /// Задание запускает Merno с правами администратора без окна UAC. Поэтому оно указывает не туда, где Merno лежит
    /// (Загрузки, Рабочий стол, папка на диске D: — там файл может подменить другой пользователь или любая программа),
    /// а на копию в Program Files, которую меняет только администратор.
    /// </summary>
    private static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Merno");
    public static string InstalledExe => Path.Combine(InstallDir, "Merno.exe");

    /// <summary>Непроверенную переносную копию нельзя автоматически переносить в доверенное место.</summary>
    static string EnsureInstalledCopy()
    {
        if ((string.Equals(Exe, InstalledExe, StringComparison.OrdinalIgnoreCase) || Updater.CanSelfUpdate(Exe)) &&
            ProtectedCode.IsTrustedFile(InstalledExe, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))) return InstalledExe;
        Log.Warn("Автозапуск работает только у Merno, установленного в папку Program Files\\Merno. В портативной версии его нет.");
        return null;
    }

    /// <summary>Путь программы в задании; null — задания нет.</summary>
    public static string TaskCommand()
    {
        var (code, outp) = Sh.Run(Schtasks, $"/Query /TN \"{TaskName}\" /XML", 15_000, quiet: true);
        if (code != 0) return null;
        var m = Regex.Match(outp ?? "", "<Command>(.*?)</Command>", RegexOptions.Singleline);
        return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim().Trim('"') : "";
    }

    public static bool IsEnabled() => TaskCommand() != null;

    public static bool Enable()
    {
        string exe = EnsureInstalledCopy();
        if (string.IsNullOrEmpty(exe)) return false;   // причину EnsureInstalledCopy уже записал в журнал
        string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        string X(string s) => SecurityElement.Escape(s);
        string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Запуск Merno в трее при входе в Windows</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{X(user)}</UserId>
      <Delay>PT30S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{X(user)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{X(exe)}""</Command>
      <Arguments>--tray</Arguments>
      <WorkingDirectory>{X(Path.GetDirectoryName(exe))}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        // не %TEMP%: оттуда файл мог бы подменить любой процесс пользователя до того, как schtasks его прочтёт
        string file;
        try { file = Path.Combine(Log.NewWorkDir("task"), "merno-task.xml"); }
        catch (Exception ex) { Log.Warn("Не удалось включить автозапуск: " + ex.Message); return false; }
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            var (code, outp) = Sh.Run(Schtasks, $"/Create /TN \"{TaskName}\" /XML \"{file}\" /F", 30_000);
            if (code == 0) { Log.Ok("Автозапуск включён: Merno будет запускаться вместе с компьютером"); return true; }
            Log.Warn("Не удалось включить автозапуск: " + outp);
            return false;
        }
        catch (Exception ex) { Log.Warn("Не удалось включить автозапуск: " + ex.Message); return false; }
        finally { try { File.Delete(file); } catch { } }
    }

    public static bool Disable()
    {
        if (TaskCommand() == null) return true;
        var (code, outp) = Sh.Run(Schtasks, $"/Delete /TN \"{TaskName}\" /F", 30_000);
        if (code == 0)
        {
            Log.Ok("Автозапуск выключен");
            return true;
        }
        Log.Warn("Не удалось выключить автозапуск: " + outp);
        return false;
    }

    /// <summary>Задание должно указывать на копию в Program Files, а копия — быть не старше запущенного Merno.
    /// Старые задания (на Загрузки, Рабочий стол…) переводятся на защищённую копию. Возвращает, есть ли задание.</summary>
    public static bool Sync()
    {
        var cmd = TaskCommand();
        if (cmd == null) return false;
        if (AppSettings.ReadOnly) return true;   // режим снимков экрана — ничего не меняем
        if (!string.Equals(cmd, InstalledExe, StringComparison.OrdinalIgnoreCase) ||
            !ProtectedCode.IsTrustedFile(InstalledExe, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)))
        {
            Log.Warn("Небезопасный автозапуск Merno отключается: путь или разрешения не прошли проверку.");
            if (!Disable()) Log.Warn("Не удалось отключить небезопасное задание Merno. Удалите его в Планировщике заданий.");
            return false;
        }
        return true;
    }
}
