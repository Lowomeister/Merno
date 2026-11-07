// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

/// <summary>
/// «Быстрый доступ»: список окон Windows, который человек собирает сам: отмечает нужное из каталога
/// и добавляет свои программы, папки и адреса.
/// </summary>
public partial class MainWindow
{
    private sealed record QuickPanel(string Id, string Group, string Name, string File, string Args = null);

    /// <summary>Каталог окон и настроек Windows без того, чего нет в этой Windows (gpedit.msc в Home-версии): плитка, которая ничего не открывает, только сбивает с толку. По умолчанию показываются все — лишнее можно снять в «Изменить список».</summary>
    private static QuickPanel[] _quickCatalog;
    private static QuickPanel[] QuickCatalog => _quickCatalog ??= QuickCatalogAll.Where(p =>
        p.Id == "cmdadmin" || QuickKind(p.File) is "uri" or "web" || File.Exists(Sh.Resolve(p.File))).ToArray();

    private static readonly QuickPanel[] QuickCatalogAll =
    {
        new("devmgmt", "Система", "Диспетчер устройств", "devmgmt.msc"),
        new("diskmgmt", "Система", "Управление дисками", "diskmgmt.msc"),
        new("services", "Система", "Службы", "services.msc"),
        new("taskmgr", "Система", "Диспетчер задач", "taskmgr.exe"),
        new("msconfig", "Система", "Конфигурация системы", "msconfig.exe"),
        new("eventvwr", "Система", "Просмотр событий", "eventvwr.msc"),
        new("ncpa", "Система", "Сетевые подключения", "ncpa.cpl"),
        new("mmsys", "Система", "Звук (классический)", "mmsys.cpl"),
        new("powercfg", "Система", "Электропитание (классическое)", "powercfg.cpl"),
        new("appwiz", "Система", "Программы и компоненты", "appwiz.cpl"),
        new("control", "Система", "Панель управления", "control.exe"),
        new("envvars", "Система", "Переменные среды", "rundll32.exe", "sysdm.cpl,EditEnvironmentVariables"),
        new("sysprops", "Система", "Свойства системы", "SystemPropertiesAdvanced.exe"),

        new("regedit", "Инструменты", "Редактор реестра", "regedit.exe"),
        new("taskschd", "Инструменты", "Планировщик заданий", "taskschd.msc"),
        new("gpedit", "Инструменты", "Локальная групповая политика", "gpedit.msc"),
        new("wf", "Инструменты", "Брандмауэр (расширенный)", "wf.msc"),
        new("resmon", "Инструменты", "Монитор ресурсов", "resmon.exe"),
        new("msinfo", "Инструменты", "Сведения о системе", "msinfo32.exe"),
        new("features", "Инструменты", "Компоненты Windows", "optionalfeatures.exe"),
        new("netplwiz", "Инструменты", "Учётные записи (вход без пароля)", "netplwiz.exe"),
        new("rstrui", "Инструменты", "Восстановление системы", "rstrui.exe"),
        new("cleanmgr", "Инструменты", "Очистка диска (Windows)", "cleanmgr.exe"),
        new("cmdadmin", "Инструменты", "Терминал", "wt.exe"),

        new("display", "Параметры", "Экран", "ms-settings:display"),
        new("sound", "Параметры", "Звук", "ms-settings:sound"),
        new("power", "Параметры", "Электропитание", "ms-settings:powersleep"),
        new("network", "Параметры", "Сеть и интернет", "ms-settings:network"),
        new("bluetooth", "Параметры", "Bluetooth и устройства", "ms-settings:bluetooth"),
        new("mouse", "Параметры", "Мышь", "ms-settings:mousetouchpad"),
        new("defaultapps", "Параметры", "Приложения по умолчанию", "ms-settings:defaultapps"),
        new("windowsupdate", "Параметры", "Центр обновления", "ms-settings:windowsupdate"),
        new("privacy", "Параметры", "Конфиденциальность", "ms-settings:privacy"),
        new("gamemode", "Параметры", "Игровой режим", "ms-settings:gaming-gamemode"),
        new("graphics", "Параметры", "Графика", "ms-settings:display-advancedgraphics"),
        new("display-adv", "Параметры", "Частота обновления экрана", "ms-settings:display-advanced"),
        new("storage", "Параметры", "Память устройства", "ms-settings:storagesense"),
        new("startupapps", "Параметры", "Автозапуск приложений", "ms-settings:startupapps"),
        new("defender", "Параметры", "Безопасность Windows", "windowsdefender:"),
        new("recovery", "Параметры", "Восстановление", "ms-settings:recovery"),
        new("activation", "Параметры", "Активация", "ms-settings:activation"),
        new("about", "Параметры", "О системе", "ms-settings:about"),
        new("nightlight", "Параметры", "Ночной свет", "ms-settings:nightlight"),
        new("mic", "Параметры", "Доступ к микрофону", "ms-settings:privacy-microphone"),
    };

    private IEnumerable<string> QuickChosen() =>
        _settings.QuickPanels ?? QuickCatalog.Select(p => p.Id);

    private static readonly bool ShowMenuToggleInQuick = false;

    /// <summary>Одна строка пояснения к окну из каталога — зачем оно нужно.</summary>
    private static readonly Dictionary<string, string> QuickInfo = new()
    {
        ["devmgmt"] = "Видеокарта, звук, USB и другое «железо», драйверы",
        ["diskmgmt"] = "Разделы и буквы дисков, форматирование",
        ["services"] = "Фоновые службы Windows: запуск и остановка",
        ["taskmgr"] = "Что нагружает компьютер, закрытие зависших программ",
        ["msconfig"] = "Безопасный режим и параметры загрузки",
        ["eventvwr"] = "Журнал ошибок и сбоев Windows",
        ["ncpa"] = "Сетевые адаптеры, Wi-Fi и DNS",
        ["mmsys"] = "Устройства записи и воспроизведения звука",
        ["powercfg"] = "Схемы питания и действия кнопки включения",
        ["appwiz"] = "Список установленных программ и их удаление",
        ["control"] = "Старые настройки Windows в привычном виде",
        ["envvars"] = "Переменные PATH, TEMP и другие",
        ["sysprops"] = "Файл подкачки, быстродействие, имя компьютера",
        ["regedit"] = "Тонкие настройки Windows. Неосторожные правки опасны",
        ["taskschd"] = "Что запускается по расписанию",
        ["gpedit"] = "Правила Windows для опытных (нет в версии Home)",
        ["wf"] = "Правила для входящих и исходящих соединений",
        ["resmon"] = "Нагрузка на процессор, память, диск и сеть",
        ["msinfo"] = "Подробные данные о системе и железе",
        ["features"] = "Включение .NET, Hyper-V, песочницы и другого",
        ["netplwiz"] = "Вход в Windows без ввода пароля",
        ["rstrui"] = "Откат Windows к точке восстановления",
        ["cleanmgr"] = "Стандартная очистка диска от временных файлов",
        ["cmdadmin"] = "Командная строка и PowerShell",
        ["display"] = "Разрешение, масштаб, несколько мониторов",
        ["sound"] = "Выбор устройств ввода и вывода, громкость",
        ["power"] = "Когда гасить экран и уходить в сон",
        ["network"] = "Wi-Fi, Ethernet, VPN, прокси",
        ["bluetooth"] = "Подключение наушников, мыши, клавиатуры",
        ["mouse"] = "Скорость курсора, кнопки, прокрутка",
        ["defaultapps"] = "Чем открывать сайты, музыку, видео, файлы",
        ["windowsupdate"] = "Проверка и пауза обновлений Windows",
        ["privacy"] = "Что приложениям разрешено знать о вас",
        ["gamemode"] = "Игровой режим Windows",
        ["graphics"] = "Какая видеокарта запускает какую игру",
        ["display-adv"] = "Частота обновления монитора (Гц)",
        ["storage"] = "Что занимает место и автоочистка",
        ["startupapps"] = "Программы, которые стартуют с Windows",
        ["defender"] = "Антивирус и защита от угроз",
        ["recovery"] = "Сброс компьютера и дополнительные варианты запуска",
        ["activation"] = "Состояние активации Windows",
        ["about"] = "Версия Windows, процессор, объём памяти",
        ["nightlight"] = "Тёплые тона экрана вечером",
        ["mic"] = "Какие приложения слышат микрофон",
    };

    /// <summary>Свой значок у каждого окна — по нему пункт находится глазами быстрее, чем по названию.</summary>
    private static readonly Dictionary<string, string> QuickGlyphs = new()
    {
        ["devmgmt"] = "", ["diskmgmt"] = "", ["services"] = "", ["taskmgr"] = "", ["msconfig"] = "",
        ["eventvwr"] = "", ["ncpa"] = "", ["mmsys"] = "", ["powercfg"] = "", ["appwiz"] = "",
        ["control"] = "", ["envvars"] = "", ["sysprops"] = "",
        ["regedit"] = "", ["taskschd"] = "", ["gpedit"] = "", ["wf"] = "", ["resmon"] = "",
        ["msinfo"] = "", ["features"] = "", ["netplwiz"] = "", ["rstrui"] = "", ["cleanmgr"] = "", ["cmdadmin"] = "",
        ["display"] = "", ["sound"] = "", ["power"] = "", ["network"] = "", ["bluetooth"] = "", ["mouse"] = "",
        ["defaultapps"] = "", ["windowsupdate"] = "", ["privacy"] = "", ["gamemode"] = "", ["graphics"] = "",
        ["display-adv"] = "", ["storage"] = "", ["startupapps"] = "", ["defender"] = "", ["recovery"] = "",
        ["activation"] = "", ["about"] = "", ["nightlight"] = "", ["mic"] = "",
    };

    private static string QuickGlyph(string group) => group switch { "Система" => "", "Инструменты" => "", "Параметры" => "", _ => "" };

    /// <summary>Что за свой пункт: сайт, папка или файл — от этого зависят значок и меню «⋯».</summary>
    private static string QuickKind(string target)
    {
        var t = (target ?? "").Trim().Trim('"');
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return "web";
        if (t.Contains(':') && !t.Contains('\\') && !t.Contains('/')) return "uri";   // ms-settings:…, steam:… и подобное
        try { if (Directory.Exists(Environment.ExpandEnvironmentVariables(t))) return "dir"; } catch { }
        return "file";
    }

    private FrameworkElement QuickIcon(string glyph, ImageSource img = null)
    {
        var box = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = B("AccentSoft")
        };
        if (img != null)
            box.Child = new Image { Source = img, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        else
            box.Child = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 16, Foreground = B("Accent"),
                                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return box;
    }

    private void QuickOpen(string name, string file, string args)
    {
        try
        {
            if (file == "wt.exe")
            {
                // не запускаем wt.exe из папки пользователя с правами администратора: Терминал открываем как приложение Windows,
                // а где его нет (Windows 10) — обычный PowerShell из System32
                string alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe");
                if (File.Exists(alias))
                    Sh.Open(@"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App");
                else
                    Sh.Open(Sh.Resolve("powershell.exe"));
                return;
            }
            var f = Environment.ExpandEnvironmentVariables(file.Trim().Trim('"'));
            if (f.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) f = "https://" + f;
            var target = QuickKind(f) is "web" or "uri" or "dir" ? f : Sh.Resolve(f);
            if (!Unelevated.Open(target, args)) throw new IOException("Windows не разрешила запуск с обычными правами пользователя.");
        }
        catch (Exception ex) { Log.Warn($"Не удалось открыть «{name}»: {ex.Message}"); SetStatus($"Не удалось открыть «{name}»"); }
    }

    private sealed record QuickItem(string Name, string Info, string Glyph, ImageSource Img, string File, string Args, (string Text, Action Act, string Done)[] Menu);

    private void BuildQuickAdv()
    {
        QuickAdvList.Children.Clear();

        // «Меню как в Windows 10» — то же, что в «Контекстном меню», чтобы включать в один клик
        if (ShowMenuToggleInQuick)   // переключатель вида меню живёт в «Контекстном меню» — здесь не дублируем
        {
            var row = new Border { Style = (Style)FindResource("Plate") };
            FlowColumns.SetFull(row, true);
            var dp = new DockPanel();
            var sw = new CheckBox { Style = (Style)FindResource("Check"), IsChecked = ShellMenu.ClassicEnabled(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            DockPanel.SetDock(sw, Dock.Left);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Меню правой кнопки как в Windows 10", FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("Text") });
            sp.Children.Add(new TextBlock { Text = Typo.Nbsp("Правый щелчок сразу открывает полное меню, без «Показать дополнительные параметры»."),
                                            Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 3, 0, 0) });
            dp.Children.Add(sw); dp.Children.Add(sp); row.Child = dp;
            bool guard = false;
            async void Apply(bool on)
            {
                if (guard) return;
                if (_busy) { guard = true; sw.IsChecked = !on; guard = false; SetStatus("Подождите, Merno ещё занят"); return; }
                try
                {
                    if (!await Busy(on ? "Включаю меню правой кнопки как в Windows 10…" : "Включаю меню Windows 11…", () => { ShellMenu.SetClassic(on); ShellMenu.RestartExplorer(); }))
                    { guard = true; sw.IsChecked = !on; guard = false; return; }
                    SetStatus(on ? "Меню правой кнопки как в Windows 10 включено" : "Меню Windows 11 включено");
                }
                catch (Exception ex) { Log.Warn("Вид меню: " + ex.Message); }
            }
            sw.Checked += (_, _) => Apply(true); sw.Unchecked += (_, _) => Apply(false);
            QuickAdvList.Children.Add(row);
        }

        var chosen = QuickChosen().ToHashSet();
        var custom = (_settings.QuickCustom ?? new()).Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Target)).ToList();
        int total = QuickCatalog.Count(p => chosen.Contains(p.Id)) + custom.Count;
        QuickCount.Text = total.ToString();
        QuickCountInfo.Text = total == 0 ? "Список пуст." : custom.Count > 0 ? $"Из них ваших: {custom.Count}. Чтобы открыть окно, нажмите «Открыть»." : "Чтобы открыть окно, нажмите «Открыть».";

        // поиск: по названию и пояснению; при поиске показываем и окна каталога, которых нет в списке
        string q = QuickSearch.Text.Trim();
        QuickSearchHint.Visibility = QuickSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool Match(params string[] s) => q.Length == 0 || s.Any(x => !string.IsNullOrEmpty(x) && x.Contains(q, StringComparison.CurrentCultureIgnoreCase));

        if (total == 0 && q.Length == 0)
        {
            var empty = new TextBlock { Text = "Список пуст. Нажмите «Выбрать окна Windows» или добавьте своё справа.", Foreground = B("Sub"), TextWrapping = TextWrapping.Wrap };
            FlowColumns.SetFull(empty, true);
            QuickAdvList.Children.Add(empty);
            return;
        }

        bool first = QuickAdvList.Children.Count == 0;
        int shown = 0;
        void AddGroup(string title, IEnumerable<QuickItem> source)
        {
            var list = source.Where(x => Match(x.Name, x.Info)).ToList();
            if (list.Count == 0) return;
            shown += list.Count;
            var (head, cnt) = ListHeader(title, first); first = false;
            cnt.Inlines.Add(new System.Windows.Documents.Run($"{list.Count}"));
            FlowColumns.SetFull(head, true);
            QuickAdvList.Children.Add(head);
            foreach (var it in list)
            {
                // каждое окно — отдельная плашка с тонкой рамкой и зазором: пункты не сливаются
                var row = new Border { Style = (Style)FindResource("Plate") };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                Grid.SetColumn(sp, 1);
                sp.Children.Add(new TextBlock { Text = it.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
                if (!string.IsNullOrWhiteSpace(it.Info))
                {
                    // пояснение мельче и бледнее названия — иначе строки сливаются и название трудно прочесть
                    var info = new ExpandText(Typo.Nbsp(it.Info)) { Margin = new Thickness(0, 2, 0, 0) };
                    info.Inner.FontSize = 12.5; info.Inner.Opacity = 0.8;
                    sp.Children.Add(info);
                }

                var open = new Button { Content = "Открыть", Style = (Style)FindResource("Outline"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(open, 2);
                open.Click += (_, _) => QuickOpen(it.Name, it.File, it.Args);

                var more = new Button { Content = "", FontFamily = (FontFamily)FindResource("Icons"), Style = (Style)FindResource("Ghost"), Padding = new Thickness(9, 7, 9, 7),
                                        Margin = new Thickness(8, 0, -4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Ещё" };
                Grid.SetColumn(more, 3);
                var menu = new ContextMenu();
                foreach (var (text, act, done) in it.Menu)
                {
                    var mi = new MenuItem { Header = text };
                    var a = act; var d = done;
                    mi.Click += (_, _) => { a(); if (d != null) { _settings.Save(); BuildQuickAdv(); SetStatus(d); } };
                    menu.Items.Add(mi);
                }
                more.ContextMenu = menu;
                more.Click += (_, _) => { menu.PlacementTarget = more; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; };
                if (menu.Items.Count == 0) more.Visibility = Visibility.Hidden;   // пустое меню не показываем

                g.Children.Add(QuickIcon(it.Glyph, it.Img)); g.Children.Add(sp); g.Children.Add(open); g.Children.Add(more);
                row.Child = g;
                QuickAdvList.Children.Add(row);
            }
        }

        // свои — первой группой: их добавили сами, они нужнее всего
        AddGroup("Мои", custom.Select(c =>
        {
            var item = c;
            var kind = QuickKind(c.Target);
            string path = Environment.ExpandEnvironmentVariables(c.Target.Trim().Trim('"'));
            var glyph = kind switch { "web" => "", "dir" => "", "uri" => "", _ => "" };
            ImageSource img = kind == "file" ? FileIcons.Get(path) : null;
            var menu = new List<(string, Action, string)>();
            if (kind is "file" or "dir")
                menu.Add(("Открыть расположение", () =>
                {
                    try
                    {
                        if (kind == "dir") QuickOpen(c.Name, path, null);
                        else if (File.Exists(path)) Sh.Open(Path.GetDirectoryName(path));
                        else SetStatus("Файл не найден. Возможно, его переместили");
                    }
                    catch (Exception ex) { Log.Warn("Открыть расположение: " + ex.Message); }
                }, null));
            menu.Add(("Убрать из списка", () => _settings.QuickCustom = (_settings.QuickCustom ?? new()).Where(x => !(x.Name == item.Name && x.Target == item.Target)).ToList(),
                      $"Убрано из списка: «{c.Name}»"));
            return new QuickItem(c.Name, c.Target, glyph, img, c.Target, null, menu.ToArray());
        }));
        foreach (var grp in QuickCatalog.Where(p => chosen.Contains(p.Id)).GroupBy(p => p.Group))
            AddGroup(grp.Key, grp.Select(p =>
            {
                var id = p.Id;
                return new QuickItem(p.Name, QuickInfo.GetValueOrDefault(id), QuickGlyphs.GetValueOrDefault(id) ?? QuickGlyph(p.Group), null, p.File, p.Args,
                    new[] { ("Убрать из списка", (Action)(() => _settings.QuickPanels = QuickChosen().Where(x => x != id).ToList()), $"Убрано из списка: «{p.Name}»") });
            }));
        if (q.Length > 0)
        {
            // при поиске — и то, что убрано из списка: открыть сразу или вернуть в список
            foreach (var grp in QuickCatalog.Where(p => !chosen.Contains(p.Id)).GroupBy(p => p.Group))
                AddGroup("Нет в вашем списке · " + grp.Key, grp.Select(p =>
                {
                    var id = p.Id;
                    return new QuickItem(p.Name, QuickInfo.GetValueOrDefault(id), QuickGlyphs.GetValueOrDefault(id) ?? QuickGlyph(p.Group), null, p.File, p.Args,
                        new[] { ("Добавить в список", (Action)(() => _settings.QuickPanels = QuickChosen().Append(id).Distinct().ToList()), $"Добавлено в список: «{p.Name}»") });
                }));
            if (shown == 0)
            {
                var none = new TextBlock { Text = $"По запросу «{q}» ничего не нашлось. Попробуйте другое слово, например «сеть» или «диск».",
                                           Foreground = B("Sub"), TextWrapping = TextWrapping.Wrap };
                FlowColumns.SetFull(none, true);
                QuickAdvList.Children.Add(none);
            }
        }
    }

    // ───────── карточка «Добавить своё» в боковой панели ─────────
    private bool _quickNameAuto = true;   // название подставлено само — его можно менять вслед за путём

    private void InitQuickAdd()
    {
        void Kind()
        {
            bool web = QuickAddWeb.IsChecked == true;
            QuickAddBrowse.Visibility = web ? Visibility.Collapsed : Visibility.Visible;
            QuickAddTargetLabel.Text = web ? "Адрес сайта" : QuickAddDir.IsChecked == true ? "Папка" : "Программа или ярлык";
            QuickAddTarget.ToolTip = web ? "Например: https://www.youtube.com" : "Нажмите «Обзор…» или вставьте путь";
            QuickAddValidate();
        }
        foreach (var rb in new[] { QuickAddExe, QuickAddDir, QuickAddWeb }) rb.Checked += (_, _) => Kind();

        QuickAddBrowse.Click += (_, _) =>
        {
            if (QuickAddDir.IsChecked == true)
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Какую папку открывать" };
                if (dlg.ShowDialog(this) == true) QuickAddTarget.Text = dlg.FolderName;
            }
            else
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Программы и ярлыки|*.exe;*.lnk;*.url;*.msc;*.cpl;*.bat;*.cmd|Все файлы|*.*" };
                if (dlg.ShowDialog(this) == true) QuickAddTarget.Text = dlg.FileName;
            }
        };
        QuickAddTarget.TextChanged += (_, _) =>
        {
            if (_quickNameAuto) { var n = QuickAutoName(QuickAddTarget.Text); _quickNameAutoSet = true; QuickAddName.Text = n; _quickNameAutoSet = false; }
            QuickAddValidate();
        };
        QuickAddName.TextChanged += (_, _) => { if (!_quickNameAutoSet) _quickNameAuto = QuickAddName.Text.Length == 0; QuickAddValidate(); };
        QuickAddBtn.Click += (_, _) =>
        {
            var t = QuickAddValue();
            if (!QuickAddValidate() || t.Length == 0) return;
            var n = QuickAddName.Text.Trim();
            if (n.Length == 0) n = QuickAutoName(t);
            (_settings.QuickCustom ??= new()).Add(new QuickCustom { Name = n.Length > 40 ? n[..40] : n, Target = t });
            _settings.Save();
            _quickNameAuto = true; QuickAddTarget.Text = ""; QuickAddName.Text = "";
            QuickSearch.Text = "";
            BuildQuickAdv();
            SetStatus($"«{n}» добавлено в «Мои»");
        };
        Kind();
    }
    private bool _quickNameAutoSet;

    private string QuickAddTargetText() => QuickAddTarget.Text.Trim().Trim('"');

    private string QuickAddValue()
    {
        var t = QuickAddTargetText();
        if (QuickAddWeb.IsChecked == true && t.Length > 0 && !t.Contains("://")) t = "https://" + t;
        return t;
    }

    /// <summary>Название из пути: имя файла, папки или адрес сайта без www.</summary>
    private string QuickAutoName(string target)
    {
        var t = (target ?? "").Trim().Trim('"');
        if (t.Length == 0) return "";
        try
        {
            if (QuickAddWeb.IsChecked == true || t.Contains("://"))
            {
                var u = new Uri(t.Contains("://") ? t : "https://" + t);
                var h = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
                return h.Length > 0 ? char.ToUpper(h[0]) + h[1..] : "";
            }
            var p = t.TrimEnd('\\', '/');
            var name = QuickAddDir.IsChecked == true ? Path.GetFileName(p) : Path.GetFileNameWithoutExtension(p);
            return string.IsNullOrEmpty(name) ? p : name;
        }
        catch { return ""; }
    }

    private bool QuickAddValidate()
    {
        var t = QuickAddValue();
        string err = null;
        if (t.Length > 0)
        {
            var path = Environment.ExpandEnvironmentVariables(t);
            if (QuickAddWeb.IsChecked == true)
            {
                if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https") || !u.Host.Contains('.')) err = "Похоже, это не адрес сайта";
            }
            else if (QuickAddDir.IsChecked == true) { if (!Directory.Exists(path)) err = "Такой папки нет"; }
            else if (!(path.Contains(':') && !path.Contains('\\')) && !File.Exists(path)) err = "Файл не найден";
        }
        QuickAddErr.Text = err ?? "";
        QuickAddErr.Visibility = err == null ? Visibility.Collapsed : Visibility.Visible;
        bool ok = t.Length > 0 && err == null;
        QuickAddBtn.IsEnabled = ok;
        return ok;
    }

    /// <summary>Окно «Мои окна Windows»: отметить окна из каталога. Свои пункты добавляются в боковой панели.</summary>
    private void EditQuick()
    {
        var chosen = QuickChosen().ToHashSet();
        var checks = new List<(CheckBox Cb, QuickPanel P)>();
        var checkStyle = TryFindResource("Check") as Style;
        var list = new StackPanel { Margin = new Thickness(24, 0, 24, 12) };
        foreach (var grp in QuickCatalog.GroupBy(p => p.Group))
        {
            list.Children.Add(new TextBlock { Text = grp.Key, Style = (Style)FindResource("H2"), FontSize = 16, Margin = new Thickness(0, 16, 0, 6) });
            var wrap = new WrapPanel();
            foreach (var p in grp)
            {
                var cb = new CheckBox { Style = checkStyle, IsChecked = chosen.Contains(p.Id), Width = 270, Margin = new Thickness(0, 4, 8, 4),
                                        Content = new TextBlock { Text = p.Name, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis },
                                        ToolTip = QuickInfo.GetValueOrDefault(p.Id) };
                checks.Add((cb, p)); wrap.Children.Add(cb);
            }
            list.Children.Add(wrap);
        }

        var root = new DockPanel { Background = B("Bg") };
        var headPanel = new StackPanel { Margin = new Thickness(24, 20, 24, 8) };
        headPanel.Children.Add(new TextBlock { Text = "Мои окна Windows", Style = (Style)FindResource("H2"), FontSize = 20 });
        headPanel.Children.Add(new TextBlock { Text = "Отметьте, что показывать в «Быстром доступе». Свои программы и сайты добавляются справа на странице.",
                                               Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 4, 0, 0) });
        DockPanel.SetDock(headPanel, Dock.Top);
        root.Children.Add(headPanel);
        var foot = new DockPanel { Margin = new Thickness(24, 12, 24, 20) };
        DockPanel.SetDock(foot, Dock.Bottom);
        var ok = new Button { Content = "Сохранить", Style = (Style)FindResource("Primary"), Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        // «Отметить все» и «Снять все» рядом — вместо «Вернуть по умолчанию»
        var all = new Button { Content = "Отметить все", Style = (Style)FindResource("Link"), VerticalAlignment = VerticalAlignment.Center };
        var none = new Button { Content = "Снять все", Style = (Style)FindResource("Link"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        all.Click += (_, _) => { foreach (var (cb, _) in checks) cb.IsChecked = true; };
        none.Click += (_, _) => { foreach (var (cb, _) in checks) cb.IsChecked = false; };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(all); left.Children.Add(none);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(cancel); right.Children.Add(ok);
        DockPanel.SetDock(right, Dock.Right);
        foot.Children.Add(right); foot.Children.Add(left);
        root.Children.Add(foot);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });

        var win = new Window
        {
            Title = "Быстрый доступ (Merno)", Owner = this, Width = 660, Height = Math.Min(620, SystemParameters.WorkArea.Height - 40),
            MinWidth = 500, MinHeight = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("Bg"), Foreground = B("Text"), FontFamily = FontFamily, FontSize = 14,
            ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize, Content = root
        };
        win.SourceInitialized += (_, _) => DarkTitleBar(win);
        ok.Click += (_, _) => win.DialogResult = true;
        if (win.ShowDialog() != true) return;
        _settings.QuickPanels = checks.Where(x => x.Cb.IsChecked == true).Select(x => x.P.Id).ToList();
        _settings.Save();
        BuildQuickAdv();
        SetStatus("Список «Быстрого доступа» сохранён");
    }
}
