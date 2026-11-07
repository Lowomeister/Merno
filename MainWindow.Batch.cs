// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Выбор нескольких программ, компонентов и драйверов: отметить галочками и установить или удалить разом.
public partial class MainWindow
{
    private List<AppItem> _appsAll = new();
    private HashSet<string> _appsInstalled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _appChecks = new(StringComparer.OrdinalIgnoreCase);

    private List<AppItem> _runtimeAll = new();
    private HashSet<string> _runtimeInstalled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _runtimeChecks = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _appSel = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _runtimeSel = new(StringComparer.OrdinalIgnoreCase);

    private TextBlock _appsSelCount, _runtimeSelCount;
    private Button _appsSelInstall, _appsSelRemove, _runtimeSelInstall, _runtimeSelRemove;

    /// <summary>Карточка «Отмеченные» в боковой панели: подсказка, счётчик, «Установить» / «Удалить», «Отметить все» / «Снять».</summary>
    private (TextBlock Count, Button Install, Button Remove) BuildSelCard(Panel host, string hint, Func<Task> install, Func<Task> remove,
                                                                           Dictionary<string, CheckBox> checks, HashSet<string> sel, string what)
    {
        host.Children.Clear();
        checks.Clear();
        host.Children.Add(new TextBlock { Text = "Отмеченные", FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = B("Text") });
        var count = new TextBlock { Style = (Style)FindResource("SubText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12), Text = Typo.Nbsp(hint) };
        host.Children.Add(count);
        var inst = new Button { Content = "Установить отмеченные", Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
        inst.Click += (_, _) => Fire(install, "Установка " + what);
        var rm = new Button { Content = "Удалить отмеченные", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
        rm.Click += (_, _) => Fire(remove, "Удаление " + what);
        host.Children.Add(inst); host.Children.Add(rm);
        var two = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        var all = new Button { Content = "Отметить все", Margin = new Thickness(0, 0, 4, 0) };
        all.Click += (_, _) => SetAllSelected(checks, sel, true);
        var none = new Button { Content = "Снять все", Margin = new Thickness(4, 0, 0, 0) };
        none.Click += (_, _) => SetAllSelected(checks, sel, false);
        two.Children.Add(all); two.Children.Add(none);
        host.Children.Add(two);
        inst.Tag = two;   // строку «Отметить все / Снять» показываем вместе с кнопками
        return (count, inst, rm);
    }

    private string _appsSelHint = "Чтобы установить или удалить несколько программ разом, отметьте их галочками.";
    private string _runtimeSelHint = "Чтобы установить или удалить несколько компонентов разом, отметьте их галочками.";

    private void BuildAppsSelBar()
    {
        (_appsSelCount, _appsSelInstall, _appsSelRemove) = BuildSelCard(AppsSelBar, _appsSelHint, () => BatchAppsAsync(true), () => BatchAppsAsync(false), _appChecks, _appSel, "программ");
        UpdateSelBars();
    }

    // «Программы» → «Удалить»: галочки у программ, которые можно удалить; выбор хранится по ключу и переживает перерисовку
    private readonly Dictionary<string, CheckBox> _unChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unSel = new(StringComparer.OrdinalIgnoreCase);
    private TextBlock _unSelCount;
    private Button _unSelInstall, _unSelRemove;
    private const string UnSelHint = "Чтобы удалить несколько программ разом, отметьте их галочками.";

    private static string UnKey(InstalledApp a) => (a.Name ?? "") + "|" + (a.AppxName ?? a.UninstallCmd ?? "");

    private void BuildUnSelBar()
    {
        (_unSelCount, _unSelInstall, _unSelRemove) = BuildSelCard(UnSelBar, UnSelHint, () => Task.CompletedTask, BatchUninstallAsync, _unChecks, _unSel, "программ");
        UpdateUnSelBar();
    }

    private void UpdateUnSelBar()
    {
        if (_unSelCount == null) return;
        _unSelCount.Text = _unSel.Count == 0 ? UnSelHint : $"Отмечено: {_unSel.Count}";
        if (_unSelRemove != null) _unSelRemove.Content = _unSel.Count > 0 ? $"Удалить отмеченные ({_unSel.Count})" : "Удалить отмеченные";
        ShowSelButtons(_unSelInstall, _unSelRemove, _unSel.Count > 0, false, _unSel.Count > 0);
    }

    private async Task BatchUninstallAsync()
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        var todo = _unApps.Where(a => !a.System && _unSel.Contains(UnKey(a))).ToList();
        // Edge, OneDrive и приложения Windows из списка — своим способом и с предупреждением; остальное — как обычно
        var builtin = todo.Select(BuiltinFor).Where(b => b != null).Distinct().ToList();
        if (builtin.Count > 0)
        {
            todo = todo.Where(a => BuiltinFor(a) == null).ToList();
            await RemoveBuiltinAsync(builtin);
            if (todo.Count == 0) { _unSel.Clear(); return; }
        }
        if (todo.Count == 0) { Info("Среди отмеченных нет программ, которые можно удалить."); return; }
        bool anyWin = todo.Any(a => a.AppxName != null), anyExe = todo.Any(a => a.AppxName == null);
        string note = "\n\n" + (anyExe ? "У обычных программ по очереди откроются окна удаления. Следуйте их шагам." : "")
                    + (anyExe && anyWin ? " " : "") + (anyWin ? "Приложения Windows можно вернуть из Microsoft Store." : "");
        if (!Ask($"Удалить {todo.Count} {Plural(todo.Count, "программу", "программы", "программ")}?" + note, "Merno", MessageBoxImage.Warning, "Удалить")) return;
        if (!await ConfirmCloseProcessesAsync(todo.SelectMany(Uninstall.RunningFor), "отмеченные программы")) return;

        var failed = new List<string>();
        int i = 0;
        QueueStart("Удаление", todo.Select(a => string.IsNullOrEmpty(a.Name) ? "Без названия" : a.Name));
        foreach (var a in todo)
        {
            i++;
            if (_qStop) break;
            string n = string.IsNullOrEmpty(a.Name) ? "Без названия" : a.Name;
            (bool Ok, string Error) r = (false, null);
            QueueSet(i - 1, QState.Now);
            if (!await Busy($"Удаляю {n} ({i} из {todo.Count})…", () => r = Uninstall.Remove(a))) { QueueFinish(); return; }
            QueueSet(i - 1, r.Ok ? QState.Done : QState.Fail, r.Error);
            if (!r.Ok) { failed.Add(n); Log.Warn($"Удаление «{n}»: {r.Error}"); }
        }
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        QueueFinish();
        _unSel.Clear();
        await BuildUninstallAsync();
        if (failed.Count > 0) Warn("Не получилось удалить: " + string.Join(", ", failed) + ".");
        else SetStatus($"Удалено программ: {todo.Count}");
    }

    private void BuildRuntimeSelBar()
    {
        (_runtimeSelCount, _runtimeSelInstall, _runtimeSelRemove) = BuildSelCard(RuntimeSelBar, _runtimeSelHint, () => BatchRuntimeAsync(true), () => BatchRuntimeAsync(false), _runtimeChecks, _runtimeSel, "компонентов");
        UpdateSelBars();
    }
    private static void SetAllSelected(Dictionary<string, CheckBox> checks, HashSet<string> sel, bool on)
    {
        foreach (var (name, cb) in checks)
        {
            // «Отметить все» — только то, что видно (поиск мог скрыть строки); «Снять все» — всё
            if (on && !cb.IsVisible) continue;
            if (on) sel.Add(name); else sel.Remove(name);
            cb.IsChecked = on;
        }
    }

    /// <summary>Merno умеет ставить программу сама: через winget или особым установщиком (Spotify).</summary>
    private static bool AutoInstallable(AppItem a) => a != null && (!string.IsNullOrWhiteSpace(a.winget) || a.loader == "spotify");

    /// <summary>Обновляет счётчик отмеченного и доступность кнопок.</summary>
    private void UpdateSelBars()
    {
        if (_appsSelCount != null)
        {
            _appsSelCount.Text = _appSel.Count == 0 ? _appsSelHint : $"Отмечено: {_appSel.Count}";
            bool anyMissing = _appsAll.Any(a => _appSel.Contains(a.name) && !_appsInstalled.Contains(a.name) && AutoInstallable(a));
            bool anyInstalled = _appsAll.Any(a => _appSel.Contains(a.name) && _appsInstalled.Contains(a.name));
            ShowSelButtons(_appsSelInstall, _appsSelRemove, _appSel.Count > 0, anyMissing, anyInstalled);
        }
        if (_runtimeSelCount != null)
        {
            _runtimeSelCount.Text = _runtimeSel.Count == 0 ? _runtimeSelHint : $"Отмечено: {_runtimeSel.Count}";
            bool anyMissing = _runtimeAll.Any(a => _runtimeSel.Contains(a.name) && !_runtimeInstalled.Contains(a.name) && AutoInstallable(a));
            bool anyInstalled = _runtimeAll.Any(a => _runtimeSel.Contains(a.name) && _runtimeInstalled.Contains(a.name));
            ShowSelButtons(_runtimeSelInstall, _runtimeSelRemove, _runtimeSel.Count > 0, anyMissing, anyInstalled);
        }
    }

    private void OnSelChanged(string name, HashSet<string> set, bool on)
    {
        if (on) set.Add(name); else set.Remove(name);
        UpdateSelBars();
        if (set == _storeGrp.Sel) UpdateGroupBar(_storeGrp);
        if (set == _drvGrp.Sel) UpdateGroupBar(_drvGrp);
    }

    private async Task BatchAppsAsync(bool install)
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        var picked = _appsAll.Where(a => _appSel.Contains(a.name)).ToList();
        var todo = install ? picked.Where(a => !_appsInstalled.Contains(a.name) && AutoInstallable(a)).ToList()
                           : picked.Where(a => _appsInstalled.Contains(a.name)).ToList();
        if (todo.Count == 0)
        {
            Info(install ? "Среди отмеченных нет программ, которые Merno ставит сама. Нажмите «Сайт», чтобы скачать вручную."
                         : "Среди отмеченных нет установленных программ.");
            return;
        }
        string what = install ? $"Установить отмеченные программы ({todo.Count})?" : $"Удалить отмеченные программы ({todo.Count})?";
        string note = install
            ? "\n\nУстановка займёт несколько минут. Установятся только отмеченные программы."
            : "\n\nУдаление нельзя отменить: программы придётся ставить заново.";
        if (!Ask(what + note, "Merno", install ? MessageBoxImage.Question : MessageBoxImage.Warning, install ? "Установить" : "Удалить")) return;

        if (!install && !await ConfirmCloseProcessesAsync(todo.SelectMany(Uninstall.RunningFor), "отмеченные программы")) return;

        var failed = new List<string>();
        QueueStart(install ? "Установка" : "Удаление", todo.Select(a => a.name), install ? RetryInstallAsync : null, card: true);
        bool ran = await Busy(install ? $"Устанавливаю отмеченные программы ({todo.Count})…" : $"Удаляю отмеченные программы ({todo.Count})…", () =>
        {
            for (int i = 0; i < todo.Count; i++)
            {
                if (_qStop) break;   // «Остановить после текущей»
                var a = todo[i];
                QueueSet(i, QState.Now);
                try
                {
                    var r = Apps.InstallOrUninstall(a, install);
                    if (!r.Ok) failed.Add(a.name);
                    QueueSet(i, r.Ok ? QState.Done : QState.Fail, r.Ok ? null : r.Message);
                }
                catch (Exception ex) { failed.Add(a.name); Log.Warn($"{a.name}: {ex.Message}"); QueueSet(i, QState.Fail, ForPerson(ex)); }
            }
        });
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);   // дождаться последних QueueSet
        QueueFinish();
        if (!ran) return;

        _appSel.Clear();
        _appsBuilt = false;
        await BuildAppsAsync();
        SetStatus(failed.Count == 0 ? (install ? "Отмеченные программы установлены" : "Отмеченные программы удалены")
                                    : "Не получилось: " + string.Join(", ", failed));
    }

    /// <summary>«Повторить для неудавшихся» из карточки «Установка»: отмечаем их и ставим заново тем же путём.</summary>
    private Task RetryInstallAsync(List<string> names)
    {
        _appSel.Clear();
        foreach (var n in names) _appSel.Add(n);
        return BatchAppsAsync(true);
    }

    private async Task BatchRuntimeAsync(bool install)
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        var picked = _runtimeAll.Where(a => _runtimeSel.Contains(a.name)).ToList();
        var todo = install ? picked.Where(a => !_runtimeInstalled.Contains(a.name) && AutoInstallable(a)).ToList()
                           : picked.Where(a => _runtimeInstalled.Contains(a.name)).ToList();
        if (todo.Count == 0)
        {
            Info(install ? "Все отмеченные компоненты уже установлены." : "Среди отмеченных нет установленных компонентов.");
            return;
        }
        string what = install ? $"Установить отмеченные компоненты ({todo.Count})?" : $"Удалить отмеченные компоненты ({todo.Count})?";
        string note = install ? "\n\nЭто займёт несколько минут."
            : "\n\nБез этих компонентов игры и программы могут перестать запускаться.";
        if (!Ask(what + note, "Merno", install ? MessageBoxImage.Question : MessageBoxImage.Warning, install ? "Установить" : "Удалить")) return;

        if (!install && !await ConfirmCloseProcessesAsync(todo.SelectMany(Uninstall.RunningFor), "отмеченные компоненты")) return;

        var failed = new List<string>();
        if (!await QueueRun(install ? "Установка компонентов" : "Удаление компонентов",
                            install ? $"Устанавливаю отмеченные компоненты ({todo.Count})…" : $"Удаляю отмеченные компоненты ({todo.Count})…",
                            todo, a => a.name, a =>
                            {
                                var r = Apps.InstallOrUninstall(a, install);
                                if (!r.Ok) lock (failed) failed.Add(a.name);
                                return (r.Ok, r.Message);
                            })) return;

        _runtimeSel.Clear();
        _runtimeBuilt = false;
        await BuildRuntimeAsync();
        SetStatus(failed.Count == 0 ? (install ? "Отмеченные компоненты установлены" : "Отмеченные компоненты удалены")
                                    : "Не получилось: " + string.Join(", ", failed));
    }

    // ---------- общий выбор для других списков программ ----------
    // «Приложения Windows» (вернуть/удалить несколько) и «Драйверы» → «Программы производителей» (поставить несколько разом)

    private sealed class AppSelGroup
    {
        public List<AppItem> All = new();
        public HashSet<string> Installed = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, CheckBox> Checks = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Sel = new(StringComparer.OrdinalIgnoreCase);
        public TextBlock Count;
        public Button Inst, Rm;
        public string Hint, What, InstallLabel = "Установить отмеченные";
        public Func<Task> Rebuild;
    }

    private readonly AppSelGroup _storeGrp = new() { Hint = "Чтобы вернуть или удалить несколько приложений разом, отметьте их галочками.", What = "приложений", InstallLabel = "Вернуть отмеченные" };
    private readonly AppSelGroup _drvGrp = new() { Hint = "Чтобы установить несколько программ разом, отметьте их галочками.", What = "программ" };

    /// <summary>Карточка «Отмеченные» для группы; host — пустая панель в боковой колонке.</summary>
    private void BuildGroupCard(Panel host, AppSelGroup g)
    {
        (g.Count, g.Inst, g.Rm) = BuildSelCard(host, g.Hint, () => BatchGroupAsync(g, true), () => BatchGroupAsync(g, false), g.Checks, g.Sel, g.What);
        g.Inst.Content = g.InstallLabel;
        UpdateGroupBar(g);
    }

    private void UpdateGroupBar(AppSelGroup g)
    {
        if (g.Count == null) return;
        g.Count.Text = g.Sel.Count == 0 ? g.Hint : $"Отмечено: {g.Sel.Count}";
        bool anyMissing = g.All.Any(a => g.Sel.Contains(a.name) && !g.Installed.Contains(a.name) && AutoInstallable(a));
        bool anyInstalled = g.All.Any(a => g.Sel.Contains(a.name) && g.Installed.Contains(a.name));
        ShowSelButtons(g.Inst, g.Rm, g.Sel.Count > 0, anyMissing, anyInstalled);
    }

    private async Task BatchGroupAsync(AppSelGroup g, bool install)
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        var picked = g.All.Where(a => g.Sel.Contains(a.name)).ToList();
        var todo = install ? picked.Where(a => !g.Installed.Contains(a.name) && AutoInstallable(a)).ToList()
                           : picked.Where(a => g.Installed.Contains(a.name)).ToList();
        if (todo.Count == 0)
        {
            Info(install ? "Среди отмеченных нет того, что Merno ставит сама. Нажмите «Сайт» или «В Microsoft Store»." : "Среди отмеченных нет установленного.");
            return;
        }
        if (!Ask((install ? $"{g.InstallLabel.Replace(" отмеченные", "")} отмеченные ({todo.Count})?\n\nЭто займёт несколько минут."
                          : $"Удалить отмеченные ({todo.Count})?\n\nУдаление нельзя отменить."),
                 "Merno", install ? MessageBoxImage.Question : MessageBoxImage.Warning, install ? "Начать" : "Удалить")) return;
        if (!install && !await ConfirmCloseProcessesAsync(todo.SelectMany(Uninstall.RunningFor), "отмеченные программы")) return;
        var failed = new List<string>();
        if (!await Busy(install ? $"Ставлю отмеченные ({todo.Count})…" : $"Удаляю отмеченные ({todo.Count})…", () =>
        {
            foreach (var a in todo)
            {
                try { if (!Apps.InstallOrUninstall(a, install).Ok) failed.Add(a.name); }
                catch (Exception ex) { failed.Add(a.name); Log.Warn($"{a.name}: {ex.Message}"); }
            }
        })) return;
        g.Sel.Clear();
        if (g.Rebuild != null) await g.Rebuild();
        SetStatus(failed.Count == 0 ? "Готово" : "Не получилось: " + string.Join(", ", failed));
    }

    /// <summary>Перед удалением предлагает закрыть запущенную программу — иначе удаление может пройти впустую.</summary>
    private async Task<bool> ConfirmCloseProcessesAsync(IEnumerable<Process> processes, string what)
    {
        var list = (processes ?? Enumerable.Empty<Process>()).ToList();
        if (list.Count == 0) return true;
        var names = string.Join(", ", list.Select(p => { try { return p.ProcessName; } catch { return "программа"; } })
                                          .Distinct(StringComparer.CurrentCultureIgnoreCase));
        if (!MernoDialog($"Сейчас запущено: {names}.\n\nЕсли не закрыть, {what} могут не удалиться. Закрыть и продолжить?",
                "Программа запущена", "Закрыть и продолжить", "Отмена", true, B("Warn"))) return false;
        // ожидание закрытия (до 10 с на программу) не на потоке окна, иначе Merno подвисает
        bool closed = false;
        if (!await Busy("Закрываю программы…", () => { closed = Uninstall.CloseAll(list); Thread.Sleep(700); })) return false;
        if (!closed)
        {
            Warn("Программа не закрылась. Если она спрашивает о сохранении, ответьте ей и повторите удаление.");
            return false;
        }
        return true;
    }

    private Task<bool> ConfirmCloseRunningAsync(AppItem a) => ConfirmCloseProcessesAsync(Uninstall.RunningFor(a), a.name);

    /// <summary>Пока ничего не отмечено — в карточке только подсказка; кнопки появляются вместе с первой галочкой и только нужные.</summary>
    private static void ShowSelButtons(Button inst, Button rm, bool any, bool canInstall, bool canRemove)
    {
        if (inst == null || rm == null) return;
        inst.Visibility = any && canInstall ? Visibility.Visible : Visibility.Collapsed;
        rm.Visibility = any && canRemove ? Visibility.Visible : Visibility.Collapsed;
        if (inst.Tag is UIElement two) two.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }
}