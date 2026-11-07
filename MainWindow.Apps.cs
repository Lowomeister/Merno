// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Программы: каталог, поиск, заголовки групп и категории; общая строка программы AppRow (её используют и Компоненты, и Драйверы)
public partial class MainWindow
{
    private async Task BuildAppsAsync()
    {
        int gen = ++_genApps;
        try
        {
        AppsList.Children.Clear();
        AppsList.Children.Add(new TextBlock { Text = "Проверяю, что уже установлено…", Foreground = B("Sub") });
        var apps = Apps.Load().Where(a => a.tab == null && Apps.Fits(a, _ctx.S)).ToList();
        HashSet<string> installed = new(); bool wg = false;
        await Task.Run(() => { wg = Apps.WingetAvailable(); installed = Apps.Installed(apps); });
        if (gen != _genApps) return;
        _wingetOk = wg;
        _appsAll = apps;
        _appsInstalled = installed;
        _appSel.IntersectWith(apps.Select(a => a.name));
        BuildAppsSelBar();
        AppsList.Children.Clear();
        _searchBox = new StackPanel();
        AppsList.Children.Add(_searchBox);
        if (!_wingetOk)
        {
            AppsList.Children.Add(WingetBanner());   // нет или старый winget — Merno поставит его сам
            AppSearch.IsEnabled = false;
        }
        SetOwn(BtnAppSearch, _wingetOk);
        AppsCats.Children.Clear();
        foreach (var grp in apps.GroupBy(a => a.category))
        {
            var head = GroupHeader(grp.Key, grp.Count(), grp.Count(a => installed.Contains(a.name)));
            AppsList.Children.Add(head);
            AppsCats.Children.Add(CategoryLink(grp.Key, grp.Count(), head));
            foreach (var a in grp) AppsList.Children.Add(AppRow(a, installed.Contains(a.name), "Сайт", showUninstall: true, selectableSet: _appSel,
                register: cb => _appChecks[a.name] = cb));
        }
        UpdateSelBars();
        _appsBuilt = true;
        if (_wingetOk) _ = BuildUpdatesCardAsync(); else AppsUpdBar.Parent.SetValue(VisibilityProperty, Visibility.Collapsed);
        }
        catch (Exception ex)
        {
            Log.Warn("Программы: " + ex.Message); Log.Skip(ex.ToString());
            if (gen == _genApps) { _searchBox = null; ShowError(AppsList, "Не получилось загрузить список программ.", BuildAppsAsync); }
        }
    }

    /// <summary>Заголовок группы в списке: название и «установлено N из M» мелко рядом. Во всю ширину (FlowColumns).</summary>
    private FrameworkElement GroupHeader(string title, int total, int installed)
    {
        // в узкой колонке «установлено N из M» уходит на следующую строку, а не за край (HeaderLine)
        var cnt = new TextBlock { Text = $"установлено {installed} из {total}", FontSize = 12.5, Foreground = B("Sub"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) };
        var sp = HeaderLine(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), VerticalAlignment = VerticalAlignment.Bottom, TextWrapping = TextWrapping.Wrap }, cnt);
        sp.Margin = new Thickness(4, 24, 0, 8);
        return sp;
    }

    /// <summary>Строка в карточке «Категории»: название и число; щелчок прокручивает список к группе.</summary>
    private FrameworkElement CategoryLink(string title, int count, FrameworkElement target)
    {
        var dp = new DockPanel();
        var n = new TextBlock { Text = count.ToString(), Foreground = B("Sub"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(n, Dock.Right);
        dp.Children.Add(n);
        dp.Children.Add(new TextBlock { Text = title, FontSize = 13.5, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var b = new Border { Child = dp, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(-10, 0, -10, 2), CornerRadius = new CornerRadius(6),
                             Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        b.MouseEnter += (_, _) => b.Background = B("CardHover");
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, _) =>
        {
            // прокручиваем так, чтобы заголовок группы оказался сверху
            if (target.Parent is FrameworkElement list && list.Parent is ScrollViewer sv)
            {
                var p = target.TransformToAncestor(sv).Transform(new Point(0, 0));
                sv.ScrollToVerticalOffset(sv.VerticalOffset + p.Y - 4);
            }
            else target.BringIntoView();
        };
        return b;
    }

    private async Task SearchAppsAsync()
    {
        var q = AppSearch.Text.Trim();
        if (_searchBox == null) { SetStatus("Подождите, список ещё загружается"); return; }
        if (q.Length == 0) { _searchBox.Children.Clear(); return; }   // пустой запрос + «Найти» = скрыть результаты
        if (q.Length < 2) { SetStatus("Введите хотя бы две буквы"); return; }
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        if (!BtnAppSearch.IsEnabled) return;   // поиск уже идёт (Enter во время поиска) или winget недоступен
        _searchBox.Children.Clear();
        var head = new DockPanel { Margin = new Thickness(0, 22, 0, 2) };
        var close = new Button { Content = "Скрыть результаты", Padding = new Thickness(14, 6, 14, 6), VerticalAlignment = VerticalAlignment.Center };
        close.Click += (_, _) => { _searchBox.Children.Clear(); AppSearch.Text = ""; };
        DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
        var headText = new TextBlock { Style = (Style)FindResource("H2"), FontSize = 18 };
        head.Children.Add(headText);

        // Яндекс — принципиально нет
        if (Apps.IsYandex(q))
        {
            headText.Text = "Программы Яндекса в Merno не устанавливаются.";
            _searchBox.Children.Add(head);
            // замена — по тому, что искали: «Яндекс Музыка» → музыка, «Яндекс Браузер» или просто «Яндекс» → браузер,
            // «Кинопоиск» → видеоплееры. Нет подходящей подборки (Диск, Карты, Алиса) — без предложений
            var rest = System.Text.RegularExpressions.Regex.Replace(q, @"yandex|яндекс|яндкс|yndx|ya\.ru", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim(' ', '.', '-');
            if (System.Text.RegularExpressions.Regex.IsMatch(q, "кинопоиск", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) rest = "видео";
            if (rest.Length == 0) rest = "браузер";
            var alt = (Apps.Topic(rest).Apps ?? new List<AppItem>()).Take(3).ToList();
            if (alt.Count == 0) return;
            _searchBox.Children.Add(new TextBlock { Text = "Вместо этого можно поставить:",
                                                    Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 2, 0, 8) });
            HashSet<string> ia = new();
            await Task.Run(() => ia = Apps.Installed(alt));
            foreach (var a in alt) _searchBox.Children.Add(AppRow(a, ia.Contains(a.name), "Подробнее", showUninstall: true));
            return;
        }

        // запрос по смыслу: «браузер», «торрент», «плеер» — подборка Merno
        var (topic, picks) = Apps.Topic(q);
        List<AppItem> found = new();
        if (picks == null)
        {
            _searchBox.Children.Add(new TextBlock { Text = $"Ищу «{q}»…", Foreground = B("Sub"), Margin = new Thickness(0, 16, 0, 0) });
            SetOwn(BtnAppSearch, false);
            HashSet<string> trusted = new();
            try { await Task.Run(() => { found = Apps.Search(q) ?? new(); trusted = Apps.TrustedIds() ?? new(); }); }
            catch (Exception ex)
            {
                Log.Warn("Поиск программ: " + ex.Message);
                _searchBox.Children.Clear();
                _searchBox.Children.Add(new TextBlock { Text = "Поиск не удался. Проверьте интернет и попробуйте ещё раз.", Foreground = B("Warn"), Margin = new Thickness(0, 16, 0, 0) });
                return;
            }
            finally { SetOwn(BtnAppSearch, _wingetOk); }
            foreach (var a in found) a.trusted = trusted.Contains(a.winget);
            found = found.OrderByDescending(a => a.trusted).ToList();   // проверенные — наверх
            _searchBox.Children.Clear();
        }
        else found = picks;
        found = found.Where(a => Apps.Fits(a, _ctx.S)).ToList();

        headText.Text = picks != null ? $"Подборка Merno: {topic.ToLowerInvariant()}"
                      : found.Count == 0 ? $"По запросу «{q}» ничего не найдено" : $"Найдено по запросу «{q}»: {found.Count}";
        _searchBox.Children.Add(head);
        if (found.Count == 0)
        {
            _searchBox.Children.Add(new TextBlock { Text = "Попробуйте написать название иначе, например по-английски, или тип программы: «браузер», «плеер», «архиватор».", Style = (Style)FindResource("SubText") });
            return;
        }
        _searchBox.Children.Add(new TextBlock
        {
            Text = picks != null ? "Проверенные программы для этой задачи. Если ищете конкретную программу, напишите её название."
                 : found.Any(a => a.trusted) ? "Значок ★ отмечает проверенные Merno программы. Остальные Merno не проверял: ставьте то, что знаете."
                 : "Эти программы Merno не проверял. Ставьте то, что знаете.",
            Style = (Style)FindResource("SubText"), Foreground = picks != null || found.Any(a => a.trusted) ? B("Sub") : B("Warn"), Margin = new Thickness(0, 0, 0, 8)
        });
        var apps = found;
        HashSet<string> inst = new();
        await Task.Run(() => { foreach (var a in apps) a.match ??= (a.name ?? "").Length >= 4 ? a.name : null; inst = Apps.Installed(apps); });
        foreach (var a in found) _searchBox.Children.Add(AppRow(a, inst.Contains(a.name), "Подробнее", showUninstall: true));
    }

    // ---------- нет winget: Merno ставит его сам ----------
    /// <summary>Плашка «Установка программ выключена» с кнопкой «Включить» (WingetSetup). После установки — перестраиваем разделы.</summary>
    private Border WingetBanner()
    {
        var state = WingetSetup.Check();
        var row = new Border { Style = (Style)FindResource("Row"), BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = B("Warn"), Margin = new Thickness(0, 0, 0, 12) };
        var dp = new DockPanel();
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = "Программы не ставятся автоматически", FontWeight = FontWeights.SemiBold, FontSize = 14.5, Foreground = B("Warn"), TextWrapping = TextWrapping.Wrap });
        string why = state switch
        {
            WingetSetup.State.Old => "Компонент Windows для установки программ устарел. Merno обновит его сам.",
            WingetSetup.State.Unsupported => "На этой версии Windows 10 установку программ не включить (нужна 1809 или новее). Откройте сайт программы через кнопку «⋯» и скачайте её оттуда.",
            _ => "Для установки программ нужен компонент Windows. Merno поставит его сам (около 300 МБ, от Microsoft)."
        };
        sp.Children.Add(new TextBlock { Text = Typo.Nbsp(why), Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (state != WingetSetup.State.Unsupported)
        {
            var btn = new Button { Content = state == WingetSetup.State.Old ? "Обновить установщик программ" : "Включить установку программ", Style = (Style)FindResource("Primary"),
                                   VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            DockPanel.SetDock(btn, Dock.Right);
            btn.Click += (_, _) => Fire(async () =>
            {
                if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
                (bool Ok, string Error) r = (false, null);
                if (!await Busy("Готовлю установку программ…", () => r = WingetSetup.Install(s => Dispatcher.BeginInvoke(new Action(() => SetStatus(s, detectError: false)))))) return;
                if (!r.Ok) { Warn(r.Error); return; }
                Apps.ResetWinget();
                SetStatus("Установка программ включена");
                _appsBuilt = false; _runtimeBuilt = false;
                if (PageApps.IsVisible) await BuildAppsAsync();
                else if (PageRuntime.IsVisible) await BuildRuntimeAsync();
                else if (PageStore.IsVisible) await BuildStoreAsync();
            }, "Установщик программ");
            dp.Children.Add(btn);
        }
        dp.Children.Add(sp);
        row.Child = dp;
        return row;
    }

    // ---------- ход установки (Apps.Progress) ----------
    private string _busyText;

    /// <summary>winget сообщил, что делает (фон-поток): показываем в строке состояния и в карточке обновлений.</summary>
    private void OnAppProgress(string p)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_updRunning && _updProgText != null) _updProgText.Text = (_updStep.Length > 0 ? _updStep + " · " : "") + p;
            if (_qRunning) { QueueDetail(p); return; }   // идёт очередь установки — строку внизу ведёт она (MainWindow.InstallProgress.cs)
            if (!_busy) return;
            string head = (_busyText ?? "").TrimEnd('…', '.', ' ');
            Status.Text = head.Length > 0 && !head.Contains(p.Split(':')[0]) ? head + " · " + p : p;
        });
    }

    // ---------- обновления установленных программ ----------
    private bool _appUpdChecking, _updRunning;
    private readonly HashSet<string> _updSel = new(StringComparer.OrdinalIgnoreCase);   // Id отмеченных; новые — отмечены
    private readonly HashSet<string> _updSeen = new(StringComparer.OrdinalIgnoreCase);
    private ProgressBar _updProgBar;
    private TextBlock _updProgText;
    private string _updStep = "";

    /// <summary>Карточка «Обновления программ»: что можно обновить (winget upgrade), галочки, «Обновить» по одной
    /// и «Обновить отмеченные»; пока идёт обновление — полоса и что сейчас происходит.</summary>
    private async Task BuildUpdatesCardAsync()
    {
        if (_appUpdChecking || _updRunning) return;
        _appUpdChecking = true;
        try
        {
            AppsUpdBar.Children.Clear();
            AppsUpdBar.Children.Add(new TextBlock { Text = "Обновления программ", FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = B("Text") });
            var status = new TextBlock { Text = "Проверяю…", Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            AppsUpdBar.Children.Add(status);
            List<Apps.WingetUpdate> all = null;
            await Task.Run(() => all = Apps.Updates());
            AppsUpdBar.Children.Remove(status);
            _updLast = all;
            // «застрявшие»: winget уже «обновлял» их, а версия та же (ChatGPT и др.) — отдельно, без галочки
            var stuckMap = _settings.UpdStuck ??= new();
            List<Apps.WingetUpdate> stuck = new(), list = all;
            if (all != null)
            {
                foreach (var k in stuckMap.Keys.ToList())
                    if (!all.Any(u => u.Id.Equals(k, StringComparison.OrdinalIgnoreCase) && u.From == stuckMap[k])) { stuckMap.Remove(k); _settings.Save(); }
                stuck = all.Where(u => stuckMap.ContainsKey(u.Id)).ToList();
                list = all.Except(stuck).ToList();
            }
            if (list == null || list.Count == 0)
            {
                AppsUpdBar.Children.Add(new TextBlock
                {
                    Text = list == null ? "Не получилось проверить обновления. Попробуйте через минуту." : "Все программы свежие.",
                    Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 4, 0, 10), TextWrapping = TextWrapping.Wrap,
                    Foreground = list == null ? B("Warn") : B("Sub")
                });
                AddStuck(stuck);
                var again = new Button { Content = "Проверить ещё раз", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, stuck.Count > 0 ? 8 : 0, 0, 0) };
                again.Click += (_, _) => _ = BuildUpdatesCardAsync();
                AppsUpdBar.Children.Add(again);
                return;
            }
            // новые программы в списке — сразу отмечены; снятые галочки помним
            foreach (var u in list) if (_updSeen.Add(u.Id)) _updSel.Add(u.Id);
            _updSel.IntersectWith(list.Select(u => u.Id));

            var allBtn = new Button { Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 0) };
            var checks = new List<CheckBox>();
            var allCb = new CheckBox { Style = (Style)FindResource("Check"), LayoutTransform = new ScaleTransform(0.8, 0.8), Content = $"Можно обновить: {list.Count}", Foreground = B("Sub"), FontSize = 15, Margin = new Thickness(0, 6, 0, 8), ToolTip = "Отметить все / снять все" };
            bool sync = false;
            void Refresh()
            {
                int n = list.Count(u => _updSel.Contains(u.Id));
                allBtn.Content = n == 0 ? "Отметьте, что обновить" : n == list.Count ? $"Обновить все ({n})" : $"Обновить отмеченные ({n})";
                allBtn.IsEnabled = n > 0;
                sync = true; allCb.IsChecked = n == list.Count ? true : n == 0 ? false : null; sync = false;
            }
            allCb.Click += (_, _) =>
            {
                if (sync) return;
                bool on = list.Any(u => !_updSel.Contains(u.Id));
                foreach (var u in list) if (on) _updSel.Add(u.Id); else _updSel.Remove(u.Id);
                foreach (var c in checks) c.IsChecked = on;
                Refresh();
            };
            AppsUpdBar.Children.Add(allCb);

            foreach (var u in list)
            {
                var upd = u;
                var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var cb = new CheckBox { Style = (Style)FindResource("Check"), LayoutTransform = new ScaleTransform(0.8, 0.8), IsChecked = _updSel.Contains(u.Id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Обновить вместе с остальными отмеченными" };
                cb.Click += (_, _) => { if (cb.IsChecked == true) _updSel.Add(upd.Id); else _updSel.Remove(upd.Id); Refresh(); };
                checks.Add(cb);
                DockPanel.SetDock(cb, Dock.Left);
                dp.Children.Add(cb);
                var one = new Button { Content = "Обновить", Style = (Style)FindResource("Ghost"), Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(one, Dock.Right);
                one.Click += (_, _) => Fire(() => RunUpgradesAsync(new List<Apps.WingetUpdate> { upd }), "Обновление программы");
                dp.Children.Add(one);
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock { Text = u.Name, FontSize = 13.5, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = u.Name });
                sp.Children.Add(new TextBlock { Text = $"{u.From} → {u.To}", Style = (Style)FindResource("SubText"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                dp.Children.Add(sp);
                AppsUpdBar.Children.Add(dp);
            }
            allBtn.Click += (_, _) => Fire(() => RunUpgradesAsync(list.Where(u => _updSel.Contains(u.Id)).ToList()), "Обновление программ");
            AppsUpdBar.Children.Add(allBtn);
            Refresh();
            AddStuck(stuck);
        }
        catch (Exception ex) { Log.Skip("Обновления программ: " + ex.Message); }
        finally { _appUpdChecking = false; }
    }

    private List<Apps.WingetUpdate> _updLast;   // последний ответ winget (null — не ответил)

    /// <summary>Блок «winget не может обновить»: программа после «обновления» осталась той же версии.</summary>
    private void AddStuck(List<Apps.WingetUpdate> stuck)
    {
        // скрытые пользователем («✕») — не показываем, пока у программы не выйдет другая версия (To)
        var hidden = _settings.UpdStuckHidden ??= new();
        if (_updLast != null)   // winget ответил — забываем скрытое для программ, которые больше не «застряли»
        foreach (var k in hidden.Keys.ToList())
            if (stuck == null || !stuck.Any(u => u.Id.Equals(k, StringComparison.OrdinalIgnoreCase) && u.To == hidden[k])) { hidden.Remove(k); _settings.Save(); }
        stuck = stuck?.Where(u => !(hidden.TryGetValue(u.Id, out var v) && v == u.To)).ToList();
        if (stuck == null || stuck.Count == 0) return;
        var head = new TextBlock
        {
            Text = "Эти программы нужно обновить в самой программе или в Microsoft Store:",
            Style = (Style)FindResource("SubText"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 6)
        };
        AppsUpdBar.Children.Add(head);
        var rows = new List<DockPanel>();
        foreach (var u in stuck)
        {
            var upd = u;
            var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 6), Opacity = 0.75, Tag = "stuck" };
            rows.Add(dp);
            var hide = new Button { Content = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11, Style = (Style)FindResource("Ghost"),
                                    Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                                    ToolTip = "Скрыть до выхода новой версии программы" };
            DockPanel.SetDock(hide, Dock.Right);
            hide.Click += (_, _) =>
            {
                hidden[upd.Id] = upd.To; _settings.Save();
                AppsUpdBar.Children.Remove(dp); rows.Remove(dp);
                if (rows.Count == 0) AppsUpdBar.Children.Remove(head);
                SetStatus($"«{upd.Name}» скрыто до следующей версии");
            };
            dp.Children.Add(hide);
            var again = new Button { Content = "Ещё раз", Style = (Style)FindResource("Ghost"), Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center,
                                     ToolTip = "Попробовать обновить ещё раз" };
            DockPanel.SetDock(again, Dock.Right);
            again.Click += (_, _) => Fire(() => RunUpgradesAsync(new List<Apps.WingetUpdate> { upd }), "Обновление программы");
            dp.Children.Add(again);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = u.Name, FontSize = 13.5, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = u.Name });
            sp.Children.Add(new TextBlock { Text = $"{u.From} → {u.To}", Style = (Style)FindResource("SubText"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            dp.Children.Add(sp);
            AppsUpdBar.Children.Add(dp);
        }
    }

    private async Task RunUpgradesAsync(List<Apps.WingetUpdate> list)
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        if (list.Count == 0) return;
        if (list.Count > 1 && !Ask($"Обновить программы ({list.Count})?\n\nЗапущенные программы лучше закрыть. Займёт несколько минут.", "Merno", MessageBoxImage.Question, "Обновить")) return;
        // сам winget (App Installer) — последним: после его обновления winget какое-то время не отвечает
        list = list.OrderBy(Apps.IsWingetItself).ToList();
        var failed = new List<string>();

        // полоса и текст хода прямо в карточке; кнопки карточки на время гасим
        foreach (var el in AppsUpdBar.Children.OfType<UIElement>()) el.IsEnabled = false;
        _updProgBar = new ProgressBar { Height = 6, Minimum = 0, Maximum = list.Count, Value = 0, Margin = new Thickness(0, 10, 0, 6), Foreground = B("Accent"), Background = B("Surface2"), BorderThickness = new Thickness(0) };
        _updProgText = new TextBlock { Style = (Style)FindResource("SubText"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Text = "Начинаю…" };
        AppsUpdBar.Children.Add(_updProgBar);
        AppsUpdBar.Children.Add(_updProgText);
        _updRunning = true;
        bool ran = false;
        // полоска внизу — как у всех массовых действий; своя полоса в карточке «Обновления программ» остаётся
        QueueStart("Обновление", list.Select(u => u.Name));
        try
        {
            ran = await Busy(list.Count == 1 ? $"Обновляю {list[0].Name}…" : $"Обновляю программы ({list.Count})…", () =>
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (_qStop) break;
                    var u = list[i];
                    QueueSet(i, QState.Now);
                    Dispatcher.Invoke(() => { _updStep = list.Count > 1 ? $"{i + 1} из {list.Count}" : ""; _updProgBar.Value = i; _updProgText.Text = (_updStep.Length > 0 ? _updStep + " · " : "") + u.Name + ": начинаю"; });
                    bool ok = Apps.Upgrade(u);
                    if (!ok) failed.Add(u.Name);
                    QueueSet(i, ok ? QState.Done : QState.Fail);
                }
                Dispatcher.Invoke(() => { _updProgBar.Value = list.Count; _updProgText.Text = "Проверяю, что обновилось…"; });
            });
        }
        finally { _updRunning = false; _updStep = ""; await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background); QueueFinish(); }

        await BuildUpdatesCardAsync();   // и после ошибки — иначе карточка осталась бы погашенной
        if (!ran) return;
        // winget мог сказать «готово», а программа осталась той же версии (ChatGPT и др.: winget путается в версиях).
        // Такие запоминаем — дальше они отдельно, без галочки, и не попадают в «Обновить все».
        var stuckNow = new List<string>();
        if (_updLast != null)
        {
            foreach (var u in list)
            {
                if (failed.Contains(u.Name)) continue;
                if (_updLast.Any(n => n.Id.Equals(u.Id, StringComparison.OrdinalIgnoreCase) && n.From == u.From))
                {
                    stuckNow.Add(u.Name);
                    (_settings.UpdStuck ??= new())[u.Id] = u.From;
                    Log.Warn($"{u.Name}: winget сообщил об успехе, но версия осталась {u.From}. Обновите в самой программе");
                }
            }
            if (stuckNow.Count > 0) { _settings.Save(); await BuildUpdatesCardAsync(); }
        }
        int okCount = list.Count - failed.Count - stuckNow.Count;
        if (failed.Count == 0 && stuckNow.Count == 0)
            SetStatus(list.Count == 1 ? $"{list[0].Name} обновлена" : $"Программы обновлены ({list.Count})");
        else if (failed.Count == 0)
            SetStatus((okCount > 0 ? $"Обновлено: {okCount}. " : "") + $"{string.Join(", ", stuckNow)}: обновите в самой программе", detectError: false);
        else
            SetStatus("Не получилось обновить: " + string.Join(", ", failed.Concat(stuckNow)));
    }

    private FrameworkElement AppIcon(AppItem a)
    {
        var tones = new[] { "#2F5D57", "#3B4F6B", "#5B4A6E", "#6B4F3A", "#3F5F3F", "#5E3F4A" };
        var nm = a.name ?? "?";
        int h = 0; foreach (var c in nm) h = h * 31 + c;
        var letter = nm.TrimStart('.', ' ').FirstOrDefault(char.IsLetterOrDigit);
        var box = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                               Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tones[Math.Abs(h % tones.Length)])),
                               Child = new TextBlock { Text = char.ToUpper(letter == default ? '?' : letter).ToString(), FontWeight = FontWeights.SemiBold, FontSize = 16,
                                                       Foreground = B("Text"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        Fire(() => LoadIcon(a, box), "Значок программы");
        return box;
    }

    private Border AppRow(AppItem a, bool isInst, string siteLabel, List<(AppItem App, Action MarkDone)> track = null, string installLabel = "Установить", bool removable = false, bool showUninstall = false,
                          HashSet<string> selectableSet = null, Action<CheckBox> register = null)
    {
        var row = new Border { Style = (Style)FindResource("Row") };
        var dp = new DockPanel();
        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        DockPanel.SetDock(btns, Dock.Right);
        // в «Программах» у установленной программы вместо надписи «Установлено» — только кнопка «Удалить»
        var state = new TextBlock { Foreground = B("Ok"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Text = "" };
        btns.Children.Add(state);
        // Spotify ставится своим установщиком, поэтому кнопка есть даже без winget
        bool canAuto = a.loader == "spotify" || (_wingetOk && !string.IsNullOrEmpty(a.winget));
        if (canAuto && !isInst)
        {
            var inst = new Button { Content = installLabel, Style = (Style)FindResource("Outline"), Margin = new Thickness(0, 0, 4, 0) };
            inst.Click += async (_, _) =>
            {
                if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
                try {
                inst.IsEnabled = false; inst.Content = "Устанавливаю…";
                (bool Ok, string Message) r = (false, "");
                QueueStart("Установка", new[] { a.name }, card: a.tab == null);
                QueueSet(0, QState.Now);
                bool ran = await Busy($"Устанавливаю {a.name}…", () => r = Apps.Install(a));
                if (ran) QueueSet(0, r.Ok ? QState.Done : QState.Fail, r.Ok ? null : r.Message);
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                QueueFinish();
                if (!ran) { inst.IsEnabled = true; inst.Content = installLabel; return; }
                if (r.Ok)
                {
                    (a.tab == "runtime" ? _runtimeInstalled : _appsInstalled).Add(a.name);
                    UpdateSelBars();
                    if (showUninstall || removable) { track?.RemoveAll(x => x.App == a); SwapRow(row, AppRow(a, true, siteLabel, track, installLabel, removable, showUninstall, selectableSet, register)); }
                    else { state.Text = "Установлено"; inst.Visibility = Visibility.Collapsed; track?.RemoveAll(x => x.App == a); }
                }
                else { inst.IsEnabled = true; inst.Content = installLabel; Info(string.IsNullOrEmpty(r.Message) ? "Не получилось установить программу." : r.Message); }
                } catch (Exception ex) { Log.Warn($"{a.name}: {ex.Message}"); inst.IsEnabled = true; inst.Content = installLabel; }
            };
            btns.Children.Add(inst);
            // для «Установить всё нужное»: отметить строку установленной снаружи
            track?.Add((a, () => { state.Text = "Установлено"; inst.Visibility = Visibility.Collapsed; }));
        }
        else track?.Add((a, null));
        if (removable && isInst)
        {   // «Компоненты»: вместо надписи «Установлено» — кнопка «Удалить»
            state.Text = "";
            var del = new Button { Content = "Удалить", Style = (Style)FindResource("Ghost"), Margin = new Thickness(0, 0, 4, 0) };
            del.Click += (_, _) => Fire(() => RemoveRuntimeAsync(a, row), "Удаление компонента");
            btns.Children.Add(del);
        }
        if (showUninstall && isInst)
        {   // «Программы»: установлена — «Удалить», нет — «Установить» (после действия строка перерисовывается)
            var del = new Button { Content = "Удалить", Style = (Style)FindResource("Ghost"), Margin = new Thickness(0, 0, 4, 0) };
            del.Click += async (_, _) =>
            {
                if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
                if (!Ask($"Удалить «{a.name}» с компьютера?\n\nЕсли откроется окно удаления программы, следуйте его шагам.", $"Удалить {a.name}?", MessageBoxImage.Warning, "Удалить")) return;
                if (!await ConfirmCloseRunningAsync(a)) return;
                (bool Ok, string Message) r = (false, null);
                bool still = true;
                del.IsEnabled = false; del.Content = "Удаляю…";
                if (!await Busy($"Удаляю {a.name}…", () => { r = Apps.Uninstall(a); still = Apps.Installed(new[] { a }, onDisk: false).Contains(a.name); }))
                { del.IsEnabled = true; del.Content = "Удалить"; return; }
                if (still) Warn(r.Ok || string.IsNullOrEmpty(r.Message) ? $"Не получилось удалить {a.name}. Удалите программу через «Параметры» → «Приложения»." : r.Message);
                else
                {
                    _appsInstalled.Remove(a.name);
                    _runtimeInstalled.Remove(a.name);
                    UpdateSelBars();
                    SetStatus($"Удалено: {a.name}");
                }
                SwapRow(row, AppRow(a, still, siteLabel, track, installLabel, removable, showUninstall, selectableSet, register));
            };
            btns.Children.Add(del);
        }
        if (!string.IsNullOrEmpty(a.url))
        {
            // «Сайт» — редкое действие: прячем в «⋯», чтобы в строке была одна понятная кнопка
            string url = a.url;
            bool onlySite = !btns.Children.OfType<Button>().Any();
            if (onlySite)
            {   // программу Merno не ставит сама — тогда «Сайт» и есть главное действие строки
                var site = new Button { Content = siteLabel, Style = (Style)FindResource("Outline"), ToolTip = url };
                site.Click += (_, _) => Sh.Open(url);
                btns.Children.Add(site);
            }
            else
            {
                var more = new Button { Content = "", FontFamily = (FontFamily)FindResource("Icons"), Style = (Style)FindResource("Ghost"), Padding = new Thickness(9, 7, 9, 7), ToolTip = "Ещё" };
                var menu = new ContextMenu();
                var mi = new MenuItem { Header = siteLabel == "Сайт" ? "Открыть сайт программы" : siteLabel };
                mi.Click += (_, _) => Sh.Open(url);
                menu.Items.Add(mi);
                more.ContextMenu = menu;
                more.Click += (_, _) => { menu.PlacementTarget = more; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; };
                btns.Children.Add(more);
            }
        }
        if (selectableSet != null)
        {
            var cb = new CheckBox
            {
                Style = (Style)FindResource("Check"), IsChecked = selectableSet.Contains(a.name),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
            };
            DockPanel.SetDock(cb, Dock.Left);
            dp.Children.Add(cb);
            // ставится только с сайта (нет winget) — отметить нечего: галочку прячем, место оставляем, чтобы значки стояли ровно
            if (!isInst && !canAuto) cb.Visibility = Visibility.Hidden;
            else
            {
                cb.Checked += (_, _) => OnSelChanged(a.name, selectableSet, true);
                cb.Unchecked += (_, _) => OnSelChanged(a.name, selectableSet, false);
                register?.Invoke(cb);
            }
        }
        var icon = AppIcon(a);
        DockPanel.SetDock(icon, Dock.Left);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 14.5, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis };
        title.Inlines.Add(new System.Windows.Documents.Run(a.name ?? ""));
        if (!string.IsNullOrEmpty(a.author))
            title.Inlines.Add(new System.Windows.Documents.Run("   автор: " + a.author) { FontWeight = FontWeights.Normal, FontSize = 13, Foreground = B("Accent") });
        // «★ Проверено» и «● Установлена» не сжимаются: раньше них ужимается название
        var trusted = a.trusted ? TagText("★ Проверено", "Accent") : null;
        TextBlock instTag = null;
        if (isInst)   // состояние — точкой и словом, не только цветом
        {
            instTag = TagText("");
            instTag.Inlines.Add(new System.Windows.Documents.Run("●") { FontSize = 9, Foreground = B("Ok") });
            instTag.Inlines.Add(new System.Windows.Documents.Run(" Установлена"));
            instTag.Tag = TitleLine.DotOnly;   // в узком окне — только «●», слово в подсказке
        }
        sp.Children.Add(new TitleLine(title, trusted, instTag));
        string sub = !string.IsNullOrEmpty(a.desc) ? a.desc : $"{(string.IsNullOrEmpty(a.version) ? "" : "Версия " + a.version)}";
        // описание в одну строку; не поместилось — «…» раскрывает его целиком
        if (sub.Length > 0) sp.Children.Add(new ExpandText(Typo.Nbsp(sub)) { Margin = new Thickness(0, 3, 0, 0) });
        dp.Children.Add(btns); dp.Children.Add(icon); dp.Children.Add(sp);
        row.Child = dp;
        return row;
    }
}
