// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Merno.Core;

namespace Merno;

// Значок в трее (WinForms NotifyIcon — его типы пишем полностью, чтобы не путать с WPF),
// тёмное меню значка (WPF ContextMenu из Dark.xaml), закрытие в трей, автозапуск и показ окна по второму запуску
public partial class MainWindow
{
    private System.Windows.Forms.NotifyIcon _tray;
    private ContextMenu _trayMenu;
    private readonly List<MenuItem> _trayBusyItems = new();
    private bool _autostartGuard;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void InitTray()
    {
        try
        {
            _tray = new System.Windows.Forms.NotifyIcon { Text = "Merno", Icon = LoadTrayIcon(), Visible = true };
            _tray.MouseUp += (_, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowFromTray();
                else if (e.Button == System.Windows.Forms.MouseButtons.Right) ShowTrayMenu();
            };
            _tray.DoubleClick += (_, _) => ShowFromTray();
            _tray.BalloonTipClicked += (_, _) => ShowFromTray();
        }
        catch (Exception ex) { Log.Warn("Значок в трее: " + ex.Message); _tray = null; }

        // повторный запуск Merno — показать это окно
        if (Program.ShowSignal != null)
            _showWait = ThreadPool.RegisterWaitForSingleObject(Program.ShowSignal,
                (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray)), null, -1, false);

        // «При закрытии окна»
        // 0: спросить при первом закрытии (так по умолчанию)
        SetCloseAsk.IsChecked = _settings.CloseAction is not (1 or 2);
        SetCloseTray.IsChecked = _settings.CloseAction == 1;
        SetCloseExit.IsChecked = _settings.CloseAction == 2;
        SetCloseAsk.Checked += (_, _) => { _settings.CloseAction = 0; _settings.Save(); Log.Write("При закрытии окна: спросить"); };
        SetCloseTray.Checked += (_, _) => { _settings.CloseAction = 1; _settings.Save(); Log.Write("При закрытии окна: сворачивать в трей"); };
        SetCloseExit.Checked += (_, _) => { _settings.CloseAction = 2; _settings.Save(); Log.Write("При закрытии окна: закрывать программу"); };

        // «Запускать вместе с Windows»: показываем реальное состояние задания в Планировщике
        _autostartGuard = true; SetAutostart.IsChecked = _settings.Autostart; _autostartGuard = false;
        SetAutostart.Checked += (_, _) => ToggleAutostart(true);
        SetAutostart.Unchecked += (_, _) => ToggleAutostart(false);
        Task.Run(Autostart.Sync).ContinueWith(t =>
        {
            if (t.IsFaulted) { _ = t.Exception; return; }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool on = t.Result;
                if (_settings.Autostart != on) { _settings.Autostart = on; _settings.Save(); }
                _autostartGuard = true; SetAutostart.IsChecked = on; _autostartGuard = false;
            }));
        });
    }

    private System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            // в цветах выбранной темы
            var themed = ThemedTrayIcon();
            if (themed != null) return themed;
            var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (res != null)
                using (var st = res.Stream)
                    return new System.Drawing.Icon(st, System.Windows.Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex) { Log.Skip("Значок трея из ресурсов: " + ex.Message); }
        try { return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }
        return System.Drawing.SystemIcons.Application;
    }

    /// <summary>Ожидание сигнала «покажи окно»; снимаем при закрытии, иначе после смены темы сигнал мог уйти старому окну.</summary>
    private RegisteredWaitHandle _showWait;

    private void DisposeTray()
    {
        try { _showWait?.Unregister(null); _showWait = null; } catch { }
        try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; } } catch { }
    }

    private void Balloon(string text, string title = "Merno")
    {
        Log.Skip($"Уведомление в трее: {text}");
        try { _tray?.ShowBalloonTip(5000, title, text, System.Windows.Forms.ToolTipIcon.Info); } catch { }
    }

    /// <summary>Запуск с --tray: окно не показываем (сведения о компьютере, «Что нового» и проверка обновлений — когда окно откроют).</summary>
    public void StartHidden()
    {
        ShowInTaskbar = false;
        try { new WindowInteropHelper(this).EnsureHandle(); } catch { }
        Log.Skip("Merno запущен в трее");
        if (_tray == null) Show();   // значка нет — без окна программой не воспользоваться
    }

    private void ShowFromTray()
    {
        try
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = _restoreState;
            Activate();
            Topmost = true; Topmost = false;   // поверх остальных окон, даже если Windows не дала фокус
            Focus();
        }
        catch (Exception ex) { Log.Warn("Показ окна: " + ex.Message); }
    }

    private void HideToTray()
    {
        if (_tray == null) { WindowState = WindowState.Minimized; return; }
        Hide();
        ShowInTaskbar = false;
        Log.Skip("Окно свёрнуто в трей");
        if (!_settings.TrayHintShown)
        {
            _settings.TrayHintShown = true; _settings.Save();
            Balloon("Merno свёрнут в трей. Дважды щёлкните по значку, чтобы открыть Merno. Правый щелчок открывает меню быстрых действий.");
        }
    }

    /// <summary>Каким было окно до сворачивания — чтобы из трея вернуть развёрнутым, если было развёрнуто.</summary>
    private WindowState _restoreState = WindowState.Normal;

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // «Свернуть» оставляет окно на панели задач; в трей уходит только по крестику
        if (WindowState != WindowState.Minimized) _restoreState = WindowState;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || App.ForceExit || _themeSwap) return;
        int act = _settings.CloseAction;
        if (act == 0 && _tray != null)
        {
            bool tray = MernoDialog("Свёрнутый Merno ждёт в трее. Правый щелчок по значку открывает быструю очистку и другие действия. " +
                                    "Выбор можно поменять в «Настройках» → «При закрытии окна».",
                                    "Свернуть Merno в трей или закрыть?", "Свернуть", "Закрыть", false);
            act = tray ? 1 : 2;
            _settings.CloseAction = act; _settings.Save();
            SetCloseAsk.IsChecked = false; SetCloseTray.IsChecked = act == 1; SetCloseExit.IsChecked = act == 2;
        }
        if (act == 1 && _tray != null) { e.Cancel = true; HideToTray(); return; }
        if (_winInstalling && !Ask("Merno ставит обновления Windows. Если закрыть сейчас, часть обновлений не установится. Всё равно закрыть?",
                          "Merno", MessageBoxImage.Warning, "Закрыть", "Не закрывать")) { e.Cancel = true; return; }
        if (_busy && !Ask("Merno ещё работает. Если закрыть сейчас, дело не будет доделано. Всё равно закрыть?",
                          "Merno", MessageBoxImage.Warning, "Закрыть", "Не закрывать")) { e.Cancel = true; return; }
        App.ForceExit = true;
        Diag.ExitStep("окно закрывается (крестик)");   // «чёрный ящик» выхода — Core/Diag.cs
    }

    private void ExitFromTray()
    {
        if (_winInstalling && !Ask("Merno ставит обновления Windows. Если выйти сейчас, часть обновлений не установится. Всё равно выйти?",
                               "Merno", MessageBoxImage.Warning, "Выйти", "Не выходить")) return;
        if (_busy && !Ask("Merno ещё работает. Если выйти сейчас, дело не будет доделано. Всё равно выйти?",
                          "Merno", MessageBoxImage.Warning, "Выйти", "Не выходить")) return;
        Log.Write("Выход из Merno (меню в трее)");
        App.ForceExit = true;
        Diag.ExitStep("выход из меню в трее");
        DisposeTray();
        Application.Current.Shutdown();
    }

    // ---------------------------------------------------------------- меню значка
    private void ShowTrayMenu()
    {
        try
        {
            if (_trayMenu == null) BuildTrayMenu();
            foreach (var mi in _trayBusyItems) mi.IsEnabled = !_busy;
            // своё окно на передний план — иначе меню не закроется щелчком мимо него
            var h = new WindowInteropHelper(this).EnsureHandle();
            SetForegroundWindow(h);
            _trayMenu.Placement = PlacementMode.MousePoint;
            _trayMenu.IsOpen = true;
        }
        catch (Exception ex) { Log.Warn("Меню в трее: " + ex.Message); }
    }

    private void BuildTrayMenu()
    {
        _trayMenu = new ContextMenu();
        MenuItem Item(string text, Action click, bool busyAware)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) => { _trayMenu.IsOpen = false; click(); };
            if (busyAware) _trayBusyItems.Add(mi);
            _trayMenu.Items.Add(mi);
            return mi;
        }
        var open = Item("Открыть Merno", ShowFromTray, false);
        open.FontWeight = FontWeights.SemiBold;
        _trayMenu.Items.Add(new Separator());
        Item("Очистить корзину", TrayRecycle, true);
        Item("Быстрая очистка", () => TrayRun("Быстрая очистка", "Очищаю мусор…", QuickClean), true);
        Item("Очистить кэш DNS", () => TrayRun("Кэш DNS", "Очищаю кэш DNS…", () =>
        {
            var (code, _) = Sh.Run(Sh.Sys32("ipconfig.exe"), "/flushdns", 30_000);
            if (code == 0) { Log.Ok("Кэш DNS очищен"); return "Готово. Если сайты не открывались, попробуйте открыть их снова."; }
            Log.Warn("Не удалось очистить кэш DNS");
            return "Не получилось очистить кэш DNS. Попробуйте ещё раз или перезагрузите компьютер.";
        }), true);
        Item("Перезапустить Проводник", () => TrayRun("Проводник", "Перезапускаю панель задач и папки…", () =>
        {
            ShellMenu.RestartExplorer();
            return "Готово. Панель задач и папки перезапущены.";
        }), true);
        _trayMenu.Items.Add(new Separator());
        Item("Выход", ExitFromTray, false);
    }

    /// <summary>Действие из трея: в фоне (окно может быть скрыто), результат — уведомлением.</summary>
    private async void TrayRun(string title, string status, Func<string> work)
    {
        try
        {
            if (_busy) { Balloon("Merno занят. Подождите немного.", title); return; }
            Log.Head("Трей: " + title);
            string msg = null;
            if (!await Busy(status, () => msg = work())) { Balloon("Merno занят. Подождите немного.", title); return; }
            SetStatus(msg ?? "Готово");
            Balloon(msg ?? "Не получилось. Попробуйте ещё раз.", title);
        }
        catch (Exception ex) { Log.Warn($"Трей ({title}): {ex.Message}"); }
    }

    private void TrayRecycle()
    {
        if (_busy) { Balloon("Merno занят. Подождите немного.", "Корзина"); return; }
        TrayRun("Корзина", "Очищаю корзину…", () =>
        {
            var t = Cleaner.Targets().First(x => x.Recycle);
            long size = Cleaner.Measure(t);
            Cleaner.Clean(t);
            Log.Ok("Корзина очищена" + (size > 0 ? $": {Sh.Size(size)}" : ""));
            return size > 0 ? $"Готово. Освобождено {Sh.Size(size)}." : "Корзина уже пустая.";
        });
    }

    /// <summary>Безопасные пункты очистки по умолчанию — без корзины, долгой очистки компонентов Windows и остатков программ (их лучше видеть в «Очистке»).</summary>
    private static string QuickClean()
    {
        long freed = 0;
        foreach (var c in Cleaner.Targets().Where(x => x.Default && !x.Recycle && !x.Dism && !x.Leftovers))
        {
            try { freed += Math.Max(0, Cleaner.Clean(c)); }
            catch (Exception ex) { Log.Warn($"{c.Name}: {ex.Message}"); }
        }
        Log.Ok($"Быстрая очистка: освобождено {Sh.Size(freed)}");
        return $"Готово. Освобождено {Sh.Size(freed)}.";
    }

    private void ToggleAutostart(bool on)
    {
        if (_autostartGuard) return;
        SetAutostart.IsEnabled = false;
        Task.Run(() => on ? Autostart.Enable() : Autostart.Disable()).ContinueWith(t =>
        {
            bool ok = !t.IsFaulted && t.Result;
            if (t.IsFaulted) Log.Warn("Автозапуск: " + t.Exception?.GetBaseException().Message);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SetAutostart.IsEnabled = true;
                if (ok) { _settings.Autostart = on; _settings.Save(); SetStatus(on ? "Merno будет запускаться вместе с компьютером" : "Автозапуск Merno выключен"); }
                else
                {
                    _autostartGuard = true; SetAutostart.IsChecked = !on; _autostartGuard = false;
                    SetStatus(on ? "Автозапуск работает только в установленной версии Merno. В портативной версии его нет." : "Не удалось выключить автозапуск. Запустите Merno от администратора.");
                }
            }));
        });
    }
}
