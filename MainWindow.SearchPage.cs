// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// «Поиск» (пункт меню, Ctrl+F): разделы, отдельные функции, программы каталога, настройки Windows и окна «Быстрого доступа».
// Нажатие на результат открывает раздел и подсвечивает нужную строку — так же, как «Где исправить →».
public partial class MainWindow
{
    private sealed record SearchHit(string Kind, string Title, string Sub, string Keys, Action Go);

    private List<SearchHit> _searchIndex;
    private bool _searchWired;

    private void InitGlobalSearch()
    {
        if (_searchWired) return;
        _searchWired = true;
        GlobalSearch.TextChanged += (_, _) => RenderGlobalSearch();
        GlobalSearch.GotKeyboardFocus += (_, _) => SyncGlobalHint();
        GlobalSearch.LostKeyboardFocus += (_, _) => SyncGlobalHint();
        // Enter — открыть первый результат
        GlobalSearch.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var first = Find(GlobalSearch.Text).FirstOrDefault();
            first?.Go();
        };
        PageSearch.IsVisibleChanged += (_, _) =>
        {
            if (!PageSearch.IsVisible) return;
            _searchIndex = null;   // каталог мог поменяться (расширенный режим, установленные программы)
            RenderGlobalSearch();
            _ = LoadInstalledForSearchAsync();
            Dispatcher.BeginInvoke(new Action(() => { GlobalSearch.Focus(); GlobalSearch.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
        };
        // Ctrl+F из любого раздела, кроме полей ввода, где Ctrl+F никто не ждёт как «поиск по программе»
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            NavSearch.IsChecked = true;
            if (PageSearch.Visibility != Visibility.Visible) ShowPage(PageSearch);
            e.Handled = true;
        };
    }

    private void SyncGlobalHint() => GlobalSearchHint.Visibility = GlobalSearch.Text.Length == 0 && !GlobalSearch.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;

    private List<SearchHit> BuildSearchIndex()
    {
        var list = new List<SearchHit>();
        void Sec(string title, string sub, string keys, string page, string target = null) =>
            list.Add(new SearchHit("Раздел", title, sub, keys, () => OpenFeature(page, target)));

        Sec("Настройка Windows", "Главная → режим, программы, внешний вид, драйверы", "оптимизировать оптимизация режим игровой бережный обычный первый запуск переустановка мастер", "PageHome", "HomeSetup");
        Sec("Главная", "Проверка компьютера и советы", "проверить советы главная", "PageHome");
        Sec("Мой компьютер", "Процессор, память, видеокарта, диски, сеть", "сведения характеристики железо процессор озу память видеокарта диск mac ip сеть адаптер скопировать", "Pc");
        Sec("Монитор", "Мой компьютер → частота экрана", "частота герц гц hz экран монитор 144 165 240", "Pc", "MonList");
        Sec("Температура", "Мой компьютер → температура процессора и видеокарты", "температура нагрев перегрев градусы", "Pc", "SysTemps");
        Sec("Программы", "Установить и обновить программы", "установить скачать обновить браузер", "PageApps", "AppsList");
        Sec("Приложения Windows", "Встроенные приложения Microsoft", "store магазин uwp встроенные", "PageStore");
        Sec("Удалить программы", "Удаление программ и приложений Windows", "удалить деинсталляция uninstall edge onedrive", "PageUninstall");
        Sec("Компоненты", "Visual C++, DirectX и .NET для игр", "visual c++ directx net java physx библиотеки игры не запускается", "PageRuntime");
        Sec("Драйверы", "Видеокарта, звук, плата и другие устройства", "драйвер nvidia amd intel видеокарта звук мышь клавиатура", "PageDrivers");
        Sec("Обновления Windows", "Обновления системы и драйверов, активация", "центр обновления update активация лицензия", "Windows");
        Sec("Автозагрузка", "Что запускается вместе с Windows", "автозапуск загрузка старт startup", "PageStartup", "StartList");
        Sec("Очистка", "Ненужные файлы и остатки программ", "мусор временные файлы место диск temp", "PageClean", "CleanList");
        Sec("Инструменты", "Починить Windows и интернет, диски, точка восстановления", "починить sfc dism безопасный режим", "PageTools");
        Sec("Быстрый доступ", "Окна Windows в один клик", "окна панель управления", "PageQuick");
        Sec("Настройки Windows", "Для опытных: каждая настройка отдельно", "твики телеметрия реклама", "PageTweaks");
        Sec("Контекстное меню", "Для опытных: меню правой кнопки", "правая кнопка меню классическое", "PageMenu");
        Sec("Частые вопросы", "Как начать и что делать, если что-то пошло не так", "помощь инструкция справка", "PageHelp");
        Sec("Связаться с нами", "Ошибка, идея или вопрос", "написать поддержка отзыв ошибка баг", "Contact");
        Sec("Настройки", "Обновления Merno, автозапуск, расширенный режим", "параметры merno расширенный режим", "Settings");
        Sec("Внешний вид", "Тема, значок, размер интерфейса", "тема цвет значок иконка масштаб размер", "Look");
        Sec("Откат изменений", "Вернуть то, что поменял Merno", "отменить вернуть откат восстановить", "PageRollback", "BakList");
        Sec("Журнал", "Всё, что делал Merno, по шагам", "лог log история", "PageLog");
        Sec("Версия и лицензия", "Версия, авторы и лицензия", "версия about авторы разработчики лицензия", "Settings", "RowUpdate");

        // плитки «Инструментов» — по названию плитки
        foreach (var (t, k) in new[] {
            ("Проверить и починить Windows", "sfc dism ошибки системные файлы"), ("Оптимизировать диски", "дефрагментация trim ssd"),
            ("Починить интернет", "сеть dns сброс winsock"), ("Создать точку восстановления", "точка восстановления бэкап"),
            ("Перезагрузить в безопасный режим", "безопасный режим safe mode"), ("Отчёт о компьютере", "отчёт сведения файл") })
        {
            string title = t;
            list.Add(new SearchHit("Инструмент", t, "Инструменты", k, () => { ShowPageByName("PageTools"); HighlightTitle(PageTools, title); }));
        }

        try
        {
            foreach (var a in Apps.Load().Where(a => a.tab == null && !Apps.IsYandex(a.name) && !Apps.IsYandex(a.winget) && Apps.Fits(a, _ctx.S)))
            {
                string name = a.name;
                list.Add(new SearchHit("Программа", a.name, a.category ?? "Программы", a.desc ?? "", () => { ShowPageByName("PageApps"); HighlightTitle(PageApps, name); }));
            }
        }
        catch (Exception ex) { Log.Skip("Поиск, программы: " + ex.Message); }

        if (_settings.Advanced)
            foreach (var t in _tweaks.Where(t => t.Applicable(_ctx.S)))
            {
                string name = t.Name;
                list.Add(new SearchHit("Настройка Windows", t.Name, t.Cat, t.Desc ?? "", () => { ShowPageByName("PageTweaks"); HighlightTitle(PageTweaks, name); }));
            }

        foreach (var q in QuickCatalog)
        {
            var p = q;
            list.Add(new SearchHit("Окно Windows", q.Name, "Быстрый доступ · " + q.Group, q.File, () => QuickOpen(p.Name, p.File, p.Args)));
        }
        // программы на этом компьютере (как в «Удалить»): Chrome, Telegram, Steam… — открыть «Удалить» с этой строкой
        foreach (var a in _searchInstalled ?? _unApps)
        {
            if (string.IsNullOrWhiteSpace(a.Name) || a.System) continue;
            string name = a.Name;
            bool builtin = BuiltinFor(a) is BuiltinApp bi && bi.TweakId == null;   // встроенное из списка — в своём разделе «Удалить»
            list.Add(new SearchHit("Установлена", a.Name, "Удалить · " + (string.IsNullOrEmpty(a.Publisher) ? a.Source : a.Publisher), a.Pkg ?? "",
                                   () => OpenUninstallFor(builtin ? BuiltinFor(a).Name : name, builtin)));
        }
        // встроенные приложения Windows из списка (Outlook, Xbox, Погода…) — даже если их сейчас не нашли
        var have = new HashSet<string>(list.Where(h => h.Kind == "Установлена").Select(h => h.Title), StringComparer.CurrentCultureIgnoreCase);
        foreach (var b in Uninstall.Builtin.Where(b => !have.Contains(b.Name)))
        {
            string name = b.Name;
            list.Add(new SearchHit("Приложение Windows", b.Name, "Удалить · встроенные приложения", string.Join(" ", b.Pkgs) + " " + b.Desc,
                                   () => OpenUninstallFor(name, b.TweakId == null)));
        }
        return list;
    }

    private List<InstalledApp> _searchInstalled;
    private bool _searchInstalledLoading;

    /// <summary>Список установленного собирается секунды — в фоне; когда готов, результаты дополняются сами.</summary>
    private async Task LoadInstalledForSearchAsync()
    {
        if (_searchInstalled != null || _searchInstalledLoading) return;
        if (_unLoaded) { _searchInstalled = _unApps; _searchIndex = null; RenderGlobalSearch(); return; }
        _searchInstalledLoading = true;
        try { var apps = await Task.Run(() => Uninstall.List() ?? new()); _searchInstalled = apps; }
        catch (Exception ex) { Log.Skip("Поиск, установленные программы: " + ex.Message); }
        finally { _searchInstalledLoading = false; }
        _searchIndex = null;
        if (PageSearch.IsVisible) RenderGlobalSearch();
    }

    /// <summary>«Удалить» с нужной строкой: вид (все / встроенные), фильтр по названию и подсветка, когда список построится.</summary>
    private void OpenUninstallFor(string name, bool builtin)
    {
        (builtin ? UnViewBuiltin : UnViewAll).IsChecked = true;
        UnFilter.Text = name;
        ShowPageByName("PageUninstall");
        HighlightTitle(PageUninstall, name);
    }

    /// <summary>Каждое слово запроса должно найтись в названии, пояснении или ключевых словах. Сначала — совпадения в начале названия.</summary>
    private IEnumerable<SearchHit> Find(string query)
    {
        _searchIndex ??= BuildSearchIndex();
        var words = (query ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Enumerable.Empty<SearchHit>();
        bool Has(string s, string w) => s != null && s.Contains(w, StringComparison.CurrentCultureIgnoreCase);
        return _searchIndex
            .Where(h => words.All(w => Has(h.Title, w) || Has(h.Sub, w) || Has(h.Keys, w)))
            .OrderBy(h => TitleRank(h.Title, words[0]))
            .ThenBy(h => KindRank(h.Kind))
            .ThenBy(h => h.Title.Length)
            .Take(60);
    }

    /// <summary>0 — название начинается с запроса, 1 — с него начинается слово («edge» → «Microsoft Edge»), 2 — где-то в названии, 3 — только в описании.</summary>
    internal static int TitleRank(string title, string w)
    {
        if (string.IsNullOrEmpty(title)) return 3;
        if (title.StartsWith(w, StringComparison.CurrentCultureIgnoreCase)) return 0;
        if (System.Text.RegularExpressions.Regex.IsMatch(title, @"(?<![\p{L}\p{N}])" + System.Text.RegularExpressions.Regex.Escape(w), System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return 1;
        return title.Contains(w, StringComparison.CurrentCultureIgnoreCase) ? 2 : 3;
    }

    /// <summary>При равном совпадении: разделы, потом то, что есть на компьютере, и только потом настройки.</summary>
    internal static int KindRank(string kind) => kind switch
    {
        "Раздел" => 0, "Установлена" => 1, "Приложение Windows" => 2, "Программа" => 3, "Инструмент" => 4, "Окно Windows" => 5, _ => 6
    };

    private void RenderGlobalSearch()
    {
        SyncGlobalHint();
        GlobalSearchList.Children.Clear();
        var q = GlobalSearch.Text.Trim();
        if (q.Length == 0)
        {
            GlobalSearchList.Children.Add(new TextBlock { Style = (Style)FindResource("SubText"), Margin = new Thickness(4, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
                Text = "Результаты появятся сразу после ввода. Enter открывает первый." });
            return;
        }
        var hits = Find(q).ToList();
        if (hits.Count == 0)
        {
            GlobalSearchList.Children.Add(new TextBlock { Style = (Style)FindResource("SubText"), Margin = new Thickness(4, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
                Text = Typo.Nbsp($"Ничего не нашлось по «{q}». Программы, которых нет в каталоге, ищите в «Программах» → «Найти».") });
            return;
        }
        foreach (var h in hits)
        {
            var row = new Border { Style = (Style)FindResource("Plate"), Cursor = Cursors.Hand };
            var dp = new DockPanel();
            var kind = Chip(h.Kind, "Sub");
            DockPanel.SetDock(kind, Dock.Right);
            dp.Children.Add(kind);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = h.Title, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(h.Sub))
                sp.Children.Add(new TextBlock { Text = h.Sub, Style = (Style)FindResource("SubText"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            dp.Children.Add(sp);
            row.Child = dp;
            var hit = h;
            row.MouseLeftButtonUp += (_, _) => hit.Go();
            GlobalSearchList.Children.Add(row);
        }
    }

    /// <summary>Подсветить строку с таким названием, когда раздел её построит (списки строятся в фоне — ждём до 10 секунд).</summary>
    private void HighlightTitle(FrameworkElement page, string title)
    {
        int tries = 0;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            if (++tries > 33 || !page.IsVisible) { timer.Stop(); return; }
            var tb = FindTitle(page, title);
            if (tb == null) return;
            timer.Stop();
            // подсвечиваем всю строку: ближайшую рамку-строку над названием
            FrameworkElement target = tb;
            for (DependencyObject el = tb; el != null && el != page; el = VisualTreeHelper.GetParent(el))
                if (el is Border b && b.Style != null && (b.Style == TryFindResource("Row") || b.Style == TryFindResource("Plate") || b.Style == TryFindResource("CardBorder"))) { target = b; break; }
            Highlight(target);
        };
        timer.Start();
    }

    private static TextBlock FindTitle(DependencyObject root, string title)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is TextBlock tb && tb.IsVisible && string.Equals(tb.Text, title, StringComparison.CurrentCultureIgnoreCase)) return tb;
            if (c is UIElement u && u.Visibility != Visibility.Visible) continue;
            var r = FindTitle(c, title);
            if (r != null) return r;
        }
        return null;
    }
}
