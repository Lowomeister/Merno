// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using Merno.Core;

namespace Merno;

// «Перенос на другой компьютер»: настройки, программы и скрытые пункты меню — в один файл
public partial class MainWindow
{
    private void InitTransfer()
    {
        // карточка справа на вкладке «Настройки»: заголовок, одна строка, две кнопки друг под другом
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Перенос на другой компьютер", FontWeight = FontWeights.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(new TextBlock { Text = Typo.Nbsp("Сохраните настройки и список программ в один файл. Откройте его в Merno на новом компьютере."),
                                        Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 4, 0, 0) });
        var save = new Button { Content = "Сохранить в файл", Style = (Style)FindResource("Outline"), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(14, 8, 14, 8) };
        var load = new Button { Content = "Загрузить из файла", Style = (Style)FindResource("Ghost"), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 0) };
        save.Click += (_, _) => Fire(ExportTransferAsync, "Перенос");
        load.Click += (_, _) => Fire(ImportTransferAsync, "Перенос");
        _actionButtons.Add(save); _actionButtons.Add(load);
        sp.Children.Add(save); sp.Children.Add(load);
        RowTransfer.Child = sp;
    }

    private async Task ExportTransferAsync()
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = Transfer.Filter, FileName = $"Перенос настроек Merno {DateTime.Now:dd.MM.yyyy}.merno" };
        if (dlg.ShowDialog(this) != true) return;
        TransferData d = null; string err = null;
        var tweaks = _tweaks.ToList();
        if (!await Busy("Сохраняю настройки…", () =>
        {
            try { d = Transfer.Collect(_ctx, tweaks); Transfer.Save(dlg.FileName, d); }
            catch (Exception ex) { err = ex.ToString(); }
        })) return;
        if (err != null || d == null) { if (err != null) Log.Warn("Перенос, сохранение: " + err); Warn("Не удалось сохранить файл переноса. Выберите другую папку."); return; }
        Log.Ok("Файл переноса сохранён: " + System.IO.Path.GetFileName(dlg.FileName));
        Info($"Сохранено. Настроек: {d.appliedTweaks.Count}, программ: {d.installedCatalogApps.Count}, пунктов меню: {d.hiddenContextMenu.Count}.\n\n" +
             "Скопируйте файл на новый компьютер и нажмите там «Загрузить из файла».");
    }

    private async Task ImportTransferAsync()
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        if (_ctx.S == null) { SetStatus("Подождите пару секунд, Merno ещё загружается"); return; }
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Transfer.Filter };
        if (dlg.ShowDialog(this) != true) return;
        TransferData d;
        try { d = Transfer.Load(dlg.FileName); }
        catch (Exception ex) { Log.Warn("Перенос, чтение: " + ex.Message); Warn("Не удалось прочитать файл. Возможно, это не файл переноса Merno."); return; }
        if (d == null) { Warn("Файл пустой или испорчен. Сохраните его заново."); return; }

        var ids = (d.appliedTweaks ?? new()).ToHashSet();
        // рискованное и необратимое (удаление приложений, Edge, OneDrive) при переносе не применяем
        var tweaks = _tweaks.Where(t => ids.Contains(t.Id) && t.Risk < 2 && t.CanRevert && t.Applicable(_ctx.S)).ToList();
        int skipped = _tweaks.Count(t => ids.Contains(t.Id) && (t.Risk == 2 || !t.CanRevert));
        var names = (d.installedCatalogApps ?? new()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var apps = Apps.Load().Where(a => a.tab == null && names.Contains(a.name) && !string.IsNullOrEmpty(a.winget) && !Apps.IsYandex(a.name) && !Apps.IsYandex(a.winget)).ToList();
        var menu = (d.hiddenContextMenu ?? new()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tweaks.Count + apps.Count + menu.Count == 0) { Info("В файле нет ничего, что можно применить на этом компьютере."); return; }
        if (!Ask($"Файл переноса от {d.date:dd.MM.yyyy}.\n\n" +
                 $"• Настроек: {tweaks.Count}" + (ids.Count - skipped > tweaks.Count ? $" (ещё {ids.Count - skipped - tweaks.Count} не подходят этому компьютеру)" : "") + "\n" +
                 (skipped > 0 ? $"• Пропущено рискованных: {skipped}\n" : "") +
                 $"• Программ: {apps.Count} (установленные будут пропущены)\n" +
                 $"• Скрыть пунктов меню: {menu.Count}\n" +
                 "\nСначала Merno создаст точку восстановления, чтобы всё можно было вернуть. Продолжить?",
                 "Перенос", MessageBoxImage.Question, "Перенести")) return;

        int tOk = 0, hidden = 0; var aOk = new List<string>(); var aFail = new List<string>(); bool noWinget = false;
        int total = 1 + tweaks.Count + apps.Count + (menu.Count > 0 ? 1 : 0), step = 0;
        void Step(string text) { step++; Dispatcher.Invoke(() => SetStatus($"{step} из {total}: {text}")); }
        // полоска внизу: точка, настройки одним пунктом, программы по одной, меню
        var qn = new List<string> { "Точка восстановления" };
        int qTw = tweaks.Count > 0 ? qn.Count : -1; if (tweaks.Count > 0) qn.Add($"Настройки Windows ({tweaks.Count})");
        int qApps = qn.Count; qn.AddRange(apps.Select(a => a.name));
        int qMenu = menu.Count > 0 ? qn.Count : -1; if (menu.Count > 0) qn.Add("Контекстное меню");
        QueueStart("Перенос", qn);
        bool ran = await Busy("Переношу настройки…", () =>
        {
            Log.Head("Перенос с другого компьютера");
            Step("Создаю точку восстановления…");
            QueueSet(0, QState.Now);
            bool pointCreated = Backup.CreateRestorePoint();
            QueueSet(0, pointCreated ? QState.Done : QState.Fail);
            if (!pointCreated) throw new IOException("Точка восстановления не создана. Перенос остановлен; проверьте защиту системы.");
            if (tweaks.Count > 0)
            {
                QueueSet(qTw, QState.Now);
                using var backupSession = Backup.Session();
                foreach (var t in tweaks)
                {
                    Step(t.Name);
                    try { if (!t.IsApplied(_ctx)) { t.Apply(_ctx); Log.Ok(t.Name); } tOk++; }
                    catch (Exception ex) { Log.Warn($"{t.Name}: {ex.Message}"); }
                }
                Backup.Save();
                _ctx.RefreshAppx();
                QueueSet(qTw, tOk == tweaks.Count ? QState.Done : QState.Fail);
            }
            if (apps.Count > 0)
            {
                noWinget = !Apps.WingetAvailable();
                var inst = noWinget ? new HashSet<string>() : Apps.Installed(apps);
                for (int ai = 0; ai < apps.Count; ai++)
                {
                    var a = apps[ai];
                    Step("Устанавливаю " + a.name);
                    QueueSet(qApps + ai, QState.Now);
                    if (noWinget) { aFail.Add(a.name); QueueSet(qApps + ai, QState.Fail, "Нет установщика программ"); continue; }
                    if (inst.Contains(a.name)) { QueueSet(qApps + ai, QState.Done, "уже стоит"); continue; }
                    try { var (ok, m) = Apps.Install(a); (ok ? aOk : aFail).Add(a.name); QueueSet(qApps + ai, ok ? QState.Done : QState.Fail, ok ? null : m); }
                    catch (Exception ex) { aFail.Add(a.name); Log.Warn($"{a.name}: {ex.Message}"); QueueSet(qApps + ai, QState.Fail, ForPerson(ex, "Не получилось установить")); }
                }
            }
            if (menu.Count > 0)
            {
                Step("Убираю лишнее из меню…");
                QueueSet(qMenu, QState.Now);
                try
                {
                    foreach (var it in ShellMenu.Scan().Where(i => !i.Hidden && menu.Contains(Transfer.MenuKey(i))))
                    {
                        try { ShellMenu.SetHidden(it, true); hidden++; } catch (Exception ex) { Log.Warn($"Меню «{it.Name}»: {ex.Message}"); }
                    }
                    if (hidden > 0) ShellMenu.RestartExplorer();
                }
                catch (Exception ex) { Log.Warn("Контекстное меню: " + ex.Message); }
                QueueSet(qMenu, QState.Done);
            }
            Log.Head("Готово");
        });
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        QueueFinish();
        if (!ran) return;

        var msg = $"Применено настроек: {tOk} из {tweaks.Count}.\n" +
                  (noWinget ? "Программы не установлены. Скачайте «Установщик приложения» из Microsoft Store.\n" : $"Установлено программ: {aOk.Count}." + (aFail.Count > 0 ? " Не удалось: " + string.Join(", ", aFail) + "." : "") + "\n") +
                  $"Скрыто пунктов меню: {hidden}.\n\nПерезагрузите компьютер, чтобы всё заработало.";
        if (aFail.Count > 0) Warn(msg, "Перенос"); else Info(msg, "Перенос");
        _appsBuilt = false;
        await BuildTweaksAsync();
        UpdatePlan();
    }
}
