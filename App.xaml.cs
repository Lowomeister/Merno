// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Threading;
using Merno.Core;

namespace Merno;

public partial class App : Application
{
    public const string Version = "1.0.1";
    /// <summary>Запуск с --tray (автозапуск): окно не показываем, только значок в трее.</summary>
    public static bool StartInTray;
    /// <summary>Программа действительно закрывается (выход из трея, обновление, выход из Windows) — окно не прячем в трей.</summary>
    public static bool ForceExit;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUiError;
        SessionEnding += (_, _) => ForceExit = true;
        // «чёрный ящик» (Core/Diag.cs): последние шаги выхода и состояние раз в 15 минут
        Exit += (_, _) => Diag.ExitStep("программа завершается");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Diag.ExitStep("процесс завершён");
        var diagTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        diagTimer.Tick += (_, _) => Diag.Periodic();
        diagTimer.Start();
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            if (a.ExceptionObject is Exception error) Log.Error(error);
            else Log.Warn("Критическая ошибка: " + a.ExceptionObject);
            // процесс закрывается: отчёт — в папку данных, предложим отправить при следующем запуске (Core/CrashReport.cs)
            CrashReport.Handle(a.ExceptionObject as Exception, "Критическая ошибка", fatal: true);
        };
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            a.SetObserved();
            var ex = a.Exception.Flatten().InnerExceptions.FirstOrDefault() ?? a.Exception;
            // где именно упало — первая строка стека из кода Merno
            var where = (ex.StackTrace ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("at Merno")) ?? "";
            if (where.Length > 0) where = where[3..].Split(" in ")[0];
            Log.Error(a.Exception);
            Log.Warn($"Внутренняя ошибка: {ex.Message}" + (where.Length > 0 ? $" ({where})" : "") + ". Merno продолжит работу");
        };
        // Встроенный шрифт Onest; если не загрузится — останется Segoe UI
        try
        {
            var baseUri = new Uri("pack://application:,,,/");
            // проверяем, что встроенный шрифт действительно найден: у его начертаний должно быть имя семейства «Onest»
            var probe = new System.Windows.Media.FontFamily(baseUri, "./Assets/Fonts/#Onest");
            bool found = probe.GetTypefaces().Any(tf =>
            {
                try
                {
                    return tf.TryGetGlyphTypeface(out var g) &&
                           g.FamilyNames.Values.Any(n => n.Contains("Onest", StringComparison.OrdinalIgnoreCase));
                }
                catch { return false; }
            });
            if (found)
            {
                var onest = new System.Windows.Media.FontFamily(baseUri, "./Assets/Fonts/#Onest, Segoe UI");
                Resources["UiFont"] = onest;
                Resources["Display"] = onest;
            }
            else Log.Warn("Шрифт Onest не найден. Используется Segoe UI");
        }
        catch (Exception ex) { Log.Warn("Шрифт Onest не загрузился: " + ex.Message); }
        base.OnStartup(e);
        // WPF мог поставить свой фильтр поверх нашего — ставим снова уже после его запуска
        CrashReport.InstallNativeHandler(force: true);
        Theme.Apply(this, AppSettings.Load().Theme);   // тема оформления — до создания окна (Core/Theme.cs)
        var w = new MainWindow();
        MainWindow = w;
        if (StartInTray) w.StartHidden(); else w.Show();
    }

    private void OnUiError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception);
        MessageBox.Show("Что-то пошло не так, но Merno продолжает работать. Если ошибка повторяется, нажмите «Связаться с нами».\n\nТехнические сведения: " + e.Exception.Message + "\n\nПодробности записаны в журнал:\n" + Log.LogFile,
                        "Merno", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
