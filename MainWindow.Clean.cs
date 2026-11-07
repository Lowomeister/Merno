// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Merno.Core;
using Run = System.Windows.Documents.Run;

namespace Merno;

// «Очистка»: пункты с галочками в двух группах, размер считается сам при открытии (в фоне, без «занятости»),
// справа — сколько можно освободить, кнопка «Очистить» и свободное место на системном диске.
public partial class MainWindow
{
    /// <summary>Подписи в интерфейсе: короткое имя, метка и пояснение. Имя из Core остаётся для журнала.</summary>
    private static readonly Dictionary<string, (string Name, string Chip, string ChipBrush, string Hint)> CleanUi = new()
    {
        ["tmp_user"] = ("Временные файлы программ", null, null, "Мусор, который программы забыли за собой убрать."),
        ["tmp_win"] = ("Временные файлы Windows", null, null, "Windows создаёт их на время и забывает удалить."),
        ["wu"] = ("Скачанные обновления Windows", null, null, null),
        ["do"] = ("Запасные файлы обновлений", null, null, "Копии обновлений для других компьютеров в сети."),
        ["dumps"] = ("Отчёты о сбоях программ", null, null, "Только старше 7 дней. Могут понадобиться для поиска причин сбоев."),
        ["thumbs"] = ("Миниатюры картинок", null, null, "Создадутся заново сами."),
        ["leftovers"] = ("Остатки удалённых программ", null, null, "Могут содержать настройки и сохранения. Удалённые папки нельзя вернуть через откат."),
        ["logs"] = ("Старые журналы Windows", null, null, "Только старше 7 дней. Могут понадобиться для диагностики Windows."),
        ["shader"] = ("Временные файлы видеокарты", null, null, null),
        ["recycle"] = ("Корзина", "Навсегда", "Warn", "Файлы, которые вы уже удалили."),
        ["winsxs"] = ("Остатки старых обновлений Windows", "До получаса", "Sub", null),
    };

    private readonly ConcurrentDictionary<string, DateTime> _cleanMeasuredAt = new();
    private readonly Dictionary<string, Panel> _cleanSizeBox = new();
    private bool _cleanMeasuring, _cleanDone, _cleaning;
    private int _cleanMeasureGen;

    private void InitClean()
    {
        BtnScan.Click += (_, _) => Fire(ScanAsync, "Очистка");
        BtnClean.Click += (_, _) => Fire(CleanAsync, "Очистка");
        CleanLogLink.Click += (_, _) => NavLog.IsChecked = true;
        UpdateCleanDisk();
    }

    /// <summary>Страницу открыли: пересортировать (пока ничего не считается) и досчитать устаревшие размеры.</summary>
    private void OnCleanShown()
    {
        if (!_cleanMeasuring && !_cleaning && !_busy && _cleanChecks.Count > 0) BuildCleanList(keepChecks: true);
        UpdateCleanDisk();
        Fire(() => MeasureCleanAsync(false), "Очистка: размер");
    }

    private void BuildCleanList(bool keepChecks = false)
    {
        var was = keepChecks ? _cleanChecks.ToDictionary(kv => kv.Key, kv => kv.Value.IsChecked == true) : new Dictionary<string, bool>();
        foreach (var box in _cleanSizeBox.Values) StopSpinners(box);
        CleanList.Children.Clear(); _cleanChecks.Clear(); _cleanSizes.Clear(); _cleanSizeBox.Clear();
        // порядок постоянный: строки не прыгают, когда досчитался размер
        var ordered = _clean;
        bool first = true;
        // рискованное и «по ситуации» (корзина, файлы видеокарты) — отдельной группой, без лишних слов
        foreach (var (def, title) in new[] { (true, "Можно смело удалить"), (false, "По желанию") })
        {
            var items = ordered.Where(c => c.Default == def).ToList();
            if (items.Count == 0) continue;
            var (head, cnt) = ListHeader(title, first); first = false;
            cnt.Text = $"{items.Count}";
            CleanList.Children.Add(head);
            foreach (var c in items) CleanList.Children.Add(CleanRow(c, was.TryGetValue(c.Id, out var on) ? on : c.Default));
        }
        HighlightBiggestClean();
        UpdateCleanSummary();
    }

    private static string CleanName(CleanTarget c) => CleanUi.TryGetValue(c.Id, out var ui) && ui.Name != null ? ui.Name : c.Name;

    /// <summary>Строка: галочка · название и метка · пояснение в одну строку · размер справа.</summary>
    private Border CleanRow(CleanTarget c, bool isChecked)
    {
        CleanUi.TryGetValue(c.Id, out var ui);
        string hint = ui.Hint ?? c.Hint ?? "";
        var row = new Border { Style = (Style)FindResource("Row"), Cursor = System.Windows.Input.Cursors.Hand };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cb = new CheckBox { Style = (Style)FindResource("Check"), IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center };
        cb.Checked += (_, _) => UpdateCleanSummary();
        cb.Unchecked += (_, _) => UpdateCleanSummary();
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
        Grid.SetColumn(sp, 1);
        var title = new TextBlock { Text = CleanName(c), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis };
        // метка («Навсегда», «До 10 минут») не сжимается: раньше неё ужимается название
        sp.Children.Add(new TitleLine(title, ui.Chip != null ? TagChip(ui.Chip, ui.ChipBrush) : null));
        if (hint.Length > 0)
            sp.Children.Add(new ExpandText(Typo.Nbsp(hint)) { Margin = new Thickness(0, 3, 0, 0) });
        var box = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(box, 2);
        var size = new TextBlock { FontFamily = (FontFamily)FindResource("Display"), FontSize = 14, Foreground = B("Sub"), VerticalAlignment = VerticalAlignment.Center,
                                   TextAlignment = TextAlignment.Right, MinWidth = 88 };
        box.Children.Add(size);
        g.Children.Add(cb); g.Children.Add(sp); g.Children.Add(box);
        row.Child = g;
        row.MouseLeftButtonUp += (_, e) => { if (!(e.OriginalSource is FrameworkElement fe && IsInside(fe, cb))) cb.IsChecked = cb.IsChecked != true; };
        _cleanChecks[c.Id] = cb; _cleanSizes[c.Id] = size; _cleanSizeBox[c.Id] = box;
        SetCleanSize(c);
        return row;
    }

    /// <summary>Размер в строке: «…» (считаем), «—» (заранее не узнать), «Пусто» или число.</summary>
    private void SetCleanSize(CleanTarget c)
    {
        if (!_cleanSizeBox.TryGetValue(c.Id, out var box) || !_cleanSizes.TryGetValue(c.Id, out var tb)) return;
        StopSpinners(box);
        foreach (var x in box.Children.OfType<UIElement>().Where(x => x != tb).ToList()) box.Children.Remove(x);
        tb.Visibility = Visibility.Visible; tb.ToolTip = null;
        if (c.Size == -2)
        {
            tb.Text = "…";
            if (_cleanMeasuring) box.Children.Insert(0, Spinner());
        }
        else if (c.Size == -1) { tb.Text = "неизвестно"; tb.ToolTip = "Размер заранее не узнать"; }
        // записи в списке программ места почти не занимают — показываем, сколько их
        else if (c.Size == 0 && c.Count > 0) { tb.Text = $"{c.Count} {Plural(c.Count, "запись", "записи", "записей")}"; tb.ToolTip = "Программы, которых уже нет, но Windows их показывает"; }
        else if (c.Size == 0) tb.Text = "Пусто";
        else tb.Text = Sh.Size(c.Size);
    }

    /// <summary>Маленький крутящийся значок 16 px.</summary>
    private TextBlock Spinner()
    {
        var rt = new RotateTransform();
        var t = new TextBlock { Text = "\uE895", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12, Foreground = B("Sub"), Width = 16, Height = 16,
                                TextAlignment = TextAlignment.Center, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = rt };
        rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
        return t;
    }
    private static void StopSpinners(Panel box)
    {
        foreach (var x in box.Children.OfType<FrameworkElement>())
            if (x.RenderTransform is RotateTransform rt) rt.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    /// <summary>Три самых больших пункта (от 100 МБ) — размер акцентным цветом и жирным.</summary>
    private void HighlightBiggestClean()
    {
        var big = _clean.Where(c => c.Size >= 100L << 20).OrderByDescending(c => c.Size).Take(3).Select(c => c.Id).ToHashSet();
        foreach (var (id, tb) in _cleanSizes)
        {
            if (tb.Text.StartsWith("✓")) continue;   // уже почищено — зелёная отметка остаётся
            bool on = big.Contains(id);
            tb.Foreground = B(on ? "Accent" : "Sub");
            tb.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    /// <summary>Живая сводка справа: сумма отмеченных посчитанных пунктов и подпись кнопки.</summary>
    private void UpdateCleanSummary()
    {
        int sel = 0; long sum = 0; bool any = false;
        foreach (var c in _clean)
        {
            if (!_cleanChecks.TryGetValue(c.Id, out var cb) || cb.IsChecked != true) continue;
            sel++;
            if (c.Size >= 0) { any = true; sum += c.Size; }
        }
        if (!_cleanDone)
        {
            CleanTotalLabel.Text = "Можно освободить";
            CleanTotal.Foreground = B("Text");
            // ничего не отмечено: освобождать нечего; отмечены только пункты без известного размера: неизвестно
            CleanTotal.Text = any ? Sh.Size(sum) : _cleanMeasuring || _clean.Any(c => c.Size == -2) ? "Считаю…" : sel == 0 ? "0 КБ" : "неизвестно";
            CleanLogLink.Visibility = Visibility.Collapsed;
        }
        CleanSelInfo.Text = sel == 0 ? "Отметьте, что удалить" : $"Отмечено {sel} из {_cleanChecks.Count}";
        if (!_cleaning) BtnClean.Content = any && sum > 0 ? $"Очистить ({Sh.Size(sum)})" : "Очистить отмеченное";
        SetOwn(BtnClean, sel > 0);
    }

    /// <summary>Свободное место на системном диске: строка и полоска 4 px.</summary>
    private void UpdateCleanDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            var d = new DriveInfo(root);
            long total = d.TotalSize, free = d.AvailableFreeSpace;
            bool low = total > 0 && free < total / 10;
            CleanDiskTitle.Text = "Диск " + root.TrimEnd('\\');
            CleanFreeDisk.Text = Sh.FreeOf(free, total, true) + (low ? ". Места мало. Очистка может освободить часть места." : "");
            CleanFreeDisk.Foreground = low ? B("Warn") : B("Sub");
            CleanDiskUsed.Width = new GridLength(Math.Max(0, total - free), GridUnitType.Star);
            CleanDiskFree.Width = new GridLength(Math.Max(1, free), GridUnitType.Star);
            CleanDiskBar.Background = low ? B("Warn") : B("Accent");
        }
        catch (Exception ex) { Log.Skip("Диск: " + ex.Message); }
    }

    /// <summary>
    /// Подсчёт размера в фоне, без «занятости» программы: строки заполняются по одной, порядок не меняется.
    /// force — пересчитать всё («Посчитать заново»), иначе только то, что не считали или считали больше 2 минут назад.
    /// Начало очистки отменяет подсчёт.
    /// </summary>
    private async Task MeasureCleanAsync(bool force)
    {
        if (_cleaning || (_cleanMeasuring && !force)) return;
        var todo = _clean.Where(c => force || c.Size == -2 || !_cleanMeasuredAt.TryGetValue(c.Id, out var at) || DateTime.Now - at > TimeSpan.FromMinutes(2)).ToList();
        if (todo.Count == 0) { UpdateCleanSummary(); return; }
        int gen = ++_cleanMeasureGen;
        _cleanMeasuring = true;
        if (force) foreach (var c in todo) c.Size = -2;
        foreach (var c in todo) if (c.Size == -2) SetCleanSize(c);
        UpdateCleanSummary();
        try
        {
            foreach (var c in todo)
            {
                var cc = c;
                long size = await Task.Run(() => { try { return Cleaner.Measure(cc); } catch (Exception ex) { Log.Skip($"{cc.Name}: {ex.Message}"); return -1L; } });
                if (gen != _cleanMeasureGen) return;
                c.Size = size; _cleanMeasuredAt[c.Id] = DateTime.Now;
                SetCleanSize(c);
                HighlightBiggestClean();
                UpdateCleanSummary();
            }
        }
        finally
        {
            if (gen == _cleanMeasureGen)
            {
                _cleanMeasuring = false;
                foreach (var c in _clean) if (c.Size == -2) SetCleanSize(c);
                HighlightBiggestClean();
                UpdateCleanSummary();
            }
        }
    }

    /// <summary>«Посчитать заново» — после того как что-то удалили.</summary>
    private Task ScanAsync() => _busy ? Task.CompletedTask : MeasureCleanAsync(true);

    /// <summary>Итог в сводке («Освобождено …»). hide — через 15 секунд плавно вернуться к живой сводке.</summary>
    private void ShowCleanTotal(string label, string value, bool hide)
    {
        _cleanTotalTimer?.Stop();
        CleanTotal.BeginAnimation(OpacityProperty, null);
        CleanTotal.Opacity = 1;
        _cleanDone = hide;
        CleanTotalLabel.Text = label;
        CleanTotal.Text = value;
        CleanTotal.Foreground = hide ? B("Ok") : B("Text");
        CleanLogLink.Visibility = hide ? Visibility.Visible : Visibility.Collapsed;
        if (!hide) return;
        _cleanTotalTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _cleanTotalTimer.Tick -= CleanTotalTick; _cleanTotalTimer.Tick += CleanTotalTick;
        _cleanTotalTimer.Start();
    }
    private void CleanTotalTick(object sender, EventArgs e)
    {
        _cleanTotalTimer.Stop();
        var shown = CleanTotal.Text;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600));
        fade.Completed += (_, _) =>
        {
            if (CleanTotal.Text != shown || !_cleanDone) return;
            CleanTotal.BeginAnimation(OpacityProperty, null); CleanTotal.Opacity = 1;
            _cleanDone = false;
            UpdateCleanSummary();
        };
        CleanTotal.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Строка чистится: вместо размера — полоска и «Чищу…». Высота строки не меняется.</summary>
    private void CleanRowBusy(CleanTarget c)
    {
        if (!_cleanSizeBox.TryGetValue(c.Id, out var box) || !_cleanSizes.TryGetValue(c.Id, out var tb)) return;
        StopSpinners(box);
        foreach (var x in box.Children.OfType<UIElement>().Where(x => x != tb).ToList()) box.Children.Remove(x);
        tb.Visibility = Visibility.Collapsed;
        var sp = new StackPanel { Width = 120, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new ProgressBar { Height = 4, IsIndeterminate = true, Foreground = B("Accent"), Background = B("Surface2"), BorderThickness = new Thickness(0) });
        sp.Children.Add(new TextBlock { Text = "Чищу…", FontSize = 12, Foreground = B("Sub"), Margin = new Thickness(0, 4, 0, 0) });
        box.Children.Add(sp);
    }

    private void CleanRowDone(CleanTarget c, long freed)
    {
        if (!_cleanSizeBox.TryGetValue(c.Id, out var box) || !_cleanSizes.TryGetValue(c.Id, out var tb)) return;
        foreach (var x in box.Children.OfType<UIElement>().Where(x => x != tb).ToList()) box.Children.Remove(x);
        tb.Visibility = Visibility.Visible; tb.ToolTip = null;
        tb.Text = "✓ " + Sh.Size(freed);
        tb.Foreground = B("Ok"); tb.FontWeight = FontWeights.Normal;
    }

    private Task CleanAsync() => CleanSelectedAsync(null);
    private async Task CleanSelectedAsync(CleanTarget only)
    {
        if (_busy) return;
        var sel = AdvicePolicy.CleanupSelection(_clean, only, c => _cleanChecks.TryGetValue(c.Id, out var cb) && cb.IsChecked == true).ToList();
        if (sel.Count == 0) { SetStatus("Ничего не выбрано"); return; }
        if (sel.Any(c => c.Recycle) && !Ask("Удалённое из корзины не вернуть. Очистить?", "Корзина", MessageBoxImage.Warning, "Очистить")) return;
        if (sel.Any(c => c.Leftovers) && !Ask("Папки остатков могут содержать настройки и сохранения, в том числе от переносных программ. Удалённые папки нельзя вернуть через откат. Продолжить?",
            "Остатки программ", MessageBoxImage.Warning, "Удалить остатки")) return;
        // подсчёт размера больше не нужен — строки сейчас изменятся
        _cleanMeasureGen++; _cleanMeasuring = false;
        foreach (var c in _clean) if (c.Size == -2) SetCleanSize(c);
        long freed = 0;
        var summary = new List<(string Name, long Freed)>();
        _cleaning = true;
        bool started;
        QueueStart("Очистка", sel.Select(c => c.Name));   // полоска внизу — как у всех массовых действий
        try
        {
            started = await Busy("Чищу…", () =>
            {
                Log.Head("Очистка");
                for (int i = 0; i < sel.Count; i++)
                {
                    if (_qStop) break;
                    var c = sel[i];
                    int k = i;
                    QueueSet(i, QState.Now);
                    Dispatcher.BeginInvoke(() => { CleanRowBusy(c); BtnClean.Content = $"Чищу… {k + 1} из {sel.Count}"; });
                    string step = $" ({i + 1} из {sel.Count})";
                    void Progress(string msg) => Dispatcher.BeginInvoke(() => SetStatus(msg.EndsWith("…") ? msg[..^1] + step + "…" : msg + step));
                    Progress(c.Recycle ? "Чищу корзину…" : c.Dism ? "Чищу старые файлы Windows (до 10 минут)…" : "Чищу: " + c.Name + "…");
                    long f = 0;
                    try { f = Cleaner.Clean(c, Progress); } catch (Exception ex) { Log.Warn($"{c.Name}: {ex.Message}"); }
                    freed += f; summary.Add((c.Name, f));
                    QueueSet(i, QState.Done, Sh.Size(f));
                    long ff = f;
                    Dispatcher.BeginInvoke(() => CleanRowDone(c, ff));
                }
                Log.Head($"Всего освобождено: {Sh.Size(freed)}");
                foreach (var (n, f) in summary) Log.Write($"  {n}: {Sh.Size(f)}");
            });
        }
        finally { _cleaning = false; await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background); QueueFinish(); }
        if (!started) { foreach (var c in _clean) SetCleanSize(c); HighlightBiggestClean(); UpdateCleanSummary(); return; }
        // почищенное посчитаем заново при следующем открытии страницы
        foreach (var c in sel) { c.Size = -2; _cleanMeasuredAt.TryRemove(c.Id, out _); }
        ShowCleanTotal("Освобождено", Sh.Size(freed), hide: true);
        UpdateCleanSummary();
        UpdateCleanDisk();
        // почищенное пересчитываем через 16 с (когда уйдёт надпись «Освобождено»), иначе в сводке висело «Считаю…»
        await RefreshCleanupAdviceAsync();
        var again = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(16) };
        again.Tick += (_, _) => { again.Stop(); if (PageClean.IsVisible && !_busy) Fire(() => MeasureCleanAsync(false), "Очистка: размер"); };
        again.Start();
        var top = summary.Where(x => x.Freed > 0).OrderByDescending(x => x.Freed).Select(x => $"{x.Name}: {Sh.Size(x.Freed)}").ToList();
        SetStatus(top.Count == 0 ? $"Готово. Освобождено {Sh.Size(freed)}." : $"Готово. Освобождено {Sh.Size(freed)}: " + string.Join("; ", top));
    }
}
