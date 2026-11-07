// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Сортировка списков и удаление встроенных приложений Windows («Программы» → «Удалить»)
public partial class MainWindow
{
    private static readonly string[] UnSortOpts = { "по имени", "по размеру", "по дате установки" };
    private static readonly string[] StartSortOpts = { "по имени", "сначала включённые", "сначала выключенные" };

    private void InitSorting()
    {
        InitSort(UnSort, UnSortOpts, () => _settings.SortUninstall, i => _settings.SortUninstall = i, RenderUninstall);
        InitSort(StartSort, StartSortOpts, () => _settings.SortStartup, i => _settings.SortStartup = i, RenderStartup);
        BuildUnSelBar();
        UnViewAll.Checked += (_, _) => RenderUninstall();
        UnViewBuiltin.Checked += (_, _) => RenderUninstall();
    }

    /// <summary>Кнопка «Сортировка: …» с тёмным выпадающим меню. Выбор запоминается в настройках Merno.</summary>
    private void InitSort(Button btn, string[] opts, Func<int> get, Action<int> set, Action resort)
    {
        int Cur() => Math.Clamp(get(), 0, opts.Length - 1);
        void Label()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 12, Foreground = B("Sub"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
            sp.Children.Add(new TextBlock { Text = "Сортировка: " + opts[Cur()], VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 9, Foreground = B("Sub"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 2, 0, 0) });
            btn.Content = sp;
        }
        Label();
        btn.Click += (_, _) =>
        {
            var cm = new ContextMenu { PlacementTarget = btn, Placement = PlacementMode.Bottom };
            for (int i = 0; i < opts.Length; i++)
            {
                int k = i;
                var mi = new MenuItem { Header = opts[k] };
                if (k == Cur()) { mi.FontWeight = FontWeights.SemiBold; mi.Foreground = B("Accent"); }
                mi.Click += (_, _) =>
                {
                    if (k == Cur()) return;
                    set(k); _settings.Save(); Label();
                    try { resort(); } catch (Exception ex) { Log.Warn("Сортировка: " + ex.Message); Log.Skip(ex.ToString()); }
                };
                cm.Items.Add(mi);
            }
            cm.IsOpen = true;
        };
    }

    // ---------- встроенные приложения Windows ----------
    private readonly Dictionary<BuiltinApp, CheckBox> _builtinChecks = new();

    private Tweak TweakById(string id) => _tweaks.FirstOrDefault(t => t.Id == id);

    private bool BuiltinInstalled(BuiltinApp b)
    {
        if (b.TweakId == null) return Uninstall.PackagesOf(b, _unApps).Count > 0;
        // Edge и OneDrive — только в расширенном режиме; «применено» значит «уже удалено»
        if (!_settings.Advanced) return false;
        var t = TweakById(b.TweakId);
        try { return t != null && !t.IsApplied(_ctx); } catch { return false; }
    }

    private void RenderBuiltin()
    {
        _builtinChecks.Clear();
        var list = Uninstall.Builtin.Where(BuiltinInstalled).ToList();
        UnList.Children.Add(new TextBlock { Text = Typo.Nbsp("Лишние приложения, которые пришли с Windows. Их можно удалить, а при желании вернуть из Microsoft Store."),
                                            Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 6, 0, 12) });
        if (list.Count == 0)
        {
            UnList.Children.Add(new TextBlock { Text = "Лишних приложений нет.", Foreground = B("Sub") });
            return;
        }
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var delSel = new Button { Content = "Удалить выбранные", Style = (Style)FindResource("Primary"), IsEnabled = false };
        DockPanel.SetDock(delSel, Dock.Right);
        var count = new TextBlock { Text = $"Найдено: {list.Count}", Foreground = B("Sub"), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(delSel); bar.Children.Add(count);
        UnList.Children.Add(bar);
        void Upd()
        {
            int n = _builtinChecks.Values.Count(c => c.IsChecked == true);
            delSel.IsEnabled = n > 0;
            delSel.Content = n > 0 ? $"Удалить выбранные ({n})" : "Удалить выбранные";
        }
        delSel.Click += (_, _) => Fire(() => RemoveBuiltinAsync(_builtinChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList()), "Встроенные приложения");

        foreach (var b in list.OrderBy(x => x.TweakId != null).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var app = b;
            var row = new Border { Style = (Style)FindResource("Row"), Tag = b.Name + " " + string.Join(" ", b.Pkgs) };
            var dp = new DockPanel();
            var del = new Button { Content = "Удалить", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            del.Click += (_, _) => Fire(() => RemoveBuiltinAsync(new List<BuiltinApp> { app }), "Встроенные приложения");
            DockPanel.SetDock(del, Dock.Right);
            var cb = new CheckBox { Style = (Style)FindResource("Check"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            cb.Checked += (_, _) => Upd(); cb.Unchecked += (_, _) => Upd();
            DockPanel.SetDock(cb, Dock.Left);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            // название ужимается «…» раньше метки — метка не вылезает за строку
            var name = new TextBlock { Text = b.Name, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis };
            sp.Children.Add(new TitleLine(name, b.Danger != null ? Chip("Для опытных", "Bad") : null));
            sp.Children.Add(new ExpandText(Typo.Nbsp(b.Desc)) { Margin = new Thickness(0, 3, 0, 0) });
            if (b.Danger != null)
                sp.Children.Add(new TextBlock { Text = Typo.Nbsp(b.Danger), Foreground = B("Bad"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            dp.Children.Add(del); dp.Children.Add(cb); dp.Children.Add(sp);
            row.Child = dp;
            UnList.Children.Add(row);
            _builtinChecks[b] = cb;
        }
    }

    private async Task RemoveBuiltinAsync(List<BuiltinApp> items)
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        if (items == null || items.Count == 0) return;
        var danger = items.Where(b => b.Danger != null).ToList();
        string text = (items.Count == 1 ? $"Удалить «{items[0].Name}»?" : "Удалить: " + string.Join(", ", items.Select(b => "«" + b.Name + "»")) + "?") +
                      "\n\nУдаление нельзя отменить в «Откате изменений». Некоторые приложения могут быть недоступны в Microsoft Store.";
        bool ok = danger.Count > 0
            ? MernoDialog(text, "Удаление приложений", "Удалить", "Отмена", true, B("Bad"), string.Join("\n\n", danger.Select(b => $"{b.Name}: {b.Danger}")))
            : Ask(text, "Удаление приложений", MessageBoxImage.Warning, "Удалить");
        if (!ok) return;
        var apps = _unApps.ToList();
        var failed = new List<string>();
        int i = 0;
        QueueStart("Удаление приложений", items.Select(b => b.Name));
        foreach (var b in items)
        {
            i++;
            if (_qStop) break;
            (bool Ok, string Error) r = (false, null);
            var st = items.Count > 1 ? $"Удаляю {b.Name} ({i} из {items.Count})…" : $"Удаляю {b.Name}…";
            QueueSet(i - 1, QState.Now);
            if (!await Busy(st, () => r = b.TweakId != null ? RemoveViaTweak(b.TweakId) : Uninstall.RemoveBuiltin(b, apps))) { QueueFinish(); return; }
            QueueSet(i - 1, r.Ok ? QState.Done : QState.Fail, r.Error);
            if (!r.Ok) { failed.Add(b.Name); Log.Warn($"Удаление «{b.Name}»: {r.Error}"); }
        }
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        QueueFinish();
        await BuildUninstallAsync();
        if (failed.Count > 0) Warn("Не получилось удалить: " + string.Join(", ", failed) + ". Попробуйте после перезагрузки.");
        else SetStatus(items.Count == 1 ? $"«{items[0].Name}» удалено" : $"Удалено приложений: {items.Count}");
    }

    /// <summary>Строка из «Удалить», которую надо удалять как встроенное приложение: Edge (свой удалятель Microsoft блокирует),
    /// OneDrive и приложения Windows из списка Builtin. null — обычная программа.</summary>
    private static BuiltinApp BuiltinFor(InstalledApp a)
    {
        if (a == null) return null;
        if (a.AppxName == null)
        {
            string n = (a.Name ?? "").Trim();
            if (n.Equals("Microsoft Edge", StringComparison.OrdinalIgnoreCase)) return Uninstall.Builtin.FirstOrDefault(b => b.TweakId == "edge_remove");
            if (n.StartsWith("Microsoft OneDrive", StringComparison.OrdinalIgnoreCase)) return Uninstall.Builtin.FirstOrDefault(b => b.TweakId == "onedrive");
            return null;
        }
        return Uninstall.Builtin.FirstOrDefault(b => b.Pkgs.Any(p => string.Equals(p, a.Pkg, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Edge и OneDrive удаляются готовыми настройками из Tweaks.cs — без копии их логики.</summary>
    private (bool Ok, string Error) RemoveViaTweak(string id)
    {
        var t = TweakById(id);
        if (t == null) return (false, "Не получилось удалить. Обновите Merno.");
        t.Apply(_ctx);
        return t.IsApplied(_ctx) ? (true, null) : (false, "Не получилось удалить. Попробуйте после перезагрузки."
);
    }
}
