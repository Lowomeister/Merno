// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Merno.Core;

namespace Merno;

/// <summary>
/// «Мой компьютер → Монитор» (карточка MonCard): текущая и максимальная частота каждого монитора и кнопка «Включить N Гц».
/// Смена — как в самом Windows: если через 15 секунд не нажать «Оставить», вернётся прежняя частота
/// (на случай, если экран погас и ничего не видно).
/// </summary>
/// 

public partial class MainWindow
{
    private const int MonRevertSec = 15;

    private void InitMonitor()
    {
        void Sync()
        {
            // «Драйверы» и «Обновления Windows» — одна страница PageDrivers, что показать — выбирает пункт меню
            bool drv = DrvTabDrv.IsChecked == true;
            bool win = DrvTabWin.IsChecked == true;
            DrvHead.Visibility = drv ? Visibility.Visible : Visibility.Collapsed;
            DrvScroll.Visibility = drv ? Visibility.Visible : Visibility.Collapsed;
            WinHead.Visibility = win ? Visibility.Visible : Visibility.Collapsed;
            WinScroll.Visibility = win ? Visibility.Visible : Visibility.Collapsed;
            // боковая панель каркаса: у каждой части свои карточки
            DrvCheckCard.Visibility = DrvSide.Visibility = drv ? Visibility.Visible : Visibility.Collapsed;
            WinCheckCard.Visibility = WinSide.Visibility = win ? Visibility.Visible : Visibility.Collapsed;
            if (win && !_winBuilt && !_winSearching) Fire(BuildWinTabAsync, "Windows");
        }
        DrvTabDrv.Checked += (_, _) => Sync();
        DrvTabWin.Checked += (_, _) => Sync();
    }

    private void BuildMonitors()
    {
        MonList.Children.Clear();
        var list = Display.Monitors();
        if (list.Count == 0)
        {
            MonList.Children.Add(DrvRow("Не удалось узнать частоту монитора", "Откройте настройки экрана Windows.", OpenDisplaySettingsButton()));
            return;
        }
        foreach (var m in list)
        {
            string sub = $"Разрешение {m.Width}×{m.Height}. Сейчас {m.Now}\u00A0Гц" + (m.Rates.Count > 1 ? $", доступно: {string.Join(", ", m.Rates)}\u00A0Гц." : ".");
            FrameworkElement right;
            Brush title = null;
            if (m.CanFaster)
            {
                title = B("Warn");
                var b = new Button { Content = $"Включить {m.Max}\u00A0Гц", Style = (Style)FindResource("Primary") };
                var mon = m;
                b.Click += (_, _) => Fire(() => RaiseRateAsync(mon, mon.Max), "Частота монитора");
                right = b;
                sub += $" Монитор умеет {m.Max}\u00A0Гц. На такой частоте изображение может выглядеть плавнее.";
            }
            else right = new TextBlock { Text = "Максимальная частота", Foreground = B("Ok"), VerticalAlignment = VerticalAlignment.Center };
            MonList.Children.Add(DrvRow($"{m.Name}{(m.Primary && list.Count > 1 ? " (основной)" : "")}: {m.Now}\u00A0Гц", sub, right, title));
        }
        MonList.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 14, 0, 0), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap,
            Text = Typo.Nbsp("Нет нужной частоты? Проверьте кабель: для 144\u00A0Гц и выше нужен DisplayPort или HDMI версии 2.0 или новее. Иногда частоту ограничивает сам монитор: проверьте его настройки кнопками на корпусе.")
        });
        var more = OpenDisplaySettingsButton();
        more.HorizontalAlignment = HorizontalAlignment.Left; more.Margin = new Thickness(0, 12, 0, 0);
        MonList.Children.Add(more);
    }

    private Button OpenDisplaySettingsButton()
    {
        var b = new Button { Content = "Настройки экрана Windows" };
        b.Click += (_, _) => Sh.Open("ms-settings:display-advanced");
        return b;
    }

    private async Task RaiseRateAsync(MonitorInfo m, int hz)
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        if (!Ask($"Включить {hz}\u00A0Гц на мониторе «{m.Name}»?\n\nЭкран может на секунду мигнуть. Если изображение пропадёт, ничего не нажимайте: " +
                 $"через {MonRevertSec} секунд Merno сам вернёт {m.Now}\u00A0Гц.", "Частота монитора", MessageBoxImage.Warning, $"Включить {hz}\u00A0Гц")) return;
        int old = m.Now;
        string err = null;
        await Task.Run(() => err = Display.SetRate(m.Device, hz));
        if (err != null) { Warn(err); BuildMonitors(); return; }
        if (ConfirmKeep(hz, old)) SetStatus($"Монитор работает на {hz}\u00A0Гц");
        else
        {
            await Task.Run(() => Display.SetRate(m.Device, old));
            SetStatus($"Вернул {old}\u00A0Гц");
        }
        BuildMonitors();
    }

    /// <summary>Окно «Оставить N Гц?» с обратным отсчётом. Не ответили — прежняя частота.</summary>
    private bool ConfirmKeep(int hz, int old)
    {
        int left = MonRevertSec;
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = B("Sub"), Margin = new Thickness(0, 6, 0, 0) };
        void Upd() => text.Text = $"Если не нажать «Оставить», через {left} сек. вернётся {old}\u00A0Гц.";
        Upd();
        var keep = new Button { Content = "Оставить", Style = (Style)FindResource("Primary"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        var back = new Button { Content = $"Вернуть {old}\u00A0Гц", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        sp.Children.Add(new TextBlock { Text = $"Экран работает нормально на {hz}\u00A0Гц?", Style = (Style)FindResource("H2"), FontSize = 18, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(text);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        btns.Children.Add(back); btns.Children.Add(keep);
        sp.Children.Add(btns);
        var win = new Window
        {
            Title = "Частота монитора (Merno)", Owner = this, Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = B("Bg"), Foreground = B("Text"), FontFamily = FontFamily, FontSize = 14,
            ShowInTaskbar = false, Content = sp, Topmost = true
        };
        win.SourceInitialized += (_, _) => DarkTitleBar(win);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { if (--left <= 0) { timer.Stop(); win.DialogResult = false; } else Upd(); };
        keep.Click += (_, _) => win.DialogResult = true;
        win.Closed += (_, _) => timer.Stop();
        timer.Start();
        return win.ShowDialog() == true;
    }
}
