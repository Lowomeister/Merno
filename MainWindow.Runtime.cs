// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Компоненты: Visual C++, .NET, DirectX, Java — установить, удалить, «Установить всё нужное»
public partial class MainWindow
{
    private bool _runtimeBuilt;

    private readonly List<(AppItem App, Action MarkDone)> _runtimeRows = new();

    private async Task BuildRuntimeAsync()
    {
        int gen = ++_genRuntime;
        try
        {
        RuntimeList.Children.Clear(); _runtimeRows.Clear();
        SetOwn(BtnRuntimeAll, false);   // до конца проверки список пуст — иначе «всё установлено» по ошибке
        RuntimeList.Children.Add(new TextBlock { Text = "Проверяю, что уже установлено…", Foreground = B("Sub") });
        var apps = Apps.Load().Where(a => a.tab == "runtime" && Apps.Fits(a, _ctx.S)).ToList();
        HashSet<string> installed = new(); bool wg = false;
        await Task.Run(() => { wg = Apps.WingetAvailable(); installed = Apps.Installed(apps); });
        if (gen != _genRuntime) return;
        _wingetOk = wg;
        _runtimeAll = apps;
        _runtimeInstalled = installed;
        _runtimeSel.IntersectWith(apps.Select(a => a.name));
        BuildRuntimeSelBar();
        RuntimeList.Children.Clear(); _runtimeRows.Clear();
        SetOwn(BtnRuntimeAll, _wingetOk);
        if (!_wingetOk)
            RuntimeList.Children.Add(WingetBanner());   // нет или старый winget — Merno поставит его сам (MainWindow.Apps.cs)
        foreach (var grp in apps.GroupBy(a => a.category))
        {
            RuntimeList.Children.Add(GroupHeader(grp.Key, grp.Count(), grp.Count(a => installed.Contains(a.name))));
            foreach (var a in grp) RuntimeList.Children.Add(AppRow(a, installed.Contains(a.name), "Сайт", _runtimeRows, removable: true,
                selectableSet: _runtimeSel, register: cb => _runtimeChecks[a.name] = cb));
        }
        UpdateSelBars();
        int need = _runtimeRows.Count(r => r.App.rec && r.MarkDone != null);
        UpdateRuntimeAll(need);
        _runtimeBuilt = true;
        }
        catch (Exception ex)
        {
            Log.Warn("Компоненты: " + ex.Message); Log.Skip(ex.ToString());
            if (gen == _genRuntime) ShowError(RuntimeList, "Не получилось проверить компоненты.", BuildRuntimeAsync);
        }
    }

    /// <summary>Удаление установленного компонента (после предупреждения) и обновление его строки.</summary>
    private async Task RemoveRuntimeAsync(AppItem a, Border row)
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        if (!MernoDialog("Без этого компонента игры и программы могут перестать запускаться. Удалить?", $"Удалить {a.name}?", "Удалить", "Отмена", true, B("Warn"))) return;
        if (!await ConfirmCloseRunningAsync(a)) return;
        (bool Ok, string Message) r = (false, null);
        bool still = true;
        if (!await Busy($"Удаляю {a.name}…", () => { r = Apps.Uninstall(a); still = Apps.Installed(new[] { a }, onDisk: false).Contains(a.name); })) return;
        if (!r.Ok) Warn(string.IsNullOrEmpty(r.Message) ? "Не получилось удалить компонент. Попробуйте ещё раз." : r.Message);
        // обновляем только эту строку
        if (!still)
        {
            _runtimeInstalled.Remove(a.name);
            UpdateSelBars();
        }
        int idx = RuntimeList.Children.IndexOf(row);
        if (idx < 0) return;
        _runtimeRows.RemoveAll(x => x.App == a);
        RuntimeList.Children.RemoveAt(idx);
        RuntimeList.Children.Insert(idx, AppRow(a, still, "Сайт", _runtimeRows, removable: true,
            selectableSet: _runtimeSel, register: cb => _runtimeChecks[a.name] = cb));
        int need = _runtimeRows.Count(x => x.App.rec && x.MarkDone != null);
        UpdateRuntimeAll(need);
        if (r.Ok) SetStatus(still ? $"{a.name} не удалён" : $"{a.name} удалён");
    }

    private async Task InstallRuntimeAllAsync()
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        var todo = _runtimeRows.Where(r => r.App.rec && r.MarkDone != null).ToList();
        if (todo.Count == 0) { UpdateRuntimeAll(0); return; }
        SetOwn(BtnRuntimeAll, false);
        var failed = new List<string>();
        int i = 0, okCount = 0;
        QueueStart("Установка компонентов", todo.Select(t => t.App.name));
        foreach (var (app, done) in todo)
        {
            i++;
            if (_qStop) break;
            (bool Ok, string Message) r = (false, "");
            QueueSet(i - 1, QState.Now);
            if (!await Busy($"Устанавливаю {app.name} ({i} из {todo.Count})…", () => r = Apps.Install(app))) { failed.Add(app.name); QueueSet(i - 1, QState.Fail); continue; }
            QueueSet(i - 1, r.Ok ? QState.Done : QState.Fail, r.Ok ? null : r.Message);
            if (r.Ok) { okCount++; done(); _runtimeRows.RemoveAll(x => x.App == app); _runtimeInstalled.Add(app.name); } else failed.Add(app.name);
        }
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        QueueFinish();
        SetOwn(BtnRuntimeAll, _wingetOk);
        if (okCount > 0)
        {
            _runtimeBuilt = false;
            await BuildRuntimeAsync();
        }
        RuntimeAllNote.Text = failed.Count == 0 ? "Готово: всё нужное установлено." : "Не установилось: " + string.Join(", ", failed) + ". Нажмите «Сайт» и скачайте вручную.";
        SetStatus(failed.Count == 0 ? "Компоненты установлены" : "Часть компонентов не установилась");
    }

    /// <summary>Карточка «Всё нужное разом»: сколько не хватает и кнопка; если всё стоит — кнопка гаснет и меняет надпись.</summary>
    private void UpdateRuntimeAll(int need)
    {
        if (need == 0)
        {
            RuntimeAllNote.Text = "Всё нужное уже установлено.";
            RuntimeAllNote.Foreground = B("Ok");
            BtnRuntimeAll.Content = "Всё установлено";
            BtnRuntimeAll.Style = (Style)FindResource(typeof(Button));
            SetOwn(BtnRuntimeAll, false);
        }
        else
        {
            RuntimeAllNote.Text = $"Не хватает {need} {Plural(need, "компонента", "компонентов", "компонентов")}. Займёт несколько минут.";
            RuntimeAllNote.Foreground = B("Sub");
            BtnRuntimeAll.Content = "Установить всё нужное";
            BtnRuntimeAll.Style = (Style)FindResource("Primary");
            SetOwn(BtnRuntimeAll, _wingetOk);
        }
    }
}