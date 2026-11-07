// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Страница «Удалить программы».
public partial class MainWindow
{
    private List<InstalledApp> _unApps = new();
    private bool _unLoaded;

    private async Task BuildUninstallAsync()
    {
        int gen = ++_genUn;
        try
        {
        UnList.Children.Clear();
        UnList.Children.Add(new TextBlock { Text = "Собираю список установленного…", Foreground = B("Sub") });
        List<InstalledApp> apps = new();
        Uninstall.ClearSizeCache();   // размеры — заново при каждом открытии и «Обновить список»
        await Task.Run(() => apps = Uninstall.List() ?? new());
        if (gen != _genUn) return;
        _unApps = apps; _unLoaded = true;
        _searchInstalled = apps; _searchIndex = null;   // «Поиск» видит свежий список (удалили — пропало и из поиска)
        RenderUninstall();
        await MeasureUninstallAsync(gen);
        }
        catch (Exception ex)
        {
            Log.Warn("Удаление программ: " + ex.Message); Log.Skip(ex.ToString());
            if (gen == _genUn) ShowError(UnList, "Не получилось загрузить список программ.", BuildUninstallAsync);
        }
    }

    private readonly Dictionary<InstalledApp, TextBlock> _unSizeTb = new();

    /// <summary>Настоящий размер программ: считаем их папки в фоне и поправляем цифры, если в реестре меньше правды.
    /// Папку, которую делят несколько программ (общая папка производителя), не считаем — непонятно, чья она.</summary>
    private async Task MeasureUninstallAsync(int gen)
    {
        var apps = _unApps.Where(a => a.AppxName == null && a.Dir != null).ToList();
        var shared = apps.GroupBy(a => a.Dir, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // папка, внутри которой лежит папка другой программы из списка (Steam → steamapps\common\<игра>,
        // Ubisoft → games\<игра>), тоже не наша: иначе Steam «весит» все игры, которые и так стоят отдельными строками
        var dirs = apps.Select(a => a.Dir.TrimEnd('\\') + "\\").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var d in dirs)
            if (dirs.Any(o => o.Length > d.Length && o.StartsWith(d, StringComparison.OrdinalIgnoreCase))) shared.Add(d.TrimEnd('\\'));
        bool changed = false;
        foreach (var a in apps.Where(a => !shared.Contains(a.Dir)))
        {
            if (gen != _genUn) return;   // список уже перечитали
            long real = await Task.Run(() => Uninstall.DirBytes(a.Dir));
            if (gen != _genUn) return;
            if (real > a.Bytes * 1.1 && real > 1L << 20)
            {
                a.Bytes = real; a.Size = Uninstall.Size(real); changed = true;
                if (_unSizeTb.TryGetValue(a, out var tb)) tb.Text = a.Size;
            }
        }
        if (changed && _settings.SortUninstall == 1) RenderUninstall();   // сортировка по размеру — пересортировать
    }

    /// <summary>Рисует уже собранный список (сортировка и переключатель вида — без повторного чтения реестра).</summary>
    private void RenderUninstall()
    {
        if (!_unLoaded) return;
        bool builtin = UnViewBuiltin.IsChecked == true;
        UnSort.Visibility = builtin ? Visibility.Collapsed : Visibility.Visible;
        UnList.Children.Clear(); _unSizeTb.Clear(); _unChecks.Clear();
        UnSelCard.Visibility = builtin ? Visibility.Collapsed : Visibility.Visible;   // у встроенных своя панель «Удалить выбранные»
        if (builtin) { RenderBuiltin(); ApplyFilter(UnFilter, UnFilterHint, UnList); return; }
        _unSel.IntersectWith(_unApps.Where(x => !x.System).Select(UnKey));   // удалённое из выбора уходит
        // встроенные приложения из своего списка показываем только в своём разделе — без повторов
        var builtinPkgs = new HashSet<string>(Uninstall.Builtin.SelectMany(x => x.Pkgs), StringComparer.OrdinalIgnoreCase);
        var name = StringComparer.CurrentCultureIgnoreCase;
        foreach (var src in new[] { "Программа", "Приложение Windows" })
        {
            var all = _unApps.Where(x => x.Source == src && !(x.Pkg != null && builtinPkgs.Contains(x.Pkg))).ToList();
            if (all.Count == 0) continue;
            var grp = (_settings.SortUninstall switch
            {
                1 => all.OrderBy(x => x.System).ThenByDescending(x => x.Bytes).ThenBy(x => x.Name, name),
                2 => all.OrderBy(x => x.System).ThenByDescending(x => x.InstallDate ?? DateTime.MinValue).ThenBy(x => x.Name, name),
                _ => all.OrderBy(x => x.System).ThenBy(x => x.Name, name)
            }).ToList();
            UnList.Children.Add(new TextBlock { Text = src == "Программа" ? $"Программы · {grp.Count}" : $"Приложения Windows · {grp.Count}",
                                                Style = (Style)FindResource("H2"), FontSize = 18, Margin = new Thickness(0, 22, 0, 6) });
            foreach (var a in grp)
            {
                string aName = string.IsNullOrEmpty(a.Name) ? "Без названия" : a.Name;
                var row = new Border { Style = (Style)FindResource("Row"), Tag = aName + " " + (a.Publisher ?? "") };
                var dp = new DockPanel();
                var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(right, Dock.Right);
                CheckBox checkBox = null;
                // размер: сначала из реестра, потом уточняется подсчётом папки (MeasureUninstallAsync)
                var sizeTb = new TextBlock { Text = a.Size, Foreground = B("Sub"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
                right.Children.Add(sizeTb);
                _unSizeTb[a] = sizeTb;
                if (!a.System)
                {
                    var key = UnKey(a);
                    var cb = new CheckBox
                    {
                        Style = (Style)FindResource("Check"), IsChecked = _unSel.Contains(key),
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
                        ToolTip = "Отметить для удаления нескольких программ"
                    };
                    cb.Checked += (_, _) => { _unSel.Add(key); UpdateUnSelBar(); };
                    cb.Unchecked += (_, _) => { _unSel.Remove(key); UpdateUnSelBar(); };
                    _unChecks[key] = cb;
                    checkBox = cb;
                    row.MouseLeftButtonUp += (_, e) =>
                    {   // щелчок по пустому месту строки переключает галочку; кнопки и «…» остаются рабочими
                        if (e.OriginalSource is not DependencyObject src) return;
                        for (DependencyObject el = src; el != null && el != row; el = VisualTreeHelper.GetParent(el))
                            if (el is System.Windows.Controls.Primitives.ButtonBase || el is ExpandText) return;
                        cb.IsChecked = cb.IsChecked != true;
                    };
                    var del = new Button { Content = "Удалить", Style = (Style)FindResource("Ghost") };
                    var item = a;
                    del.Click += (_, _) => Fire(async () =>
                    {
                        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
                        // Edge, OneDrive и приложения Windows — своим способом, с тем же предупреждением, что в «Приложениях Windows»
                        if (BuiltinFor(item) is BuiltinApp bi) { await RemoveBuiltinAsync(new List<BuiltinApp> { bi }); return; }
                        if (!Ask($"Удалить «{aName}»?" + (item.AppxName == null ? "\n\nСледуйте шагам в открывшемся окне удаления." : "\n\nВернуть его можно в «Программы» → «Приложения Windows»."),
                                 "Merno", MessageBoxImage.Warning, "Удалить")) return;
                        if (!await ConfirmCloseProcessesAsync(Uninstall.RunningFor(item), aName)) return;
                        (bool Ok, string Error) r = (false, null);
                        if (!await Busy($"Удаляю {aName}…", () => r = Uninstall.Remove(item))) return;
                        if (!r.Ok) { Log.Warn($"Удаление «{aName}»: {r.Error}"); Warn("Не получилось удалить программу. Попробуйте ещё раз."); }
                        await BuildUninstallAsync();
                    }, "Удаление программы");
                    right.Children.Add(del);
                }
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                // длинное название ужимается «…», метка «Системное» остаётся видна и не вылезает за строку
                var nameTb = new TextBlock { Text = aName, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = a.System ? B("Sub") : B("Text"), TextTrimming = TextTrimming.CharacterEllipsis };
                sp.Children.Add(new TitleLine(nameTb, a.System ? Chip("Системное", "Warn") : null));
                var sub = string.Join(" · ", new[] { a.Publisher, !string.IsNullOrEmpty(a.Version) && a.Version.Length <= 8 ? "версия " + a.Version : null, a.InstallDate is DateTime d ? "установлено " + d.ToString("dd.MM.yyyy") : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (sub.Length > 0) sp.Children.Add(new ExpandText(sub) { Margin = new Thickness(0, 3, 0, 0) });
                var ico = FileIconBox(aName, a.Icon);
                DockPanel.SetDock(ico, Dock.Left);
                dp.Children.Add(right);
                if (checkBox != null) { DockPanel.SetDock(checkBox, Dock.Left); dp.Children.Add(checkBox); }
                dp.Children.Add(ico); dp.Children.Add(sp);
                row.Child = dp;
                UnList.Children.Add(row);
            }
        }
        UpdateUnSelBar();
        ApplyFilter(UnFilter, UnFilterHint, UnList);   // повторно применить фильтр к новому списку
    }

}
