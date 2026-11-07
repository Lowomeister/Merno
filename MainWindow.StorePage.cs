// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Страница «Приложения Windows».
public partial class MainWindow
{
    // ---------- вернуть встроенные приложения ----------
    private async Task BuildStoreAsync()
    {
        int gen = ++_genStore;
        try
        {
        StoreList.Children.Clear();
        StoreList.Children.Add(new TextBlock { Text = "Проверяю, какие приложения сейчас стоят…", Foreground = B("Sub") });
        var apps = Apps.Load().Where(a => a.tab == "store").ToList();
        HashSet<string> installed = new(); bool? store = true; bool wg = false, appxFailed = false;
        await Task.Run(() => { wg = Apps.WingetAvailable(); installed = Apps.Installed(apps, onAppxFailed: () => appxFailed = true); store = Apps.StorePresent(); });
        if (gen != _genStore) return;
        _wingetOk = wg;
        StoreList.Children.Clear();
        if (appxFailed)
        {
            // список приложений не получен: «нет на компьютере» писать нельзя, мы просто не знаем
            ShowError(StoreList, "Не удалось получить список приложений Windows. Это не значит, что их нет. Нажмите «Повторить».", BuildStoreAsync);
            if (!_wingetOk) StoreList.Children.Add(WingetBanner());
            return;
        }
        if (store == null)
            StoreList.Children.Add(new TextBlock { Text = "Не удалось проверить, есть ли Microsoft Store. Попробуйте открыть страницу ещё раз.", Foreground = B("Warn"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        else if (store == false)
        {
            // без Магазина приложения не вернуть — сначала предлагаем поставить его
            // как плашка «Программы не ставятся автоматически»: жёлтая полоса слева
            var warn = new Border { Style = (Style)FindResource("Row"), BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = B("Warn"), Margin = new Thickness(0, 0, 0, 12) };
            var dp = new DockPanel();
            var btn = new Button { Content = "Установить Microsoft Store", Style = (Style)FindResource(wg ? "Primary" : "Outline"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(btn, Dock.Right);
            btn.Click += (_, _) => Fire(async () =>
            {
                if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
                btn.IsEnabled = false; btn.Content = "Устанавливаю…";
                bool ok = false;
                if (!await Busy("Устанавливаю Microsoft Store…", () => ok = Apps.InstallStore())) { btn.IsEnabled = true; btn.Content = "Установить Microsoft Store"; return; }
                if (ok) await BuildStoreAsync();
                else { btn.IsEnabled = true; btn.Content = "Установить Microsoft Store";
                       Info("Microsoft Store не установился. Перезагрузите компьютер и попробуйте ещё раз."); }
            }, "Microsoft Store");
            var sp = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            sp.Children.Add(new TextBlock { Text = "Microsoft Store не найден", FontWeight = FontWeights.SemiBold, FontSize = 14.5, Foreground = B("Warn"), TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = "Без него приложения не вернуть. Нажмите кнопку и подождите пару минут.", Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
            dp.Children.Add(btn); dp.Children.Add(sp);
            warn.Child = dp;
            StoreList.Children.Add(warn);
        }
        if (!_wingetOk)
            StoreList.Children.Add(WingetBanner());   // нет или старый winget — Merno поставит его сам (MainWindow.Apps.cs)
        // галочки: вернуть или удалить несколько приложений разом (MainWindow.Batch.cs)
        _storeGrp.All = apps; _storeGrp.Installed = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase); _storeGrp.Rebuild = BuildStoreAsync;
        BuildGroupCard(StoreSelBar, _storeGrp);
        // по группам; внутри группы сначала удалённые — их и хотят вернуть
        foreach (var grp in apps.GroupBy(a => a.category))
        {
            int missing = grp.Count(a => !installed.Contains(a.name));
            StoreList.Children.Add(new TextBlock { Text = grp.Key + (missing > 0 ? $" · нет на компьютере: {missing}" : ""), Style = (Style)FindResource("H2"), FontSize = 18, Margin = new Thickness(0, 22, 0, 6) });
            foreach (var a in grp.OrderBy(a => installed.Contains(a.name)))
            {
                var r = AppRow(a, installed.Contains(a.name), "В Microsoft Store", installLabel: "Вернуть", showUninstall: true,
                                selectableSet: _storeGrp.Sel, register: cb => _storeGrp.Checks[a.name] = cb);
                r.Tag = a.name + " " + a.desc;
                StoreList.Children.Add(r);
            }
        }
        ApplyFilter(StoreFilter, StoreFilterHint, StoreList);
        }
        catch (Exception ex)
        {
            Log.Warn("Приложения Windows: " + ex.Message); Log.Skip(ex.ToString());
            if (gen == _genStore) ShowError(StoreList, "Не получилось проверить приложения Windows.", BuildStoreAsync);
        }
    }

}
