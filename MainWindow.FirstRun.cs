// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// «Первый запуск» — мастер «после переустановки Windows»: шаги по порядку, любой можно пропустить,
// в конце — итог и одна кнопка «Начать». Всё делается за один проход, отменить настройки можно в «Откате».
public partial class MainWindow
{
    private sealed class FrStep
    {
        public string Title, Sub;
        public StackPanel Body = new();
        public CheckBox Skip;   // «Пропустить этот шаг»
    }

    private readonly List<FrStep> _frSteps = new();
    private int _frIndex;
    private readonly List<(CheckBox Cb, Tweak T)> _frLook = new();      // шаг «Внешний вид»
    private readonly List<(CheckBox Cb, Tweak T)> _frDebloat = new();   // шаг «Лишние приложения»
    private readonly List<(CheckBox Cb, AppItem A)> _frApps = new();
    private readonly List<(CheckBox Cb, CleanTarget C)> _frClean = new();
    private CheckBox _frRp, _frDrivers, _frSound;
    private RadioButton _frModeRec, _frModeGame, _frModeMin;
    private TextBlock _frModeInfo;
    private StackPanel _frBody, _frNav;
    private TextBlock _frStepTitle, _frStepSub, _frDots, _frProgress, _frResult;
    private Button _frBack, _frNext, _frGo;
    private bool _frBuilt, _frBuilding;

    private void InitFirstRun()
    {
        // заголовок и подзаголовок — в шапке страницы (MainWindow.xaml, каркас PageShell)
        _frDots = new TextBlock { Foreground = B("Accent"), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        _frStepTitle = new TextBlock { Style = (Style)FindResource("H2"), FontSize = 20 };
        _frStepSub = new TextBlock { Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 4, 0, 10), TextWrapping = TextWrapping.Wrap };
        FirstRunPanel.Children.Add(_frDots); FirstRunPanel.Children.Add(_frStepTitle); FirstRunPanel.Children.Add(_frStepSub);
        _frBody = new StackPanel();
        FirstRunPanel.Children.Add(_frBody);
        _frBody.Children.Add(new TextBlock { Text = "Готовлю шаги…", Foreground = B("Sub") });

        _frNav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
        _frBack = new Button { Content = "Назад", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 10, 0) };
        _frNext = new Button { Content = "Дальше", Style = (Style)FindResource("Primary"), Padding = new Thickness(22, 8, 22, 8), Margin = new Thickness(0, 0, 10, 0) };
        _frGo = new Button { Content = "Начать", Style = (Style)FindResource("Big"), Visibility = Visibility.Collapsed };
        _frBack.Click += (_, _) => FrShow(_frIndex - 1);
        _frNext.Click += (_, _) => FrShow(_frIndex + 1);
        _frGo.Click += (_, _) => Fire(FirstRunGoAsync, "Первый запуск");
        _actionButtons.Add(_frGo);
        _frNav.Children.Add(_frBack); _frNav.Children.Add(_frNext); _frNav.Children.Add(_frGo);
        _frNav.Visibility = Visibility.Collapsed;
        FirstRunPanel.Children.Add(_frNav);
        _frProgress = new TextBlock { Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        FirstRunPanel.Children.Add(_frProgress);
        _frResult = new TextBlock { Style = (Style)FindResource("SubText"), Foreground = B("Text"), Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed, LineHeight = 21, TextWrapping = TextWrapping.Wrap };
        FirstRunPanel.Children.Add(_frResult);
    }

    private void OnFirstRunShown()
    {
        // мастеру нужны настройки и сведения о ПК — строим после загрузки (InitAsync зовёт ещё раз)
        if (_ready && !_frBuilt && !_frBuilding) Fire(BuildFirstRunAsync, "Настройка Windows");
    }

    /// <summary>Набор настроек выбранного режима, без «Внешнего вида» и без необратимого (удаление приложений — отдельный шаг).</summary>
    private List<Tweak> FrModeTweaks()
    {
        string mode = _frModeGame?.IsChecked == true ? "gaming" : _frModeMin?.IsChecked == true ? "minimal" : "rec";
        var ids = Catalog.Preset(mode, _tweaks).ToHashSet();
        return _tweaks.Where(t => ids.Contains(t.Id) && t.CanRevert && t.Risk < 2 && t.Cat != "Защита Windows" && !t.Id.StartsWith("conv_")
                                  && t.Id != "classic_menu" && t.Applicable(_ctx.S)).ToList();
    }

    private async Task BuildFirstRunAsync()
    {
        if (_frBuilding) return;
        _frBuilding = true;
        int gen = ++_genFr;
        try
        {
            _frNav.Visibility = Visibility.Collapsed;
            _frBody.Children.Clear();
            _frBody.Children.Add(new TextBlock { Text = "Готовлю шаги…", Foreground = B("Sub") });
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (_ctx.S == null)
            {
                if (waited.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Сведения о компьютере не собраны за 60 секунд");
                await Task.Delay(300);
            }
            var lookTweaks = _tweaks.Where(t => (t.Id.StartsWith("conv_") || t.Id is "classic_menu" or "file_ext" or "this_pc") && t.Applicable(_ctx.S)).ToList();
            var debloat = _tweaks.Where(t => t.Cat == "Приложения" && t.Applicable(_ctx.S)).ToList();
            var apps = Apps.Load().Where(a => a.tab == null && !a.tuning && Apps.Fits(a, _ctx.S) && !string.IsNullOrEmpty(a.winget) && !Apps.IsYandex(a.name) && !Apps.IsYandex(a.winget)).ToList();
            var applied = new HashSet<string>();
            HashSet<string> installed = new();
            List<MonitorInfo> mons = new();
            List<SoundDevice> sound = new();
            await Task.Run(() =>
            {
                foreach (var t in lookTweaks.Concat(debloat)) try { if (t.IsApplied(_ctx)) applied.Add(t.Id); } catch { }
                try { installed = Apps.Installed(apps); } catch { }
                try { mons = Display.Monitors(); } catch { }
                try { sound = Sound.List(); } catch { }
            });
            if (gen != _genFr) return;

            _frSteps.Clear(); _frLook.Clear(); _frDebloat.Clear(); _frApps.Clear(); _frClean.Clear();
            FrStep Step(string title, string sub, bool skippable = true)
            {
                var s = new FrStep { Title = title, Sub = sub };
                if (skippable)
                {
                    s.Skip = new CheckBox { Content = "Пропустить этот шаг", Style = (Style)FindResource("Check"), Margin = new Thickness(0, 14, 0, 0), Foreground = B("Sub") };
                    s.Skip.Checked += (_, _) => s.Body.Opacity = 0.4; s.Skip.Unchecked += (_, _) => s.Body.Opacity = 1;
                }
                _frSteps.Add(s);
                return s;
            }

            // 1. Оптимизация
            var s1 = Step("Оптимизация Windows", "Выберите режим. Всё, что изменится, можно вернуть в «Настройки» → «Откат изменений».");
            var seg = new Border { Background = B("Panel"), CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            var segSp = new StackPanel { Orientation = Orientation.Horizontal };
            _frModeRec = new RadioButton { Style = (Style)FindResource("Seg"), GroupName = "frMode", Content = "Обычный", IsChecked = true, ToolTip = "Подойдёт почти всем." };
            _frModeGame = new RadioButton { Style = (Style)FindResource("Seg"), GroupName = "frMode", Content = "Игровой", ToolTip = "Для плавных игр." };
            _frModeMin = new RadioButton { Style = (Style)FindResource("Seg"), GroupName = "frMode", Content = "Бережный", ToolTip = "Только самое безопасное." };
            segSp.Children.Add(_frModeRec); segSp.Children.Add(_frModeGame); segSp.Children.Add(_frModeMin); seg.Child = segSp;
            s1.Body.Children.Add(seg);
            _frModeInfo = new TextBlock { Style = (Style)FindResource("SubText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            s1.Body.Children.Add(_frModeInfo);
            void ModeInfo()
            {
                var list = FrModeTweaks();
                _frModeInfo.Text = $"Будет включено настроек: {list.Count}. " + string.Join(", ", list.Take(6).Select(t => "«" + t.Name + "»")) + (list.Count > 6 ? " и другие." : ".");
            }
            foreach (var rb in new[] { _frModeRec, _frModeGame, _frModeMin }) rb.Checked += (_, _) => ModeInfo();
            ModeInfo();
            _frRp = FrCheck("Точка восстановления перед важными изменениями", "Только если будут настройки «Осторожно» или драйверы. Обычные изменения можно отменить и без неё.", true, out var rpRow);
            s1.Body.Children.Add(rpRow);

            // 2. Программы
            var s2 = Step("Программы", "Отмечено то, что нужно почти всем. Снимите лишнее и добавьте своё. Выбранные программы установятся в конце. Остальные программы есть в разделе «Программы».");
            var missing = apps.Where(a => !installed.Contains(a.name)).ToList();
            if (missing.Count == 0) s2.Body.Children.Add(new TextBlock { Text = "Все программы из списка уже стоят.", Style = (Style)FindResource("SubText") });
            // как в «Программах»: значок, название, одна строка о главном; рекомендуемые группы — первыми
            foreach (var grp in missing.GroupBy(a => a.category ?? "Другое").OrderByDescending(g => g.Any(a => a.rec)))
            {
                s2.Body.Children.Add(FrHead(grp.Key));
                foreach (var a in grp.OrderByDescending(a => a.rec))
                {
                    var cb = FrAppRow(a, out var row);
                    _frApps.Add((cb, a)); s2.Body.Children.Add(row);
                }
            }

            // 3. Лишние приложения
            var s3 = Step("Лишние приложения Windows", "Удаление нельзя отменить в «Откате изменений». Некоторые приложения могут отсутствовать в Microsoft Store.");
            var debTodo = debloat.Where(t => !applied.Contains(t.Id)).ToList();
            if (debTodo.Count == 0) s3.Body.Children.Add(new TextBlock { Text = "Лишнее уже удалено.", Style = (Style)FindResource("SubText") });
            foreach (var t in debTodo)
            {
                var cb = FrCheck(t.Name, t.Desc, t.Id == "debloat", out var row);
                _frDebloat.Add((cb, t)); s3.Body.Children.Add(row);
            }

            // 4. Внешний вид
            // совет «как в Windows 10» — только на Windows 11: на «десятке» он бессмыслен
            var s4 = Step("Внешний вид", _ctx.S?.IsWin11 == true
                ? "Как будет выглядеть Windows. Если хотите панель задач и «Пуск» как в Windows 10, найдите StartAllBack и ExplorerPatcher в «Программах»."
                : "Как будет выглядеть Windows.");
            var lookTodo = lookTweaks.Where(t => !applied.Contains(t.Id)).ToList();
            if (lookTodo.Count == 0) s4.Body.Children.Add(new TextBlock { Text = "Всё уже настроено.", Style = (Style)FindResource("SubText") });
            foreach (var t in lookTodo)
            {
                var cb = FrCheck(t.Name, t.Desc, t.Id is "file_ext" or "this_pc", out var row);
                _frLook.Add((cb, t)); s4.Body.Children.Add(row);
            }

            // 5. Драйверы и монитор
            var s5 = Step("Драйверы и монитор", "Windows сама подберёт драйверы для большинства устройств. Видеокарту лучше обновить с сайта производителя. Ссылка есть в «Драйверах».");
            _frDrivers = FrCheck("Поставить драйверы через «Обновления Windows»", "Чипсет, сеть, звук и прочее. Займёт несколько минут.", true, out var drvRow);
            s5.Body.Children.Add(drvRow);
            // звук видеокарты (HDMI/DisplayPort): после драйверов Windows часто переключает звук на монитор без динамиков
            // предлагаем, только если кроме видеокарты есть другой работающий звук — иначе компьютер останется без звука
            if (Sound.VideoDevices(sound).Any(d => !d.Disabled && d.Present) && sound.Any(d => !d.Video && !d.Disabled && d.Present))
            {
                _frSound = FrCheck("Не переключать звук на монитор", "Merno выключит звук видеокарты, чтобы колонки и наушники не пропадали после установки драйверов. Вернуть можно в «Драйверы» → «Звук».", false, out var sRow);
                s5.Body.Children.Add(sRow);
            }
            else _frSound = null;
            foreach (var m in mons.Where(m => m.CanFaster))
            {
                var open = new Button { Content = "Открыть «Монитор»" };
                open.Click += (_, _) => ShowPageByName("Monitor");
                s5.Body.Children.Add(DrvRow($"{m.Name}: {m.Now}\u00A0Гц, можно поставить до {m.Max}\u00A0Гц", "Для более плавного изображения включите максимальную частоту. Это делается отдельно: так вы сразу увидите экран.", open, B("Warn")));
            }

            // Шага «Очистка» нет: после чистой установки Windows чистить почти нечего.
            // _frClean остаётся пустым — итог и запуск ниже просто его пропускают.

            // 6. Итог — заполняется при показе
            Step("Всё готово к запуску", "Проверьте список и нажмите «Начать». Merno выполнит выбранные действия, дождитесь завершения.", skippable: false);

            _frBuilt = true;
            _frNav.Visibility = Visibility.Visible;
            FrShow(0);
        }
        catch (Exception ex)
        {
            Log.Warn("Первый запуск: " + ex.Message); Log.Skip(ex.ToString());
            if (gen == _genFr)
                ShowError(_frBody, "Не получилось подготовить шаги. Попробуйте через минуту.", async () => { _frBuilding = false; await BuildFirstRunAsync(); });
        }
        finally { _frBuilding = false; }
    }

    private bool FrOn(FrStep s) => s.Skip?.IsChecked != true;

    private void FrShow(int i)
    {
        if (_frSteps.Count == 0) return;
        _frIndex = Math.Clamp(i, 0, _frSteps.Count - 1);
        var s = _frSteps[_frIndex];
        bool last = _frIndex == _frSteps.Count - 1;
        _frDots.Text = last ? "Итог" : $"Шаг {_frIndex + 1} из {_frSteps.Count - 1}";
        _frStepTitle.Text = s.Title;
        _frStepSub.Text = Typo.Nbsp(s.Sub);
        _frBody.Children.Clear();
        if (last) FrSummary(s);
        _frBody.Children.Add(s.Body);
        if (s.Skip != null) _frBody.Children.Add(s.Skip);
        _frBack.Visibility = _frIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        _frNext.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        _frGo.Visibility = last ? Visibility.Visible : Visibility.Collapsed;
        SetOwn(_frGo, last);
        // новый шаг — начало карточки «Настройка Windows» на виду (если она ушла выше края)
        if (PageHome.IsVisible)
            try
            {
                double top = HomeSetup.TransformToAncestor(PageHome).Transform(new Point(0, 0)).Y;
                if (top < 0) PageHome.ScrollToVerticalOffset(PageHome.VerticalOffset + top - 12);
            }
            catch { }
    }

    private void FrSummary(FrStep s)
    {
        s.Body.Children.Clear();
        var lines = new List<string>();
        if (FrOn(_frSteps[0]))
        {
            if (_frRp?.IsChecked == true) lines.Add("• Точка восстановления");
            string mode = _frModeGame.IsChecked == true ? "Игровой" : _frModeMin.IsChecked == true ? "Бережный" : "Обычный";
            lines.Add($"• Оптимизация, режим «{mode}»: до {FrModeTweaks().Count} настроек (уже включённые Merno пропустит)");
        }
        int apps = FrOn(_frSteps[1]) ? _frApps.Count(x => x.Cb.IsChecked == true) : 0;
        if (apps > 0) lines.Add($"• Установить программ: {apps}");
        int deb = FrOn(_frSteps[2]) ? _frDebloat.Count(x => x.Cb.IsChecked == true) : 0;
        if (deb > 0) lines.Add($"• Удалить: " + string.Join(", ", _frDebloat.Where(x => x.Cb.IsChecked == true).Select(x => x.T.Name.Replace("Удалить ", ""))));
        int look = FrOn(_frSteps[3]) ? _frLook.Count(x => x.Cb.IsChecked == true) : 0;
        if (look > 0) lines.Add($"• Внешний вид: {look} " + Plural(look, "настройка", "настройки", "настроек"));
        if (FrOn(_frSteps[4]) && _frDrivers?.IsChecked == true) lines.Add("• Драйверы через «Обновления Windows»");
        if (FrOn(_frSteps[4]) && _frSound?.IsChecked == true) lines.Add("• Выключить звук видеокарты (не переключать звук на монитор)");
        int cl = _frClean.Count > 0 && FrOn(_frSteps[5]) ? _frClean.Count(x => x.Cb.IsChecked == true) : 0;
        if (cl > 0) lines.Add($"• Очистка: {cl} " + Plural(cl, "пункт", "пункта", "пунктов"));
        s.Body.Children.Add(new TextBlock
        {
            Text = lines.Count > 0 ? string.Join("\n", lines) : "Ничего не выбрано. Вернитесь назад и отметьте нужное.",
            Foreground = B("Text"), FontSize = 14.5, LineHeight = 24, Margin = new Thickness(0, 4, 0, 0)
        });
    }

    private TextBlock FrHead(string text) =>
        new TextBlock { Text = text, Style = (Style)FindResource("H2"), FontSize = 16, Margin = new Thickness(0, 16, 0, 6) };

    /// <summary>Строка программы в мастере — в стиле раздела «Программы»: галочка, значок, название, первая фраза описания (целиком — в подсказке).</summary>
    private CheckBox FrAppRow(AppItem a, out Border row)
    {
        row = new Border { Style = (Style)FindResource("Row"), ToolTip = string.IsNullOrEmpty(a.desc) ? null : Typo.Nbsp(a.desc) };
        var dp = new DockPanel();
        var cb = new CheckBox { Style = (Style)FindResource("Check"), IsChecked = a.rec, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(cb, Dock.Left);
        var r = row;
        r.Cursor = System.Windows.Input.Cursors.Hand;
        r.MouseLeftButtonUp += (_, e) => { if (cb.IsEnabled && !(e.OriginalSource is FrameworkElement fe && IsInside(fe, cb))) cb.IsChecked = cb.IsChecked != true; };
        dp.Children.Add(cb);
        var icon = AppIcon(a);
        DockPanel.SetDock(icon, Dock.Left);
        dp.Children.Add(icon);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = a.name, FontWeight = FontWeights.SemiBold, FontSize = 14.5, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis };
        sp.Children.Add(new TitleLine(name, a.rec ? Chip("Рекомендуем", "Accent") : null));
        string first = FirstPhrase(a.desc);
        if (first.Length > 0)
            sp.Children.Add(new TextBlock { Text = Typo.Nbsp(first), Style = (Style)FindResource("SubText"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
        dp.Children.Add(sp);
        row.Child = dp;
        return cb;
    }

    /// <summary>Первая фраза описания: в мастере — только главное, подробности — в подсказке и в «Программах».</summary>
    internal static string FirstPhrase(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^.+?[.!?](?=\s|$)");
        return (m.Success ? m.Value : s).TrimEnd('.');
    }

    private CheckBox FrCheck(string title, string sub, bool on, out Border row)
    {
        row = new Border { Style = (Style)FindResource("Row") };
        var dp = new DockPanel();
        // галочка слева: в мастере ничего не меняется сразу — всё выполнится в конце, по кнопке «Начать»
        var cb = new CheckBox { Style = (Style)FindResource("Check"), IsChecked = on, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
        DockPanel.SetDock(cb, Dock.Left);
        var r = row;   // out-параметр нельзя захватить в лямбду
        r.Cursor = System.Windows.Input.Cursors.Hand;
        r.MouseLeftButtonUp += (_, e) => { if (cb.IsEnabled && !(e.OriginalSource is FrameworkElement fe && IsInside(fe, cb))) cb.IsChecked = cb.IsChecked != true; };
        dp.Children.Add(cb);
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(sub)) sp.Children.Add(new TextBlock { Text = Typo.Nbsp(sub), Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 3, 0, 0) });
        dp.Children.Add(sp);
        row.Child = dp;
        return cb;
    }

    private async Task FirstRunGoAsync()
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        bool rp = FrOn(_frSteps[0]) && _frRp?.IsChecked == true;
        var tweaks = new List<Tweak>();
        if (FrOn(_frSteps[0])) tweaks.AddRange(FrModeTweaks());
        if (FrOn(_frSteps[2])) tweaks.AddRange(_frDebloat.Where(x => x.Cb.IsChecked == true).Select(x => x.T));
        if (FrOn(_frSteps[3])) tweaks.AddRange(_frLook.Where(x => x.Cb.IsChecked == true).Select(x => x.T));
        var apps = FrOn(_frSteps[1]) ? _frApps.Where(x => x.Cb.IsChecked == true).Select(x => x.A).ToList() : new();
        var clean = _frClean.Count > 0 && FrOn(_frSteps[5]) ? _frClean.Where(x => x.Cb.IsChecked == true).Select(x => x.C).ToList() : new();
        bool drivers = FrOn(_frSteps[4]) && _frDrivers?.IsChecked == true;
        bool muteGpu = FrOn(_frSteps[4]) && _frSound?.IsChecked == true;
        if (tweaks.Count + apps.Count + clean.Count == 0 && !drivers && !muteGpu) { SetStatus("Ничего не выбрано"); return; }
        // точка восстановления — только перед рискованным: настройки «Осторожно» или установка драйверов
        rp = rp && (tweaks.Any(t => t.Risk >= 1) || drivers);
        if (!Ask("Начать? Это займёт от нескольких минут до получаса, в зависимости от программ и драйверов.\n\nСохранённые настройки можно вернуть: «Настройки» → «Откат изменений». Очистку файлов и удаление приложений отменить нельзя.",
                 "Первый запуск", MessageBoxImage.Question, "Начать")) return;

        int total = (rp ? 1 : 0) + tweaks.Count + apps.Count + clean.Count + (drivers ? 1 : 0), step = 0;
        void Step(string text) => Dispatcher.Invoke(() => { _frProgress.Text = $"{Math.Min(step + 1, total)} из {total}: {text}"; SetStatus(text); });
        _frNav.IsEnabled = false;
        _frResult.Visibility = Visibility.Collapsed;
        _frProgress.Visibility = Visibility.Visible;

        bool rpOk = false; int tOk = 0; long freed = 0; int dOk = 0; bool dReboot = false; string dErr = null;
        var aOk = new List<string>(); var aFail = new List<string>(); var tFail = new List<string>();
        bool noWinget = false;
        // полоска внизу: настройки — одним пунктом, программы — каждая отдельно
        var qn = new List<string>();
        int qRp = rp ? qn.Count : -1; if (rp) qn.Add("Точка восстановления");
        int qTw = tweaks.Count > 0 ? qn.Count : -1; if (tweaks.Count > 0) qn.Add($"Настройки Windows ({tweaks.Count})");
        int qApps = qn.Count; qn.AddRange(apps.Select(a => a.name));
        int qDrv = drivers ? qn.Count : -1; if (drivers) qn.Add("Драйверы из «Обновлений Windows»");
        int qSnd = muteGpu ? qn.Count : -1; if (muteGpu) qn.Add("Звук видеокарты");
        QueueStart("Настройка Windows", qn);
        bool ran = await Busy("Настраиваю компьютер…", () =>
        {
            Log.Head("Первый запуск");
            if (rp) { Step("Создаю точку восстановления…"); QueueSet(qRp, QState.Now); rpOk = Backup.CreateRestorePoint(); QueueSet(qRp, rpOk ? QState.Done : QState.Fail); if (!rpOk) throw new IOException("Точка восстановления не создана. Настройка остановлена; проверьте защиту системы или снимите эту галочку."); step++; }
            if (tweaks.Count > 0)
            {
                QueueSet(qTw, QState.Now);
                using var backupSession = Backup.Session();
                foreach (var t in tweaks)
                {
                    Step(t.Name);
                    try { if (!t.IsApplied(_ctx)) t.Apply(_ctx); tOk++; }
                    catch (Exception ex) { tFail.Add(t.Name); Log.Warn($"{t.Name}: {ex.Message}"); }
                    step++;
                }
                Backup.Save();
                _ctx.RefreshAppx();
                QueueSet(qTw, tFail.Count == 0 ? QState.Done : QState.Fail, tFail.Count > 0 ? "Не включились: " + string.Join(", ", tFail) : null);
            }
            if (apps.Count > 0)
            {
                noWinget = !Apps.WingetAvailable();
                if (noWinget)
                {
                    // Windows 10 без winget (или со старым): человек выбрал программы — ставим winget сам, без лишних вопросов
                    Step("Готовлю установку программ");
                    var (wOk, wErr) = WingetSetup.Install(null);
                    if (wOk) { Apps.ResetWinget(); noWinget = !Apps.WingetAvailable(); }
                    else Log.Warn("Установщик программ: " + wErr);
                }
                for (int ai = 0; ai < apps.Count; ai++)
                {
                    var a = apps[ai];
                    Step("Устанавливаю " + a.name);
                    QueueSet(qApps + ai, QState.Now);
                    if (noWinget) { aFail.Add(a.name); QueueSet(qApps + ai, QState.Fail, "Нет установщика программ"); }
                    else
                    {
                        try { var (ok, msg) = Apps.Install(a); (ok ? aOk : aFail).Add(a.name); QueueSet(qApps + ai, ok ? QState.Done : QState.Fail, ok ? null : msg); }
                        catch (Exception ex) { aFail.Add(a.name); Log.Warn($"{a.name}: {ex.Message}"); QueueSet(qApps + ai, QState.Fail, ForPerson(ex, "Не получилось установить")); }
                    }
                    step++;
                }
            }
            if (drivers)
            {
                Step("Ищу драйверы в «Обновлениях Windows»…");
                QueueSet(qDrv, QState.Now);
                var (ups, err) = Drivers.FindUpdates();
                if (err != null) { dErr = err; Log.Warn("Драйверы из «Обновлений Windows»: " + err); }
                else if (ups.Count == 0) Log.Write("Драйверы из «Обновлений Windows»: новых нет");
                else
                {
                    Log.Write($"Драйверы из «Обновлений Windows»: найдено {ups.Count}: " + string.Join("; ", ups.Select(u => u.Title)));
                    Step($"Ставлю драйверы ({ups.Count})…");
                    var r = Drivers.Install(ups.Select(u => u.Id));
                    dOk = r.Installed; dReboot = r.Reboot; dErr = r.Error;
                    if (r.Error == null) Log.Ok($"Драйверы: установлено {r.Installed} из {ups.Count}" + (r.Reboot ? ", нужна перезагрузка" : ""));
                }
                QueueSet(qDrv, dErr == null ? QState.Done : QState.Fail, dErr);
                step++;
            }
            if (muteGpu)
            {
                Step("Выключаю звук видеокарты…");
                QueueSet(qSnd, QState.Now);
                try
                {
                    var all = Sound.List();
                    // ещё раз проверяем перед выключением: звук должен остаться хотя бы на одном устройстве не от видеокарты
                    if (all.Any(d => !d.Video && !d.Disabled && d.Present))
                        foreach (var d in Sound.VideoDevices(all).Where(d => !d.Disabled && d.Present)) Sound.SetEnabled(d, false);
                    else Log.Skip("Звук видеокарты не выключен: другого звука в компьютере нет");
                }
                catch (Exception ex) { Log.Warn("Звук видеокарты: " + ex.Message); }
                QueueSet(qSnd, QState.Done);
            }
            foreach (var c in clean)
            {
                Step("Чищу: " + c.Name);
                try { freed += Cleaner.Clean(c); } catch (Exception ex) { Log.Warn($"{c.Name}: {ex.Message}"); }
                step++;
            }
            Log.Head("Готово");
        });
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        QueueFinish();
        _frNav.IsEnabled = true;
        _frProgress.Visibility = Visibility.Collapsed;
        if (!ran) return;

        var lines = new List<string> { "Готово." };
        if (rp) lines.Add(rpOk ? "Точка восстановления создана." : "Точку восстановления создать не удалось. Отменить настройки всё равно можно в «Откате изменений».");
        if (tweaks.Count > 0) lines.Add($"Применено настроек: {tOk} из {tweaks.Count}." + (tFail.Count > 0 ? " Не удалось: " + string.Join(", ", tFail) + "." : ""));
        if (apps.Count > 0)
        {
            if (noWinget) lines.Add("Программы не установлены. Скачайте «Установщик приложения» из Microsoft Store и попробуйте снова.");
            else lines.Add($"Установлено программ: {aOk.Count} из {apps.Count}." + (aFail.Count > 0 ? " Не удалось: " + string.Join(", ", aFail) + ". Попробуйте установить их в разделе «Программы»." : ""));
        }
        if (drivers) lines.Add(dErr != null ? "Драйверы проверить не удалось. Попробуйте позже в разделе «Драйверы»." : dOk > 0 ? $"Установлено драйверов: {dOk}." : "Новых драйверов от Windows нет.");
        if (clean.Count > 0) lines.Add($"Освобождено: {Sh.Size(freed)}.");
        if (tOk > 0 || dReboot) lines.Add("Перезагрузите компьютер, чтобы всё заработало.");
        _frResult.Text = string.Join("\n", lines);
        _frResult.Visibility = Visibility.Visible;
        SetStatus("Готово");
        if (!_settings.FirstRunDone) { _settings.FirstRunDone = true; _settings.Save(); }   // дальше «Главная» открывается на «Проверке»

        _appsBuilt = false;
        await BuildTweaksAsync();
        BuildCleanList();
        await RefreshCleanupAdviceAsync();
        UpdatePlan();
        _frBuilt = false;
        await BuildFirstRunAsync();
        _frResult.Visibility = Visibility.Visible;
    }
}
