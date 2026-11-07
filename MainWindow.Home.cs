// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Главная: проверка, оптимизация, советы, «О компьютере», последние действия
public partial class MainWindow
{
    private void AddResult(string glyph, Brush color, string title, string text, string page = null, string target = null, Button action = null, Action go = null)
    {
        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var ic = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 16, Foreground = color, Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(ic, Dock.Left);
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = B("Text") });
        if (!string.IsNullOrEmpty(text)) sp.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("SubText"), FontSize = 13.5, Margin = new Thickness(0, 2, 0, 0) });
        if (page != null || go != null)
        {   // где именно это исправить — переход прямо к нужному месту с подсветкой
            var link = new TextBlock { Margin = new Thickness(0, 3, 0, 0), FontSize = 13.5 };
            var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Где исправить →")) { Foreground = B("Accent"), Cursor = System.Windows.Input.Cursors.Hand };
            h.Click += (_, _) => { if (go != null) go(); else OpenFeature(page, target); };
            link.Inlines.Add(h);
            sp.Children.Add(link);
        }
        if (action != null) { action.HorizontalAlignment = HorizontalAlignment.Left; action.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(action); }
        dp.Children.Add(ic); dp.Children.Add(sp);
        CheckResults.Children.Add(dp);
    }

    private long _adviceJunk, _adviceRecycle;
    /// <summary>Не включённые рекомендуемые и игровые настройки (номера) — для советов и «Где исправить».</summary>
    private List<string> _adviceTodo = new(), _adviceGameTodo = new();
    private List<StartupItem> _adviceStartup = new();
    private int _adviceCleanGeneration;
    private void RenderCheckAdvice()
    {
        if (!_homeChecked || _settings.CheckDismissed) return;
        CheckResults.Children.Clear();
        CheckResults.Visibility = Visibility.Visible;
        HomeTipsEmpty.Visibility = Visibility.Collapsed;
        var todoIds = _adviceTodo; var gameIds = _adviceGameTodo;
        int todo = todoIds.Count, gameTodo = gameIds.Count;
        int startupOn = AdvicePolicy.StartupCandidates(_settings, _adviceStartup);
        if (todo > 0) AddResult("", B("Warn"), $"Можно применить ещё {todo} {Plural(todo, "рекомендуемую настройку", "рекомендуемые настройки", "рекомендуемых настроек")}",
                                TweakExamples(todoIds) + "«Где исправить» отметит их в «Настройках Windows».", go: () => OpenTweakAdvice(todoIds));
        if (gameTodo > 0) AddResult("", B("Warn"), $"Для игр можно включить ещё {gameTodo} {Plural(gameTodo, "настройку", "настройки", "настроек")}", "Они делают игры плавнее. " + TweakExamples(gameIds) + "«Где исправить» отметит их в «Настройках Windows».", go: () => OpenTweakAdvice(gameIds));
        if (AdvicePolicy.SuggestCleanup(_adviceJunk)) AddResult("", B("Warn"), $"Можно очистить: {Sh.Size(_adviceJunk)}",
            "Оценка объёма временных файлов. Проверьте выбранные категории перед удалением.", "PageClean", "CleanList");
        var bin = _clean.FirstOrDefault(c => c.Recycle);
        if (bin != null && AdvicePolicy.SuggestRecycle(_adviceRecycle))
        {
            var empty = new Button { Content = "Очистить корзину" };
            empty.Click += (_, _) => Fire(() => CleanSelectedAsync(bin), "Корзина");
            AddResult("", B("Warn"), $"В корзине: {Sh.Size(_adviceRecycle)}", "Перед очисткой проверьте, нет ли там нужных файлов.", action: empty);
        }
        if (startupOn > 6) AddResult("", B("Warn"), $"Можно проверить {startupOn} {Plural(startupOn, "программу", "программы", "программ")} в автозагрузке", "Некоторые программы могут замедлять запуск. Нужные отметьте «Не предлагать отключать» в «Автозагрузке».", "PageStartup", "StartList");
        foreach (var i in AdvicePolicy.VisibleIssues(_settings, _ctx.S?.Issues ?? new()))
            AddResult(i.Severity == 0 ? "" : "", i.Severity == 2 ? B("Bad") : i.Severity == 1 ? B("Warn") : B("Ok"), i.Text, i.Advice, i.Page, i.Target);
        AddDismiss();
    }
    private void RefreshAdviceChoices()
    {
        RenderCheckAdvice();
        BuildSystem();
        UpdateIssueHealth();
    }
    private async Task RefreshCleanupAdviceAsync()
    {
        if (!_homeChecked) return;
        int generation = ++_adviceCleanGeneration;
        var targets = _clean.Where(c => (c.Default && !c.Dism) || c.Recycle).ToList();
        var sizes = await Task.Run(() => targets.Select(c => (Target: c, Size: Cleaner.Measure(c))).ToList());
        if (generation != _adviceCleanGeneration) return;
        _adviceJunk = sizes.Where(x => !x.Target.Recycle).Sum(x => Math.Max(0, x.Size));
        _adviceRecycle = sizes.Where(x => x.Target.Recycle).Sum(x => Math.Max(0, x.Size));
        _homeJunk = _adviceJunk;
        RenderCheckAdvice();
    }

    private async Task CheckAsync()
    {
        if (_busy) return;
        ++_adviceCleanGeneration;
        long junk = 0, recycle = 0; int recTotal = 0; bool chosen = false;
        List<string> todo = new(), gameTodo = new();
        List<StartupItem> startup = new();
        SysInfo s = null;
        if (!await Busy("Проверяю компьютер…", () =>
        {
            s = SysInfo.Collect();
            _ctx.RefreshAppx();
            foreach (var t in _clean.Where(c => c.Default && !c.Dism)) { t.Size = Cleaner.Measure(t); _cleanMeasuredAt[t.Id] = DateTime.Now; if (t.Size > 0) junk += t.Size; }
            var bin = _clean.FirstOrDefault(c => c.Recycle);
            if (bin != null) recycle = Cleaner.Measure(bin);
            recTotal = _tweaks.Count(t => t.Rec && t.Applicable(s));
            todo = _tweaks.Where(t => t.Rec && t.Applicable(s) && !t.IsApplied(_ctx)).Select(t => t.Id).ToList();
            chosen = AdvicePolicy.TweaksChosen(Backup.List().Select(b => b.Data));
            // только то, что действительно включит режим «Игровой» (рискованное — лишь в «Настройках Windows» для опытных),
            // иначе совет обещает больше, чем сделает кнопка «Оптимизировать»
            var gameIds = Catalog.Preset("gaming", _tweaks).ToHashSet();
            gameTodo = _tweaks.Where(t => gameIds.Contains(t.Id) && !t.Rec && t.Risk < 2 && t.CanRevert && t.Applicable(s) && !t.IsApplied(_ctx)).Select(t => t.Id).ToList();
            if (chosen) { todo.Clear(); gameTodo.Clear(); }   // уже выбирал сам — остальное не навязываем
            try { startup = Startup.List(); } catch (Exception ex) { Log.Warn("Автозагрузка: " + ex.Message); }
        })) return;
        if (s == null) { Warn("Не получилось проверить компьютер. Попробуйте ещё раз."); return; }
        _ctx.S = s;
        HomeSys.Text = s.OneLine() + "  •  " + s.OS;
        BuildHwTags();
        _homeChecked = true; _homeJunk = junk;
        _adviceTodo = todo; _adviceGameTodo = gameTodo;
        _adviceJunk = junk; _adviceRecycle = recycle; _adviceStartup = startup;
        _settings.CheckDismissed = false;
        RenderCheckAdvice();
        BuildSystem();
        BuildCleanList();
        _settings.CheckDismissed = false; _settings.Save();   // новая проверка — советы снова актуальны
        UpdateIssueHealth();
        SetHealth("tweaks", todo.Count > 0 ? 1 : 0, $"Не применено {todo.Count} {Plural(todo.Count, "рекомендуемая настройка", "рекомендуемые настройки", "рекомендуемых настроек")}. Подробнее на «Главной»");
        var worst = WorstHealth();
        SetStatus(worst.Sev >= 2 ? "Проверка завершена: есть проблемы" : worst.Sev == 1 ? "Проверка завершена: есть советы" : "Проверка завершена: всё в порядке");
        Step1Text.Text = $"Проверено в {DateTime.Now:HH:mm}.";
        BtnCheck.Style = (Style)FindResource("Outline");
        BtnCheck.Content = "Проверить ещё раз";
        BtnGo.Style = (Style)FindResource("Big");
        SetMark(1, 2); SetMark(2, 1); SetMark(3, 1);
        await BuildTweaksAsync();
        UpdatePlan();
    }

    /// <summary>Кнопка «Понятно» под итогами проверки: советы скрываются до следующей проверки, кружок внизу — обычный.</summary>
    private void AddDismiss()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var ok = new Button { Content = "Понятно", Padding = new Thickness(14, 6, 14, 6) };
        ok.Click += (_, _) =>
        {
            _settings.CheckDismissed = true; _settings.Save();
            CheckResults.Visibility = Visibility.Collapsed;
            HomeTipsEmpty.Text = "Советов нет.";
            HomeTipsEmpty.Visibility = Visibility.Visible;
            SetHealth("issues", 0, null); SetHealth("tweaks", 0, null);
        };
        row.Children.Add(ok);
        CheckResults.Children.Add(row);
    }

    /// <summary>Кружок внизу по найденным проблемам: красный — есть важные, жёлтый — есть советы.</summary>
    private void UpdateIssueHealth()
    {
        if (_settings.CheckDismissed) { SetHealth("issues", 0, null); return; }   // «Понятно» — до следующей проверки
        var iss = AdvicePolicy.VisibleIssues(_settings, _ctx.S?.Issues ?? new()).ToList();
        var bad = iss.Where(i => i.Severity >= 2).ToList();
        var tips = iss.Count(i => i.Severity == 1);
        // До полной проверки советы из сведений о компьютере видны только на вкладке «Мой компьютер».
        // Карточка «Советы» не должна говорить «нажмите проверить», когда вверху уже «Есть совет».
        if (!_homeChecked && CheckResults.Visibility != Visibility.Visible)
        {
            int found = bad.Count + tips;
            HomeTipsEmpty.Text = found == 0
                ? "Чтобы узнать, что стоит исправить и где, нажмите «Проверить компьютер»."
                : $"{(found == 1 ? "Один совет уже есть" : $"Советы уже есть: {found}")} на вкладке «Мой компьютер». Чтобы найти всё, нажмите «Проверить компьютер».";
        }
        if (bad.Count == 1) SetHealth("issues", 2, bad[0].Text);
        else if (bad.Count > 1) SetHealth("issues", 2, $"Найдено проблем: {bad.Count}. Подробнее на «Главной»");
        else SetHealth("issues", tips > 0 ? 1 : 0, $"Есть {tips} {Plural(tips, "совет", "совета", "советов")}. Подробнее на «Главной»");
    }

    private async Task OptimizeAsync()
    {
        if (_busy || _ctx.S == null) return;
        string profile = ProfGame.IsChecked == true ? "gaming" : ProfMin.IsChecked == true ? "minimal" : "recommended";
        var ids = Catalog.Preset(profile, _tweaks).ToHashSet();
        // необратимое (удаление приложений: debloat, widgets…) — только вручную, как в мастере первого запуска
        var list = _tweaks.Where(t => ids.Contains(t.Id) && t.Applicable(_ctx.S) && t.Risk < 2 && t.CanRevert).ToList();
        // точка восстановления — только если будут включены настройки «Осторожно»: обычные Merno откатывает сам
        bool doClean = HomeClean.IsChecked == true,
             doRp = HomeRP.IsChecked == true && HomeRP.Visibility == Visibility.Visible && list.Any(t => t.Risk >= 1 && !t.IsApplied(_ctx));
        var cleanList = doClean ? _clean.Where(c => c.Default).ToList() : new();

        if (!Ask($"Будет применено: {list.Count} {Plural(list.Count, "настройка", "настройки", "настроек")}{(doClean ? ", а также очистка" : "")}.\n\n" +
                 "Сохранённые настройки можно вернуть в «Откате изменений». Очистку файлов и удаление приложений отменить нельзя.\nСохраните документы и закройте игры.\n\nОптимизировать?",
                 "Merno", MessageBoxImage.Question, "Оптимизировать")) return;

        GoDone.Visibility = Visibility.Collapsed;
        GoProgress.Visibility = Visibility.Visible;
        GoBar.Value = 0;
        int total = list.Count + cleanList.Count + (doRp ? 1 : 0) + 1, step = 0;
        void Step(string text) => Dispatcher.Invoke(() => { GoStep.Text = text; GoBar.Value = 100.0 * step / total; GoPercent.Text = $"{(int)GoBar.Value}%"; });

        var applied = new List<Tweak>(); long freed = 0; bool rpOk = true; string rpLine = "";
        if (!await Busy("Оптимизирую…", () =>
        {
            Log.Head($"Оптимизация: профиль «{profile}»");
            if (doRp) { Step("Создаю точку восстановления…"); rpOk = Backup.CreateRestorePoint(); if (!rpOk) throw new IOException("Точка восстановления не создана. Оптимизация остановлена; проверьте защиту системы или снимите эту галочку."); rpLine = RpLine(); step++; }
            using var backupSession = Backup.Session();
            foreach (var t in list)
            {
                Step(t.Name);
                try
                {
                    if (t.IsApplied(_ctx)) { step++; continue; }
                    t.Apply(_ctx); applied.Add(t); Log.Ok(t.Name);
                }
                catch (Exception ex) { Log.Warn($"{t.Name}: {ex.Message}"); }
                step++;
            }
            Backup.Save();
            foreach (var c in cleanList) { Step("Чищу: " + c.Name); freed += Cleaner.Clean(c); step++; }
            Step("Сохраняю отчёт…");
            _ctx.RefreshAppx();
            _lastReport = Report.Write(_ctx.S, applied, freed);
            step++; Step("Готово");
        })) { GoProgress.Visibility = Visibility.Collapsed; return; }

        GoProgress.Visibility = Visibility.Collapsed;
        GoDone.Visibility = Visibility.Visible; BuildHomeRecent();
        GoDoneTitle.Text = applied.Count > 0 || freed > 0 ? "Готово" : "Всё уже было настроено";
        GoDoneText.Text = $"Применено настроек: {applied.Count}." + (doClean ? $" Освобождено места: {Sh.Size(freed)}." : "") +
                          (doRp && !rpOk ? " Точку восстановления создать не получилось, но «Откат изменений» работает." : "") +
                          (doRp && rpOk ? rpLine : "") +
                          "\nОтчёт сохранён на рабочем столе. Перезагрузите компьютер, чтобы всё заработало.";
        SetMark(1, 2); SetMark(2, 2); SetMark(3, 2);
        await BuildTweaksAsync();
        if (_health.ContainsKey("tweaks"))
        {
            int left = 0;
            // после «Начать» человек уже выбрал сам — остальные рекомендуемые не навязываем (AdvicePolicy.TweaksChosen)
            await Task.Run(() => left = AdvicePolicy.TweaksChosen(Backup.List().Select(b => b.Data)) ? 0
                : _tweaks.Count(t => t.Rec && t.Applicable(_ctx.S) && !t.IsApplied(_ctx)));
            SetHealth("tweaks", left > 0 ? 1 : 0, $"Не применено {left} {Plural(left, "рекомендуемая настройка", "рекомендуемые настройки", "рекомендуемых настроек")}. Подробнее на «Главной»");
        }
        BuildCleanList();
        if (doClean) await RefreshCleanupAdviceAsync();
        UpdatePlan();
        if (_ready) BuildStartup();
    }

    private static string RpLine()
    {
        var (found, name, when) = Backup.LastRestorePoint();
        return found ? $"\nТочка восстановления создана в {when:HH:mm}." : "";
    }

    /// <summary>Состояние шага: 0 — впереди, 1 — текущий, 2 — выполнен.</summary>
    private void SetMark(int n, int state)
    {
        var (mark, text) = n switch { 1 => (Mark1, Mark1Text), 2 => (Mark2, Mark2Text), _ => (Mark3, Mark3Text) };
        mark.BorderBrush = state switch { 2 => B("Ok"), 1 => B("Accent"), _ => B("Line") };
        text.Foreground = state switch { 2 => B("Ok"), 1 => B("Accent"), _ => B("Sub") };
        if (state == 2) { text.Text = "\uE73E"; text.FontFamily = (FontFamily)FindResource("Icons"); text.FontSize = 13; }
        else { text.Text = n.ToString(); text.FontFamily = (FontFamily)FindResource("UiFont"); text.FontSize = 14; }
    }

    /// <summary>«О компьютере» на Главной: строки «значок — что — какое».</summary>
    private void BuildHwTags()
    {
        HwTags.Children.Clear();
        var s = _ctx.S;
        if (s == null) { HwTags.Children.Add(new TextBlock { Text = "Собираю сведения…", Style = (Style)FindResource("SubText") }); return; }
        string cpu = System.Text.RegularExpressions.Regex.Replace(s.Cpu ?? "", @"\s+\d+-Core Processor|\(R\)|\(TM\)|CPU @.*$", "").Trim();
        string os = System.Text.RegularExpressions.Regex.Replace(s.OS ?? "", @"\s*\(сборка.*\)", "").Trim();
        var rows = new List<(string Glyph, string Name, string Value, string Sub)>
        {
            ("", "Windows", os, null),
            ("", "Процессор", cpu, s.Cores > 0 ? $"{s.Cores} {Plural(s.Cores, "ядро", "ядра", "ядер")}, {s.Threads} {Plural(s.Threads, "поток", "потока", "потоков")}" : null),
            ("", "Видеокарта", string.IsNullOrEmpty(s.Gpu) ? "Встроенная графика" : s.Gpu, ShortDriver(s.GpuDriver)),
            ("", "Память", s.RamGB > 0 ? $"{s.RamGB} ГБ" : null, s.RamSpeed > 0 ? $"{s.RamSpeed} МГц" + (s.RamSticks > 0 ? $", {s.RamSticks} {Plural(s.RamSticks, "планка", "планки", "планок")}" : "") : null),
            ("", "Системный диск", s.SysDisk == SysInfo.UnknownDisk ? null : s.SysDisk, FreeOnSystem()),
            (s.Laptop ? "" : "", "Компьютер", s.Laptop ? "Ноутбук" : "Настольный компьютер", ShortBoard(s.Board)),
        };
        foreach (var (glyph, name, value, sub) in rows)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "Unknown") continue;
            var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var ic = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 15, Foreground = B("Accent"), Width = 28, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
            DockPanel.SetDock(ic, Dock.Left);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = name, FontSize = 12.5, Foreground = B("Sub") });
            // длинное значение — «…», нажатие раскрывает его целиком (без всплывающей подсказки)
            var val = new ExpandText(value);
            val.Inner.FontSize = 14; val.Inner.Foreground = B("Text");
            sp.Children.Add(val);
            if (!string.IsNullOrWhiteSpace(sub)) { var st = new ExpandText(sub); st.Inner.FontSize = 12.5; sp.Children.Add(st); }
            dp.Children.Add(ic); dp.Children.Add(sp);
            HwTags.Children.Add(dp);
        }
        var more = new TextBlock { Margin = new Thickness(28, 0, 0, 4) };
        var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Подробнее о компьютере →")) { Foreground = B("Accent") };
        h.Click += (_, _) => HomeTabPc.IsChecked = true;
        more.Inlines.Add(h);
        HwTags.Children.Add(more);
    }

    /// <summary>«NVIDIA GeForce RTX 4060: 566.36 (в Windows: 32.0.15.6636) от 03.12.2024» → «драйвер 566.36 от 03.12.2024».</summary>
    private static string ShortDriver(string d)
    {
        if (string.IsNullOrWhiteSpace(d)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(d, @"(\d{3}\.\d{2})(?!\.\d)");
        var date = System.Text.RegularExpressions.Regex.Match(d, @"\d{2}\.\d{2}\.\d{4}");
        string ver = m.Success ? m.Value : System.Text.RegularExpressions.Regex.Match(d, @"\d+(\.\d+)+").Value;
        return string.IsNullOrEmpty(ver) ? null : "драйвер " + ver + (date.Success ? " от " + date.Value : "");
    }

    /// <summary>Плата без длинных юридических названий: «Micro-Star International Co., Ltd. PRO B550M-P» → «MSI PRO B550M-P».</summary>
    private static string ShortBoard(string b)
    {
        if (string.IsNullOrWhiteSpace(b)) return null;
        foreach (var (from, to) in new[] { ("Micro-Star International Co., Ltd.", "MSI"), ("ASUSTeK COMPUTER INC.", "ASUS"), ("Gigabyte Technology Co., Ltd.", "Gigabyte"), ("ASRock", "ASRock") })
            b = b.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        return System.Text.RegularExpressions.Regex.Replace(b, @"\s*\(MS-\w+\)", "").Trim();
    }

    private static string FreeOnSystem()
    {
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
            return Sh.FreeOf(d.AvailableFreeSpace, d.TotalSize);
        }
        catch { return null; }
    }

    private bool _homeChecked;

    private long _homeJunk = -1;

    /// <summary>Строка состояния вверху Главной: одна фраза и цвет — по найденным проблемам и советам.</summary>
    private void UpdateHomeState()
    {
        if (HomeStateText == null) return;
        var w = WorstHealth();
        int bad = _health.Values.Count(x => x.Sev >= 2), tips = _health.Values.Count(x => x.Sev == 1);
        string text, brush;
        if (w.Sev >= 2) { text = bad == 1 ? "Есть проблема" : $"Есть проблемы: {bad}"; brush = "Bad"; }
        else if (w.Sev == 1) { text = tips == 1 ? "Есть совет" : $"Есть советы: {tips}"; brush = "Warn"; }
        else if (_homeChecked) { text = "Всё в порядке"; brush = "Ok"; }
        else { text = "Компьютер ещё не проверен"; brush = "Sub"; }
        HomeStateText.Text = text;
        // без значка: состояние — цветом самой надписи
        HomeStateText.Foreground = B(brush == "Sub" ? "Text" : brush);
    }

    /// <summary>«Последние действия»: последние оптимизации из резервных копий (их же можно отменить в «Откате»).</summary>
    private void BuildHomeRecent()
    {
        HomeRecent.Children.Clear();
        List<(string File, BackupData Data)> list;
        try { list = Backup.List(); } catch { list = new(); }
        var names = _tweaks.ToDictionary(t => t.Id, t => t.Name);
        foreach (var (file, d) in list.Where(x => x.Data != null).Take(4))
        {
            int n = d.Tweaks.Count;
            string what = d.Title != null ? d.Title : n == 0 ? "Изменения в Windows" : n == 1 && names.TryGetValue(d.Tweaks[0], out var one) ? one : $"Применено {n} {Plural(n, "настройка", "настройки", "настроек")}";
            string when = d.Date.Date == DateTime.Today ? $"Сегодня {d.Date:HH:mm}" : d.Date.Date == DateTime.Today.AddDays(-1) ? $"Вчера {d.Date:HH:mm}" : d.Date.ToString("dd.MM.yyyy HH:mm");
            var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
            var ok = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12, Foreground = B("Ok"), Width = 28, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(ok, Dock.Left);
            var t = new TextBlock { Text = when, FontSize = 12.5, Foreground = B("Sub"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(t, Dock.Right);
            dp.Children.Add(ok); dp.Children.Add(t);
            var whatTb = new ExpandText(what) { VerticalAlignment = VerticalAlignment.Center };
            whatTb.Inner.FontSize = 13.5; whatTb.Inner.Foreground = B("Text");
            dp.Children.Add(whatTb);
            HomeRecent.Children.Add(dp);
        }
        if (HomeRecent.Children.Count == 0)
            HomeRecent.Children.Add(new TextBlock { Text = "Пока ничего не менялось.\nПосле оптимизации здесь появится запись. Сохранённые настройки можно будет вернуть.", Style = (Style)FindResource("SubText"), TextWrapping = TextWrapping.Wrap });
    }

    /// <summary>Прозрачность: заранее показать, что именно поменяет выбранный режим.</summary>
    private void UpdatePlan()
    {
        if (_ctx.S == null || PlanList == null) return;
        string profile = ProfGame.IsChecked == true ? "gaming" : ProfMin.IsChecked == true ? "minimal" : "recommended";
        var ids = Catalog.Preset(profile, _tweaks).ToHashSet();
        var list = _tweaks.Where(t => ids.Contains(t.Id) && t.Risk < 2 && t.CanRevert && _state.TryGetValue(t.Id, out var st) && st.ok && !st.applied).ToList();
        // всё из режима уже включено — раскрывать нечего, пункт не показываем
        PlanExpander.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        // переключатель точки восстановления — только когда среди изменений есть «Осторожно»
        HomeRP.Visibility = list.Any(t => t.Risk >= 1) ? Visibility.Visible : Visibility.Collapsed;
        if (list.Count == 0) PlanExpander.IsExpanded = false;
        PlanHeader.Text = $"Что будет изменено: {list.Count} {Plural(list.Count, "настройка", "настройки", "настроек")}";
        var est = new List<string>();
        if (list.Count > 0) est.Add($"{list.Count} {Plural(list.Count, "настройка", "настройки", "настроек")}");
        if (HomeClean.IsChecked == true) est.Add(_homeJunk > 0 ? $"освободится около {Sh.Size(_homeJunk)}" : "очистка мусора");
        HomeEstimate.Text = (est.Count > 0 ? char.ToUpper(string.Join(", ", est)[0]) + string.Join(", ", est)[1..] + ". " : "") + "Это займёт до 10 минут. Сохраните документы и закройте игры.";
        PlanList.Children.Clear();
        foreach (var t in list)
            PlanList.Children.Add(new TextBlock { Text = "• " + t.Name, Foreground = B("Sub"), FontSize = 13, Margin = new Thickness(18, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
    }
}
