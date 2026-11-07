// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using Merno.Core;

namespace Merno;

// Обновление Merno с GitHub Releases (Core/Updater.cs): галочка и кнопка в «Настройках», полоска над строкой состояния
public partial class MainWindow
{
    private UpdateInfo _update;
    private bool _updChecking, _updInstalling;

    /// <summary>
    /// Человек вошёл в Windows под обычной учётной записью, а Merno запустил с паролем другого администратора:
    /// тогда настройки «для пользователя» (тема, Проводник, мышь) попадут не к нему. Предупреждаем один раз за запуск.
    /// </summary>
    private async Task CheckOtherUserAsync()
    {
        try
        {
            string owner = await Task.Run(() =>
            {
                var (_, o) = Sh.PS("$p = Get-CimInstance Win32_Process -Filter \"Name='explorer.exe'\" | Select-Object -First 1; if ($p) { (Invoke-CimMethod -InputObject $p -MethodName GetOwner).User }", 30_000);
                return o.Trim();
            });
            if (owner.Length == 0 || owner.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase)) return;
            // без имён учётных записей: это окно и журнал попадают на скриншоты
            Log.Warn("Merno запущен от имени другой учётной записи, чем та, под которой вошли в Windows. Настройки вида Windows, Проводника и мыши применятся к ней.");
            Warn("Merno запущен от имени другой учётной записи. Настройки Проводника, темы и мыши применятся к ней, а не к вашей. " +
                 "Чтобы всё применилось к вам, войдите в Windows под учётной записью администратора.");
        }
        catch (Exception ex) { Log.Skip("Проверка учётной записи: " + ex.Message); }
    }

    private void InitUpdate()
    {
        SetAutoUpdate.IsChecked = _settings.AutoUpdateCheck;
        SetAutoUpdate.Checked += (_, _) => { _settings.AutoUpdateCheck = true; _settings.Save(); };
        SetAutoUpdate.Unchecked += (_, _) => { _settings.AutoUpdateCheck = false; _settings.Save(); };
        BtnUpdCheck.Click += (_, _) => Fire(() => CheckUpdateAsync(false), "Обновления");
        UpdBtnInstall.Click += (_, _) => Fire(InstallUpdateAsync, "Обновление");
        UpdBtnNotes.Click += (_, _) => ShowWhatsNew();
        UpdBtnLater.Click += (_, _) => UpdBanner.Visibility = Visibility.Collapsed;   // до следующего запуска
        ShowUpdStatus();

        Loaded += async (_, _) =>
        {
            try
            {
                if (ShotDir != null) return;   // режим снимков — никаких проверок и окон
                _ = Updater.CleanupAsync();
                // прошлый запуск ставил обновление, а работает всё та же версия: говорим, что не встало
                if (Updater.TakeFailedUpdate() is { } failed) SetError(failed);
                _ = CheckOtherUserAsync();
                if (!_settings.AutoUpdateCheck || !Updater.KeyConfigured) return;
                // при каждом запуске: раз в сутки новая версия находилась бы не сразу
                await Task.Delay(5000);
                await CheckUpdateAsync(true);
            }
            catch (Exception ex) { Log.Skip("Проверка обновлений: " + ex.Message); }
        };
    }

    private void ShowUpdStatus()
    {
        // версия крупно написана над этой строкой в карточке — здесь только итог проверки
        string s = "";
        if (!Updater.KeyConfigured) s = "Проверка обновлений в этой сборке недоступна.";
        else if (_settings.LastUpdateCheck != default)
            s = $"{_settings.LastUpdateResult ?? "Проверено"} · {_settings.LastUpdateCheck:dd.MM HH:mm}";
        UpdStatusText.Text = s;
        UpdStatusText.Visibility = s.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>silent — фоновая проверка при запуске: ничего не пишет в строку состояния, если обновлений нет.</summary>
    private async Task CheckUpdateAsync(bool silent)
    {
        if (_updChecking) return;
        _updChecking = true;
        BtnUpdCheck.IsEnabled = false;
        try
        {
            if (!silent) UpdStatusText.Text = "Ищу новую версию…";
            var r = await Updater.CheckAsync();
            _settings.LastUpdateCheck = DateTime.Now;
            _settings.LastUpdateResult = r.Message;
            _settings.Save();
            ShowUpdStatus();
            if (r.Info != null)
            {
                _update = r.Info;
                UpdBannerText.Text = $"Вышла версия {r.Info.Version}." + (r.Info.Notes.Length > 0 ? " " + r.Info.Notes : "");
                UpdBannerText.ToolTip = UpdBannerText.Text;
                UpdBanner.Visibility = Visibility.Visible;
                if (!silent && !_busy) SetStatus($"Вышла версия {r.Info.Version}");
            }
            else if (!silent && !_busy) SetStatus(r.Message);
        }
        finally { _updChecking = false; BtnUpdCheck.IsEnabled = true; }
    }

    private async Task InstallUpdateAsync()
    {
        var info = _update;
        if (info == null || _updInstalling) return;
        if (!Updater.CanSelfUpdate(Environment.ProcessPath)) { Warn(Updater.ManualUpdateMessage); return; }
        if (_busy) { SetStatus("Merno ещё занят. Подождите и нажмите «Обновить» снова"); return; }
        if (BlockedByWinInstall("Обновите Merno")) return;   // установка Merno закрывает программу без вопроса
        string mb = (info.Size / 1048576.0).ToString("0.#");
        if (!Ask($"Скачать версию {info.Version} ({mb} МБ) и перезапустить Merno?",
                 "Обновление Merno", MessageBoxImage.Question, "Обновить", "Не сейчас")) return;
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        if (BlockedByWinInstall("Обновите Merno")) return;   // пока висел вопрос, могли начать установку

        _updInstalling = true;
        UpdBtnInstall.IsEnabled = false;
        SetBusy(true, "Скачиваю обновление…");
        string file = null, error = null;
        try
        {
            var progress = new Progress<int>(p =>
                SetStatus(p < 0 ? "Почти готово…" : $"Скачиваю обновление… {p} %"));
            (file, error) = await Updater.DownloadAndVerifyAsync(info, progress);
            if (file != null)
            {
                SetStatus("Обновляю Merno…");
                error = await Task.Run(() => Updater.Apply(file, info));
                if (error == null)
                {
                    Log.Write("Перезапуск после обновления");
                    StartExitGuard();
                    App.ForceExit = true;
                    Application.Current.Shutdown();
                    return;
                }
            }
        }
        catch (Exception ex) { error = "Не удалось обновить Merno. Попробуйте позже."; Log.Warn("Обновление: " + ex.Message); }
        finally
        {
            _updInstalling = false;
            UpdBtnInstall.IsEnabled = true;
            SetBusy(false, error == null ? null : "Merno не обновлён");
        }
        if (error == "sig") Warn("Не удалось проверить загрузку обновления. Попробуйте позже.", "Обновление Merno");

        else if (error != null) Warn(error, "Обновление Merno");
    }

    /// <summary>
    /// Установщик ждёт, пока эта копия закроется (до минуты, tools/installer/Merno.iss), и только потом ставит обновление.
    /// Подвиснет выход, тогда через 20 с завершаемся сами (как Program.cs закрывает подвисшую копию при --updated).
    /// Kill, а не Environment.Exit: тот снова вызвал бы обработчики выхода, а они пишут в журнал под блокировкой.
    /// </summary>
    private static void StartExitGuard()
    {
        var guard = new Thread(() =>
        {
            Thread.Sleep(20_000);
            // запись в журнал не дольше 2 с: если выход подвис на журнале, она не должна задержать завершение
            try { Task.Run(() => Log.Warn("Merno не закрылся за 20 с после запуска обновления. Закрываю принудительно")).Wait(2000); } catch { }
            try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
        }) { IsBackground = true, Name = "Merno update exit guard" };
        guard.Start();
    }
}
