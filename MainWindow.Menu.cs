// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using Merno.Core;
using Run = System.Windows.Documents.Run;

namespace Merno;

// «Контекстное меню» (для опытных): пункты меню правой кнопки, «Убрать лишнее», вид меню как в Windows 10.
public partial class MainWindow
{
    private List<MenuItemInfo> _menuItems = new();
    private readonly List<(MenuItemInfo It, Border Row)> _menuRows = new();
    private readonly List<(FrameworkElement Head, TextBlock Count, TextBlock Jump, List<MenuItemInfo> Items)> _menuGroups = new();
    /// <summary>Изменили пункт-обработчик — Проводник покажет это только после перезапуска.</summary>
    private bool _menuNeedsRestart, _menuClassicGuard;
    private readonly HashSet<MenuItemInfo> _menuSel = new();
    private readonly List<(MenuItemInfo It, CheckBox Cb)> _menuChecks = new();

    private void InitMenu()
    {
        BtnMenuApply.Click += (_, _) => Fire(ApplyMenuAsync, "Контекстное меню");
        BtnMenuSelNone.Click += (_, _) => { foreach (var (it, cb) in _menuChecks.ToList()) cb.IsChecked = !it.Hidden; _menuSel.Clear(); UpdateMenuSummary(); };
        SetOwn(BtnMenuApply, false);
        BtnMenuJunk.Click += (_, _) => Fire(MenuJunkAsync, "Контекстное меню");
        BtnMenuReset.Click += (_, _) => Fire(async () =>
        {
            if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
            if (!Ask("Вернуть все скрытые пункты? Открытые папки закроются.", "Merno", MessageBoxImage.Question, "Вернуть все пункты")) return;
            int n = 0;
            if (!await Busy("Возвращаю пункты меню…", () => { n = ShellMenu.RestoreAll(); ShellMenu.RestartExplorer(); })) return;
            _menuNeedsRestart = false;
            await BuildMenuAsync();
            SetStatus(n == 0 ? "Меню и так в исходном виде" : $"Готово. Возвращено пунктов: {n}");
        }, "Контекстное меню");
        BtnMenuExplorer.Click += (_, _) => Fire(async () =>
        {
            if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
            if (await Busy("Перезапускаю Проводник…", ShellMenu.RestartExplorer)) { _menuNeedsRestart = false; UpdateMenuSummary(); SetStatus("Готово"); }
        }, "Проводник");
        MenuSearch.TextChanged += (_, _) => FilterMenu();
        foreach (var rb in new[] { MenuFilterAll, MenuFilterShown, MenuFilterHidden }) rb.Checked += (_, _) => FilterMenu();
        NarrowToolbar(MenuFilter);
        // классическое меню Windows 10 — общая настройка, а не пункт меню; есть только в Windows 11
        MenuClassicCard.Visibility = Environment.OSVersion.Version.Build >= 22000 ? Visibility.Visible : Visibility.Collapsed;
        MenuClassic.Checked += (_, _) => SetClassic(true);
        MenuClassic.Unchecked += (_, _) => SetClassic(false);
    }

    private async void SetClassic(bool on)
    {
        if (_menuClassicGuard) return;
        void Revert() { _menuClassicGuard = true; MenuClassic.IsChecked = !on; _menuClassicGuard = false; }
        if (_busy) { Revert(); SetStatus("Подождите, Merno ещё занят"); return; }
        try
        {
            if (!await Busy(on ? "Включаю меню правой кнопки как в Windows 10…" : "Включаю меню Windows 11…", () => { ShellMenu.SetClassic(on); ShellMenu.RestartExplorer(); }))
            { Revert(); return; }
            _menuNeedsRestart = false;
            UpdateMenuSummary();
            SetStatus(on ? "Меню правой кнопки как в Windows 10 включено" : "Меню Windows 11 включено");
        }
        catch (Exception ex) { Log.Warn("Вид меню: " + ex.Message); }
    }

    private async Task BuildMenuAsync()
    {
        int gen = ++_genMenu;
        try
        {
        MenuList.Children.Clear(); _menuRows.Clear(); _menuGroups.Clear(); MenuJump.Children.Clear(); _menuSel.Clear(); _menuChecks.Clear();
        MenuList.Children.Add(new TextBlock { Text = "Читаю пункты меню…", Foreground = B("Sub"), Margin = new Thickness(4, 4, 0, 0) });
        MenuJunkCount.Text = "…";
        List<MenuItemInfo> items = new();
        bool classic = false, win11 = Environment.OSVersion.Version.Build >= 22000;
        await Task.Run(() => { items = ShellMenu.Scan(); if (win11) classic = ShellMenu.ClassicEnabled(); });
        if (gen != _genMenu) return;
        MenuList.Children.Clear();
        _menuClassicGuard = true; MenuClassic.IsChecked = classic; _menuClassicGuard = false;
        _menuItems = items;
        bool first = true;
        void Group(string title, string jumpTitle, IEnumerable<MenuItemInfo> group)
        {
            var g = group.OrderBy(i => i.Where).ThenBy(i => i.Name).ToList();
            if (g.Count == 0) return;
            var (head, cnt) = ListHeader(title, first); first = false;
            MenuList.Children.Add(head);
            var jump = SideJump(jumpTitle, head, out var jr);
            MenuJump.Children.Add(jump);
            head.Tag = jump;
            _menuGroups.Add((head, cnt, jr, g));
            foreach (var it in g)
            {
                var r = MenuRow(it);
                MenuList.Children.Add(r);
                _menuRows.Add((it, r));
            }
        }
        Group("Пункты Windows", "Пункты Windows", items.Where(i => i.System));
        Group("Пункты ваших программ", "Пункты программ", items.Where(i => !i.System));
        if (items.Count == 0) MenuList.Children.Add(new TextBlock { Text = "Пункты не найдены.", Style = (Style)FindResource("SubText"), Margin = new Thickness(4, 4, 0, 0) });
        UpdateMenuSummary();
        FilterMenu();
        }
        catch (Exception ex)
        {
            Log.Warn("Контекстное меню: " + ex.Message); Log.Skip(ex.ToString());
            if (gen == _genMenu) { _menuRows.Clear(); _menuGroups.Clear(); ShowError(MenuList, "Не получилось загрузить пункты меню.", BuildMenuAsync); }
        }
    }

    /// <summary>Сводка справа, счётчики групп, «Разделов» и фильтра.</summary>
    private void UpdateMenuSummary()
    {
        int junk = _menuItems.Count(i => i.Junk && !i.Hidden);
        if (junk > 0)
        {
            MenuJunkLabel.Visibility = Visibility.Visible;
            MenuJunkCount.Text = junk.ToString(); MenuJunkCount.FontSize = 28; MenuJunkCount.Foreground = B("Text");
            BtnMenuJunk.Style = (Style)FindResource("Outline");   // залитая на странице одна — «Применить»
            BtnMenuJunk.Content = $"Убрать лишнее ({junk})";
        }
        else
        {
            MenuJunkLabel.Visibility = Visibility.Collapsed;
            MenuJunkCount.Text = "Лишнего уже нет"; MenuJunkCount.FontSize = 18; MenuJunkCount.Foreground = B("Ok");
            BtnMenuJunk.Style = (Style)FindResource("Outline");
            BtnMenuJunk.Content = "Лишнего нет";
        }
        BtnMenuJunk.Padding = new Thickness(14, 9, 14, 9);
        SetOwn(BtnMenuJunk, junk > 0);
        var restart = _menuNeedsRestart ? Visibility.Visible : Visibility.Collapsed;
        MenuRestartNote.Visibility = restart; BtnMenuExplorer.Visibility = restart;
        foreach (var (_, cnt, jr, g) in _menuGroups)
        {
            int shown = g.Count(i => !i.Hidden), sel = g.Count(i => _menuSel.Contains(i));
            cnt.Inlines.Clear();
            cnt.Inlines.Add(new Run($"включено {shown} из {g.Count}"));
            if (sel > 0) cnt.Inlines.Add(new Run($"  ·  отмечено {sel}") { Foreground = B("Accent") });
            jr.Text = $"{shown}/{g.Count}";
            jr.Foreground = sel > 0 ? B("Accent") : B("Sub");
        }
        MenuFilterAll.Content = $"Все · {_menuItems.Count}";
        MenuFilterShown.Content = $"Включены · {_menuItems.Count(i => !i.Hidden)}";
        MenuFilterHidden.Content = $"Выключены · {_menuItems.Count(i => i.Hidden)}";
        // «Отмеченные»: кнопки появляются вместе с первой галочкой и только нужные
        // _menuSel — пункты, где галочку изменили, но ещё не применили
        int toShow = _menuSel.Count(i => i.Hidden), toHide = _menuSel.Count(i => !i.Hidden), n = _menuSel.Count;
        MenuSelCount.Text = n == 0 ? "Галочка означает, что пункт есть в меню. Измените нужное и нажмите «Применить»."
            : string.Join(" · ", new[] { toShow > 0 ? $"вернуть: {toShow}" : null, toHide > 0 ? $"убрать: {toHide}" : null }.Where(x => x != null));
        BtnMenuApply.Content = n > 0 ? $"Применить ({n})" : "Применить";
        SetOwn(BtnMenuApply, n > 0);
        BtnMenuSelNone.Visibility = _menuSel.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Поиск по названию, месту и пояснению + фильтр «Все / Включены / Выключены». Пустая группа прячется.</summary>
    private void FilterMenu()
    {
        MenuSearchHint.Visibility = MenuSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var q = MenuSearch.Text.Trim();
        int mode = MenuFilterShown.IsChecked == true ? 1 : MenuFilterHidden.IsChecked == true ? 2 : 0;
        bool Match(string s) => !string.IsNullOrEmpty(s) && s.Contains(q, StringComparison.CurrentCultureIgnoreCase);
        foreach (var (it, row) in _menuRows)
        {
            bool vis = q.Length == 0 || Match(it.Name) || Match(it.Where) || Match(it.Desc);
            if (mode == 1) vis &= !it.Hidden;
            if (mode == 2) vis &= it.Hidden;
            row.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var (head, _, _, g) in _menuGroups)
        {
            bool any = _menuRows.Any(x => g.Contains(x.It) && x.Row.Visibility == Visibility.Visible);
            head.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            if (head.Tag is FrameworkElement jump) jump.Visibility = head.Visibility;
        }
    }

    private async Task MenuJunkAsync()
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        var todo = _menuItems.Where(i => i.Junk && !i.Hidden).ToList();
        if (todo.Count == 0) { SetStatus("Лишнего уже нет"); return; }
        if (!Ask($"Скрыть бесполезные пункты меню ({todo.Count})?\n\n" + string.Join("\n", todo.Select(i => "• " + i.Name)) +
                 "\n\nОткрытые папки закроются. Любой пункт можно вернуть.",
                 "Merno", MessageBoxImage.Question, "Скрыть")) return;
        int fail = 0;
        if (!await Busy("Убираю лишние пункты меню…", () =>
        {
            foreach (var it in todo) { try { ShellMenu.SetHidden(it, true); } catch (Exception ex) { fail++; Log.Warn($"«{it.Name}»: {ex.Message}"); } }
            ShellMenu.RestartExplorer();
        })) return;
        _menuNeedsRestart = false;   // Проводник уже перезапущен
        await BuildMenuAsync();
        SetStatus(fail == 0 ? $"Скрыто пунктов: {todo.Count}" : $"Скрыто {todo.Count - fail} из {todo.Count}");
    }

    /// <summary>Строка пункта — как в «Настройках Windows»: галочка выбора, значок, «Название [Лишнее]», ниже «Где · пояснение».
    /// Состояние показывает только галочка.</summary>
    private Border MenuRow(MenuItemInfo it)
    {
        // значок — по тому, где появляется пункт
        string w = it.Where ?? "";
        string glyph = !it.System ? ""
            : w.Contains("Картин", StringComparison.OrdinalIgnoreCase) ? ""
            : w.Contains("Папк", StringComparison.OrdinalIgnoreCase) ? ""
            : w.Contains("Рабоч", StringComparison.OrdinalIgnoreCase) ? ""
            : "";
        // название ужимается «…» раньше метки «Лишнее»
        var title = new TextBlock { Text = it.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis };
        var line = new TitleLine(title, it.Junk && !it.Hidden ? TagChip("Лишнее", "Warn") : null);
        string desc = !string.IsNullOrEmpty(it.Desc) ? it.Desc : it.System ? null : "добавила программа";
        string sub = string.Join(" · ", new[] { it.Where, desc }.Where(x => !string.IsNullOrEmpty(x)));
        // галочка = пункт есть в меню; изменение — кнопкой «Применить»
        var cb = new CheckBox { IsChecked = !it.Hidden };
        void Changed() { if ((cb.IsChecked == true) == it.Hidden) _menuSel.Add(it); else _menuSel.Remove(it); UpdateMenuSummary(); }
        cb.Checked += (_, _) => Changed();
        cb.Unchecked += (_, _) => Changed();
        _menuChecks.Add((it, cb));
        return SelRow(QuickIcon(glyph), line, sub, cb);
    }

    /// <summary>«Применить»: вернуть отмеченные пункты, убрать снятые.</summary>
    private async Task ApplyMenuAsync()
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        var todo = _menuSel.ToList();
        if (todo.Count == 0) return;
        int fail = 0;
        if (!await QueueRun("Контекстное меню", "Применяю изменения меню…", todo, it => (it.Hidden ? "Вернуть: " : "Убрать: ") + it.Name, it =>
        {
            try { ShellMenu.SetHidden(it, !it.Hidden); return (true, null); }
            catch (Exception ex) { Interlocked.Increment(ref fail); Log.Warn($"Меню «{it.Name}»: {ex.Message}"); return (false, ForPerson(ex, "Не получилось изменить")); }
        })) return;
        // пункты-обработчики Проводник покажет только после перезапуска
        if (todo.Any(i => i.Clsid != null)) _menuNeedsRestart = true;
        _menuSel.Clear();
        bool restart = _menuNeedsRestart;
        await BuildMenuAsync();
        _menuNeedsRestart = restart; UpdateMenuSummary();
        SetStatus(fail > 0 ? $"Готово {todo.Count - fail} из {todo.Count}" : restart ? "Готово. Чтобы увидеть изменения, перезапустите Проводник" : "Готово");
    }
}
