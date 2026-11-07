// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

/// <summary>
/// «Обновления Windows» (пункт меню; та же страница PageDrivers, часть DrvTabWin): активация, обновления Windows и драйверов.
/// Установка обновлений Windows идёт в фоне и не блокирует остальные разделы Merno:
/// в интерфейсе видно, что именно происходит сейчас — поиск, скачивание или установка N-го обновления.
/// </summary>
public partial class MainWindow
{
    private bool _winBuilt, _winSearching, _winInstalling;

    /// <summary>
    /// Идёт фоновая установка обновлений Windows или драйверов: Merno нельзя закрывать, перезапускать и пересоздавать окно,
    /// иначе установка оборвётся на середине (обычное закрытие и так спрашивает, MainWindow.Tray.cs). true — действие отменено.
    /// </summary>
    private bool BlockedByWinInstall(string what)
    {
        if (!_winInstalling) return false;
        Info($"Сейчас Merno ставит обновления Windows или драйверы. {what}, когда они закончатся: иначе их установка оборвётся на середине.", "Подождите");
        return true;
    }
    private List<WinPending> _winPending = new();
    private List<DriverUpdate> _drvUpdatesWin = new();
    private readonly HashSet<string> _drvUpdSel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _drvUpdChecks = new(StringComparer.OrdinalIgnoreCase);
    private Button _drvUpdBtn, _drvUpdAllBtn;
    private TextBlock _drvUpdCount;

    private ProgressBar _winBar;
    private TextBlock _winStageText, _winPercentText, _winItemText, _winSubText;
    private Border _winProgressBox;

    private void InitWinTab()
    {
        BtnWinRefresh.Click += (_, _) =>
        {
            if (_winSearching || _winInstalling) { SetStatus("Подождите: обновления уже проверяются или устанавливаются"); return; }
            _winBuilt = false;
            Fire(BuildWinTabAsync, "Windows");
        };
    }

    private void NoteWin(string text, string tone = "sub")
    {
        WinNote.Text = text ?? "";
        WinNoteBox.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        WinNoteIcon.Text = tone switch { "ok" => Glyph.Check, "warn" or "bad" => Glyph.Warn, _ => Glyph.Sync };
        WinNoteIcon.Foreground = B(tone switch { "ok" => "Ok", "warn" => "Warn", "bad" => "Bad", _ => "Sub" });
    }

    private async Task BuildWinTabAsync()
    {
        if (_winSearching || _winInstalling) return;
        if (_winBuilt) return;
        _winBuilt = true;
        _winSearching = true;
        try
        {
            SetOwn(BtnWinRefresh, false);
            NoteWin("Проверяю активацию и ищу обновления…");
            WinMain.Children.Clear();
            WinSide.Children.Clear();

            // Карточки-заготовки: страница не пустая, пока идёт долгий поиск
            var (actCard, actBody) = DrvCard("Активация Windows", Glyph.Key);
            actBody.Children.Add(DrvRow("Проверяю активацию…", null));
            WinSide.Children.Add(actCard);

            var (updCard, updBody) = DrvCard("Обновления Windows", Glyph.Sync);
            updBody.Children.Add(DrvRow("Ищу обновления Windows…", "Обычно от 1 до 3 минут. Остальными разделами Merno можно пользоваться."));
            WinMain.Children.Add(updCard);

            var (drvCard, drvBody) = DrvCard("Обновления драйверов", Glyph.Download,
                "Драйверы из «Обновлений Windows».");
            drvBody.Children.Add(DrvRow("Ищу обновления драйверов…", "Иногда это занимает пару минут."));
            WinMain.Children.Add(drvCard);

            var (sysCard, sysBody) = DrvCard("Система", Glyph.Info);
            WinSide.Children.Add(sysCard);
            FillWinSystem(sysBody);

            // три проверки параллельно: активация быстрая, списки обновлений — нет
            var actTask = Task.Run(() => WinUpdate.Activation());
            var updTask = Task.Run(() => WinUpdate.FindPending());
            var drvTask = Task.Run(() => Drivers.FindUpdates());

            (bool? Active, string Text) act = (null, "Не удалось проверить активацию");
            try { act = await actTask; } catch (Exception ex) { Log.Warn("Активация: " + ex.Message); }
            actBody.Children.Clear();
            if (act.Active == true) actBody.Children.Add(DrvRow(act.Text, null, null, B("Ok"), Glyph.Check));
            else
            {
                var open = new Button { Content = "Открыть параметры" };
                open.Click += (_, _) => WinUpdate.OpenActivationSettings();
                actBody.Children.Add(DrvRow(act.Text, null, open,
                    act.Active == false ? B("Warn") : null, act.Active == false ? Glyph.Warn : Glyph.Info));
            }

            List<WinPending> pending = new(); string updErr = null;
            try { (pending, updErr) = await updTask; } catch (Exception ex) { updErr = ex.Message; }
            pending ??= new();
            _winPending = pending;
            RenderWinUpdates(updBody, pending, updErr);

            List<DriverUpdate> drvUps = new(); string drvErr = null;
            try { (drvUps, drvErr) = await drvTask; } catch (Exception ex) { drvErr = ex.Message; }
            drvUps ??= new();
            _drvUpdatesWin = drvUps;
            RenderDriverUpdates(drvBody, drvUps, drvErr);

            int problems = pending.Count + drvUps.Count;
            NoteWin(updErr != null || drvErr != null ? "Не всё удалось проверить" : problems == 0 ? "Всё актуально" : $"Найдено обновлений: {problems}",
                    updErr != null || drvErr != null ? "bad" : problems == 0 ? "ok" : "warn");
        }
        catch (Exception ex)
        {
            Log.Warn("Windows: " + ex.Message); Log.Skip(ex.ToString());
            NoteWin("Не получилось проверить обновления. Нажмите «Проверить ещё раз».", "bad");
        }
        finally { _winSearching = false; SetOwn(BtnWinRefresh, true); }
    }

    private void FillWinSystem(StackPanel body)
    {
        var s = _ctx.S;
        if (s == null)
        {
            body.Children.Add(DrvRow("Сведения ещё собираются", "Откройте раздел чуть позже."));
            return;
        }
        string win = string.Join(" ", new[] { System.Text.RegularExpressions.Regex.Replace(s.OS ?? "", @"\s*\(сборка.*\)", "").Trim(), s.Edition }.Where(x => !string.IsNullOrWhiteSpace(x)));
        body.Children.Add(DrvRow(win.Length > 0 ? win : "Windows", null));
        body.Children.Add(DrvRow("Разрядность", Environment.Is64BitOperatingSystem ? "64-битная" : "32-битная"));
        body.Children.Add(DrvRow("Перезагрузка", WinUpdate.RebootPending() ? "Требуется перезагрузка" : "Не требуется",
            null, WinUpdate.RebootPending() ? B("Warn") : B("Ok"), WinUpdate.RebootPending() ? Glyph.Warn : Glyph.Check));
    }

    private void RenderWinUpdates(StackPanel body, List<WinPending> list, string err)
    {
        body.Children.Clear();
        _winProgressBox = null;
        if (err != null)
        {
            Log.Warn("Обновления Windows: " + err);
            var retry = new Button { Content = "Повторить" };
            retry.Click += (_, _) => { _winBuilt = false; Fire(BuildWinTabAsync, "Windows"); };
            body.Children.Add(DrvRow("Не удалось проверить обновления Windows", "Проверьте интернет и попробуйте позже.", retry, B("Warn"), Glyph.Warn));
            return;
        }
        if (list.Count == 0)
        {
            body.Children.Add(DrvRow("Обновлений нет", null, null, B("Ok"), Glyph.Check));
            return;
        }

        // список с галочками: по умолчанию отмечены все; можно снять лишнее и поставить только отмеченные
        _winSel.Clear();
        foreach (var u in list) if (!string.IsNullOrEmpty(u.Id)) _winSel.Add(u.Id);
        var install = new Button { Content = "Установить отмеченные", Style = (Style)FindResource("Primary"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 5, 14, 5) };
        var installAll = new Button { Content = "Установить все", Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 5, 14, 5) };
        var count = new TextBlock { Foreground = B("Accent"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 8) };
        void Upd()
        {
            count.Text = $"Отмечено: {_winSel.Count} из {list.Count}";
            install.IsEnabled = _winSel.Count > 0 && !_winInstalling;
            install.Content = _winSel.Count > 0 ? $"Установить отмеченные ({_winSel.Count})" : "Установить отмеченные";
        }
        install.Click += (_, _) => StartWinUpdateInstall(install, _winSel.ToList());
        installAll.Click += (_, _) => StartWinUpdateInstall(installAll, null);
        var bar = new WrapPanel { Margin = new Thickness(12, 8, 12, 4) };
        bar.Children.Add(count); bar.Children.Add(install); bar.Children.Add(installAll);
        body.Children.Add(bar);
        foreach (var u in list)
        {
            var row = new Border { Style = (Style)FindResource("Row"), Cursor = System.Windows.Input.Cursors.Hand };
            var dp = new DockPanel();
            var cb = new CheckBox { Style = (Style)FindResource("Check"), IsChecked = u.Id == null || _winSel.Contains(u.Id), IsEnabled = u.Id != null,
                                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var id = u.Id;
            cb.Checked += (_, _) => { if (id != null) _winSel.Add(id); Upd(); };
            cb.Unchecked += (_, _) => { if (id != null) _winSel.Remove(id); Upd(); };
            row.MouseLeftButtonUp += (_, e) => { if (!(e.OriginalSource is FrameworkElement fe && IsInside(fe, cb)) && cb.IsEnabled) cb.IsChecked = cb.IsChecked != true; };
            DockPanel.SetDock(cb, Dock.Left);
            dp.Children.Add(cb);
            if (!string.IsNullOrEmpty(u.Size))
            {
                var size = new TextBlock { Text = u.Size, Foreground = B("Sub"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                DockPanel.SetDock(size, Dock.Right);
                dp.Children.Add(size);
            }
            dp.Children.Add(new TextBlock { Text = u.Title ?? "Обновление", FontSize = 14, Foreground = B("Text"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            row.Child = dp;
            body.Children.Add(row);
        }
        Upd();

        var box = new Border { Style = (Style)FindResource("Row"), Visibility = Visibility.Collapsed };
        var sp = new StackPanel();
        var head = new DockPanel();
        _winPercentText = new TextBlock { Foreground = B("Accent"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_winPercentText, Dock.Right);
        head.Children.Add(_winPercentText);
        _winStageText = new TextBlock { Text = "Готовлю установку…", FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("Text"), VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(_winStageText);
        sp.Children.Add(head);
        _winBar = new ProgressBar { Height = 6, Margin = new Thickness(0, 9, 0, 6), Minimum = 0, Maximum = 1, Value = 0, IsIndeterminate = true, Foreground = B("Accent"), Background = B("Surface2"), BorderThickness = new Thickness(0) };
        sp.Children.Add(_winBar);
        _winItemText = new TextBlock { Style = (Style)FindResource("SubText"), TextWrapping = TextWrapping.Wrap };
        sp.Children.Add(_winItemText);
        _winSubText = new TextBlock { Style = (Style)FindResource("SubText"), Foreground = B("Warn"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
        sp.Children.Add(_winSubText);
        box.Child = sp;
        body.Children.Add(box);
        _winProgressBox = box;

        body.Children.Add(new TextBlock
        {
            Text = Typo.Nbsp("Merno ставит обновления по одному. Пока идёт установка, остальными разделами можно пользоваться. Перезагружать компьютер до конца установки не нужно."),
            Style = (Style)FindResource("SubText"), Margin = new Thickness(12, 6, 12, 4), TextWrapping = TextWrapping.Wrap
        });
    }

    private void RenderDriverUpdates(StackPanel body, List<DriverUpdate> list, string err)
    {
        body.Children.Clear();
        _drvUpdChecks.Clear();
        _drvUpdSel.IntersectWith(list.Select(u => u.Id));
        if (err != null)
        {
            Log.Warn("Обновления драйверов: " + err);
            var retry = new Button { Content = "Повторить" };
            retry.Click += (_, _) => { _winBuilt = false; Fire(BuildWinTabAsync, "Windows"); };
            body.Children.Add(DrvRow("Не удалось проверить драйверы", "Проверьте интернет и попробуйте позже.", retry, B("Warn"), Glyph.Warn));
            return;
        }
        if (list.Count == 0)
        {
            body.Children.Add(DrvRow("Обновлений драйверов нет", "Всё свежее.", null, B("Ok"), Glyph.Check));
            return;
        }

        var bar = new WrapPanel { Margin = new Thickness(12, 8, 12, 4) };
        _drvUpdCount = new TextBlock { Foreground = B("Accent"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 8), FontWeight = FontWeights.SemiBold };
        _drvUpdBtn = new Button { Content = "Установить отмеченные", Style = (Style)FindResource("Primary"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 5, 14, 5) };
        _drvUpdBtn.Click += (_, _) => StartDriverInstall(_drvUpdSel.ToList());
        _drvUpdAllBtn = new Button { Content = "Установить все", Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 5, 14, 5) };
        _drvUpdAllBtn.Click += (_, _) => StartDriverInstall(_drvUpdatesWin.Select(u => u.Id).ToList());
        var all = new Button { Content = "Отметить все", Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 5, 12, 5) };
        all.Click += (_, _) => SetAllDrvSelected(true);
        var none = new Button { Content = "Снять все", Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 5, 12, 5) };
        none.Click += (_, _) => SetAllDrvSelected(false);
        bar.Children.Add(_drvUpdCount);
        bar.Children.Add(_drvUpdBtn);
        bar.Children.Add(_drvUpdAllBtn);
        bar.Children.Add(all);
        bar.Children.Add(none);
        body.Children.Add(bar);

        foreach (var u in list)
        {
            var row = new Border { Style = (Style)FindResource("Row") };
            var dp = new DockPanel();
            var one = new Button { Content = "Установить", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            var id = u.Id;
            one.Click += (_, _) => StartDriverInstall(new List<string> { id });
            DockPanel.SetDock(one, Dock.Right);
            dp.Children.Add(one);
            var cb = new CheckBox
            {
                Style = (Style)FindResource("Check"), IsChecked = _drvUpdSel.Contains(u.Id),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
                ToolTip = "Отметить драйвер"
            };
            cb.Checked += (_, _) => { _drvUpdSel.Add(u.Id); UpdateDrvSel(); };
            cb.Unchecked += (_, _) => { _drvUpdSel.Remove(u.Id); UpdateDrvSel(); };
            DockPanel.SetDock(cb, Dock.Left);
            dp.Children.Add(cb);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = u.Title ?? "Драйвер", ToolTip = u.Raw, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("Text"), TextWrapping = TextWrapping.Wrap });
            var sub = string.Join(" · ", new[] { !string.IsNullOrEmpty(u.Date) ? "от " + u.Date : null, u.Size }.Where(x => !string.IsNullOrEmpty(x)));
            if (sub.Length > 0) sp.Children.Add(new TextBlock { Text = Typo.Nbsp(sub), Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 3, 0, 0) });
            dp.Children.Add(sp);
            row.Child = dp;
            body.Children.Add(row);
            _drvUpdChecks[u.Id] = cb;
        }
        UpdateDrvSel();
    }

    private void SetAllDrvSelected(bool on)
    {
        foreach (var (id, cb) in _drvUpdChecks)
        {
            if (on) _drvUpdSel.Add(id); else _drvUpdSel.Remove(id);
            cb.IsChecked = on;
        }
        UpdateDrvSel();
    }

    private void UpdateDrvSel()
    {
        if (_drvUpdCount != null) _drvUpdCount.Text = _drvUpdSel.Count == 0 ? "" : $"Отмечено: {_drvUpdSel.Count}";
        if (_drvUpdBtn != null) _drvUpdBtn.IsEnabled = _drvUpdSel.Count > 0 && !_winInstalling;
        if (_drvUpdAllBtn != null) _drvUpdAllBtn.IsEnabled = _drvUpdatesWin.Count > 0 && !_winInstalling;
    }

    // ---------- установка обновлений Windows (без блокировки программы) ----------
    private readonly HashSet<string> _winSel = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>only — какие обновления ставить (null — все).</summary>
    private void StartWinUpdateInstall(Button btn, List<string> only)
    {
        if (_winInstalling || _winSearching) { SetStatus("Подождите: обновления уже проверяются или устанавливаются"); return; }
        if (_updInstalling) { SetStatus("Подождите: Merno обновляется и скоро перезапустится"); return; }   // иначе перезапуск оборвёт установку
        int count = only?.Count ?? _winPending.Count;
        if (count == 0) return;
        if (!Ask($"Установить обновления Windows ({count})?\n\nЭто может занять до часа. Компьютер пока работает как обычно, " +
                 "а Merno будет показывать, что происходит сейчас. Перезагружать до конца установки не нужно.",
                 "Обновления Windows", MessageBoxImage.Question, "Установить")) return;

        _winInstalling = true; Cleaner.UpdatesInstalling = true;
        btn.IsEnabled = false;
        UpdateDrvSel();
        _winBuilt = false;
        ShowWinProgress("Готовлю установку…");
        SetStatus("Устанавливаю обновления Windows в фоне. Можно пользоваться Merno", detectError: false);
        Log.Head("Обновления Windows");
        _qWinGen = QueueSingle("Обновления Windows", $"Обновления Windows ({count})");

        Task.Run(() =>
        {
            try { Backup.CreateRestorePoint(); } catch (Exception ex) { Log.Skip("Точка восстановления: " + ex.Message); }
            var result = WinUpdate.InstallAll(p => Dispatcher.BeginInvoke(new Action(() => ShowWinProgress(p))), only);
            Dispatcher.BeginInvoke(new Action(async () => await FinishWinInstallAsync(result, count)));
        });
    }

    private int _qWinGen = -1, _qDrvGen = -1;   // полоска внизу (MainWindow.InstallProgress.cs) для фоновых установок

    private void ShowWinProgress(string text)
    {
        QueueSingleDetail(_qWinGen, text);
        if (_winProgressBox == null) return;
        _winProgressBox.Visibility = Visibility.Visible;
        _winStageText.Text = text;
        _winBar.IsIndeterminate = true;
        _winPercentText.Text = "";
        _winItemText.Text = "";
        _winSubText.Visibility = Visibility.Collapsed;
    }

    private void ShowWinProgress(WinProgress p)
    {
        if (_winProgressBox == null) return;
        _winProgressBox.Visibility = Visibility.Visible;
        int total = Math.Max(1, p.Total);
        _winBar.Maximum = total;
        _winBar.IsIndeterminate = p.Total <= 0;
        _winBar.Value = Math.Max(0, p.Index - (p.Stage == WinStage.Done ? 0 : 1));
        _winStageText.Text = p.Stage switch
        {
            WinStage.Searching => p.Index > 0 ? $"Проверяю обновление {p.Index} из {total}" : "Ищу обновления Windows…",
            WinStage.Downloading => $"Скачиваю обновление {p.Index} из {total}",
            WinStage.Installing => $"Устанавливаю обновление {p.Index} из {total}",
            WinStage.Done => $"Установлено {p.Index} из {total}",
            _ => "Работаю…"
        };
        _winPercentText.Text = p.Total > 0 ? $"{Math.Min(100, (int)Math.Round(_winBar.Value * 100.0 / total))}%" : "";
        if (!string.IsNullOrWhiteSpace(p.Title)) _winItemText.Text = p.Title;
        QueueSingleDetail(_qWinGen, _winStageText.Text);
        if (!string.IsNullOrWhiteSpace(p.Note))
        {
            _winSubText.Text = "Примечание: " + p.Note.TrimEnd('.') + ". Это обновление можно попробовать поставить позже.";
            _winSubText.Visibility = Visibility.Visible;
        }
        else _winSubText.Visibility = Visibility.Collapsed;
    }

    private async Task FinishWinInstallAsync((int Installed, bool Reboot, string Error) r, int count)
    {
        _winInstalling = false; Cleaner.UpdatesInstalling = false;
        UpdateDrvSel();
        QueueSingleDone(_qWinGen, r.Error == null, r.Error);
        if (r.Error != null) Log.Warn("Установка обновлений Windows: " + r.Error);
        if (r.Error != null)
        {
            Warn("Не удалось установить обновления Windows. Попробуйте позже.");
        }
        else
        {
            string msg = $"Установлено обновлений: {r.Installed} из {count}." + (r.Reboot ? "\n\nПерезагрузите компьютер, чтобы завершить установку." : "");
            Info(msg);
            if (r.Reboot) AskReboot();
        }
        _winPending = new();   // только что поставлены — старый список обновлений больше не актуален
        await RefreshPendingAsync();
        if (r.Error == null) ShowPendingHint("Обновления Windows установлены.");
        _winBuilt = false;
        await BuildWinTabAsync();
        await RefreshPendingAsync();   // после поиска: вдруг что-то не установилось
        SetStatus(r.Error == null ? "Обновления Windows установлены" : "Обновления Windows установились не полностью");
    }

    // ---------- установка драйверов (тоже без блокировки программы) ----------
    private void StartDriverInstall(List<string> ids)
    {
        if (_winInstalling || _winSearching) { SetStatus("Подождите: обновления уже проверяются или устанавливаются"); return; }
        if (_updInstalling) { SetStatus("Подождите: Merno обновляется и скоро перезапустится"); return; }
        ids = (ids ?? new()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0) { SetStatus("Не отмечено ни одного драйвера"); return; }
        if (!Ask($"Установить драйверы ({ids.Count})?\n\nЭкран может мигнуть, а интернет может ненадолго пропасть. " +
                 "Остальными разделами Merno можно пользоваться.",
                 "Обновления драйверов", MessageBoxImage.Question, "Установить")) return;

        _winInstalling = true; Cleaner.UpdatesInstalling = true;
        UpdateDrvSel();
        _winBuilt = false;
        NoteWin($"Устанавливаю драйверы ({ids.Count})…");
        _qDrvGen = QueueSingle("Драйверы", $"Драйверы ({ids.Count})");
        SetStatus("Устанавливаю драйверы в фоне. Можно пользоваться Merno", detectError: false);

        Task.Run(() =>
        {
            try { Backup.CreateRestorePoint(); } catch (Exception ex) { Log.Skip("Точка восстановления: " + ex.Message); }
            var result = Drivers.Install(ids);
            Dispatcher.BeginInvoke(new Action(async () => await FinishDriverInstallAsync(result, ids.Count)));
        });
    }

    private async Task FinishDriverInstallAsync((int Installed, bool Reboot, string Error) r, int count)
    {
        _winInstalling = false; Cleaner.UpdatesInstalling = false;
        QueueSingleDone(_qDrvGen, r.Error == null, r.Error);
        UpdateDrvSel();
        if (r.Error != null) Log.Warn("Установка драйверов: " + r.Error);
        if (r.Error != null) Warn("Не получилось установить драйверы. Проверьте интернет и попробуйте позже.");
        else
        {
            string msg = $"Установлено драйверов: {r.Installed} из {count}." + (r.Reboot ? "\n\nПерезагрузите компьютер, чтобы новые драйверы заработали." : "");
            Info(msg);
            if (r.Reboot) AskReboot();
        }
        await RefreshPendingAsync();
        if (r.Error == null) ShowPendingHint("Драйверы установлены.");
        _winBuilt = false;
        await BuildWinTabAsync();
        SetStatus(r.Error == null ? "Драйверы установлены" : "Драйверы установились не полностью");
    }
}
