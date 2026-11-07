// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Merno.Core;
using Run = System.Windows.Documents.Run;

namespace Merno;

// «Автозагрузка»: группы «Программы» и «Драйверы и защита». Строка — как в «Настройках Windows»: галочка (включено или нет), значок,
// название, одна строка пояснения, «⋯» («Показать в Проводнике», «Убрать из автозагрузки»).
// Справа — «Отмеченные» (включить/выключить разом), сколько включено, «Добавить свою программу» и «Обновить список».
public partial class MainWindow
{
    private bool _startLoading;
    private readonly HashSet<StartupItem> _startSel = new();
    private readonly List<(StartupItem It, CheckBox Cb)> _startChecks = new();

    private void InitStartup()
    {
        BtnStartApply.Click += (_, _) => Fire(ApplyStartAsync, "Автозагрузка");
        BtnStartSelNone.Click += (_, _) => { foreach (var (it, cb) in _startChecks.ToList()) cb.IsChecked = it.Enabled; _startSel.Clear(); UpdateStartSummary(); };
        SetOwn(BtnStartApply, false);
    }

    private async void BuildStartup()
    {
        if (_startLoading) return;
        _startLoading = true;
        _startSel.Clear();
        StartList.Children.Clear();
        StartList.Children.Add(new TextBlock { Text = "Ищу всё, что запускается вместе с Windows…", Foreground = B("Sub") });
        UpdateStartSummary();
        List<StartupItem> list = new();
        try { await Task.Run(() => list = Startup.List() ?? new()); }
        catch (Exception ex)
        {
            Log.Warn("Автозагрузка: " + ex.Message); _startLoading = false;
            ShowError(StartList, "Не получилось загрузить автозагрузку.", () => { BuildStartup(); return Task.CompletedTask; });
            UpdateStartSummary();
            return;
        }
        _startup = list; _startLoading = false;
        _adviceStartup = list;
        RenderCheckAdvice();
        RenderStartup();
    }

    /// <summary>Рисует уже прочитанный список автозагрузки: по группам, внутри — в выбранном порядке.</summary>
    private void RenderStartup()
    {
        if (_startLoading) return;
        StartList.Children.Clear(); _startChecks.Clear();
        UpdateStartSummary();
        if (_startup.Count == 0) { StartList.Children.Add(new TextBlock { Text = "В автозагрузке ничего нет.", Foreground = B("Sub") }); return; }
        var byName = StringComparer.CurrentCultureIgnoreCase;
        IEnumerable<StartupItem> Order(IEnumerable<StartupItem> src) => _settings.SortStartup switch
        {
            1 => src.OrderByDescending(x => x.Enabled).ThenBy(x => x.Title ?? x.Name, byName),
            2 => src.OrderBy(x => x.Enabled).ThenBy(x => x.Title ?? x.Name, byName),
            _ => src.OrderBy(x => x.Title ?? x.Name, byName)
        };
        bool first = true;
        foreach (var (prot, title) in new[] { (false, "Программы"), (true, "Драйверы и защита") })
        {
            var items = Order(_startup.Where(x => x.Protected == prot)).ToList();
            if (items.Count == 0) continue;
            var (head, cnt) = ListHeader(title, first); first = false;
            cnt.Inlines.Add(new Run($"{items.Count}"));
            if (prot) cnt.Inlines.Add(new Run("  ·  лучше не выключать") { Foreground = B("Warn") });
            StartList.Children.Add(head);
            foreach (var it in items) StartList.Children.Add(StartRow(it));
        }
    }

    /// <summary>Сводка справа: отмеченное и сколько записей включено.</summary>
    private void UpdateStartSummary()
    {
        // _startSel — строки, где галочку изменили, но ещё не применили
        int toOn = _startSel.Count(x => !x.Enabled), toOff = _startSel.Count(x => x.Enabled), n = _startSel.Count;
        StartSelCount.Text = n == 0 ? "Галочка означает запуск вместе с Windows. Измените нужное и нажмите «Применить»."
            : string.Join(" · ", new[] { toOn > 0 ? $"включить: {toOn}" : null, toOff > 0 ? $"выключить: {toOff}" : null }.Where(x => x != null));
        BtnStartApply.Content = n > 0 ? $"Применить ({n})" : "Применить";
        SetOwn(BtnStartApply, n > 0);
        BtnStartSelNone.Visibility = _startSel.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_startLoading) { StartCount.Text = "…"; StartCountInfo.Text = "Ищу программы…"; return; }
        int on = _startup.Count(x => x.Enabled);
        StartCount.Text = _startup.Count == 0 ? "0" : $"{on} из {_startup.Count}";
        StartCountInfo.Text = _startup.Count == 0 ? "Вместе с Windows ничего не запускается."
            : on == 0 ? "Всё выключено."
            : AdvicePolicy.StartupCandidates(_settings, _startup) == 0 ? "Включены только защищённые программы и те, которые вы решили оставить."
            : "Нужную программу можно оставить: в её меню «⋯» выберите «Не предлагать отключать».";
    }

    /// <summary>Одна строка пояснения: кто сделал программу и откуда она запускается.</summary>
    private static string StartWhat(StartupItem it)
    {
        string who = it.Company;
        if (string.IsNullOrWhiteSpace(who) && it.FilePath != null && !it.FilePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) who = Path.GetFileName(it.FilePath);
        string how = it.Kind switch { StartKind.Task => "через планировщик", StartKind.Store => "приложение Microsoft Store", StartKind.Folder => "из папки автозагрузки", _ => null };
        var s = string.Join(" · ", new[] { who, how }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (s.Length == 0) return "Запускается вместе с Windows";
        return char.ToUpper(s[0]) + s[1..];
    }

    /// <summary>Строка: галочка · значок · название · пояснение · «⋯».</summary>
    private Border StartRow(StartupItem it)
    {
        string name = it.Title ?? it.Name;
        var ico = FileIconBox(name, it.IconPath);   // значок найден заранее в Startup.List()
        var title = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis,
                                    ToolTip = it.Title != null && it.Title != it.Name ? $"{it.Title}\n{it.Name}" : null };
        var line = new TitleLine(title);
        if (AdvicePolicy.StartupIgnored(_settings, it)) line.Children.Add(Chip("Нужна", "Sub"));
        // галочка = запускается с Windows; изменение — кнопкой «Применить»
        var cb = new CheckBox { IsChecked = _startSel.Contains(it) ? !it.Enabled : it.Enabled };
        void Changed() { if (cb.IsChecked != it.Enabled) _startSel.Add(it); else _startSel.Remove(it); UpdateStartSummary(); }
        cb.Checked += (_, _) => Changed();
        cb.Unchecked += (_, _) => Changed();
        _startChecks.Add((it, cb));

        var more = new Button { Content = "", FontFamily = (FontFamily)FindResource("Icons"), Style = (Style)FindResource("Ghost"), Padding = new Thickness(9, 7, 9, 7),
                                Margin = new Thickness(8, 0, -4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Ещё" };
        var menu = new ContextMenu();
        var ignore = new MenuItem { Header = "Не предлагать отключать", IsCheckable = true, IsChecked = AdvicePolicy.StartupIgnored(_settings, it) };
        ignore.Click += (_, _) =>
        {
            string key = AdvicePolicy.StartupKey(it);
            _settings.StartupAdviceIgnored ??= new();
            _settings.StartupAdviceIgnored.RemoveAll(value => string.Equals(value, key, StringComparison.OrdinalIgnoreCase));
            if (ignore.IsChecked) _settings.StartupAdviceIgnored.Add(key);
            _settings.Save();
            RenderStartup();
            RefreshAdviceChoices();
        };
        menu.Items.Add(ignore);
        string file = it.FilePath;
        if (file != null)
        {
            var mi = new MenuItem { Header = "Показать в Проводнике" };
            mi.Click += (_, _) => ShowStartFile(file);
            menu.Items.Add(mi);
        }
        // убрать совсем — только записи реестра и папки автозагрузки; задачи и приложения Store можно лишь выключить
        if (it.Removable && it.Kind != StartKind.Task)
        {
            var mi = new MenuItem { Header = "Убрать из автозагрузки" };
            mi.Click += (_, _) => Fire(() => RemoveStartAsync(it), "Автозагрузка");
            menu.Items.Add(mi);
        }
        more.ContextMenu = menu;
        more.Click += (_, _) => { menu.PlacementTarget = more; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; };
        if (menu.Items.Count == 0) more.Visibility = Visibility.Hidden;   // место остаётся — строки стоят ровно
        return SelRow(ico, line, StartWhat(it), cb, more, string.IsNullOrWhiteSpace(it.Command) ? null : it.Command);
    }

    /// <summary>«Применить»: включить отмеченные, выключить снятые.</summary>
    private async Task ApplyStartAsync()
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        var todo = _startSel.ToList();
        if (todo.Count == 0) return;
        var prot = todo.Where(x => x.Enabled && x.Protected).ToList();
        if (prot.Count > 0 && !Ask("Это нужно драйверам, защите или античиту. Без них что-то может перестать работать:\n\n" +
                                   string.Join("\n", prot.Select(x => "• " + (x.Title ?? x.Name))) + "\n\nВсё равно выключить?",
                                   "Лучше не выключать", MessageBoxImage.Warning, "Выключить")) return;
        using var backupSession = Backup.Session();
        var failed = new List<string>();
        // задачи планировщика переключаются через schtasks — это пара секунд на каждую
        bool ran = await QueueRun("Автозагрузка", $"Применяю изменения ({todo.Count})…", todo,
            it => (it.Enabled ? "Выключить: " : "Включить: ") + (it.Title ?? it.Name), it =>
            {
                bool on = !it.Enabled;
                try { Startup.SetEnabled(it, on); it.Enabled = on; Log.Ok($"{it.Name}: {(on ? "будет запускаться" : "больше не запускается")} вместе с Windows"); return (true, null); }
                catch (Exception ex) { lock (failed) failed.Add(it.Title ?? it.Name); Log.Warn($"{it.Name}: {ex.Message}"); return (false, ForPerson(ex, "Не получилось изменить")); }
            });
        try { Backup.Save(); } catch (Exception ex) { Log.Warn("Автозагрузка, копия: " + ex.Message); }
        if (!ran) return;
        _startSel.Clear();
        _adviceStartup = _startup;
        RenderCheckAdvice();
        RenderStartup();
        SetStatus(failed.Count == 0 ? "Готово. Сработает при следующем включении компьютера"
                                    : "Не получилось: " + string.Join(", ", failed));
    }

    /// <summary>Открывает папку программы в Проводнике и выделяет файл.</summary>
    private void ShowStartFile(string file)
    {
        if (!File.Exists(file)) { SetStatus("Не удалось найти файл. Возможно, программу уже удалили"); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{file}\"") { UseShellExecute = false }); }
        catch (Exception ex) { Log.Warn("Открытие папки: " + ex.Message); SetStatus("Не удалось открыть папку"); }
    }

    /// <summary>«Убрать из автозагрузки»: запись удаляется совсем, сама программа остаётся.</summary>
    private async Task RemoveStartAsync(StartupItem it)
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        string name = it.Title ?? it.Name;
        if (!Ask($"Убрать «{name}» из автозагрузки?\n\nСама программа останется на компьютере. Вернуть её сюда можно кнопкой «Добавить свою программу».",
                 "Убрать из автозагрузки", MessageBoxImage.Question, "Убрать")) return;
        try { await Task.Run(() => Startup.Remove(it)); }
        catch (Exception ex) { Log.Warn($"Автозагрузка, удаление {it.Name}: {ex.Message}"); Warn("Не получилось убрать программу из автозагрузки."); return; }
        _startup.Remove(it); _startSel.Remove(it);
        _adviceStartup = _startup;
        RenderCheckAdvice();
        RenderStartup();
        SetStatus($"«{name}» больше не в автозагрузке");
    }
}
