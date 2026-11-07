// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// «Драйверы»: сборка страницы. Общие карточки — MainWindow.Cards.cs.
public partial class MainWindow
{
    private bool _drvBuilt, _drvSearching;
    private void AddGameDrivers(StackPanel body, DriverInfo g, string current)
    {
        var list = Drivers.GameDrivers(g.Device, _ctx.S?.Laptop == true, _settings.Advanced);
        if (list.Count == 0) return;
        if (current != null && Drivers.BadFor(current, g.Device) is string bad)
            body.Children.Add(DrvRow($"У вас драйвер {current}, с которым есть известные проблемы", bad, null, B("Warn"), Glyph.Warn));
        // про старый драйвер говорим, только если его нет среди вариантов ниже (там у него и так честное описание)
        else if (!AdvicePolicy.DriverKept(_settings, g.DeviceId, g.Version) && !list.Any(d => d.Version == current) && Drivers.OldFpsNote(current, g.Device) is string old)
            body.Children.Add(DrvRow($"У вас старый драйвер {current}", old, null, B("Warn"), Glyph.Warn));
        foreach (var d in list)
        {
            FrameworkElement right;
            if (d.Version == current) right = Chip("Установлен", "Ok");
            else
            {
                var b = new Button { Content = "Скачать",
                    ToolTip = "Скачается официальный установщик NVIDIA. Если ставите драйвер старше текущего, в установщике выберите «Выборочная установка» и «Чистая установка»." };
                var url = d.Url;
                b.Click += (_, _) => { Sh.Open(url); SetStatus($"Скачивается драйвер {d.Version}. После загрузки запустите установщик"); };
                right = b;
            }
            body.Children.Add(DrvRow($"{d.Version}: {d.Title}", d.Text, right));
        }
    }

    /// <summary>AMD: Windows Update ставит поверх Adrenalin свой старый драйвер. Галочка = настройка no_driver_wu
    /// (та же, что в «Настройках Windows»); меняется кнопкой «Применить».</summary>
    private Border AmdLockRow()
    {
        var t = Catalog.All().First(x => x.Id == "no_driver_wu");
        bool on = t.IsApplied(_ctx);
        var cb = new CheckBox { IsChecked = on };
        var apply = new Button { Content = "Применить", Style = (Style)FindResource("Outline"), Margin = new Thickness(12, 0, 0, 0),
                                 VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        cb.Checked += (_, _) => apply.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        cb.Unchecked += (_, _) => apply.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        apply.Click += async (_, _) =>
        {
            bool want = cb.IsChecked == true; string msg = null;
            bool started = await Busy(want ? "Запрещаю Windows менять драйверы…" : "Снимаю запрет…", () =>
            {
                if (want) { using var backupSession = Backup.Session(); try { t.Apply(_ctx); } finally { Backup.Save(); } }
                else { var (ok, m) = Backup.RevertTweak(t, _ctx); if (!ok) msg = m; }
            });
            if (!started) return;
            if (msg != null) { Warn("Не получилось снять запрет:\n\n" + msg); cb.IsChecked = on; return; }
            on = want; apply.Visibility = Visibility.Collapsed;
            SetStatus(want ? "Готово. Windows больше не будет менять драйверы" : "Готово. Windows снова может ставить драйверы");
        };
        var title = new TextBlock { Text = "Не давать Windows менять драйвер", FontWeight = FontWeights.SemiBold, FontSize = 14,
                                    Foreground = B("Text"), TextWrapping = TextWrapping.Wrap };
        var icon = new Border { Width = 0 };
        return SelRow(icon, title,
            "Центр обновления иногда ставит поверх драйвера AMD старую версию. После этого Adrenalin может сообщать об ошибках, а игры могут вылетать. " +
            "Драйверы для всех устройств придётся обновлять самому.", cb, apply);
    }
    private async Task BuildDriversAsync()
    {
        if (_drvSearching) return;
        _drvBuilt = true; _drvSearching = true;
        try
        {
            SetOwn(BtnDrvRefresh, false);
            Note("Проверяю устройства…");
            DrvMain.Children.Clear();
            DrvSide.Children.Clear();
            // без сведений о компьютере Apps.Fits пропускает всё: иначе на виртуальной видеокарте показались бы программы NVIDIA и AMD
            for (int i = 0; i < 200 && _ctx.S == null; i++) await Task.Delay(300);
            List<DeviceProblem> problems = new(); List<DriverInfo> installed = new();
            bool listFailed = false;
            await Task.Run(() =>
            {
                problems = Drivers.Problems();
                var got = Drivers.InstalledOrNull();
                listFailed = got == null; installed = got ?? new();
                // сайт NVIDIA спрашиваем, только если видеокарта NVIDIA
                if (installed.Any(d => d.Class == "Видеокарта" && (d.Device ?? "").Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))) Drivers.RefreshNvidia();
            });

            if (listFailed)
                DrvMain.Children.Add(DrvErrorRow("Не получилось получить список драйверов. Попробуйте ещё раз.", () => { _drvBuilt = false; return BuildDriversAsync(); }));

            // ---------- слева: что можно поставить ----------
            // Видеокарта и драйвер, подобранный под неё
            var gpus = installed.Where(d => d.Class == "Видеокарта").ToList();
            if (gpus.Count > 0)
            {
                var (card, body) = DrvCard("Видеокарта", Glyph.Gpu);
                foreach (var g in gpus)
                {
                    var (vendor, url) = Drivers.GpuVendor(g.Device + " " + g.Provider);
                    Button site = null;
                    if (url != null) { site = new Button { Content = $"Сайт {vendor}", ToolTip = url }; var u = url; site.Click += (_, _) => Sh.Open(u); }
                    var nv = Drivers.NvidiaVersion(g.Version);
                    var ver = vendor == "NVIDIA" && nv != null ? nv : g.Version;
                    string tip = Drivers.IsGood(nv) && vendor == "NVIDIA" ? "Этот драйвер проверен для игр. Обновлять его необязательно." : "";
                    // дата из сведений о видеокарте (Win32_VideoController): в списке драйверов Windows путает день и месяц
                    string date = _ctx.S?.GpuDriverDate is DateTime gd && gpus.Count == 1 ? gd.ToString("dd.MM.yyyy") : g.Date;
                    body.Children.Add(DrvRow(g.Device, $"Версия {ver}{(!string.IsNullOrEmpty(date) ? " от " + date : "")}. {tip}", site));
                    // без кода устройства или версии запомнить выбор нельзя: такую галочку не показываем
                    // (выключенная кнопка иначе остаётся белым квадратом с нечитаемой подписью)
                    // стиль программы «Check»: без него WPF рисовал белый системный квадрат
                    var keep = new CheckBox { Content = "Оставить эту версию", Style = (Style)FindResource("Check"), Margin = new Thickness(20, 4, 12, 8),
                        Visibility = !string.IsNullOrWhiteSpace(g.DeviceId) && !string.IsNullOrWhiteSpace(g.Version) ? Visibility.Visible : Visibility.Collapsed,
                        IsChecked = AdvicePolicy.DriverKept(_settings, g.DeviceId, g.Version),
                        ToolTip = "Не напоминать об обновлении из-за возраста драйвера. Это не блокирует обновления Windows. После смены версии напоминания вернутся. Предупреждения об известных проблемах останутся." };
                    body.Children.Add(keep);
                    var choices = new StackPanel(); body.Children.Add(choices);
                    void RenderChoices() { choices.Children.Clear(); AddGameDrivers(choices, g, vendor == "NVIDIA" ? nv : null); }
                    void SaveChoice()
                    {
                        _settings.KeptDriverVersions ??= new();
                        foreach (var key in _settings.KeptDriverVersions.Keys.Where(key => string.Equals(key, g.DeviceId, StringComparison.OrdinalIgnoreCase)).ToList()) _settings.KeptDriverVersions.Remove(key);
                        if (keep.IsChecked == true) _settings.KeptDriverVersions[g.DeviceId] = g.Version;
                        _settings.Save();
                        RenderChoices(); RefreshAdviceChoices();
                    }
                    keep.Checked += (_, _) => SaveChoice();
                    keep.Unchecked += (_, _) => SaveChoice();
                    RenderChoices();
                }
                if (gpus.Any(g => Drivers.GpuVendor(g.Device + " " + g.Provider).Item1 == "AMD"))
                    body.Children.Add(AmdLockRow());
                DrvMain.Children.Add(card);
                // подсказка «драйвер старше года» подсвечивает именно эту карточку
                try { UnregisterName("DrvCardGpu"); } catch { }
                try { RegisterName("DrvCardGpu", card); } catch { }
            }
            // Звук — сразу под видеокартой: пропавший звук обычно последствие её драйвера (MainWindow.Sound.cs)
            await AddSoundBlockAsync(DrvMain);
            // Материнская плата / ноутбук и фирменный софт для мыши, клавиатуры, наушников
            (string Title, string Url) board = (null, null);
            List<Drivers.GearApp> gear = new();
            bool laptop = _ctx.S?.Laptop == true;
            await Task.Run(() => { board = Drivers.Board(laptop); gear = Drivers.Gear(); });
            if (board.Title != null)
            {
                var (card, body) = DrvCard(laptop ? "Ноутбук" : "Материнская плата", Glyph.Chip);
                var open = new Button { Content = "Найти драйверы", ToolTip = board.Url };
                var bu = board.Url; open.Click += (_, _) => Sh.Open(bu);
                body.Children.Add(DrvRow(board.Title, "Драйверы чипсета, звука и сети найдите на сайте производителя по модели устройства.", open));
                DrvMain.Children.Add(card);
            }
            var wheels = gear.Where(g => g.Wheel).ToList();
            gear = gear.Where(g => !g.Wheel).ToList();
            if (wheels.Count > 0)
            {
                var (card, body) = DrvCard("Руль и педали", Glyph.Gear,
                    "Без драйвера производителя руль может не работать в играх или работать без отдачи.");
                foreach (var g in wheels)
                {
                    var b = new Button { Content = "Скачать", ToolTip = g.Url };
                    var gu = g.Url; b.Click += (_, _) => Sh.Open(gu);
                    body.Children.Add(DrvRow(g.App, $"Найден руль {g.Brand}. Скачайте с сайта производителя.", b));
                }
                DrvMain.Children.Add(card);
            }
            if (gear.Count > 0)
            {
                var (card, body) = DrvCard("Мышь, клавиатура, наушники", Glyph.Gear,
                    "Фирменные программы: чувствительность, подсветка, кнопки.");
                foreach (var g in gear)
                {
                    var b = new Button { Content = "Скачать", ToolTip = g.Url };
                    var gu = g.Url; b.Click += (_, _) => Sh.Open(gu);
                    body.Children.Add(DrvRow(g.App, $"Найдено устройство {g.Brand}.", b));
                }
                DrvMain.Children.Add(card);
            }
            // Программы производителей: что показать — решает Apps.Fits по железу (карта NVIDIA, процессор AMD и т. д.).
            // Программы разгона и проверки (Ryzen Master, Afterburner, OCCT) — в «Программах», группа «Проверка железа и разгон».
            var drvApps = Apps.Load().Where(a => a.tab == "drivers" && Apps.Fits(a, _ctx.S)).ToList();
            if (drvApps.Count > 0)
            {
                HashSet<string> vi = new();
                await Task.Run(() => { _wingetOk = Apps.WingetAvailable(); vi = Apps.Installed(drvApps); });
                var vendorApps = drvApps.Where(a => !a.tuning).ToList();
                if (vendorApps.Count > 0)
                {
                    var (card, body) = DrvCard("Программы производителей", Glyph.Gear,
                        "Официальные программы производителей.");
                    // галочки: после переустановки ставят сразу несколько (карточка «Отмеченные» справа, MainWindow.Batch.cs)
                    _drvGrp.All = vendorApps; _drvGrp.Installed = new HashSet<string>(vi, StringComparer.OrdinalIgnoreCase);
                    _drvGrp.Rebuild = () => { _drvBuilt = false; return BuildDriversAsync(); };
                    var selHost = new StackPanel();
                    DrvSide.Children.Add(new Border { Style = (Style)FindResource("CardBorder"), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 16), Child = selHost });
                    BuildGroupCard(selHost, _drvGrp);
                    foreach (var a in vendorApps) body.Children.Add(AppRow(a, vi.Contains(a.name), "Сайт", showUninstall: true,
                        selectableSet: _drvGrp.Sel, register: cb => _drvGrp.Checks[a.name] = cb));
                    DrvMain.Children.Add(card);
                }
            }

            // ---------- справа: состояние компьютера ----------
            // «Требуют внимания» — только когда есть что исправлять; всё в порядке — карточки нет, итог виден в «Проверке»
            if (problems.Count > 0)
            {
                var (attn, attnBody) = DrvCard("Требуют внимания", null);
                foreach (var p in problems) attnBody.Children.Add(ProblemRow(p));
                DrvSide.Children.Add(attn);
            }

            // ничего не нашлось (например, стоят только драйверы Microsoft) — пишем это, а не оставляем пустую страницу
            if (DrvMain.Children.Count == 0)
                DrvMain.Children.Add(DrvErrorRow("Отдельных драйверов от производителей не найдено. Драйверы от Windows ищите в разделе «Обновления Windows».", null));
            if (listFailed) Note("Список драйверов не получен", "bad");
            else if (problems.Count == 0) Note("Все устройства работают", "ok");
            else Note($"Требуют внимания: {problems.Count}", "warn");
        }
        catch (Exception ex)
        {
            Log.Warn("Драйверы: " + ex.Message); Log.Skip(ex.ToString());
            Note("Не получилось проверить драйверы. Нажмите «Проверить ещё раз».", "bad");
            DrvMain.Children.Clear();
            DrvMain.Children.Add(DrvErrorRow("Не получилось проверить драйверы. Попробуйте ещё раз.", () => { _drvBuilt = false; return BuildDriversAsync(); }));
        }
        finally { SetOwn(BtnDrvRefresh, true); _drvSearching = false; }
    }

    /// <summary>Строка вместо пустой страницы: что случилось и (если есть) «Повторить».</summary>
    private Border DrvErrorRow(string text, Func<Task> retry)
    {
        var row = new Border { Style = (Style)FindResource("Row") };
        var dp = new DockPanel();
        if (retry != null)
        {
            var btn = new Button { Content = "Повторить", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            btn.Click += (_, _) => Fire(retry, "Драйверы");
            DockPanel.SetDock(btn, Dock.Right);
            dp.Children.Add(btn);
        }
        dp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = B(retry != null ? "Warn" : "Sub") });
        row.Child = dp;
        return row;
    }

    /// <summary>Устройство без драйвера: чьё оно (по коду PCI/USB) и как найти драйвер, если в «Обновлениях Windows» его нет.</summary>
    private Border ProblemRow(DeviceProblem p)
    {
        bool vm = p.Maker is "VirtualBox" or "VMware" or "Red Hat VirtIO" or "Microsoft Hyper-V";
        string sub = vm
            ? $"{p.Problem}. Это устройство виртуальной машины ({p.Maker}). Драйвер ставится вместе с «Дополнениями гостевой ОС» (VirtualBox Guest Additions, VMware Tools)."
            : p.Maker.Length > 0
                ? $"{p.Problem}. Производитель: {p.Maker}. Поищите драйвер в «Обновлениях Windows». Если его там нет, проверьте сайт {p.Maker}."
                : $"{p.Problem}. Поищите драйвер в «Обновлениях Windows». Если его там нет, найдите его по коду устройства.";
        if (!string.IsNullOrEmpty(p.HwId)) sub += $"\nКод устройства: {p.HwId}";
        FrameworkElement right = null;
        if (!string.IsNullOrEmpty(p.HwId))
        {
            var menu = new ContextMenu();
            var find = new MenuItem { Header = "Найти драйвер в интернете" };
            find.Click += (_, _) => Sh.Open(Drivers.HwSearchUrl(p.HwId));
            var copy = new MenuItem { Header = "Скопировать код устройства" };
            copy.Click += (_, _) => { try { Clipboard.SetText(p.HwId); SetStatus("Код устройства скопирован"); } catch { } };
            menu.Items.Add(find); menu.Items.Add(copy);
            var btn = new Button { Content = "Найти драйвер", Style = (Style)FindResource("Outline"), ContextMenu = menu };
            btn.Click += (_, _) => { menu.PlacementTarget = btn; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; };
            right = btn;
        }
        return DrvRow(p.Name, sub, right, B("Warn"), Glyph.Warn);
    }

}
