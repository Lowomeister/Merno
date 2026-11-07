// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;

namespace Merno;

/// <summary>Точка входа: ловит любые ошибки запуска и сохраняет их в файл на рабочем столе.</summary>
public static class Program
{
    /// <summary>Одна копия Merno: пока держим мьютекс, повторный запуск только показывает окно уже запущенной.</summary>
    private static Mutex _instance;
    /// <summary>Сигнал «покажи окно» от повторного запуска.</summary>
    internal static EventWaitHandle ShowSignal;
    private const string MutexName = "Local\\Merno.SingleInstance";
    private const string ShowName = "Local\\Merno.ShowWindow";

    [STAThread]
    public static void Main()
    {
        var args = Environment.GetCommandLineArgs();

        // Installed kit: native host sanitizes the environment before loading CLR; no native extraction.
        // Портативная версия (один exe): .NET распаковывает библиотеки WPF в %TEMP%. До любого WPF проверяем,
        // что они подписаны Microsoft, и закрываем папку от записи (Core/BundleGuard.cs). У установленной распаковки нет.
#pragma warning disable IL3000
        if (string.IsNullOrEmpty(typeof(Program).Assembly.Location)) Merno.Core.BundleGuard.Check();
#pragma warning restore IL3000

        bool Has(string a) => args.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
        bool updated = Has("--updated") || Has("--restart");   // --restart: перезапуск после смены темы — ждём, пока закроется прежняя копия
        App.StartInTray = Has("--tray");
        // служебный режим снимков экрана для проверки внешнего вида: --shot <папка> [страницы]
        int si = Array.FindIndex(args, a => a.Equals("--shot", StringComparison.OrdinalIgnoreCase));
        if (si >= 0 && si + 1 < args.Length)
        {
            MainWindow.ShotDir = args[si + 1];
            Merno.Core.AppSettings.ReadOnly = true;   // снимки ничего не сохраняют
            if (si + 2 < args.Length) MainWindow.ShotPages = args[si + 2];
            try { var app = new App(); app.InitializeComponent(); app.Run(); } catch (Exception ex) { try { File.WriteAllText(Path.Combine(args[si + 1], "shot-error.txt"), ex.ToString()); } catch { } }
            return;
        }
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("Merno нужно запускать от имени администратора. Откройте его через ярлык или файл Merno.exe в папке установки.", "Merno");
            return;
        }
        try
        {
            // До настроек, отчётов, заданий и любых изменений Windows.
            Merno.Core.ProcessEnvironment.Initialize();
            try { Merno.Core.SafeBoot.CleanupStored(); }
            catch (Exception ex) { Merno.Core.Log.Warn("Не удалось убрать прежнюю запись безопасного режима: " + ex.Message); }
            if (Merno.Core.Log.DataWarning != null)
                MessageBox.Show(Merno.Core.Log.DataWarning + "\n\nMerno начнёт с новыми настройками. Старые копии отката автоматически не загружаются.",
                    "Merno", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось подготовить защищённую папку Merno. Программа остановлена.\n\n" +
                ex.GetBaseException().Message, "Merno", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        // системные сбои (нарушение доступа в чужой DLL) не доходят до AppDomain.UnhandledException.
        // После --shot: режим снимков ничего не отправляет.
        Merno.Core.CrashReport.InstallNativeHandler();
        bool owned = false;
        try
        {
            _instance = new Mutex(true, MutexName, out bool created);
            owned = created;
            if (!created)
            {
                if (updated)
                {
                    // после обновления старая копия ещё закрывается. --updated <PID>: ждём именно её до 30 с;
                    // подвисла при выходе — закрываем сами, иначе работали бы две копии (обе в трее, обе пишут settings.json)
                    int ui = Array.FindIndex(args, a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase));
                    if (ui >= 0 && ui + 1 < args.Length && int.TryParse(args[ui + 1], out int pid) && pid != Environment.ProcessId)
                    {
                        try
                        {
                            using var oldProc = System.Diagnostics.Process.GetProcessById(pid);
                            if (oldProc.ProcessName.Equals("Merno", StringComparison.OrdinalIgnoreCase) && !oldProc.WaitForExit(30_000))
                            { oldProc.Kill(); oldProc.WaitForExit(5_000); }
                        }
                        catch { /* процесса уже нет */ }
                    }
                    try { owned = _instance.WaitOne(10_000); } catch (AbandonedMutexException) { owned = true; }
                }
                else
                {
                    // уже запущено: просим ту копию показать окно (при автозапуске в трей — просто выходим)
                    if (!App.StartInTray)
                        try { using var ev = EventWaitHandle.OpenExisting(ShowName); ev.Set(); } catch { }
                    return;
                }
            }
            ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
        }
        catch { /* без защиты от второй копии программа всё равно работает */ }

        // Рабочие папки прошлых запусков (установщики, TEMP дочерних программ): только когда мы единственная копия,
        // в фоне, чтобы не задерживать окно.
        if (owned)
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int removed = Merno.Core.TrustedDataDirectory.RemoveStaleWorkDirectories(Merno.Core.Log.DataDir);
                    if (removed > 0) Merno.Core.Log.Skip($"Удалены рабочие папки прошлых запусков: {removed}");
                }
                catch (Exception ex) { Merno.Core.Log.Warn("Уборка рабочих папок: " + ex.Message); }
            });

        try
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
            GC.KeepAlive(_instance);
        }
        catch (Exception ex)
        {
            // отчёт ляжет в папку данных — при следующем запуске его предложат отправить в Telegram (Core/CrashReport.cs)
            Merno.Core.CrashReport.Handle(ex, "Запуск", fatal: true);
            string text = $"Merno {App.Version}: ошибка запуска {DateTime.Now:dd.MM.yyyy HH:mm:ss}\r\n\r\n{ex}";
            try
            {
                var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Ошибка запуска Merno.txt");
                Merno.Core.Unelevated.WriteText(file, Merno.Core.CrashReport.Sanitize(text, int.MaxValue));
            }
            catch { }
            MessageBox.Show("Программа не смогла запуститься.\n\n" + ex.GetBaseException().Message +
                            "\n\nПодробности сохранены на рабочем столе в файле «Ошибка запуска Merno.txt».",
                            "Merno", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
