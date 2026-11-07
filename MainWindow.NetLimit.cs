// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Merno.Core;

namespace Merno;

/// <summary>
/// «Настройки → Ограничить скорость скачивания»: «Нет · 5 · 10 · 25 · 50 · 100 · Своё» Мбит/с (Core/NetLimit.cs).
/// «Своё» — поле для числа 1–1000; применяется по Enter или когда поле теряет фокус.
/// </summary>
public partial class MainWindow
{
    private void InitNetLimit()
    {
        NetLimit.Mbps = _settings.DownloadLimitMbps;   // скачивания самого Merno — сразу с этим лимитом
        NetLimitSeg.Children.Clear();
        bool custom = _settings.DownloadLimitMbps > 0 && !NetLimit.Choices.Contains(_settings.DownloadLimitMbps);
        foreach (int v in NetLimit.Choices)
        {
            int val = v;
            var rb = new RadioButton
            {
                Style = (Style)FindResource("Seg"), GroupName = "netLimit",
                Content = val == 0 ? "Нет" : val.ToString(),
                IsChecked = !custom && _settings.DownloadLimitMbps == val,
                ToolTip = val == 0 ? "Без ограничения" : $"Не быстрее {val} Мбит/с, примерно {val * 125 / 1024.0:0.#} МБ в секунду"
            };
            rb.Checked += (_, _) => { NetLimitCustom.Visibility = Visibility.Collapsed; ApplyNetLimit(val); };
            NetLimitSeg.Children.Add(rb);
        }
        // своё значение
        var own = new RadioButton { Style = (Style)FindResource("Seg"), GroupName = "netLimit", Content = "Своё", IsChecked = custom,
                                    ToolTip = "Ввести свою скорость, от 1 до 1000 Мбит/с" };
        own.Checked += (_, _) =>
        {
            NetLimitCustom.Visibility = Visibility.Visible;
            if (string.IsNullOrWhiteSpace(NetLimitCustom.Text)) NetLimitCustom.Text = _settings.DownloadLimitMbps > 0 ? _settings.DownloadLimitMbps.ToString() : "";
            NetLimitCustom.Focus(); NetLimitCustom.SelectAll();
        };
        NetLimitSeg.Children.Add(own);
        NetLimitCustom.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (custom) NetLimitCustom.Text = _settings.DownloadLimitMbps.ToString();
        // только цифры
        NetLimitCustom.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        DataObject.AddPastingHandler(NetLimitCustom, (_, e) =>
        {
            if (!(e.DataObject.GetData(typeof(string)) is string s) || !s.Trim().All(char.IsDigit)) e.CancelCommand();
        });
        void ApplyCustom()
        {
            if (own.IsChecked != true) return;
            if (!int.TryParse(NetLimitCustom.Text.Trim(), out int v) || v < 1 || v > 1000)
            {
                SetStatus("Введите скорость от 1 до 1000 Мбит/с");
                NetLimitCustom.Text = _settings.DownloadLimitMbps > 0 ? _settings.DownloadLimitMbps.ToString() : "";
                return;
            }
            ApplyNetLimit(v);
        }
        NetLimitCustom.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyCustom(); e.Handled = true; } };
        NetLimitCustom.LostKeyboardFocus += (_, _) => ApplyCustom();
    }

    private async void ApplyNetLimit(int val)
    {
        if (_settings.DownloadLimitMbps == val) return;
        _settings.DownloadLimitMbps = val;
        _settings.Save();
        NetLimit.Mbps = val;
        if (AppSettings.ReadOnly) return;   // режим снимков — Windows не трогаем
        bool byMerno = _settings.WingetDoByMerno;
        _settings.WingetDoByMerno = await Task.Run(() => NetLimit.ApplySystem(val, byMerno));
        _settings.Save();
        SetStatus(val == 0 ? "Скорость скачивания больше не ограничена" : $"Скорость скачивания ограничена: {val} Мбит/с");
    }
}
