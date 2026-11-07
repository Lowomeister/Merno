// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Merno.Core;

namespace Merno;

// Переходы по разделам и подсветка «Где исправить».
public partial class MainWindow
{
    // ---------- меню, режим, обучение ----------
    /// <summary>Открыть часть «Главной» (оптимизация, мой компьютер, первый запуск), не сбивая подсветку меню.</summary>
    private void ToHomePart(FrameworkElement pg)
    {
        if (pg.Visibility == Visibility.Visible) return;
        if (NavHome.IsChecked != true) { _navSync = true; try { NavHome.IsChecked = true; } finally { _navSync = false; } }
        ShowPage(pg);
    }

    /// <summary>
    /// Открыть раздел по x:Name страницы («PageClean», «PageHome»…). Для «Моего компьютера» можно передать
    /// «Pc» / «PageSystem». Пункт меню подсвечивается как при обычном переходе.
    /// </summary>
    public void ShowPageByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var n = name.Trim();
        if (n.Equals("FirstRun", StringComparison.OrdinalIgnoreCase) || n.Equals("PageFirstRun", StringComparison.OrdinalIgnoreCase)) n = "PageHome";   // мастер — карточка HomeSetup на «Главной»
        if (n.Equals("Pc", StringComparison.OrdinalIgnoreCase) || n.Equals("System", StringComparison.OrdinalIgnoreCase)) n = "PageSystem";
        // «Монитор» живёт в «Моём компьютере»; «Windows» — пункт меню «Обновления Windows» (та же страница, что «Драйверы»)
        if (n.Equals("Monitor", StringComparison.OrdinalIgnoreCase) || n.Equals("PageMonitor", StringComparison.OrdinalIgnoreCase)) n = "PageSystem";
        else if (n.Equals("Windows", StringComparison.OrdinalIgnoreCase) || n.Equals("PageWindows", StringComparison.OrdinalIgnoreCase) || n.Equals("WinUpdate", StringComparison.OrdinalIgnoreCase)) { n = "PageDrivers"; DrvTabWin.IsChecked = true; }
        else if (n.Equals("PageDrivers", StringComparison.OrdinalIgnoreCase) || n.Equals("Drivers", StringComparison.OrdinalIgnoreCase)) DrvTabDrv.IsChecked = true;
        // вкладки «Настроек»; старое имя «О программе» ведёт в «Настройки» (версия, авторы и лицензия — в карточке «Версия»)
        if (n.Equals("PageAbout", StringComparison.OrdinalIgnoreCase) || n.Equals("About", StringComparison.OrdinalIgnoreCase)) n = "PageSettings";
        var tab = n.Equals("Contact", StringComparison.OrdinalIgnoreCase) || n.Equals("PageContact", StringComparison.OrdinalIgnoreCase) ? SetTabContact :
                  n.Equals("Look", StringComparison.OrdinalIgnoreCase) || n.Equals("PageLook", StringComparison.OrdinalIgnoreCase) ? SetTabLook : null;
        if (tab != null) { n = "PageSettings"; tab.IsChecked = true; }
        else if (n.Equals("PageSettings", StringComparison.OrdinalIgnoreCase) || n.Equals("Settings", StringComparison.OrdinalIgnoreCase)) SetTabMain.IsChecked = true;
        if (!n.StartsWith("Page", StringComparison.Ordinal)) n = "Page" + n;
        if (FindName(n) is not FrameworkElement page) { Log.Warn("Нет раздела " + name); return; }
        // разделы «для опытных» видны только в расширенном режиме
        if ((page == PageTweaks || page == PageMenu) && !_settings.Advanced) { SetStatus("Сначала включите «Расширенный режим» в настройках"); page = PageSettings; }
        RadioButton nav =
            page == PageHome || page == PageSystem || page == PageFirstRun ? NavHome :
            page == PageApps || page == PageUninstall || page == PageStore ? NavApps :
            page == PageSettings && SetTabContact.IsChecked == true ? NavContact :
            page == PageSettings || page == PageRollback || page == PageLog ? NavSettings :
            page == PageClean ? NavClean : page == PageStartup ? NavStartup : page == PageRuntime ? NavRuntime :
            page == PageDrivers ? (DrvTabWin.IsChecked == true ? NavUpdates : NavDrivers) : page == PageTools ? NavTools : page == PageTweaks ? NavTweaks :
            page == PageMenu ? NavMenu : page == PageQuick ? NavQuick : page == PageHelp ? NavHelp : page == PageSearch ? NavSearch : null;
        if (nav != null && nav.IsChecked != true) { _navSync = true; try { nav.IsChecked = true; } finally { _navSync = false; } }
        if (page.Visibility != Visibility.Visible) ShowPage(page);
    }

    /// <summary>Прокручивает к элементу, обводит его бирюзовой рамкой и показывает подсказку,
    /// которая висит, пока её не закроют или не уйдут в другой раздел.</summary>
    public void Highlight(FrameworkElement el, string text = null)
    {
        if (el == null) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                ClearHint();
                el.BringIntoView();
                UpdateLayout();
                var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(el);
                if (layer != null)
                {
                    _hintAdorner = new HintAdorner(el, B("Accent"));
                    layer.Add(_hintAdorner);
                    // рамка мигает 5 секунд и пропадает сама — без полосы с текстом и кнопки «Понятно»
                    _hintAdorner.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.15, TimeSpan.FromMilliseconds(450))
                        { AutoReverse = true, RepeatBehavior = new RepeatBehavior(TimeSpan.FromSeconds(5)) });
                }
                StartHintTimer(5);
            }
            catch (Exception ex) { Log.Skip("Подсветка: " + ex.Message); }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

}
