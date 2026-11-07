// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Ход массовых действий — одна очередь, два вида:
//  А) карточка «Установка» в боковой панели «Программ» (InstCard): всё по пунктам, «Остановить», «Повторить для неудавшихся».
//     Перерисовывается, только пока раздел открыт; при возврате в раздел — один раз целиком.
//  Б) строка состояния внизу (InstBar) — у ЛЮБОГО массового действия (установка, удаление, обновления, очистка, настройки…):
//     деление на пункт и «3 из 5»; по щелчку — список (InstPop), кроме случая, когда тот же список уже в карточке.
public partial class MainWindow
{
    private enum QState { Wait, Now, Done, Fail }
    private sealed class QItem { public string Name; public QState State; public string Note; }

    private List<QItem> _q;
    private string _qTitle = "";
    private bool _qRunning, _qStop, _qWired, _qCard;   // _qCard — очередь программ: есть карточка в «Программах»
    private string _qDetail = "";
    private Func<List<string>, Task> _qRetry;
    private int _qGen;

    private void InitInstallProgress()
    {
        if (_qWired) return;
        _qWired = true;
        // вернулись в «Программы» — карточку перерисовываем один раз; список внизу там не нужен
        PageApps.IsVisibleChanged += (_, _) =>
        {
            if (CardShown) { InstPop.IsOpen = false; RenderInstCard(); }
            RenderInstBar(false);
        };
        // список закрывается сам при щелчке мимо; щелчок по строке, которым его закрыли, не должен тут же открыть снова
        var closedAt = DateTime.MinValue;
        InstPop.Closed += (_, _) => closedAt = DateTime.Now;
        InstBar.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_q == null || CardShown) return;   // в «Программах» список программ уже в карточке справа
            if ((DateTime.Now - closedAt).TotalMilliseconds < 300) return;
            InstPop.IsOpen = !InstPop.IsOpen;
            if (InstPop.IsOpen) RenderInstPop();
        };
    }

    /// <summary>Карточка справа сейчас на экране: очередь программ и открыты «Программы».</summary>
    private bool CardShown => _qCard && PageApps.IsVisible;

    /// <summary>Новая очередь (из потока окна) — для любого массового действия: полоска внизу будет всегда.
    /// card — это программы: ещё и карточка в «Программах»; retry — чем повторить для неудавшихся (null — кнопки нет).</summary>
    private readonly System.Threading.AsyncLocal<int?> _queueOperation = new();
    private void QueueStart(string title, IEnumerable<string> names, Func<List<string>, Task> retry = null, bool card = false)
    {
        _q = names.Select(n => new QItem { Name = n, State = QState.Wait }).ToList();
        if (_q.Count == 0) { _q = null; return; }
        _qTitle = title; _qRunning = true; _qStop = false; _qDetail = ""; _qRetry = retry; _qCard = card; _qGen++;
        _queueOperation.Value = _qGen;
        if (!card) { InstCard.Visibility = Visibility.Collapsed; _instNowText = null; }
        RenderInstAll();
    }

    /// <summary>Массовое действие целиком: очередь внизу, работа в фоне (Busy), пункт за пунктом.
    /// work — для одного пункта, возвращает успех и причину неудачи. false — не запущено (Merno занят).</summary>
    private async Task<bool> QueueRun<T>(string title, string busyText, IList<T> items, Func<T, string> name, Func<T, (bool Ok, string Note)> work)
    {
        if (_busy) { SetStatus("Дождитесь завершения текущего действия"); return false; }
        QueueStart(title, items.Select(name));
        bool ran = await Busy(busyText, () =>
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (_qStop) break;
                QueueSet(i, QState.Now);
                try { var (ok, note) = work(items[i]); QueueSet(i, ok ? QState.Done : QState.Fail, note); }
                catch (Exception ex) { Log.Warn($"{name(items[i])}: {ex.Message}"); QueueSet(i, QState.Fail, ForPerson(ex)); }
            }
        });
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);   // дождаться последних QueueSet
        QueueFinish();
        return ran;
    }

    /// <summary>Один долгий шаг без деления на пункты (драйверы и обновления Windows ставятся одной командой).</summary>
    /// Идёт в фоне, пока можно начать другое — поэтому возвращает номер очереди: если за это время началась новая, её не трогаем.
    private int QueueSingle(string title, string what) { QueueStart(title, new[] { what }); QueueSet(0, QState.Now); return _qGen; }
    private void QueueSingleDetail(int gen, string text) { if (gen == _qGen) QueueDetail(text); }
    private void QueueSingleDone(int gen, bool ok, string note = null)
    {
        if (gen != _qGen) return;
        QueueSet(0, ok ? QState.Done : QState.Fail, note);
        Dispatcher.BeginInvoke(new Action(() => { if (gen == _qGen) QueueFinish(); }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Состояние пункта. Можно звать из фонового потока.</summary>
    private void QueueSet(int i, QState st, string note = null)
    {
        int generation = _queueOperation.Value ?? _qGen;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (generation != _qGen || _q == null || i < 0 || i >= _q.Count) return;
            _q[i].State = st; _q[i].Note = note;
            if (st == QState.Now) _qDetail = "";
            RenderInstAll();
        }));
    }

    private void QueueFinish()
    {
        if (_queueOperation.Value is int generation && generation != _qGen) return;
        if (_q == null) return;
        _qRunning = false; _qDetail = "";
        foreach (var it in _q.Where(x => x.State == QState.Now)) it.State = QState.Wait;   // остановили посреди — не «сейчас»
        RenderInstAll();
        // строка внизу гаснет сама через 10 секунд, если ничего нового не началось; карточка остаётся до «Скрыть»
        int gen = _qGen;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        t.Tick += (_, _) => { t.Stop(); if (gen == _qGen && !_qRunning) { InstBar.Visibility = Visibility.Collapsed; InstPop.IsOpen = false; } };
        t.Start();
    }

    /// <summary>Что сейчас делает winget («скачиваю — 23 МБ · 0:41»). Раз в секунду — поэтому трогаем только текст.</summary>
    private void QueueDetail(string p)
    {
        if (!_qRunning) return;
        _qDetail = p;
        var cur = _q?.FirstOrDefault(x => x.State == QState.Now);
        if (cur != null) Status.Text = $"{_qTitle}: {cur.Name} · {p}";
        if (CardShown && _instNowText != null) _instNowText.Text = NowLine();
        if (InstPop.IsOpen) RenderInstPop();
    }

    private string NowLine()
    {
        var cur = _q?.FirstOrDefault(x => x.State == QState.Now);
        return cur == null ? "" : "Сейчас: " + cur.Name + (_qDetail.Length > 0 ? " · " + _qDetail : "");
    }

    private void RenderInstAll()
    {
        RenderInstBar(true);
        if (CardShown) RenderInstCard();   // вне раздела карточку не трогаем — пустая работа
        if (InstPop.IsOpen) RenderInstPop();
    }

    // ---------- Б: строка внизу ----------
    private void RenderInstBar(bool segments)
    {
        if (_q == null || _q.Count == 0) { InstBar.Visibility = Visibility.Collapsed; return; }
        InstBar.Visibility = Visibility.Visible;
        int n = _q.Count, done = _q.Count(x => x.State == QState.Done), idx = _q.FindIndex(x => x.State == QState.Now);
        InstCount.Text = _qRunning ? $"{(idx >= 0 ? idx + 1 : done)} из {n}" : $"готово {done} из {n}";
        InstToggle.Visibility = CardShown ? Visibility.Collapsed : Visibility.Visible;
        if (!segments) return;
        InstSeg.Children.Clear();
        // при большой очереди деления тоньше, вся полоска — не шире 200
        double w = Math.Clamp(200.0 / n - 3, 4, 28);
        foreach (var it in _q)
            InstSeg.Children.Add(new Border
            {
                Width = w, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 3, 0),
                Background = it.State switch { QState.Done => B("Ok"), QState.Now => B("Accent"), QState.Fail => B("Bad"), _ => B("Line") }
            });
    }

    private void RenderInstPop()
    {
        InstPopList.Children.Clear();
        InstPopList.Children.Add(new TextBlock { Text = _qTitle, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("Text"), Margin = new Thickness(0, 0, 0, 6) });
        foreach (var it in _q ?? new()) InstPopList.Children.Add(QRow(it));
    }

    // ---------- А: карточка в «Программах» ----------
    private TextBlock _instNowText;

    private void RenderInstCard()
    {
        if (_q == null || _q.Count == 0 || !_qCard) { InstCard.Visibility = Visibility.Collapsed; _instNowText = null; return; }
        InstCard.Visibility = Visibility.Visible;
        InstCardBody.Children.Clear();
        int n = _q.Count, done = _q.Count(x => x.State == QState.Done), fails = _q.Count(x => x.State == QState.Fail), idx = _q.FindIndex(x => x.State == QState.Now);

        var head = new DockPanel();
        var cnt = new TextBlock { Text = _qRunning ? $"{(idx >= 0 ? idx + 1 : done)} из {n}" : $"{done} из {n}", Style = (Style)FindResource("SubText"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(cnt, Dock.Right); head.Children.Add(cnt);
        string title = _qRunning ? _qTitle : fails > 0 ? _qTitle + ": есть ошибки" : (_qTitle == "Удаление" ? "Всё удалено" : "Всё установлено");
        head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = B("Text") });
        InstCardBody.Children.Add(head);

        double pct = _qRunning ? Math.Min(99, (done + fails + (idx >= 0 ? 0.35 : 0)) * 100.0 / n) : 100;
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = pct, Height = 6, Margin = new Thickness(0, 10, 0, 0) };
        if (!_qRunning) bar.Foreground = fails > 0 ? B("Warn") : B("Ok");
        InstCardBody.Children.Add(bar);

        _instNowText = new TextBlock { Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap,
                                       Text = _qRunning ? NowLine() : fails > 0 ? "Не получилось: " + string.Join(", ", _q.Where(x => x.State == QState.Fail).Select(x => x.Name)) : "Можно пользоваться." };
        InstCardBody.Children.Add(_instNowText);
        foreach (var it in _q) InstCardBody.Children.Add(QRow(it));

        var btns = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (_qRunning)
        {
            var stop = new Button { Content = _qStop ? "Остановлю после текущей" : "Остановить после текущей", IsEnabled = !_qStop, Margin = new Thickness(0, 0, 8, 0) };
            stop.Click += (_, _) => { _qStop = true; RenderInstCard(); };
            btns.Children.Add(stop);
        }
        else
        {
            var failed = _q.Where(x => x.State == QState.Fail || x.State == QState.Wait).Select(x => x.Name).ToList();
            if (failed.Count > 0 && _qRetry != null)
            {
                var again = new Button { Content = "Повторить для неудавшихся", Style = (Style)FindResource("Outline"), Margin = new Thickness(0, 0, 8, 0) };
                var retry = _qRetry;
                again.Click += (_, _) => Fire(() => retry(failed), "Повтор установки");
                btns.Children.Add(again);
            }
            var hide = new Button { Content = "Скрыть" };
            hide.Click += (_, _) => { _q = null; RenderInstAll(); };
            btns.Children.Add(hide);
        }
        InstCardBody.Children.Add(btns);
    }

    /// <summary>Пункт очереди: кружок состояния, название и что с ним.</summary>
    private FrameworkElement QRow(QItem it)
    {
        var dp = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var (glyph, brush) = it.State switch
        {
            QState.Done => (Glyph.Check, "Ok"), QState.Fail => (Glyph.Warn, "Bad"), QState.Now => ("", "Accent"), _ => ("", "Sub")
        };
        var ic = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12, Foreground = B(brush), Width = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(ic, Dock.Left); dp.Children.Add(ic);
        string tail = it.State switch { QState.Done => "готово", QState.Fail => "не получилось", QState.Now => "сейчас", _ => "в очереди" };
        var tb = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                                 Foreground = it.State == QState.Now ? B("Text") : B("Sub"), FontWeight = it.State == QState.Now ? FontWeights.SemiBold : FontWeights.Normal,
                                 ToolTip = string.IsNullOrEmpty(it.Note) ? null : it.Note };
        tb.Inlines.Add(new System.Windows.Documents.Run(it.Name));
        tb.Inlines.Add(new System.Windows.Documents.Run(": " + tail) { Foreground = B("Sub"), FontWeight = FontWeights.Normal });
        dp.Children.Add(tb);
        return dp;
    }
}
