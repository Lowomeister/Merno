// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Merno.Core;
using Run = System.Windows.Documents.Run;

namespace Merno;

// «Настройки Windows» (для опытных): как в «Программах» — галочка показывает текущее состояние, а изменения применяются кнопкой «Применить»,
// справа карточка «Отмеченные» с кнопками «Включить» / «Выключить», наборы и разделы.
// Здесь же общие куски для страниц-списков: заголовок группы, ссылка «Разделы», строка с галочкой (SelRow).
public partial class MainWindow
{
    private readonly HashSet<string> _tweakSel = new();
    private readonly List<(string Cat, TextBlock Count, TextBlock Jump, List<Tweak> Items)> _tweakCats = new();

    private void InitTweaks()
    {
        PRec.Click += (_, _) => SetPreset(Catalog.Preset("recommended", _tweaks));
        PGame.Click += (_, _) => SetPreset(Catalog.Preset("gaming", _tweaks));
        PMin.Click += (_, _) => SetPreset(Catalog.Preset("minimal", _tweaks));
        PSave.Click += (_, _) => SavePreset();
        PLoad.Click += (_, _) => LoadPreset();
        TweakMore.Click += (_, _) =>
        {
            var cm = TweakMore.ContextMenu;
            cm.PlacementTarget = TweakMore; cm.Placement = PlacementMode.Bottom; cm.IsOpen = true;
        };
        Search.TextChanged += (_, _) => FilterTweaks();
        foreach (var rb in new[] { TweakFilterAll, TweakFilterChanged, TweakFilterRisk }) rb.Checked += (_, _) => FilterTweaks();
        NarrowToolbar(TweakFilter);
        BtnApply.Click += (_, _) => Fire(ApplyTweaksAsync, "Применение настроек");
        BtnResetSel.Click += (_, _) => { foreach (var (id, cb) in _tweakChecks) if (cb.IsEnabled) cb.IsChecked = TweakOn(id); };
        TweakRollbackLink.Click += (_, _) => NavRollback.IsChecked = true;
        SetOwn(BtnApply, false);
        UpdatePresetButtons();
    }

    // ---------- общее для страниц-списков ----------

    /// <summary>Заголовок группы во всю ширину: «Название · счётчик». Счётчик заполняет вызывающий.</summary>
    private (StackPanel Head, TextBlock Count) ListHeader(string title, bool first)
    {
        // в узкой колонке счётчик уходит на следующую строку, а не за край (HeaderLine)
        var sp = new StackPanel { Margin = new Thickness(4, first ? 4 : 24, 0, 8) };
        var cnt = new TextBlock { FontSize = 12.5, Foreground = B("Sub"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) };
        sp.Children.Add(HeaderLine(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), VerticalAlignment = VerticalAlignment.Bottom, TextWrapping = TextWrapping.Wrap }, cnt));
        return (sp, cnt);
    }

    /// <summary>
    /// Полоса «поиск + фильтр справа». Если поиску осталось меньше SearchMin, фильтр уходит под поиск:
    /// иначе в окне 860 px поле сжималось до «Найти настр». Решаем по ширине полосы, а не по месту поиска —
    /// иначе перенос освобождал бы место и полоса дёргалась.
    /// </summary>
    private static void NarrowToolbar(FrameworkElement filter, double searchMin = 220)
    {
        if (filter.Parent is not DockPanel bar) return;
        double height = bar.Height;
        void Sync()
        {
            filter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            bool narrow = bar.ActualWidth > 0 && bar.ActualWidth - filter.DesiredSize.Width - 12 < searchMin;
            var dock = narrow ? Dock.Bottom : Dock.Right;
            if (DockPanel.GetDock(filter) == dock) return;
            DockPanel.SetDock(filter, dock);
            filter.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(12, 0, 0, 0);
            filter.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            // поиску — прежняя высота, полосе — сколько нужно на две строки
            if (bar.Children.Count > 0 && bar.Children[bar.Children.Count - 1] is FrameworkElement search) search.Height = narrow ? height : double.NaN;
            bar.Height = narrow ? double.NaN : height;
        }
        bar.SizeChanged += (_, _) => Sync();
        filter.SizeChanged += (_, _) => Sync();
    }

    /// <summary>
    /// Строка-плашка «значок · текст · элементы справа». Если тексту остаётся меньше textMin, элементы справа
    /// уходят под текст (значок слева остаётся, поэтому они встают ровно под описанием).
    /// </summary>
    private static void NarrowRow(FrameworkElement side, double textMin = 260)
    {
        if (side.Parent is not DockPanel row) return;
        var wide = side.Margin;
        void Sync()
        {
            side.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // только явно прижатые влево (значок): у текста Dock не задан, а по умолчанию он тоже «Left»
            double left = row.Children.OfType<FrameworkElement>().Where(c => c != side && c.ReadLocalValue(DockPanel.DockProperty) != DependencyProperty.UnsetValue
                                                                              && DockPanel.GetDock(c) == Dock.Left)
                                      .Sum(c => c.ActualWidth + c.Margin.Left + c.Margin.Right);
            bool narrow = row.ActualWidth > 0 && row.ActualWidth - left - side.DesiredSize.Width - wide.Left - wide.Right < textMin;
            var dock = narrow ? Dock.Bottom : Dock.Right;
            if (DockPanel.GetDock(side) == dock) return;
            DockPanel.SetDock(side, dock);
            side.Margin = narrow ? new Thickness(0, 10, 0, 0) : wide;
            side.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        }
        row.SizeChanged += (_, _) => Sync();
    }

    /// <summary>
    /// «Название · счётчик». Не влезает в строку — счётчик переносится вниз, а точка-разделитель прячется,
    /// чтобы вторая строка не начиналась с «·». Ширину считаем по размерам, а не по положению —
    /// иначе спрятанная точка снова дала бы место и строка дёргалась.
    /// </summary>
    private WrapPanel HeaderLine(TextBlock title, TextBlock cnt)
    {
        var dot = new TextBlock { Text = "  ·  ", FontSize = 12.5, Foreground = B("Sub"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) };
        var wp = new WrapPanel();
        wp.Children.Add(title); wp.Children.Add(dot); wp.Children.Add(cnt);
        void Sync()
        {
            if (wp.ActualWidth <= 0) return;
            var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            double minW = cnt.MinWidth;
            cnt.MinWidth = 0;   // естественная ширина счётчика, без растяжки
            title.Measure(inf); dot.Measure(inf); cnt.Measure(inf);
            double cntW = cnt.DesiredSize.Width;
            bool fits = title.DesiredSize.Width + dot.DesiredSize.Width + cntW <= wp.ActualWidth + 0.5;
            var v = fits && cntW >= 1 ? Visibility.Visible : Visibility.Collapsed;
            if (dot.Visibility != v) dot.Visibility = v;
            // не влезает — счётчик во всю ширину, чтобы точно встал на новую строку, а не прилип к названию без точки
            double want = fits ? 0 : wp.ActualWidth;
            cnt.MinWidth = Math.Abs(minW - want) < 0.5 ? minW : want;
        }
        wp.SizeChanged += (_, _) => Sync();
        cnt.SizeChanged += (_, _) => Sync();
        return wp;
    }

    /// <summary>Строка в карточке «Разделы»: название и число справа; щелчок прокручивает список к группе.</summary>
    private FrameworkElement SideJump(string title, FrameworkElement target, out TextBlock right)
    {
        var dp = new DockPanel();
        var n = new TextBlock { Foreground = B("Sub"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(n, Dock.Right);
        dp.Children.Add(n);
        dp.Children.Add(new TextBlock { Text = title, FontSize = 13.5, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var b = new Border { Child = dp, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(-10, 0, -10, 2), CornerRadius = new CornerRadius(6),
                             Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        b.MouseEnter += (_, _) => b.Background = B("CardHover");
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, _) =>
        {
            if (target.Parent is FrameworkElement list && list.Parent is ScrollViewer sv && target.IsVisible)
            {
                var p = target.TransformToAncestor(sv).Transform(new Point(0, 0));
                sv.ScrollToVerticalOffset(sv.VerticalOffset + p.Y - 4);
            }
            else target.BringIntoView();
        };
        right = n;
        return b;
    }

    /// <summary>Метка рядом с названием (для TitleLine: не сжимается, название ужимается раньше неё).</summary>
    private Border TagChip(string text, string brush, string tip = null)
    {
        var c = Chip(text, brush);
        c.Margin = new Thickness(8, 0, 0, 0);
        c.ToolTip = tip;
        return c;
    }

    /// <summary>Короткая надпись рядом с названием: состояние («● Установлена»).</summary>
    private TextBlock TagText(string text, string brush = "Sub", double size = 12.5) =>
        new() { Text = text, FontSize = size, Foreground = B(brush), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };

    /// <summary>Метка «● Включено» у названия — как «● Установлена» у программ.</summary>
    private TextBlock OnTag(string text = "Включено")
    {
        var tb = TagText("");
        tb.Inlines.Add(new Run("●") { FontSize = 9, Foreground = B("Ok") });
        tb.Inlines.Add(new Run(" " + text));
        return tb;
    }

    /// <summary>Строка списка с галочкой выбора — общий вид «Настроек Windows», «Автозагрузки» и «Контекстного меню»:
    /// [галочка] [значок] название + метки, ниже описание (не влезло — «ещё»), справа «⋯». Щелчок по строке ставит/снимает галочку.</summary>
    private Border SelRow(FrameworkElement icon, UIElement titleLine, string desc, CheckBox cb, Button more = null, object descTip = null)
    {
        var row = new Border { Style = (Style)FindResource("Plate") };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cb.Style = (Style)FindResource("Check");
        cb.VerticalAlignment = VerticalAlignment.Center;
        cb.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(cb, 0);
        Grid.SetColumn(icon, 1);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(sp, 2);
        sp.Children.Add(titleLine);
        if (!string.IsNullOrWhiteSpace(desc))
            sp.Children.Add(new ExpandText(Typo.Nbsp(desc)) { Margin = new Thickness(0, 3, 0, 0), ToolTip = descTip });
        g.Children.Add(cb); g.Children.Add(icon); g.Children.Add(sp);
        if (more != null) { Grid.SetColumn(more, 3); g.Children.Add(more); }
        row.Child = g;
        row.Cursor = cb.IsEnabled ? System.Windows.Input.Cursors.Hand : null;
        cb.IsEnabledChanged += (_, _) => row.Cursor = cb.IsEnabled ? System.Windows.Input.Cursors.Hand : null;
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (!cb.IsEnabled) return;
            if (e.OriginalSource is DependencyObject d && (IsInside(d, cb) || (more != null && IsInside(d, more)))) return;
            cb.IsChecked = cb.IsChecked != true;
        };
        return row;
    }

    /// <summary>Кнопка «⋯» с выпадающим меню.</summary>
    private Button MoreButton(ContextMenu menu)
    {
        var more = new Button { Content = "", FontFamily = (FontFamily)FindResource("Icons"), Style = (Style)FindResource("Ghost"), Padding = new Thickness(9, 7, 9, 7),
                                Margin = new Thickness(8, 0, -4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Ещё" };
        more.ContextMenu = menu;
        more.Click += (_, _) => { menu.PlacementTarget = more; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; };
        return more;
    }

    // ---------- список настроек ----------
    private async Task BuildTweaksAsync()
    {
        int gen = ++_genTweaks;
        try
        {
        var s = _ctx.S;
        bool chosen = false;
        var state = await Task.Run(() =>
        {
            // копии отката читаются с диска — не в потоке окна
            try { chosen = AdvicePolicy.TweaksChosen(Backup.List().Select(b => b.Data)); } catch { }
            return _tweaks.ToDictionary(t => t.Id, t =>
            {
                bool ok = false, applied = false;
                try { ok = t.Applicable(s); applied = ok && t.IsApplied(_ctx); } catch (Exception ex) { Log.Skip($"{t.Name}: {ex.Message}"); }
                return (ok, applied);
            });
        });
        if (gen != _genTweaks) return;
        _state = state;
        if (_homeChecked)
        {
            // chosen — уже выбирал сам, остальное не навязываем
            _adviceTodo = chosen ? new() : _tweaks.Where(t => t.Rec && state.TryGetValue(t.Id, out var value) && value.ok && !value.applied).Select(t => t.Id).ToList();
            var gameIds = Catalog.Preset("gaming", _tweaks).ToHashSet();
            _adviceGameTodo = chosen ? new() : _tweaks.Where(t => gameIds.Contains(t.Id) && !t.Rec && t.Risk < 2 && t.CanRevert && state.TryGetValue(t.Id, out var value) && value.ok && !value.applied).Select(t => t.Id).ToList();
            RenderCheckAdvice();
        }
        TweakList.Children.Clear(); _tweakChecks.Clear(); _tweakRows.Clear(); _catHeaders.Clear(); _tweakCats.Clear();
        TweakJump.Children.Clear();
        bool first = true;
        foreach (var cat in Catalog.Categories)
        {
            var items = _tweaks.Where(t => t.Cat == cat).ToList();
            if (items.Count == 0) continue;
            var (head, cnt) = ListHeader(cat, first); first = false;
            TweakList.Children.Add(head); _catHeaders[cat] = head;
            var jump = SideJump(cat, head, out var jr);
            TweakJump.Children.Add(jump);
            head.Tag = jump;   // ссылка прячется вместе с заголовком, когда фильтр не оставил строк
            _tweakCats.Add((cat, cnt, jr, items));
            foreach (var t in items) TweakList.Children.Add(TweakRow(t, state[t.Id].ok, state[t.Id].applied));
        }
        FilterTweaks();
        UpdateCount();
        }
        catch (Exception ex)
        {
            Log.Warn("Список настроек: " + ex.Message); Log.Skip(ex.ToString());
            if (gen == _genTweaks) { _tweakChecks.Clear(); _tweakRows.Clear(); _catHeaders.Clear(); _tweakCats.Clear(); ShowError(TweakList, "Не получилось загрузить настройки.", BuildTweaksAsync); }
        }
    }

    /// <summary>Значок раздела для плитки слева (как у окон в «Быстром доступе»).</summary>
    private static string TweakGlyph(string cat) => cat switch
    {
        "Производительность" => "", "Игры" => "", "Конфиденциальность" => "", "Интерфейс" => "",
        "Удобство" => "", "Проводник" => "", "Службы" => "", "Обновления" => "",
        "Защита Windows" => "", "Приложения" => "", _ => ""
    };

    /// <summary>Строка настройки: галочка показывает текущее состояние, изменения применяются кнопкой «Применить».</summary>
    private Border TweakRow(Tweak t, bool ok, bool applied)
    {
        var tile = new Border { Style = (Style)FindResource("IconTile"),
                                Child = new TextBlock { Text = TweakGlyph(t.Cat), Style = (Style)FindResource("IconGlyph") } };
        // название ужимается «…» первым; за ним метки: плашки риска, серый текст — прочее
        var title = new TextBlock { Text = t.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis };
        // коротко: длинное «не подходит этому компьютеру» обрезало название даже на 1920
        TextBlock notFit = null;
        if (!ok) { notFit = TagText("не подходит"); notFit.ToolTip = "Не подходит этому компьютеру: не действует в этой версии, редакции или с этими настройками Windows"; }
        var line = new TitleLine(title,
            t.Risk == 1 ? TagChip("Осторожно", "Warn") : null,
            t.Risk == 2 ? TagChip("Риск", "Bad", "Может сломать Windows") : null,
            notFit);
        // галочка = состояние; изменения — кнопкой «Применить». Сделанное без отмены (удалённые приложения) снять нельзя
        bool keep = _tweakSel.Contains(t.Id);   // после перестройки списка ручной выбор сохраняется
        var cb = new CheckBox { IsEnabled = ok && (!applied || t.CanRevert), IsChecked = keep ? !applied : applied,
                                ToolTip = applied && !t.CanRevert ? "Уже сделано. Отменить кнопкой нельзя" : null };
        void Changed() { if (cb.IsChecked != applied) _tweakSel.Add(t.Id); else _tweakSel.Remove(t.Id); UpdateCount(); }
        cb.Checked += (_, _) => Changed();
        cb.Unchecked += (_, _) => Changed();
        // «Подробнее» — только когда есть что добавить к описанию под названием, а не повторять его
        var menu = new ContextMenu();
        string details = TweakDetails(t);
        if (details.Length > 0)
        {
            var mi = new MenuItem { Header = "Подробнее" };
            mi.Click += (_, _) => Info(details, t.Name);
            menu.Items.Add(mi);
        }
        var more = MoreButton(menu);
        if (menu.Items.Count == 0) more.Visibility = Visibility.Hidden;   // место остаётся — строки стоят ровно
        var row = SelRow(tile, line, t.Desc, cb, more);
        if (!ok) row.Opacity = 0.5;
        _tweakChecks[t.Id] = cb; _tweakRows[t.Id] = row;
        return row;
    }

    /// <summary>Текст для «Подробнее»: только то, чего нет в строке. Описание, «не подходит» и плашка «Осторожно»
    /// уже видны — их не повторяем. Пусто — кнопки «⋯» у строки нет.</summary>
    private static string TweakDetails(Tweak t) => string.Join("\n\n", new[] {
        t.Hint == t.Desc ? null : t.Hint,
        t.Reboot ? "Нужна перезагрузка." : null,
        t.Risk == 2 ? "Может сломать Windows. Только если знаете, что делаете." : null,
        t.CanRevert ? null
            : t.Id == "edge_remove" ? "Отменить кнопкой нельзя: Edge можно скачать заново с сайта Microsoft."
            : t.Id == "onedrive" ? "Отменить кнопкой нельзя: OneDrive можно скачать заново с сайта Microsoft."
            : "Отменить кнопкой нельзя: удалённые приложения возвращаются через «Программы» → «Приложения Windows»."
    }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Typo.Nbsp));

    /// <summary>Карточка «Изменения», счётчики разделов, фильтров и наборов.</summary>
    private void UpdateCount()
    {
        var (on, off) = Pending();
        int n = on.Count + off.Count;
        TweakCount.Text = n == 0 ? "Пока ничего не изменено. Windows изменится только после «Применить»."
            : string.Join(" · ", new[] { on.Count > 0 ? $"включить: {on.Count}" : null, off.Count > 0 ? $"выключить: {off.Count}" : null }.Where(x => x != null));
        TweakRebootNote.Visibility = on.Concat(off).Any(t => t.Reboot) ? Visibility.Visible : Visibility.Collapsed;
        TweakOptions.Visibility = on.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnApply.Content = n > 0 ? $"Применить ({n})" : "Применить";
        BtnApply.Visibility = Visibility.Visible;
        SetOwn(BtnApply, n > 0);
        BtnResetSel.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (_, cnt, jr, items) in _tweakCats)
        {
            int applied = items.Count(t => _state.TryGetValue(t.Id, out var st) && st.applied);
            int sel = items.Count(t => _tweakSel.Contains(t.Id));
            cnt.Inlines.Clear();
            cnt.Inlines.Add(new Run($"включено {applied} из {items.Count}"));
            if (sel > 0) cnt.Inlines.Add(new Run($"  ·  изменено {sel}") { Foreground = B("Accent") });
            jr.Text = $"{applied}/{items.Count}";
            jr.Foreground = sel > 0 ? B("Accent") : B("Sub");
        }
        TweakFilterAll.Content = $"Все · {_tweakRows.Count}";
        TweakFilterChanged.Content = n > 0 ? $"Изменено · {n}" : "Изменено";
        TweakFilterRisk.Content = $"Осторожно · {_tweaks.Count(t => t.Risk >= 1 && _tweakRows.ContainsKey(t.Id))}";
        if (n == 0 && TweakFilterChanged.IsChecked == true) TweakFilterAll.IsChecked = true;
        UpdatePresetButtons();
    }

    private bool TweakOn(string id) => _state.TryGetValue(id, out var st) && st.applied;

    /// <summary>Кнопки наборов: название, пояснение и сколько набор ещё отметит.</summary>
    private void UpdatePresetButtons()
    {
        foreach (var (b, name, desc, id) in new[] {
            (PRec, "Обычный", "Безопасные улучшения", "recommended"),
            (PGame, "Игровой", "Больше FPS, меньше задержек", "gaming"),
            (PMin, "Бережный", "Только самое безопасное", "minimal") })
        {
            int? add = null; bool allOn = false;
            if (_tweakChecks.Count > 0)
            {
                var ids = Catalog.Preset(id, _tweaks).Where(x => _tweakChecks.TryGetValue(x, out var cb) && cb.IsEnabled && !TweakOn(x)).ToList();
                allOn = ids.Count == 0;
                add = ids.Count(x => !_tweakSel.Contains(x));
            }
            var dp = new DockPanel();
            if (add != null)
            {
                var right = new TextBlock { Text = add > 0 ? $"+{add}" : "✓", Foreground = add > 0 ? B("Accent") : B("Ok"), FontWeight = FontWeights.SemiBold,
                                            FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(right, Dock.Right);
                dp.Children.Add(right);
            }
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 13.5, Foreground = B("Text") });
            sp.Children.Add(new TextBlock { Text = allOn ? "Уже всё включено" : add == 0 ? "Уже отмечено" : desc, FontSize = 12, Foreground = B("Sub"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
            dp.Children.Add(sp);
            b.Content = dp;
            b.IsEnabled = add != 0;
            b.ToolTip = add > 0 ? $"Отметит ещё {add} {Plural(add.Value, "настройку", "настройки", "настроек")}." : null;
        }
    }

    /// <summary>Отмеченное: что включить (сейчас выключено) и что выключить (сейчас включено и можно отменить).</summary>
    private (List<Tweak> On, List<Tweak> Off) Pending()
    {
        var on = new List<Tweak>(); var off = new List<Tweak>();
        foreach (var t in _tweaks)
        {
            if (!_tweakSel.Contains(t.Id) || !_tweakChecks.TryGetValue(t.Id, out var cb) || !cb.IsEnabled) continue;
            if (!TweakOn(t.Id)) on.Add(t);
            else if (t.CanRevert) off.Add(t);
        }
        return (on, off);
    }

    /// <summary>«Например: «А», «Б». » — первые два названия для текста совета.</summary>
    private string TweakExamples(IEnumerable<string> ids)
    {
        var names = ids.Select(id => _tweaks.FirstOrDefault(t => t.Id == id)?.Name).Where(n => !string.IsNullOrEmpty(n)).Take(2).ToList();
        return names.Count == 0 ? "" : "Например: " + string.Join(", ", names.Select(n => "«" + n + "»")) + ". ";
    }

    /// <summary>
    /// «Где исправить» у советов о настройках: открыть «Настройки Windows», отметить именно эти пункты
    /// и показать только их — останется нажать «Применить». Страница — для опытных, поэтому сначала спрашиваем.
    /// </summary>
    private void OpenTweakAdvice(IReadOnlyCollection<string> ids)
    {
        if (!_settings.Advanced)
        {
            if (!ConfirmAdvanced()) return;
            SetAdvancedMode(true);
        }
        foreach (var id in ids) if (!TweakOn(id)) _tweakSel.Add(id);   // если строки ещё не построены — галочки встанут при построении
        SetPreset(ids);
        ShowPageByName("PageTweaks");
        UpdateCount();
        TweakFilterChanged.IsChecked = true;
        FilterTweaks();
        var first = ids.Select(id => _tweakRows.TryGetValue(id, out var r) ? r : null).FirstOrDefault(r => r != null && r.Visibility == Visibility.Visible);
        if (first != null) Highlight(first, "Отмеченные настройки ещё не включены. Проверьте их и нажмите «Применить».");
        else SetStatus("Отмечено в «Настройках Windows». Проверьте и нажмите «Применить».");
    }

    /// <summary>Набор отмечает свои выключенные настройки; уже отмеченное не снимается.</summary>
    private void SetPreset(IEnumerable<string> ids)
    {
        foreach (var id in ids)
            if (_tweakChecks.TryGetValue(id, out var cb) && cb.IsEnabled && !TweakOn(id)) cb.IsChecked = true;
    }

    /// <summary>Поиск + фильтр «Все / Отмечено / Осторожно». Пустые разделы прячутся вместе со ссылкой в «Разделах».</summary>
    private void FilterTweaks()
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        string q = Search.Text.Trim();
        int mode = TweakFilterChanged.IsChecked == true ? 1 : TweakFilterRisk.IsChecked == true ? 2 : 0;
        foreach (var t in _tweaks)
        {
            if (!_tweakRows.TryGetValue(t.Id, out var row)) continue;
            bool vis = q.Length == 0 || t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || (t.Desc ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
            if (mode == 1) vis &= _tweakSel.Contains(t.Id);
            if (mode == 2) vis &= t.Risk >= 1;
            row.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var (cat, h) in _catHeaders)
        {
            bool any = _tweaks.Any(t => t.Cat == cat && _tweakRows.TryGetValue(t.Id, out var r) && r.Visibility == Visibility.Visible);
            h.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            if (h.Tag is FrameworkElement jump) jump.Visibility = h.Visibility;
        }
    }

    private void SavePreset()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Набор Merno (*.json)|*.json", FileName = "Мой набор.json" };
        if (dlg.ShowDialog(this) != true) return;
        var ids = _tweakChecks.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToArray();
        try
        {
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(new { app = "Merno", version = App.Version, tweaks = ids }, new JsonSerializerOptions { WriteIndented = true }));
            Log.Ok("Набор сохранён: " + System.IO.Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { Log.Warn("Сохранение набора: " + ex.Message); Warn("Не получилось сохранить файл. Выберите другую папку."); }
    }

    private void LoadPreset()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Набор Merno (*.json)|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(dlg.FileName));
            var ids = doc.RootElement.GetProperty("tweaks").EnumerateArray().Select(x => x.GetString()).ToList();
            SetPreset(ids);
            Log.Ok("Набор загружен: " + System.IO.Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { Log.Warn("Загрузка набора: " + ex.Message); Warn("Не получилось открыть файл. Похоже, это не набор Merno."); }
    }

    /// <summary>«Применить»: включить новые галочки, выключить снятые.</summary>
    private async Task ApplyTweaksAsync()
    {
        if (_busy) return;
        var (sel, undo) = Pending();
        if (sel.Count + undo.Count == 0) { SetStatus("Изменений нет"); return; }
        var irreversible = sel.Where(t => !t.CanRevert).ToList();
        if (irreversible.Count > 0 && !Ask("Эти действия нельзя отменить кнопкой отката:\n\n" + string.Join("\n", irreversible.Select(t => "• " + t.Name)) + "\n\nПродолжить?", "Merno", MessageBoxImage.Warning, "Продолжить")) return;
        var risky = sel.Where(t => t.Risk == 2).ToList();
        if (risky.Count > 0 && !Ask("Эти настройки могут сломать Windows:\n\n" + string.Join("\n", risky.Select(r => "• " + r.Name)) + "\n\nВключить всё равно?", "Merno", MessageBoxImage.Warning, "Включить")) return;
        // точка восстановления — только если среди изменений есть «Осторожно»/«Риск»: обычные настройки и так
        // откатываются из копии Merno, а точка — это минута ожидания и место на диске
        bool rp = TweakRP.IsChecked == true && sel.Any(t => t.Risk >= 1), rep = TweakReport.IsChecked == true;
        var applied = new List<Tweak>(); var reverted = new List<string>(); var failed = new List<string>();
        // полоска внизу: сначала то, что выключается, потом то, что включается
        QueueStart("Настройки Windows", undo.Select(t => "Выключить: " + t.Name).Concat(sel.Select(t => t.Name)));
        bool started = await Busy("Применяю изменения…", () =>
        {
            if (rp && !Backup.CreateRestorePoint()) throw new IOException("Точка восстановления не создана. Применение остановлено; проверьте защиту системы или снимите эту галочку.");
            int qi = 0;
            foreach (var t in undo)
            {
                QueueSet(qi, QState.Now);
                var (ok, msg) = Backup.RevertTweak(t, _ctx);
                QueueSet(qi++, ok ? QState.Done : QState.Fail, msg);
                if (ok) reverted.Add(t.Name); else failed.Add($"{t.Name}: {msg}");
            }
            if (sel.Count > 0)
            {
                Log.Head($"Включение: {sel.Count} {Plural(sel.Count, "настройка", "настройки", "настроек")}");
                using var backupSession = Backup.Session();
                foreach (var t in sel)
                {
                    QueueSet(qi, QState.Now);
                    try { t.Apply(_ctx); applied.Add(t); Log.Ok(t.Name); QueueSet(qi, QState.Done); }
                    catch (Exception ex) { Log.Warn($"{t.Name}: {ex.Message}"); QueueSet(qi, QState.Fail, ForPerson(ex, "Не получилось включить")); }
                    qi++;
                }
                Backup.Save();
                _ctx.RefreshAppx();
                if (rep) _lastReport = Report.Write(_ctx.S, applied, 0);
            }
            Log.Head("Готово");
        });
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        QueueFinish();
        if (!started) return;
        foreach (var t in sel.Concat(undo)) _tweakSel.Remove(t.Id);
        // список перестраивается на месте: прокрутка и фильтр остаются
        double offset = TweakScroll.VerticalOffset;
        await BuildTweaksAsync();
        _ = Dispatcher.BeginInvoke(() => TweakScroll.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
        UpdatePlan();
        if (_ready) BuildStartup();
        if (failed.Count > 0)
            Info("Не всё получилось выключить:\n\n" + string.Join("\n", failed));
        if (applied.Any(t => t.Reboot) || applied.Count + reverted.Count > 3) AskReboot();
        else if (applied.Count + reverted.Count > 0) SetStatus("Готово. Часть изменений заработает после перезагрузки.");
    }
}
