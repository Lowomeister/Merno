// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Merno.Core;

namespace Merno;

public partial class MainWindow : Window
{
    private readonly Ctx _ctx = new();
    private List<Tweak> _tweaks = Catalog.All();
    private readonly Dictionary<string, CheckBox> _tweakChecks = new();
    private readonly Dictionary<string, FrameworkElement> _tweakRows = new();
    private readonly Dictionary<string, FrameworkElement> _catHeaders = new();
    private readonly List<CleanTarget> _clean = Cleaner.Targets();
    private readonly Dictionary<string, CheckBox> _cleanChecks = new();
    private readonly Dictionary<string, TextBlock> _cleanSizes = new();
    private List<StartupItem> _startup = new();
    private string _lastReport;
    private bool _busy, _ready;
    private System.Windows.Threading.DispatcherTimer _cleanTotalTimer;
    private readonly List<Button> _actionButtons = new();
    private Storyboard _spin;
    private AppSettings _settings = new();
    private bool _appsBuilt;
    private int _tourStep;
    // Обучение: «Главная» с «Настройкой Windows», «Поиск» и полоска хода. Цели — TourTarget, что открыть — TourOpen.
    private static readonly (string Title, string Text)[] TourSteps =
    {
        ("Проверьте компьютер", "Merno проверит компьютер, оценит объём мусора и найдёт устройства без драйверов. Настройки и файлы не изменятся."),
        ("Настройка Windows", "Настройте режим, программы, внешний вид и драйверы по шагам. Отметьте нужное и нажмите «Начать». Лишний шаг можно пропустить."),
        ("Мой компьютер", "Что внутри компьютера, мониторы и температура. Любую карточку можно скопировать."),
        ("Поиск", "Не помните, где что лежит? Введите несколько букв, чтобы открыть нужный раздел. Поиск также открывается сочетанием Ctrl+F."),
        ("Программы и компоненты", "Поставить нужное после переустановки Windows. Отметьте несколько программ, чтобы установить их по очереди. В «Компонентах» находятся библиотеки, необходимые для запуска игр."),
        ("Драйверы и обновления", "Драйверы видеокарты и других устройств, обновления Windows."),
        ("Строка внизу", "Что Merno делает сейчас. Во время установки, очистки или обновления появляется полоска с делениями; нажмите на неё, чтобы увидеть список."),
        ("Меню", "Эту кнопку можно нажать, чтобы свернуть меню в значки. Рядом находятся кнопки «Назад» и «Вперёд» для перехода между разделами."),
        ("Вопросы и связь", "В «Частых вопросах» описано, как начать и что делать при проблемах. Если не нашли ответ, напишите через «Связаться с нами»."),
        ("Откат изменений", "Любое изменение Merno можно вернуть. Журнал находится рядом, в «Настройках»."),
    };
    private Dictionary<string, (bool ok, bool applied)> _state = new();
    private bool _navSync;
    // поколения сборки списков: если страницу перестроили ещё раз, старая сборка молча выходит
    private int _genStore, _genMenu, _genUn, _genApps, _genRuntime, _genTweaks, _genFr;

    public MainWindow()
    {
        InitializeComponent();
        VerText.Text = "Версия " + App.Version;
        Title = $"Merno {App.Version}";
        Log.Written += OnLog;
        PageLog.IsVisibleChanged += (_, _) => { if (PageLog.IsVisible && _logStale) Dispatcher.BeginInvoke(RefreshLogBox, System.Windows.Threading.DispatcherPriority.Background); };
        Apps.Progress += OnAppProgress;   // ход установки/обновления программ — в строку состояния (MainWindow.Apps.cs)
        FitToWorkArea();
        SourceInitialized += (_, _) => DarkTitleBar(this);
        ApplyThemeLogo();   // значок в цветах темы (MainWindow.Look.cs)
        Loaded += (_, _) => Fire(InitAsync, "Запуск");
        // новое окно после смены темы — сразу там, где тему выбирали (MainWindow.Look.cs)
        Loaded += (_, _) => { if (OpenLookOnStart) { NavSettings.IsChecked = true; SetTabLook.IsChecked = true; } };
        if (ShotDir != null) Loaded += (_, _) => Fire(RunShotsAsync, "Снимки");   // служебный режим --shot (MainWindow.Shots.cs)
        foreach (var rb in new[] { ProfRec, ProfGame, ProfMin }) rb.Checked += (_, _) => UpdatePlan();
        SizeChanged += (_, _) => { if (Tour.Visibility == Visibility.Visible) ShowTour(); };

        // навигация
        foreach (var (nav, page) in new (RadioButton, FrameworkElement)[] {
            (NavTweaks, PageTweaks), (NavClean, PageClean), (NavStartup, PageStartup),
            (NavSystem, PageSystem), (NavTools, PageTools), (NavRollback, PageRollback), (NavHelp, PageHelp), (NavLog, PageLog), (NavSettings, PageSettings), (NavApps, PageApps), (NavRuntime, PageRuntime), (NavStore, PageStore), (NavMenu, PageMenu), (NavQuick, PageQuick), (NavSearch, PageSearch) })
        {
            var p = page;
            nav.Checked += (_, _) => { if (!_navSync) ShowPage(p); };
        }
        // «Драйверы» и «Обновления Windows» — одна страница с двумя частями
        NavDrivers.Checked += (_, _) => { if (_navSync) return; DrvTabDrv.IsChecked = true; ShowPage(PageDrivers); };
        NavUpdates.Checked += (_, _) => { if (_navSync) return; DrvTabWin.IsChecked = true; ShowPage(PageDrivers); };
        LinkHelpRun.Click += (_, _) => NavHelp.IsChecked = true;
        BtnLogo.Click += (_, _) => NavHome.IsChecked = true;
        BtnBack.Click += (_, _) => GoBack();
        BtnForward.Click += (_, _) => GoForward();
        // плитки «Инструментов»: столько колонок, сколько помещается (2–4), чтобы не было пустых полей и тесноты
        ToolList.SizeChanged += (_, e) => { int n = Math.Clamp((int)(e.NewSize.Width / 330), 2, 4); if (ToolList.Columns != n) ToolList.Columns = n; };
        // Alt+← / Alt+→ и боковые кнопки мыши «назад» / «вперёд»
        PreviewKeyDown += (_, e) =>
        {
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) == 0) return;
            if (e.SystemKey == System.Windows.Input.Key.Left) { GoBack(); e.Handled = true; }
            else if (e.SystemKey == System.Windows.Input.Key.Right) { GoForward(); e.Handled = true; }
        };
        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.XButton1) { GoBack(); e.Handled = true; }
            else if (e.ChangedButton == System.Windows.Input.MouseButton.XButton2) { GoForward(); e.Handled = true; }
        };
        // «Главная» делится на две части: оптимизация и сведения о компьютере.
        // У каждой части свой переключатель вверху; «Главная» в меню остаётся подсвеченной
        foreach (var (rb, pg) in new (RadioButton, FrameworkElement)[] {
            (HomeTabOpt, PageHome), (HomeTabPc, PageSystem), (HomeTabFirst, PageFirstRun),
            (SysTabOpt, PageHome), (SysTabPc, PageSystem), (SysTabFirst, PageFirstRun),
            (FrTabOpt, PageHome), (FrTabPc, PageSystem), (FrTabFirst, PageFirstRun) })
        { var target = pg; rb.Checked += (_, _) => ToHomePart(target); }
        // «Главная»: проверка и «Настройка Windows» на одной странице
        NavHome.Checked += (_, _) => { if (!_navSync) ShowPage(HomeStart()); };
        // «Главная» уже отмечена, пока открыт «Мой компьютер» — Checked не сработает, поэтому по клику
        NavHome.Click += (_, _) => { if (PageHome.Visibility != Visibility.Visible && PageFirstRun.Visibility != Visibility.Visible) ShowPage(HomeStart()); };
        BtnOpenRollback.Click += (_, _) => NavRollback.IsChecked = true;
        LinkHomeHistory.Click += (_, _) => NavRollback.IsChecked = true;
        HomeClean.Checked += (_, _) => UpdatePlan(); HomeClean.Unchecked += (_, _) => UpdatePlan();
        BtnOpenLog.Click += (_, _) => NavLog.IsChecked = true;
        // «Откат» и «Журнал» открываются из «Настроек» — «Настройки» в меню остаются подсвеченными
        void ToSettings() { if (NavSettings.IsChecked == true) ShowPage(PageSettings); else NavSettings.IsChecked = true; }
        BackFromRollback.Click += (_, _) => ToSettings();
        BackFromLog.Click += (_, _) => ToSettings();
        // «Настройки» в меню — открывают первую вкладку (если до этого была «Связаться с нами»)
        NavSettings.Click += (_, _) => { if (SetTabContact.IsChecked == true) SetTabMain.IsChecked = true; if (PageSettings.Visibility != Visibility.Visible) ShowPage(PageSettings); };
        // «Связаться с нами» в меню — это вкладка «Настроек», открываем сразу её
        NavContact.Checked += (_, _) => { if (_navSync) return; SetTabContact.IsChecked = true; if (PageSettings.Visibility != Visibility.Visible) ShowPage(PageSettings); };
        NavToggle.Click += (_, _) => SetSide(!_sideShown, true);
        StatusBar.MouseLeftButtonUp += (_, _) => OnStatusClick();
        BtnAppSearch.Click += (_, _) => Fire(SearchAppsAsync, "Поиск программ");
        InitMonitor();   // переключение «Драйверы» / «Обновления Windows»; монитор — в «Моём компьютере» (MainWindow.Monitor.cs)
        InitWinTab();    // «Обновления Windows» — MainWindow.WinUpdate.cs
        BtnDrvRefresh.Click += (_, _) => { if (!_busy && !_drvSearching) Fire(BuildDriversAsync, "Драйверы"); };
        InitMenu();   // MainWindow.Menu.cs
        InitGlobalSearch();   // «Поиск» по всей программе — MainWindow.SearchPage.cs
        InitInstallProgress();   // ход установки: карточка в «Программах» и строка внизу — MainWindow.InstallProgress.cs
        BtnRuntimeAll.Click += (_, _) => Fire(InstallRuntimeAllAsync, "Компоненты");
        PreviewKeyDown += (_, e) =>
        {
            var k = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            bool win = System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LWin) || System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RWin);
            bool shift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
            if (k == System.Windows.Input.Key.Escape && Tour.Visibility == Visibility.Visible) { EndTour(); e.Handled = true; return; }
            if (k == System.Windows.Input.Key.Escape && FeatureHint.Visibility == Visibility.Visible) { ClearHint(); e.Handled = true; return; }
            if (k == System.Windows.Input.Key.Snapshot || (win && shift && k == System.Windows.Input.Key.S))
            { Sh.Open("ms-screenclip:"); e.Handled = true; }
        };
        AppSearch.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Fire(SearchAppsAsync, "Поиск программ"); };
        void SyncHint() => AppSearchHint.Visibility = AppSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        // подсказка прячется, как только в поле встаёт курсор, — иначе курсор мигает поверх текста подсказки
        AppSearch.TextChanged += (_, _) => SyncHint();
        AppSearch.GotKeyboardFocus += (_, _) => SyncHint();
        AppSearch.LostKeyboardFocus += (_, _) => SyncHint();
        BtnTour.Click += (_, _) => StartTour();
        FeatureHintClose.Click += (_, _) => ClearHint();
        FeatureHintGo.Click += (_, _) =>
        {
            _pendingReminder = false;   // пользователь сам идёт смотреть список — баннер больше не нужен
            ShowPageByName("PageHome");
        };
        TourNext.Click += (_, _) => { if (TourNext.IsEnabled) NextTour(); };
        TourSkip.Click += (_, _) => EndTour();
        AboutText.Text = $"Merno {App.Version}";
        _settings = AppSettings.Load();
        _settingsLoaded = true;
        BuildLook();   // «Внешний вид» — после загрузки настроек, чтобы отметить выбранную тему
        SetSide(true, false);   // при каждом запуске меню развёрнуто; свернуть можно стрелкой на время работы
        InitUiScale(); Loaded += (_, _) => ApplyUiScale(resize: false);
        InitNetLimit();   // «Ограничить скорость скачивания» — MainWindow.NetLimit.cs
        InitBanner();   // живой баннер Главной (луна и вода) — MainWindow.Banner.cs
        SetAdvancedMode(_settings.Advanced);
        SetAdvanced.IsChecked = _settings.Advanced; SetSideOpen.IsChecked = _settings.SideOpen;
        SetAdvanced.Checked += (_, _) =>
        {
            if (_settings.Advanced) return;
            if (ConfirmAdvanced()) SetAdvancedMode(true);
            else SetAdvanced.IsChecked = false;   // отказались — переключатель остаётся выключенным
        };
        SetAdvanced.Unchecked += (_, _) => { if (_settings.Advanced) SetAdvancedMode(false); };
        SetSideOpen.Checked += (_, _) => SetSide(true, true); SetSideOpen.Unchecked += (_, _) => SetSide(false, true);
        // в «Настройках» галочка слева, а щёлкнуть можно по всей строке
        foreach (var c in new[] { SetAdvanced, SetSideOpen, SetAutostart, SetAutoUpdate })
        {
            var cb = c;
            if (cb.Parent is FrameworkElement dp && dp.Parent is Border row)
            {
                row.Cursor = System.Windows.Input.Cursors.Hand;
                row.MouseLeftButtonUp += (_, e) => { if (cb.IsEnabled && !(e.OriginalSource is DependencyObject d && IsInside(d, cb))) cb.IsChecked = cb.IsChecked != true; };
            }
        }

        _actionButtons.AddRange(new[] { BtnCheck, BtnGo, BtnApply, BtnStartApply, BtnMenuApply, BtnClean, BtnScan, BtnRollback, BtnStartRefresh,
                                        BtnRuntimeAll, BtnMenuJunk, BtnMenuReset, BtnMenuExplorer, BtnAppSearch, BtnDrvRefresh, BtnWinRefresh, BtnStartAdd, BtnUnRefresh });
        SetOwn(BtnRuntimeAll, false);   // доступны после проверки

        BtnCheck.Click += (_, _) => Fire(CheckAsync, "Проверка");
        SizeChanged += (_, _) => AutoSide();
        InitNavFit();   // невысокое окно: пункты меню ниже и стрелка «ещё» (MainWindow.NavFit.cs)
        // узкий баннер: картинка меньше, кнопка «Проверить» уходит под текст
        HomeStateCard.SizeChanged += (_, e) =>
        {
            bool tight = e.NewSize.Width < 640;
            HomeArtCol.Width = new GridLength(tight ? 150 : 220);
            DockPanel.SetDock(BtnCheck, tight ? Dock.Bottom : Dock.Right);
            BtnCheck.HorizontalAlignment = tight ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            BtnCheck.Margin = new Thickness(0, tight ? 12 : 0, 0, 0);
        };
        BtnGo.Click += (_, _) => Fire(OptimizeAsync, "Оптимизация");
        // после оптимизации — быстрый путь к откату
        if (BtnOpenReport.Parent is Panel donePanel)
        {
            BtnOpenReport.Margin = new Thickness(0, 0, 10, 0);
            var undoBtn = new Button { Content = "Отменить изменения" };
            undoBtn.Click += (_, _) => NavRollback.IsChecked = true;
            donePanel.Children.Add(undoBtn);
        }
        BtnReboot.Click += (_, _) => AskReboot(true);
        BtnOpenReport.Click += (_, _) => { if (_lastReport != null) Sh.Open(_lastReport); };

        InitTweaks(); InitClean();   // MainWindow.Tweaks.cs, MainWindow.Clean.cs

        BtnStartRefresh.Click += (_, _) => BuildStartup();
        InitStartup();   // MainWindow.Startup.cs
        BuildFaq(); BuildQuick();
        BtnUnRefresh.Click += (_, _) => Fire(BuildUninstallAsync, "Удаление программ");
        // новые части — каждая в своём partial-файле
        InitFirstRun(); InitTransfer();
        InitWhatsNew();   // MainWindow.WhatsNew.cs
        InitUpdate();     // MainWindow.Update.cs
        InitSorting();    // MainWindow.Builtin.cs
        InitAbout();      // MainWindow.About.cs
        InitTray();       // MainWindow.Tray.cs
        // переключатель частей «Программ»: «Программы» в меню остаются подсвеченными
        void ToPart(FrameworkElement pg) { if (pg.Visibility != Visibility.Visible) { if (NavApps.IsChecked != true) NavApps.IsChecked = true; ShowPage(pg); } }
        foreach (var (rb, pg) in new (RadioButton, FrameworkElement)[] { (ApAInst, PageApps), (ApADel, PageUninstall), (ApAWin, PageStore),
                                                                          (ApDInst, PageApps), (ApDDel, PageUninstall), (ApDWin, PageStore),
                                                                          (ApWInst, PageApps), (ApWDel, PageUninstall), (ApWWin, PageStore) })
        { var target = pg; rb.Checked += (_, _) => ToPart(target); }
        // «Программы» в меню уже отмечены, пока открыта другая часть, — Checked не сработает, поэтому ловим клик
        NavApps.Click += (_, _) => { if (PageApps.Visibility != Visibility.Visible) ShowPage(PageApps); };
        StoreFilter.TextChanged += (_, _) => ApplyFilter(StoreFilter, StoreFilterHint, StoreList);
        UnFilter.TextChanged += (_, _) => ApplyFilter(UnFilter, UnFilterHint, UnList);
        BtnStartAdd.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Какую программу запускать вместе с Windows?", Filter = "Программы и ярлыки (*.exe;*.lnk)|*.exe;*.lnk",
                                                           InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            if (dlg.ShowDialog(this) != true) return;
            try { var n = Startup.Add(dlg.FileName); SetStatus($"«{n}» будет запускаться вместе с Windows"); BuildStartup(); }
            catch (Exception ex) { Log.Warn("Автозагрузка, добавление: " + ex); Warn("Не получилось добавить программу. Попробуйте выбрать другой файл."); }
        };
        BtnCopyInfo.Click += (_, _) =>
        {
            if (_ctx.S == null) return;
            try { Clipboard.SetText(_ctx.S.ToText()); SetStatus("Скопировано. Вставьте сведения в сообщение."); }
            catch (Exception ex) { Log.Warn("Буфер обмена: " + ex.Message); SetStatus("Не получилось скопировать. Попробуйте ещё раз."); }
        };

        BtnRollback.Click += (_, _) => Fire(RollbackAsync, "Откат");
        BtnSysRestore.Click += (_, _) => Sh.Open(Sh.Sys32("rstrui.exe"));
        BtnOpenLogs.Click += (_, _) => Sh.Open(Log.LogDir);

        BuildHelp();
        BuildTools();
        // боковые карточки страниц на каркасе PageShell
        BtnToolsRollback.Click += (_, _) => NavRollback.IsChecked = true;
        BtnToolsContact.Click += (_, _) => NavContact.IsChecked = true;
        BtnQuickEdit.Click += (_, _) => EditQuick();
        InitQuickAdd();   // карточка «Добавить своё» — MainWindow.Quick.cs
        QuickSearch.TextChanged += (_, _) => BuildQuickAdv();   // поиск в «Быстром доступе» — MainWindow.Quick.cs
    }

    // ================================================================== ИНФРАСТРУКТУРА
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private static void DarkTitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            int on = 1;
            if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4);
            // цвет заголовка окна — как боковое меню текущей темы (BGR)
            var side = (Application.Current.TryFindResource("Side") as SolidColorBrush)?.Color ?? Color.FromRgb(0x10, 0x11, 0x14);
            int color = side.B << 16 | side.G << 8 | side.R;
            DwmSetWindowAttribute(h, 35, ref color, 4);
        }
        catch { }
    }

    private Brush B(string key) => (Brush)FindResource(key);

    /// <summary>Окно не больше рабочей области экрана (маленькие ноутбуки, масштаб 150%) и по центру.</summary>
    private void FitToWorkArea()
    {
        var wa = SystemParameters.WorkArea;   // уже в независимых пикселях
        const double margin = 24;
        double maxW = Math.Max(400, wa.Width - margin), maxH = Math.Max(300, wa.Height - margin);
        if (MinWidth > maxW) MinWidth = maxW;
        if (MinHeight > maxH) MinHeight = maxH;
        if (double.IsNaN(Width) || Width > maxW) Width = maxW;
        if (double.IsNaN(Height) || Height > maxH) Height = maxH;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;
    }

    /// <summary>Запуск задачи «в фоне» без потери ошибок: исключение пишется в журнал, а не роняет программу.</summary>
    private async void Fire(Func<Task> f, string what)
    {
        try { await f(); }
        catch (Exception ex) { Log.Warn($"{what}: {ex.Message}"); Log.Skip(ex.ToString()); }
    }

    /// <summary>Ошибка вместо списка — с кнопкой «Повторить».</summary>
    private void ShowError(Panel panel, string msg, Func<Task> retry)
    {
        SetError(msg);
        panel.Children.Clear();
        var row = new Border { Style = (Style)FindResource("Row") };
        var dp = new DockPanel();
        if (retry != null)
        {
            var btn = new Button { Content = "Повторить", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            btn.Click += (_, _) => Fire(retry, "Повтор");
            DockPanel.SetDock(btn, Dock.Right);
            dp.Children.Add(btn);
        }
        dp.Children.Add(new TextBlock { Text = msg, Foreground = B("Warn"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        row.Child = dp;
        panel.Children.Add(row);
    }

    // поиск по спискам: строки помечены именем в Tag
    private static void ApplyFilter(TextBox box, TextBlock hint, StackPanel list)
    {
        hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var q = box.Text.Trim();
        foreach (var el in list.Children.OfType<FrameworkElement>())
            if (el.Tag is string name) el.Visibility = q.Length == 0 || name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    }

    // кнопки, выключенные по своей причине (нечего ставить, идёт поиск), не включаются обратно после занятости
    private readonly Dictionary<Button, bool> _own = new();
    private void SetOwn(Button b, bool on) { _own[b] = on; b.IsEnabled = on && !_busy; }

    private bool _sysTempsBuilt;
    private readonly List<string> _history = new();
    /// <summary>Куда идти по «Вперёд»: разделы, из которых вернулись «Назад». Обычный переход очищает.</summary>
    private readonly List<string> _forward = new();
    private bool _goingBack, _goingForward;

    private static readonly string[] PageOrder = { "PageHome", "PageSystem", "PageFirstRun", "PageClean", "PageStartup", "PageApps", "PageUninstall", "PageStore", "PageRuntime", "PageDrivers", "PageTools", "PageTweaks", "PageMenu", "PageQuick", "PageHelp", "PageSettings", "PageRollback", "PageLog", "PageSearch" };

    /// <summary>Имя открытого раздела в том же виде, что в истории («Windows» — «Обновления Windows»).</summary>
    private string CurrentPageName()
    {
        foreach (var n in PageOrder)
            if (FindName(n) is FrameworkElement p && p.Visibility == Visibility.Visible)
                return p == PageDrivers && DrvTabWin.IsChecked == true ? "Windows" : n;
        return null;
    }

    private void UpdateNavButtons()
    {
        BtnBack.IsEnabled = _history.Count > 0;
        BtnForward.IsEnabled = _forward.Count > 0;
    }

    private void GoBack()
    {
        if (_history.Count == 0) return;
        var name = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        if (CurrentPageName() is string cur) { _forward.Add(cur); if (_forward.Count > 30) _forward.RemoveAt(0); }
        _goingBack = true;
        try { ShowPageByName(name); }
        finally { _goingBack = false; }
        UpdateNavButtons();
    }

    private void GoForward()
    {
        if (_forward.Count == 0) return;
        var name = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _goingForward = true;   // текущий раздел уходит в историю «Назад», а список «Вперёд» не очищается
        try { ShowPageByName(name); }
        finally { _goingForward = false; }
        UpdateNavButtons();
    }

    private void ShowPage(FrameworkElement page)
    {
        var pages = new FrameworkElement[] { PageHome, PageSystem, PageFirstRun, PageClean, PageStartup, PageApps, PageUninstall, PageStore, PageRuntime, PageDrivers, PageTools, PageTweaks, PageMenu, PageQuick, PageHelp, PageSettings, PageRollback, PageLog, PageSearch };
        int from = Array.FindIndex(pages, p => p.Visibility == Visibility.Visible), to = Array.IndexOf(pages, page);
        // история для «Назад»
        if (from >= 0 && from != to && !_goingBack)
        {
            _history.Add(pages[from] == PageDrivers && DrvTabWin.IsChecked == true ? "Windows" : pages[from].Name);
            if (_history.Count > 30) _history.RemoveAt(0);
            if (!_goingForward) _forward.Clear();   // новый переход — «Вперёд» больше некуда
        }
        UpdateNavButtons();
        foreach (var p in pages)
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        bool keepPending = _pendingReminder;   // напоминание «осталось доделать» переживает переход в другой раздел
        ClearHint();   // подсказка «где исправить» живёт до перехода в другой раздел
        if (keepPending && page != PageHome) ShowPendingHint(_pendingLead);
        // плавная смена: раздел проявляется и чуть выезжает — справа, если идём вниз по меню, слева — если вверх
        if (from >= 0 && from != to)
        {
            if (page.RenderTransform is not TranslateTransform tt) page.RenderTransform = tt = new TranslateTransform();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to > from ? 20 : -20, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        }
        // «Откат» и «Журнал» — часть «Настроек»: подсвечиваем их в меню
        if ((page == PageRollback || page == PageLog) && NavSettings.IsChecked != true)
        { _navSync = true; try { NavSettings.IsChecked = true; } finally { _navSync = false; } }
        if (page == PageHome) { HomeTabOpt.IsChecked = true; if (_ready) BuildHomeRecent(); OnFirstRunShown(); }
        if (page == PageSystem)
        {
            SysTabPc.IsChecked = true;
            BuildMonitors();   // частота могла поменяться — читаем заново (MainWindow.Monitor.cs)
            if (!_sysTempsBuilt) { _sysTempsBuilt = true; AddTempsBlock(SysTemps); }   // живая температура (MainWindow.Temps.cs)
        }
        if (page == PageFirstRun) { FrTabFirst.IsChecked = true; OnFirstRunShown(); }
        // «Мой компьютер» — часть «Главной»: подсвечиваем её в меню
        if ((page == PageSystem || page == PageFirstRun) && NavHome.IsChecked != true)
        { _navSync = true; try { NavHome.IsChecked = true; } finally { _navSync = false; } }
        // «Программы»: три части с общим переключателем
        if (page == PageApps) ApAInst.IsChecked = true;
        if (page == PageUninstall) { ApDDel.IsChecked = true; Fire(BuildUninstallAsync, "Удаление программ"); }   // список свежий при каждом открытии
        if (page == PageStore) ApWWin.IsChecked = true;
        if (page == PageRollback) BuildBackups();
        if (page == PageApps && !_appsBuilt) Fire(BuildAppsAsync, "Программы");
        if (page == PageRuntime && !_runtimeBuilt) Fire(BuildRuntimeAsync, "Компоненты");
        if (page == PageStore) Fire(BuildStoreAsync, "Приложения Windows");
        if (page == PageMenu) Fire(BuildMenuAsync, "Контекстное меню");
        if (page == PageClean) OnCleanShown();   // MainWindow.Clean.cs: пересортировка и подсчёт размера в фоне
        if (page == PageDrivers)
        {
            if (DrvTabWin.IsChecked == true) Fire(BuildWinTabAsync, "Windows");
            else if (!_drvBuilt) Fire(BuildDriversAsync, "Драйверы");
        }
        if (page == PageHome) Fire(RefreshPendingAsync, "Что осталось сделать");
        if (page == PageStartup && _ready) BuildStartup();
    }

    private const int LogKeep = 1500;
    private readonly Queue<string> _logLines = new();
    private bool _logStale = true;
    private int _logBoxLines;

    private void RefreshLogBox()
    {
        string text;
        lock (_logLines) text = string.Join(Environment.NewLine, _logLines) + Environment.NewLine;
        LogBox.Text = text;
        _logBoxLines = LogKeep;
        LogBox.ScrollToEnd();
        _logStale = false;
    }

    private void OnLog(string line, LogLevel level)
    {
        // в окне — только последние LogKeep строк, каждая не длиннее 600 знаков; полный журнал — в файле.
        // Огромный текст в TextBox валил Merno при открытии «Журнала» («Не удаётся создать страницу для защиты стека»).
        string shown = $"[{DateTime.Now:HH:mm:ss}] " + (line.Length > 600 ? line[..600] + " …" : line);
        lock (_logLines)
        {
            _logLines.Enqueue(shown);
            while (_logLines.Count > LogKeep) _logLines.Dequeue();
        }
        Dispatcher.BeginInvoke(() =>
        {
            // пока «Журнал» закрыт, текстовое поле не трогаем — заполним при открытии (RefreshLogBox)
            if (PageLog.IsVisible)
            {
                if (++_logBoxLines > LogKeep + 300) RefreshLogBox();
                else { LogBox.AppendText(shown + Environment.NewLine); LogBox.ScrollToEnd(); }
            }
            else _logStale = true;
            // строки журнала — это ход работы, а не ошибка для пользователя: красным их не подсвечиваем
            if (level != LogLevel.Skip) SetStatus(line.Length > 4 ? line[4..] : line, detectError: false);
        });
    }

    private void SetBusy(bool busy, string status = null)
    {
        _busy = busy;
        _busyText = busy ? status : null;
        foreach (var b in _actionButtons) b.IsEnabled = !busy && (!_own.TryGetValue(b, out var own) || own);
        BusyIcon.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusDot.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;   // пока работаем — крутится значок, кружок прячем
        if (busy)
        {
            _error = null;   // новое действие — старая ошибка больше не актуальна
            _spin ??= new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            if (_spin.Children.Count == 0)
            {
                var a = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1));
                Storyboard.SetTarget(a, BusyIcon);
                Storyboard.SetTargetProperty(a, new PropertyPath("RenderTransform.Angle"));
                _spin.Children.Add(a);
            }
            _spin.Begin();
            SetStatus(status ?? "Работаю…", detectError: false);
        }
        else
        {
            _spin?.Stop();
            SetStatus(status ?? "Готово");
        }
        Cursor = busy ? System.Windows.Input.Cursors.AppStarting : null;
    }

    // ---------- строка состояния внизу ----------
    // В покое — только кружок: зелёный — всё хорошо, жёлтый — есть советы, красный — проблема или ошибка.
    // Если что-то не так, рядом с кружком написано что. Пока Merno работает — пишет, что делает;
    // после действия итог висит 10 секунд и исчезает. Ошибка висит, пока не начнётся следующее действие или по ней не нажмут.
    private const int StatusHoldSec = 10;
    private System.Windows.Threading.DispatcherTimer _statusTimer;
    private string _error;
    private readonly Dictionary<string, (int Sev, string Text)> _health = new();

    private static bool IsErrorText(string t) =>
        t.StartsWith("Не получилось", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Не удалось", StringComparison.OrdinalIgnoreCase) ||
        t.StartsWith("Не удаётся", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Не получается", StringComparison.OrdinalIgnoreCase);

    /// <summary>Сообщение в строке состояния. Когда Merno не занят — через 10 секунд строка вернётся к покою.</summary>
    private void SetStatus(string text, bool detectError = true)
    {
        if (string.IsNullOrWhiteSpace(text)) { ShowIdle(); return; }
        if (detectError && IsErrorText(text)) { SetError(text); return; }
        _statusTimer?.Stop();
        Status.Text = text;
        Status.Foreground = B("Sub");
        if (_busy) return;
        if (_error == null) StatusDot.Fill = HealthBrush();
        _statusTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(StatusHoldSec) };
        _statusTimer.Tick -= StatusTimerTick; _statusTimer.Tick += StatusTimerTick;
        _statusTimer.Start();
    }
    private void StatusTimerTick(object sender, EventArgs e) { _statusTimer.Stop(); if (!_busy) ShowIdle(); }

    /// <summary>Ошибка: красный кружок и текст, пока не начнётся следующее действие.</summary>
    private void SetError(string text)
    {
        _error = FirstSentence(text);
        if (!_busy) ShowIdle();
    }

    /// <summary>Постоянное состояние компьютера по итогам проверки. sev: 0 — всё хорошо, 1 — совет, 2 — проблема.</summary>
    private void SetHealth(string key, int sev, string text)
    {
        if (sev <= 0 || string.IsNullOrEmpty(text)) _health.Remove(key); else _health[key] = (sev, text);
        UpdateHomeState();
        if (!_busy && !(_statusTimer?.IsEnabled ?? false)) ShowIdle();
        else if (!_busy && _error == null) StatusDot.Fill = HealthBrush();
    }

    private (int Sev, string Text) WorstHealth() => _health.Values.OrderByDescending(x => x.Sev).FirstOrDefault();
    private Brush HealthBrush() { var w = WorstHealth(); return w.Sev >= 2 ? B("Bad") : w.Sev == 1 ? B("Warn") : B("Ok"); }

    /// <summary>Покой: кружок, а текст — только если есть ошибка или проблема.</summary>
    private void ShowIdle()
    {
        _statusTimer?.Stop();
        if (_busy) return;
        StatusDot.Visibility = Visibility.Visible;
        if (_error != null) { StatusDot.Fill = B("Bad"); Status.Text = _error; Status.Foreground = B("Bad"); Status.ToolTip = "Нажмите, чтобы скрыть"; return; }
        var w = WorstHealth();
        StatusDot.Fill = HealthBrush();
        Status.Text = w.Sev > 0 ? w.Text : "";
        Status.Foreground = w.Sev >= 2 ? B("Bad") : w.Sev == 1 ? B("Warn") : B("Sub");
        Status.ToolTip = w.Sev > 0 ? "Подробнее на «Главной»" : null;
    }

    /// <summary>Нажатие на строку состояния: ошибку — скрыть, проблему — показать на «Главной».</summary>
    private void OnStatusClick()
    {
        if (_busy) return;
        if (_error != null) { _error = null; ShowIdle(); return; }
        if (WorstHealth().Sev > 0) ShowPageByName("PageHome");
    }

    private static string FirstSentence(string t)
    {
        t = (t ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return t.Length > 160 ? t[..157] + "…" : t;
    }

    /// <summary>Выполняет работу в фоне, пока окно «занято». false — не запущено: уже идёт другая операция.</summary>
    private async Task<bool> Busy(string status, Action work)
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return false; }
        SetBusy(true, status);
        Exception error = null;
        try { await Task.Run(work); }
        catch (Exception ex) { error = ex; Log.Warn("Ошибка: " + ex.Message); Log.Skip(ex.ToString()); }
        finally { SetBusy(false); }
        // ошибка — не «Готово»: false, и вызывающий не покажет итог успеха (откат, оптимизация, мастер)
        if (error != null) { Warn(ForPerson(error, "Не получилось.") + "\n\nПодробности: «Настройки» → «Журнал»."); return false; }
        return true;
    }

    private void AskReboot(bool direct = false)
    {
        if (direct || Ask("Чтобы всё заработало, перезагрузите компьютер.\n\nСохраните документы. Перезагрузить сейчас?", "Merno", MessageBoxImage.Question, "Перезагрузить", "Позже"))
            Sh.Run(Sh.Sys32("shutdown.exe"), "/r /t 5 /c \"Merno: перезагрузка для применения настроек\"");
    }

    // ================================================================== ЗАПУСК
    private async Task InitAsync()
    {
        SetBusy(true, "Смотрю, что у вас за компьютер…");
        string done = "Готово к работе";
        try
        {
            Log.Skip($"Merno {App.Version} запущен. Журнал: {Log.LogFile}");
            BtnGo.Style = (Style)FindResource("Big");
            SetMark(1, 1); SetMark(2, 0); SetMark(3, 0);
            try { await Task.Run(() => { _ctx.S = SysInfo.Collect(); _ctx.RefreshAppx(); }); }
            catch (Exception ex) { Log.Warn("Сведения о компьютере: " + ex.Message); Log.Skip(ex.ToString()); done = "Не получилось узнать сведения о компьютере. Нажмите «Проверить компьютер»."; }
            if (_ctx.S != null)
            {
                HomeSys.Text = _ctx.S.OneLine() + "  •  " + System.Text.RegularExpressions.Regex.Replace(_ctx.S.OS ?? "", @"\s*\(сборка.*\)", "").Trim();
                if (_ctx.S.IsVM) Log.Warn("Обнаружена виртуальная машина. Часть настроек недоступна");
            }
            BuildHwTags();
            await BuildTweaksAsync();
            BuildCleanList();
            BuildSystem();
            BuildStartup();
            UpdatePlan();
            UpdateIssueHealth();
            UpdateHomeState();
            BuildHomeRecent();
            await RefreshPendingAsync();
            _ready = true;
            OnFirstRunShown();   // «Настройка Windows» на «Главной» (MainWindow.FirstRun.cs)
            if (!_settings.Onboarded && ShotDir == null) StartTour();
            else HelpLine.Visibility = Visibility.Collapsed;
        }
        finally { SetBusy(false, done); }
    }

    // ================================================================== ГЛАВНАЯ



    private static string Plural(int n, string one, string few, string many)
    {
        n = Math.Abs(n) % 100; int n1 = n % 10;
        if (n > 10 && n < 20) return many;
        if (n1 > 1 && n1 < 5) return few;
        return n1 == 1 ? one : many;
    }

    // ---------- полезные программы ----------
    private bool _wingetOk;
    private StackPanel _searchBox;



    // ---------- системные компоненты ----------






    /// <summary>«Драйвер под игры»: рекомендуемый свежий; в расширенном режиме ещё старый 566.36 (Drivers.GameDrivers).</summary>








    private System.Windows.Documents.Adorner _hintAdorner;
    private System.Windows.Threading.DispatcherTimer _hintTimer;
    private bool _hintMouseHooked;

    /// <summary>Подсказка не висит вечно: уходит через N секунд или по любому щелчку в окне (кроме самой подсказки).
    /// Закрывать её кнопкой не нужно.</summary>
    private void StartHintTimer(int seconds)
    {
        _hintTimer ??= new System.Windows.Threading.DispatcherTimer();
        _hintTimer.Stop();
        _hintTimer.Interval = TimeSpan.FromSeconds(seconds);
        _hintTimer.Tick -= HintTimerTick; _hintTimer.Tick += HintTimerTick;
        _hintTimer.Start();
        if (!_hintMouseHooked)
        {
            _hintMouseHooked = true;
            PreviewMouseDown += (_, e) =>
            {
                if (FeatureHint.Visibility != Visibility.Visible) return;
                if (FeatureHint.IsMouseOver) return;   // щелчок по самой подсказке (её кнопкам) — пусть кнопки отработают
                ClearHint();   // щелчок не перехватываем — он идёт туда, куда нажали
            };
        }
    }
    private void HintTimerTick(object s, EventArgs e) { _hintTimer?.Stop(); ClearHint(); }

    /// <summary>Убирает рамку и подсказку. Вызывается при переходе в другой раздел, закрытии и Esc.</summary>
    private void ClearHint()
    {
        try
        {
            if (_hintAdorner != null)
            {
                var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(_hintAdorner.AdornedElement);
                layer?.Remove(_hintAdorner);
            }
        }
        catch { }
        _hintAdorner = null;
        _hintTimer?.Stop();
        _pendingReminder = false;
        if (FeatureHint != null) FeatureHint.Visibility = Visibility.Collapsed;
        if (FeatureHintGo != null) FeatureHintGo.Visibility = Visibility.Collapsed;
    }

    private sealed class HintAdorner : System.Windows.Documents.Adorner
    {
        private readonly Pen _pen;
        private readonly Brush _glow;
        public HintAdorner(UIElement el, Brush accent) : base(el)
        {
            IsHitTestVisible = false;
            _pen = new Pen(accent, 2); _pen.Freeze();
            var c = accent is SolidColorBrush sb ? sb.Color : Colors.Teal;
            _glow = new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B)); _glow.Freeze();
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = c, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 };
        }
        protected override void OnRender(DrawingContext dc)
        {
            var r = new Rect(AdornedElement.RenderSize); r.Inflate(3, 3);
            dc.DrawRoundedRectangle(_glow, _pen, r, 8, 8);
        }
    }

    /// <summary>Подтверждение перед включением расширенного режима. true — пользователь согласился.</summary>
    private bool ConfirmAdvanced()
    {
        return MernoDialog(
            "Что появится:\n" +
            "• В «Настройках Windows» каждую настройку можно изменить по отдельности.\n" +
            "• В «Контекстном меню» можно убрать лишние пункты из меню, которое открывается правой кнопкой мыши.",
            "Расширенный режим", "Включить", "Отмена", true,
            warning: "Дальнейшие действия выполняются на свой страх и риск: эти настройки могут сломать Windows. Разработчики не несут ответственности.");
    }

    /// <summary>Меню показано раскрытым (в узком окне оно сворачивается само, выбор пользователя не меняется).</summary>
    private bool _sideShown = true;
    private const double AutoCollapseBelow = 1000;

    /// <summary>Окно стало уже 1000 — сворачиваем меню в иконки, чтобы страницам хватило места; шире — возвращаем как было.</summary>
    private void AutoSide()
    {
        if (_settings == null || SideBar == null) return;
        bool want = _settings.SideOpen && ActualWidth / _uiScale >= AutoCollapseBelow;   // в единицах содержимого (с учётом «Размера интерфейса»)
        if (want != _sideShown) ApplySide(want, true);
    }

    private void SetSide(bool open, bool save)
    {
        _settings.SideOpen = open;
        // в узком окне раскрытое меню не помещается: запоминаем выбор, показываем свёрнутым
        ApplySide(open && (ActualWidth <= 0 || ActualWidth / _uiScale >= AutoCollapseBelow || save), save);
        if (SetSideOpen.IsChecked != open) SetSideOpen.IsChecked = open;
        if (save) _settings.Save();
    }

    private void ApplySide(bool open, bool animate)
    {
        _sideShown = open;
        // Без анимации ширины: при ней вся страница перекладывалась на каждом кадре (на 144–165 Гц — до 165 раз
        // в секунду, с подсчётом «ещё» в каждой строке) — окно тормозило, процессор нагружался. Теперь одна перекладка,
        // а «свайп» рисуется поверх неё (AnimateSide): сдвиг и маска не перекладывают страницу.
        SideBar.BeginAnimation(WidthProperty, null);
        double from = SideBar.ActualWidth > 0 ? SideBar.ActualWidth : SideBar.Width;
        SideBar.Width = open ? 224 : 64;
        if (animate) AnimateSide(from, SideBar.Width);
        NavToggle.Content = "\uE700";   // три полоски — привычный значок меню, одинаковый в обоих видах
        NavToggle.ToolTip = open ? "Свернуть меню" : "Развернуть меню";
        // кнопка сворачивания всегда под логотипом — не переезжает; «Назад» — справа от логотипа (раскрыто) или под ней (свёрнуто)
        NavToggle.Margin = new Thickness(16, 48, 0, 0);
        // раскрыто — «Назад» и «Вперёд» парой у правого края меню, свёрнуто — под кнопкой меню
        BtnBack.HorizontalAlignment = open ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        BtnBack.Margin = open ? new Thickness(0, 48, 46, 0) : new Thickness(16, 84, 0, 0);
         BtnForward.HorizontalAlignment = open ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        BtnForward.Margin = open ? new Thickness(0, 48, 16, 0) : new Thickness(16, 120, 0, 0);
        SideHead.Height = open ? 84 : 156;
        AdvLabel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        AdvLine.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        // ресурс — в самом меню, а не во всём окне: смена ресурса окна перепроверяла каждый элемент всех открытых
        // разделов (до секунды фриза при сворачивании), а так затрагивается только меню
        SideBar.Resources["NavLabelVis"] = open ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Плавный «свайп» меню без перекладки страницы на каждом кадре: ширина уже сменилась (одна перекладка),
    /// содержимое справа стартует со старого места и доезжает сдвигом (RenderTransform), а раскрывающееся меню
    /// проявляется маской слева направо. Если в Windows выключена анимация — сразу, без движения.
    /// </summary>
    private void AnimateSide(double from, double to)
    {
        double delta = to - from;
        if (Math.Abs(delta) < 1 || ShotDir != null || !SystemParameters.ClientAreaAnimation || !IsLoaded) return;
        var dur = new Duration(TimeSpan.FromMilliseconds(220));
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

        if (MainArea.RenderTransform is not System.Windows.Media.TranslateTransform shift)
            MainArea.RenderTransform = shift = new System.Windows.Media.TranslateTransform();
        double start = shift.X - delta;   // если прошлая анимация не доехала — продолжаем с текущего места
        shift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
            new System.Windows.Media.Animation.DoubleAnimation(start, 0, dur) { EasingFunction = ease });

        if (delta > 0)
        {
            var clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, from, Math.Max(1, SideBar.ActualHeight)));
            SideBar.Clip = clip;
            var reveal = new System.Windows.Media.Animation.RectAnimation(new Rect(0, 0, to, Math.Max(1, SideBar.ActualHeight)), dur) { EasingFunction = ease };
            reveal.Completed += (_, _) => { if (SideBar.Clip == clip) SideBar.Clip = null; };
            clip.BeginAnimation(System.Windows.Media.RectangleGeometry.RectProperty, reveal);
        }
        else SideBar.Clip = null;
    }

    private void SetAdvancedMode(bool on)
    {
        _settings.Advanced = on;
        var v = on ? Visibility.Visible : Visibility.Collapsed;
        AdvancedNav.Visibility = v;
        if (SetAdvanced.IsChecked != on) SetAdvanced.IsChecked = on;
        if (!on && (NavTweaks.IsChecked == true || NavMenu.IsChecked == true)) NavHome.IsChecked = true;
        _settings.Save();
    }

    private void StartTour()
    {
        _tourStep = 0; NavHome.IsChecked = true; ShowPage(PageHome);
        PageHome.ScrollToTop();   // «Главная» могла быть прокручена вниз — тогда подсвечивать было бы нечего
        Tour.Visibility = Visibility.Visible;
        LayoutUpdated -= TourFollow; LayoutUpdated += TourFollow;
        Dispatcher.BeginInvoke(new Action(ShowTourStep), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private FrameworkElement TourTarget() => _tourStep switch
    {
        0 => BtnCheck,
        1 => HomeSetup,
        2 => SysTabPc,   // «Мой компьютер» уже открыт — подсвечиваем переключатель на нём
        3 => NavSearch, 4 => NavApps, 5 => NavDrivers, 6 => StatusBar, 7 => NavToggle, 8 => NavHelp, _ => BtnRollback
    };

    // Что открыть на шаге обучения: сначала подсвечиваем кнопку, через пару секунд открываем сам раздел
    private Action TourOpen() => _tourStep switch
    {
        0 or 1 => () => { if (PageHome.Visibility != Visibility.Visible) { NavHome.IsChecked = true; ShowPage(PageHome); } },
        2 => () => HomeTabPc.IsChecked = true,
        3 => () => NavSearch.IsChecked = true,
        4 => () => NavApps.IsChecked = true,
        5 => () => NavDrivers.IsChecked = true,
        8 => () => NavHelp.IsChecked = true,
        9 => () => NavRollback.IsChecked = true,
        _ => null
    };
    private int _tourToken;

    /// <summary>Прокручивает страницу так, чтобы цель подсказки была видна (начало цели — сверху).</summary>
    private void BringTourTargetIntoView()
    {
        try
        {
            var t = TourTarget();
            if (t.IsDescendantOf(SideBar)) return;
            for (DependencyObject p = VisualTreeHelper.GetParent(t); p != null; p = VisualTreeHelper.GetParent(p))
            {
                if (p is not ScrollViewer sv) continue;
                var top = t.TransformToAncestor(sv).Transform(new Point(0, 0)).Y;
                if (top < 0 || top + Math.Min(t.ActualHeight, sv.ViewportHeight) > sv.ViewportHeight)
                    sv.ScrollToVerticalOffset(Math.Max(0, sv.VerticalOffset + top - 12));
                break;
            }
        }
        catch { }
    }

    // Раздел может догрузиться уже после того, как подсказка встала (списки, проверка), и цель сдвинется.
    // Пока обучение открыто, следим за положением цели и переставляем рамку и карточку.
    private Rect _tourLast;
    private bool _tourMovePending;
    private void TourFollow(object sender, EventArgs e)
    {
        if (Tour.Visibility != Visibility.Visible || _tourMovePending || _tourStep < 0 || _tourStep >= TourSteps.Length) return;
        Rect now;
        try { var t = TourTarget(); now = t.TransformToVisual(Tour).TransformBounds(new Rect(0, 0, t.ActualWidth, t.ActualHeight)); }
        catch { return; }
        if (Math.Abs(now.X - _tourLast.X) < 1 && Math.Abs(now.Y - _tourLast.Y) < 1 && Math.Abs(now.Height - _tourLast.Height) < 1) return;
        _tourMovePending = true;
        Dispatcher.BeginInvoke(() => { _tourMovePending = false; if (Tour.Visibility == Visibility.Visible) ShowTour(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private async void ShowTourStep()
    {
        int token = ++_tourToken;
        // раздел, о котором шаг, открываем сразу, подсказку ставим после перерисовки
        TourFill.BeginAnimation(WidthProperty, null); TourFill.Width = 0;
        var open = TourOpen();
        open?.Invoke();
        // цель могла оказаться за краем прокрутки (страница прокручена вниз) — показываем её
        await Dispatcher.InvokeAsync(() => { BringTourTargetIntoView(); }, System.Windows.Threading.DispatcherPriority.Loaded);
        // раздел въезжает с анимацией ~250 мс — рамку ставим после неё, иначе она смещается вместе с уезжающей кнопкой
        if (open != null) { await Dispatcher.InvokeAsync(ShowTour, System.Windows.Threading.DispatcherPriority.Loaded); await Task.Delay(280); if (token != _tourToken) return; }
        await Dispatcher.InvokeAsync(ShowTour, System.Windows.Threading.DispatcherPriority.Loaded);
        // «Далее» доступна чуть позже: дорожка плавно заливается бирюзой, затем кнопка становится обычной основной
        const int lockMs = 1200;
        var track = (FrameworkElement)TourNext.Parent;
        TourNext.IsEnabled = false; TourNext.Cursor = null;
        TourNext.Foreground = B("Text");
        TourFill.BeginAnimation(WidthProperty, new DoubleAnimation(0, track.ActualWidth, TimeSpan.FromMilliseconds(lockMs))
            { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        await Task.Delay(lockMs);
        if (token != _tourToken || Tour.Visibility != Visibility.Visible) return;
        TourFill.BeginAnimation(WidthProperty, null); TourFill.Width = track.ActualWidth;   // остаётся залитой — это и есть «готово»
        TourNext.Foreground = B("OnAccent");
        TourNext.IsEnabled = true; TourNext.Cursor = System.Windows.Input.Cursors.Hand;
        TourNext.Focus();
    }

    /// <summary>Затемняет окно, оставляя «окошко» над тем, о чём подсказка, и ставит карточку рядом.</summary>
    private void ShowTour()
    {
        if (_tourStep < 0 || _tourStep >= TourSteps.Length) return;
        var (title, text) = TourSteps[_tourStep];
        TourStep.Text = $"Подсказка {_tourStep + 1} из {TourSteps.Length}";
        TourTitle.Text = title; TourText.Text = text;
        TourNext.Content = _tourStep == TourSteps.Length - 1 ? "Понятно" : "Далее";
        UpdateLayout();
        double W = Tour.ActualWidth, H = Tour.ActualHeight;
        var t = TourTarget();
        Rect r;
        try { r = t.TransformToVisual(Tour).TransformBounds(new Rect(0, 0, t.ActualWidth, t.ActualHeight)); }
        catch { r = new Rect(W / 2, H / 2, 0, 0); }
        _tourLast = r;
        // группы пунктов меню подсвечиваем вместе
        FrameworkElement until = t == NavStartup ? NavClean : t == NavApps ? NavRuntime : t == NavDrivers ? NavUpdates : t == NavHelp ? NavContact : null;
        if (until != null)
            try { r.Union(until.TransformToVisual(Tour).TransformBounds(new Rect(0, 0, until.ActualWidth, until.ActualHeight))); } catch { }
        // элементы меню шире видимой полосы (свёрнутое меню обрезает их) — подсвечиваем только видимую часть
        if (t != SideBar && t.IsDescendantOf(SideBar))
        {
            var side = SideBar.TransformToVisual(Tour).TransformBounds(new Rect(0, 0, SideBar.ActualWidth, SideBar.ActualHeight));
            r.Intersect(side);
            if (r.IsEmpty) r = side;
        }
        r.Inflate(6, 6);
        r.Intersect(new Rect(0, 0, W, H));
        if (r.IsEmpty) r = new Rect(W / 2, H / 2, 0, 0);
        bool inSide = t.IsDescendantOf(SideBar);
        var sideRect = SideBar.TransformToVisual(Tour).TransformBounds(new Rect(0, 0, SideBar.ActualWidth, SideBar.ActualHeight));
        // пункт меню: затемняем только меню — открытый раздел остаётся ярким и читаемым
        TourShade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(inSide ? sideRect : new Rect(0, 0, W, H)), new RectangleGeometry(r, 10, 10));
        Canvas.SetLeft(TourRing, r.X); Canvas.SetTop(TourRing, r.Y); TourRing.Width = r.Width; TourRing.Height = r.Height;
        TourCard.Measure(new Size(400, double.PositiveInfinity));
        double cw = 400, ch = TourCard.DesiredSize.Height, x, y;
        TourArrow.Visibility = Visibility.Collapsed;
        if (inSide)
        {   // пункт меню: карточка сразу справа от меню, на высоте пункта, со стрелкой на него — взгляд не прыгает
            double cy = r.Y + r.Height / 2;
            x = sideRect.Right + 18;
            y = Math.Min(Math.Max(16, cy - 40), H - ch - 16);
            Canvas.SetLeft(TourArrow, x - 9);
            Canvas.SetTop(TourArrow, Math.Min(Math.Max(cy - 9, y + 14), y + ch - 32));
            TourArrow.Visibility = Visibility.Visible;
        }
        else if (r.Right + 24 + cw < W) { x = r.Right + 24; y = Math.Min(Math.Max(16, r.Y), H - ch - 16); }   // справа от цели
        else if (r.Bottom + 24 + ch < H) { x = Math.Min(Math.Max(16, r.Right - cw), W - cw - 16); y = r.Bottom + 24; } // под целью
        else if (r.Y - 24 - ch > 0) { x = Math.Min(Math.Max(16, r.Right - cw), W - cw - 16); y = r.Y - 24 - ch; }      // над целью
        else { x = (W - cw) / 2; y = Math.Max(16, r.Y + Math.Min(120, r.Height / 3)); }                      // поверх большой цели
        Canvas.SetLeft(TourCard, x); Canvas.SetTop(TourCard, y);
    }
    private void NextTour() { if (++_tourStep >= TourSteps.Length) EndTour(); else ShowTourStep(); }
    private void EndTour() { _tourToken++; LayoutUpdated -= TourFollow; Tour.Visibility = Visibility.Collapsed; NavHome.IsChecked = true; ShowPage(HomeStart()); _settings.Onboarded = true; _settings.Save(); HelpLine.Visibility = Visibility.Collapsed; }





    private static bool IsInside(DependencyObject el, DependencyObject parent)
    {
        while (el != null) { if (el == parent) return true; el = VisualTreeHelper.GetParent(el); }
        return false;
    }

    // ================================================================== МОЙ КОМПЬЮТЕР
    private void BuildSystem()
    {
        var s = _ctx.S; if (s == null) return;
        SysIssues.Children.Clear(); SysColL.Children.Clear(); SysColR.Children.Clear();
        int hL = 0, hR = 0;
        var visibleIssues = AdvicePolicy.VisibleIssues(_settings, s.Issues).ToList();
        foreach (var i in visibleIssues)
        {
            var color = i.Severity == 2 ? B("Bad") : i.Severity == 1 ? B("Warn") : B("Ok");
            var b = new Border { BorderBrush = color, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(16, 4, 0, 4), Margin = new Thickness(0, 0, 0, 16) };
            var dp = new DockPanel();
            var ic = new TextBlock { Text = i.Severity == 0 ? "" : "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 18, Foreground = color, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(ic, Dock.Left);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = i.Text, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = i.Advice, Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 3, 0, 0) });
            dp.Children.Add(ic); dp.Children.Add(sp); b.Child = dp;
            SysIssues.Children.Add(b);
        }
        SysIssuesEmpty.Visibility = visibleIssues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (SysIssues.Children.Count > 0 && SysIssues.Children[^1] is FrameworkElement last) last.Margin = new Thickness(0);
        void Card(string glyph, string title, params (string k, string v)[] rows)
        {
            var b = new Border { Style = (Style)FindResource("CardBorder"), Margin = new Thickness(0, 0, 12, 12) };
            var sp = new StackPanel();
            // заголовок карточки переносится, а не уходит за край
            var hd = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var hdIc = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 18, Foreground = B("Accent"), Margin = new Thickness(0, 3, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(hdIc, Dock.Left);
            hd.Children.Add(hdIc);
            // «Скопировать» — вся карточка текстом; отдельная строка — правой кнопкой по ней
            string cardText = title + "\n" + string.Join("\n", rows.Where(r => !string.IsNullOrWhiteSpace(r.v)).Select(r => $"{r.k}: {(IsSecret(r.k) ? MaskSecret(r.v) : r.v.Replace("\n", "; "))}"));   // карточку целиком вставляют в чаты — секреты в ней скрыты
            var copyBtn = new Button { Content = "", FontFamily = (FontFamily)FindResource("Icons"), Style = (Style)FindResource("Ghost"), Padding = new Thickness(8, 4, 8, 4),
                                       ToolTip = "Скопировать", VerticalAlignment = VerticalAlignment.Top };
            copyBtn.Click += (_, _) => CopyText(cardText);
            DockPanel.SetDock(copyBtn, Dock.Right);
            hd.Children.Add(copyBtn);
            hd.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("H2"), TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(hd);
            foreach (var (k, v) in rows)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                var g = new Grid { Margin = new Thickness(0, 0, 0, 5), Background = Brushes.Transparent, ToolTip = "Щёлкните правой кнопкой мыши, чтобы скопировать" };
                var cm = new ContextMenu();
                var miOne = new MenuItem { Header = $"Скопировать «{k}»" }; string val = v; miOne.Click += (_, _) => CopyText(val);
                var miAll = new MenuItem { Header = "Скопировать карточку" }; miAll.Click += (_, _) => CopyText(cardText);
                cm.Items.Add(miOne); cm.Items.Add(miAll); g.ContextMenu = cm;
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var kt = new TextBlock { Text = k, Foreground = B("Sub") };
                var vt = new TextBlock { Text = v, Foreground = B("Text"), TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(vt, 1);
                g.Children.Add(kt); g.Children.Add(vt);
                if (IsSecret(k))
                {
                    // скрыто, как пароль: попадёт на скриншот — не выдаст компьютер. В режиме снимков не открывается вовсе
                    vt.Text = MaskSecret(v);
                    if (ShotDir == null)
                    {
                        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        var show = new Button { Content = "Показать", Style = (Style)FindResource("Link"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, FontSize = 12.5 };
                        bool open = false;
                        show.Click += (_, _) => { open = !open; vt.Text = open ? val : MaskSecret(val); show.Content = open ? "Скрыть" : "Показать"; };
                        Grid.SetColumn(show, 2);
                        g.Children.Add(show);
                    }
                }
                sp.Children.Add(g);
            }
            b.Child = sp;
            // кладём в ту колонку, где сейчас меньше строк
            int h = 3 + rows.Where(r => !string.IsNullOrWhiteSpace(r.v)).Sum(r => r.v.Split('\n').Length);
            if (hL <= hR) { SysColL.Children.Add(b); hL += h; } else { SysColR.Children.Add(b); hR += h; }
        }
        Card("", "Процессор", ("Модель", s.Cpu), ("Ядра", $"{s.Cores} {Plural(s.Cores, "ядро", "ядра", "ядер")}, {s.Threads} {Plural(s.Threads, "поток", "потока", "потоков")}"));
        Card("", "Оперативная память", ("Объём", $"{s.RamGB} ГБ"), ("Планок", s.RamSticks.ToString()), ("Тип", s.RamType), ("Частота", s.RamSpeed > 0 ? $"{s.RamSpeed} МГц" + (s.RamMaxSpeed > s.RamSpeed ? $" (можно до {s.RamMaxSpeed} МГц)" : "") : "?"),
             ("Модель", s.RamModels));
        Card("", "Видеокарта и экран", ("Видеокарта", s.Gpu), ("Драйвер", s.GpuDriver), ("Экран", s.Screens));
        Card("", "Диски", ("Устройства", s.Disks.Replace("; ", "\n")), ("Место", s.Volumes.Replace("; ", "\n")), ("Системный", $"{s.SysDisk} {s.SysBus}"));
        Card("", "Windows", ("Версия", System.Text.RegularExpressions.Regex.Replace(s.OS ?? "", @"\s*\(сборка.*\)", "").Trim()), ("Редакция", s.Edition), ("Установлена", s.InstallDate == DateTime.MinValue ? "?" : s.InstallDate.ToString("dd.MM.yyyy")),
             ("Питание", s.PowerPlan), ("Компьютер", (s.Laptop ? "Ноутбук" : "Настольный компьютер") + (s.IsVM ? ", виртуальная машина" : "")));
        Card("", "Безопасность и плата", ("VBS", s.Vbs), ("Целостность", s.Hvci), ("Secure Boot", s.SecureBoot), ("TPM", s.Tpm), ("Плата", s.Board), ("BIOS", s.Bios));
        if (s.Nics.Count > 0)
            Card("", "Сеть", s.Nics.SelectMany(n => new[] { ("Адаптер", n.Name), ("MAC-адрес", n.Mac), ("IP в сети", n.Ip) }).ToArray());
    }

    private FrameworkElement HomeStart() => PageHome;   // «Настройка Windows» теперь внутри «Главной»

    /// <summary>Что в «Моём компьютере» скрыто по умолчанию (кнопка «Показать»).</summary>
    private static bool IsSecret(string key) => key is "MAC-адрес";

    /// <summary>«A4-5E-60-12-3A-7F» → «••-••-••-••-3A-7F»: видно, какой адаптер, но не весь адрес.</summary>
    internal static string MaskSecret(string v)
    {
        if (string.IsNullOrEmpty(v)) return v;
        var parts = v.Split('-', ':');
        if (parts.Length >= 4) return string.Join("-", parts.Select((p, i) => i < parts.Length - 2 ? "••" : p));
        return v.Length <= 4 ? new string('•', v.Length) : new string('•', v.Length - 4) + v[^4..];
    }

    private void CopyText(string text)
    {
        try { Clipboard.SetText(text); SetStatus("Скопировано"); }
        catch (Exception ex) { Log.Warn("Буфер обмена: " + ex.Message); SetStatus("Не получилось скопировать. Попробуйте ещё раз."); }
    }

    // ================================================================== ИНСТРУМЕНТЫ
    private void BuildTools()
    {
        // плитка: значок, название, пояснение и стрелка; нажимается вся карточка (btn сейчас не показывается)
        // confirm — подробно, что сделает инструмент: перед запуском всегда спрашиваем «Запустить / Отмена»,
        // чтобы случайное нажатие на плитку ничего не начинало
        void Tool(string glyph, string title, string desc, string btn, Func<Task> act, string confirm = null, bool warn = false)
        {
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 22, Foreground = B("Accent"), Margin = new Thickness(0, 0, 0, 10) };
            var t = new TextBlock { Text = title, FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), TextWrapping = TextWrapping.Wrap };
            var d = new TextBlock { Text = Typo.Nbsp(desc), Style = (Style)FindResource("SubText"), FontSize = 13, Margin = new Thickness(0, 4, 12, 0), TextWrapping = TextWrapping.Wrap };
            var arrow = new TextBlock { Text = "\uE72A", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 14, Foreground = B("Accent"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) };
            Grid.SetRow(t, 1); Grid.SetRow(d, 2); Grid.SetColumn(arrow, 1); Grid.SetRow(arrow, 2);
            Grid.SetColumnSpan(ic, 2); Grid.SetColumnSpan(t, 2);
            g.Children.Add(ic); g.Children.Add(t); g.Children.Add(d); g.Children.Add(arrow);
            var tile = new Button { Style = (Style)FindResource("Tile"), Content = g, Margin = new Thickness(0, 0, 12, 12) };
            tile.Click += (_, _) =>
            {
                if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
                if (confirm != null && !MernoDialog(Typo.Nbsp(confirm), title, btn, "Отмена", safe: warn)) return;
                Fire(act, title);
            };
            _actionButtons.Add(tile);
            ToolList.Children.Add(tile);
        }
        Tool("", "Проверить и починить Windows", "Помогает при ошибках и вылетах Windows. Займёт от 10 до 30 минут.", "Запустить",
             () => Busy("Чиню Windows… обычно от 10 до 30 минут", Tools.Repair),
             "Что сделает Merno:\n" +
             "1. Проверит образ Windows и скачает с серверов Microsoft исправные копии повреждённых файлов. Нужен интернет.\n" +
             "2. Проверит системные файлы и заменит испорченные.\n\n" +
             "Займёт от 10 до 30 минут, компьютером можно пользоваться. Ваши файлы, программы и настройки не трогаются. После окончания лучше перезагрузить компьютер.\n\n" +
             "Когда помогает: Windows выдаёт ошибки, вылетают встроенные программы, не открываются Параметры или Пуск.");
        Tool("", "Оптимизировать диски", "Оптимизирует диски, чтобы файлы читались быстрее. На SSD это безопасно. Несколько минут.", "Запустить",
             () => Busy("Оптимизирую диски…", Tools.OptimizeDisks),
             "Что сделает Merno:\n" +
             "Windows выберет подходящий способ для каждого встроенного диска. На SSD выполнит встроенную оптимизацию Windows. На обычном жёстком диске упорядочит расположение файлов.\n\n" +
             "Флешки и внешние диски не трогаются. Файлы не удаляются и не перемещаются между дисками.\n\n" +
             "Займёт от пары минут (SSD) до часа (большой жёсткий диск). Компьютером можно пользоваться, но игры во время дефрагментации могут подтормаживать.");
        Tool("", "Починить интернет", "Помогает, если интернет есть, а сайты не открываются. Нужна перезагрузка. VPN, возможно, придётся настроить заново.", "Сбросить",
             () => Busy("Чиню интернет…", Tools.NetReset),
             "Что сделает Merno:\n" +
             "1. Очистит сохранённые адреса сайтов. Это может помочь, если сайт переехал, а компьютер «помнит» старый адрес.\n" +
             "2. Сбросит сетевые настройки Windows. Это может помочь, если их изменила программа или вирус.\n\n" +
             "После этого нужно перезагрузить компьютер. До перезагрузки интернет может не работать.\n\n" +
             "Учтите: VPN, прокси и вручную прописанные сетевые адреса могут сброситься. Их придётся настроить заново. Пароль от Wi-Fi останется.", warn: true);
        Tool("", "Создать точку восстановления", "Если что-то сломается, вы сможете вернуть Windows к этому моменту.", "Создать",
             () => Busy("Создаю точку восстановления…", () => Backup.CreateRestorePoint()),
             "Что сделает Merno:\n" +
             "Попросит Windows запомнить текущее состояние системы: настройки, драйверы и системные файлы.\n\n" +
             "Если потом что-то сломается, Windows можно вернуть к этому моменту («Откат изменений» → «Восстановление системы»). Ваши документы, фото и игры точка не сохраняет и при откате не трогает.\n\n" +
             "Займёт около минуты. Если защита системы выключена, Merno включит её для системного диска.");
        bool inSafe = System.Windows.Forms.SystemInformation.BootMode != System.Windows.Forms.BootMode.Normal;
        Tool("", inSafe ? "Вернуться в обычную Windows" : "Перезагрузить в безопасный режим",
             inSafe ? "Сейчас Windows в безопасном режиме. Merno вернёт обычный запуск и перезагрузит компьютер."
                    : "Windows запустится только с самым нужным. Удобно, чтобы начисто удалить драйвер видеокарты или упрямую программу.", "Перезагрузить",
             () => Busy("Готовлю перезагрузку…", () => Tools.SafeMode(!inSafe)),
             inSafe
                 ? "Что сделает Merno:\n• Уберёт отметку «запускаться в безопасном режиме» и перезагрузит компьютер в обычную Windows.\n\nСохраните открытые файлы. Компьютер перезагрузится."
                 : "Что сделает Merno:\n" +
                   "1. Попросит Windows один раз запуститься в безопасном режиме только с самыми нужными драйверами и службами.\n" +
                   "2. Перезагрузит компьютер.\n\n" +
                   "Сохраните открытые файлы. В безопасном режиме не будет интернета, звука и обычного разрешения экрана. Это нормально.\n\n" +
                   "Это только на один раз: следующая перезагрузка будет уже в обычную Windows.\n\nЕсли включён BitLocker или шифрование устройства, заранее сохраните ключ восстановления. Windows может запросить его при загрузке.", warn: true);
        Tool("", "Отчёт о компьютере", "Файл со сведениями о компьютере на рабочем столе. Удобно показать тому, кто помогает.", "Сохранить",
             () => Busy("Сохраняю отчёт…", () => { _lastReport = Report.Write(_ctx.S ?? SysInfo.Collect(), null, 0); Sh.Open(_lastReport); }),
             "Что сделает Merno:\n" +
             "1. Сохранит на рабочий стол текстовый файл со сведениями о компьютере: процессор, видеокарта, память, диски, версии Windows и драйверов, найденные проблемы.\n" +
             "2. Откроет его, чтобы вы сразу увидели, что внутри.\n\n" +
             "В Windows ничего не меняется. Файл можно показать тому, кто помогает, или приложить к сообщению в Telegram («Связаться с нами»).");
    }

    // ================================================================== ОТКАТ
    private int _backupGeneration;
    private async void BuildBackups()
    {
        int generation = ++_backupGeneration;
        BakList.Items.Clear();
        List<(string File, BackupData Data)> list;
        try { list = await Task.Run(Backup.List); }
        catch (Exception ex) { Log.Warn("Не удалось прочитать копии отката: " + ex.Message); return; }
        if (generation != _backupGeneration) return;
        // кнопке «Вернуть как было» нечего возвращать, пока записей нет (SetOwn: иначе конец любой операции включит её снова)
        SetOwn(BtnRollback, list.Count > 0);
        if (list.Count == 0)
        {
            BakList.Items.Add(new ListBoxItem { Content = new TextBlock
            {
                Text = "Пока нет сохранённых изменений. Перед каждым изменением настроек Merno сохраняет копию, и она появится здесь.",
                Foreground = B("Sub"), TextWrapping = TextWrapping.Wrap
            }, IsEnabled = false });
            return;
        }
        var names = _tweaks.ToDictionary(t => t.Id, t => t.Name);
        foreach (var (file, d) in list)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = d.Date.ToString("dd MMMM yyyy, HH:mm"), FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("Text") });
            var what = d.Tweaks.Distinct().Select(id => names.TryGetValue(id, out var n) ? n : id).ToList();
            string sum = d.Title != null ? d.Title : what.Count > 0
                ? $"{what.Count} {Plural(what.Count, "настройка", "настройки", "настроек")}: " + string.Join(", ", what.Take(4)) + (what.Count > 4 ? " и др." : "")
                : d.Reg.Any(r => r.Path.Contains("StartupApproved")) ? "Изменения автозагрузки" : $"Изменений: {d.Reg.Count}";
            var sumTb = new ExpandText(sum) { Margin = new Thickness(0, 3, 0, 0) };
            sumTb.Inner.FontSize = 13.5;
            sp.Children.Add(sumTb);
            BakList.Items.Add(new ListBoxItem { Content = sp, Tag = file });
        }
        BakList.SelectedIndex = 0;   // самая свежая запись выбрана сразу — кнопке «Вернуть как было» есть что вернуть
    }

    private async Task RollbackAsync()
    {
        if (_busy) return;
        if (BakList.SelectedItem is not ListBoxItem { Tag: string file }) { SetStatus("Выберите запись в списке"); return; }
        if (!Ask("Вернуть всё, что было изменено в выбранной записи?", "Merno", MessageBoxImage.Question, "Вернуть как было")) return;
        if (!await Busy("Возвращаю как было…", () => Backup.Rollback(file))) return;
        BuildBackups();
        await BuildTweaksAsync();
        BuildStartup();
        AskReboot();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_themeSwap) Diag.ExitStep("окно закрыто");
        Log.Written -= OnLog;
        Apps.Progress -= OnAppProgress;   // после смены темы окно закрывается, а программа работает дальше — не держим старое окно
        DisposeTray();   // MainWindow.Tray.cs
        if (!_themeSwap) Diag.ExitStep("значок в трее убран");
        base.OnClosed(e);
    }

    // ================================================================== ИНСТРУКЦИЯ
    // Страница «Частые вопросы»: «Как начать» → «Что есть в программе» → вопросы (FaqBox, BuildFaq) → «Если что-то пошло не так».
    // Справа «Разделы» — прокрутка к этим блокам.
    private void BuildHelp()
    {
        HelpStart.Children.Clear(); HelpSections.Children.Clear(); HelpPanel.Children.Clear(); HelpJump.Children.Clear();

        // Как начать — три шага; плитка с цифрой вместо значка
        var steps = new[]
        {
            ("Пройдите «Настройку Windows»", "На «Главной», под проверкой: режим, программы, внешний вид, драйверы. Ничего не меняется, пока не нажмёте «Начать»."),
            ("Перезагрузите компьютер", "Так все изменения заработают."),
            ("Иногда нажимайте «Проверить компьютер»","Merno подскажет, если появится мусор, лишнее в автозагрузке или устаревший драйвер."),
        };
        for (int i = 0; i < steps.Length; i++)
        {
            var num = new TextBlock { Style = (Style)FindResource("IconGlyph"), Text = (i + 1).ToString(), FontFamily = (FontFamily)FindResource("Display"), FontWeight = FontWeights.SemiBold };
            HelpStart.Children.Add(HelpRow(num, steps[i].Item1, steps[i].Item2, i == 0 ? "Открыть" : null, i == 0 ? () => OpenFeature("PageHome", "HomeSetup") : null));
        }

        // Что есть в программе — по пункту меню на строку
        var secs = new (string Glyph, string Title, string Sub, string Page, string Target)[]
        {
            ("", "Главная", "Проверка компьютера и настройка Windows по шагам. Начинайте отсюда.", "PageHome", "HomeSetup"),
            ("", "Программы", "Установить, обновить и удалить программы. Можно отметить несколько сразу.", "PageApps", "AppsList"),
            ("", "Компоненты", "То, без чего не запускаются игры: Visual C++, DirectX, .NET.", "PageRuntime", null),
            ("", "Драйверы", "Скачайте драйверы видеокарты, звука и других устройств со страницы производителя.", "PageDrivers", "DrvCardGpu"),
            ("", "Обновления Windows", "Обновления системы и драйверов от Windows. Показывает, активирована ли Windows.", "Windows", null),
            ("", "Автозагрузка", "Что запускается вместе с Windows. Меньше программ может ускорить загрузку.", "PageStartup", "StartList"),
            ("", "Очистка", "Ненужные файлы и остатки удалённых программ. Личные файлы не трогает.", "PageClean", "CleanList"),
            ("", "Инструменты", "Починить Windows и интернет, оптимизировать диски, точка восстановления.", "PageTools", null),
            ("", "Быстрый доступ", "Нужные окна Windows в один клик.", "PageQuick", null),
            ("", "Настройки Windows", "Для опытных: каждая настройка по отдельности. Нужен «Расширенный режим».", "PageTweaks", null),
            ("", "Контекстное меню", "Для опытных: лишние пункты меню правой кнопки мыши. Нужен «Расширенный режим».", "PageMenu", null),
        };
        foreach (var s in secs)
        {
            var (pg, tg) = (s.Page, s.Target);
            var g = new TextBlock { Style = (Style)FindResource("IconGlyph"), Text = s.Glyph };
            HelpSections.Children.Add(HelpRow(g, s.Title, s.Sub, "Открыть", () => OpenFeature(pg, tg)));
        }

        // Если что-то пошло не так
        var fix = new (string Glyph, string Title, string Sub, string Page, string Target)[]
        {
            ("", "Откат изменений", "Вернуть настройки, которые поменял Merno. Затем перезагрузите компьютер.", "PageRollback", "BakList"),
            ("", "Восстановление системы", "Если Windows работает неправильно, можно вернуться к точке восстановления.", "PageRollback", "BtnSysRestore"),
            ("", "Журнал", "Всё, что делал Merno, по шагам.", "PageLog", null),
            ("", "Связаться с нами", "Ошибка, идея или вопрос. Напишите нам в Telegram, ответ придёт туда же.", "PageContact", null),
        };
        foreach (var s in fix)
        {
            var (pg, tg) = (s.Page, s.Target);
            var g = new TextBlock { Style = (Style)FindResource("IconGlyph"), Text = s.Glyph };
            HelpPanel.Children.Add(HelpRow(g, s.Title, s.Sub, "Открыть", () => OpenFeature(pg, tg)));
        }

        foreach (var h in new[] { HelpStartHead, HelpSecHead, HelpHead })
            HelpJump.Children.Add(SideJump(h.Text, h, out _));
    }

    /// <summary>Строка-плашка страницы «Частые вопросы»: значок, название, пояснение и ссылка «Открыть →» справа.</summary>
    private Border HelpRow(TextBlock glyph, string title, string sub, string link, Action open)
    {
        var dp = new DockPanel();
        var tile = new Border { Style = (Style)FindResource("IconTile"), Child = glyph };
        DockPanel.SetDock(tile, Dock.Left);
        dp.Children.Add(tile);
        if (link != null)
        {
            var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(link + " →")) { Foreground = B("Accent"), TextDecorations = null };
            h.Click += (_, _) => open();
            var lt = new TextBlock(h) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0), FontSize = 13.5 };
            DockPanel.SetDock(lt, Dock.Right);
            dp.Children.Add(lt);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Style = (Style)FindResource("RowTitle"), Text = Typo.Nbsp(title) });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("RowSub"), Text = Typo.Nbsp(sub) });
        dp.Children.Add(text);
        return new Border { Style = (Style)FindResource("Plate"), Child = dp };
    }
}
