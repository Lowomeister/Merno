// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

/// <summary>
/// «Осталось доделать» на главной: перезагрузка, повышенная частота монитора и найденные
/// обновления Windows. Подсказки висят, пока дело не сделано, — их легко пропустить,
/// если уйти с вкладки сразу после установки.
/// </summary>
public partial class MainWindow
{
    private bool _pendingLoading;
    /// <summary>Баннер «осталось доделать» показывается, пока его не закроют.</summary>
    private bool _pendingReminder;
    private string _pendingLead;
    private List<string> _pendingWhat = new();

    private async Task RefreshPendingAsync()
    {
        if (_pendingLoading) return;
        _pendingLoading = true;
        try
        {
            bool reboot = false;
            var monitors = new List<MonitorInfo>();
            try
            {
                await Task.Run(() =>
                {
                    reboot = WinUpdate.RebootPending();
                    monitors = Display.Monitors();
                });
            }
            catch (Exception ex) { Log.Skip("Что осталось сделать: " + ex.Message); }

            var updates = _winPending?.ToList() ?? new List<WinPending>();
            bool updatesBusy = _winSearching || _winInstalling;
            RenderPending(reboot, monitors, updatesBusy ? null : updates);
        }
        finally { _pendingLoading = false; }
    }

    private void RenderPending(bool reboot, List<MonitorInfo> monitors, List<WinPending> updates)
    {
        PendingBox.Children.Clear();
        var cards = new List<FrameworkElement>();
        _pendingWhat = new List<string>();

        if (reboot)
        {
            _pendingWhat.Add("перезагрузить компьютер");
            cards.Add(PendingCard(Glyph.Sync, B("Warn"),
                "Нужна перезагрузка",
                "Перезагрузите компьютер, чтобы применить обновления и настройки.\nПеред этим сохраните открытые документы.",
                PendingButton("Перезагрузить", true, (_, _) => AskReboot())));
        }

        foreach (var m in monitors.Where(m => m.CanFaster))
        {
            var mon = m;
            _pendingWhat.Add($"включить {mon.Max}\u00A0Гц вместо {mon.Now}");
            cards.Add(PendingCard(Glyph.Pulse, B("Warn"),
                $"Монитор работает на {mon.Now}\u00A0Гц вместо {mon.Max}\u00A0Гц",
                $"Экран умеет {mon.Max}\u00A0Гц. На такой частоте изображение может выглядеть плавнее. Переключение занимает пару секунд, " +
                $"а если изображение пропадёт, Merno сам вернёт {mon.Now}\u00A0Гц.",
                PendingButton($"Включить {mon.Max}\u00A0Гц", true,
                    (_, _) => Fire(async () => { await RaiseRateAsync(mon, mon.Max); await RefreshPendingAsync(); }, "Частота монитора"))));
        }

        if (updates is { Count: > 0 })
        {
            _pendingWhat.Add(updates.Count == 1 ? "поставить обновление Windows" : $"поставить обновления Windows ({updates.Count})");
            string sub = updates.Count == 1
                ? "Windows нашла одно обновление. Merno поставит его в фоне. Остальными разделами можно пользоваться."
                : $"Windows нашла обновлений: {updates.Count}. Merno поставит их по одному в фоне. Остальными разделами можно пользоваться.";
            cards.Add(PendingCard(Glyph.Download, B("Accent"),
                "Есть обновления Windows",
                sub,
                PendingButton("Открыть обновления", false, (_, _) => ShowPageByName("Windows"))));
        }

        if (cards.Count == 0)
        {
            PendingBox.Visibility = Visibility.Collapsed;
            if (_pendingReminder) ClearHint();   // всё сделали — напоминание больше не нужно
            return;
        }

        PendingBox.Visibility = Visibility.Visible;
        PendingBox.Children.Add(new TextBlock
        {
            Text = "Осталось доделать",
            Style = (Style)FindResource("H2"),
            FontSize = 17,
            Margin = new Thickness(0, 0, 0, 10)
        });
        foreach (var card in cards) PendingBox.Children.Add(card);
        PendingBox.Children.Add(new Border { Height = 14 });
    }

    /// <summary>
    /// Заметное напоминание после установки: что ещё осталось сделать. Уходит само через 15 секунд или по щелчку
    /// в окне (StartHintTimer) — список целиком всегда есть на «Главной» в «Осталось доделать».
    /// </summary>
    private void ShowPendingHint(string lead)
    {
        if (_pendingWhat == null || _pendingWhat.Count == 0) return;
        _pendingLead = lead;
        string what = string.Join(", ", _pendingWhat);
        FeatureHintText.Text = Typo.Nbsp((string.IsNullOrEmpty(lead) ? "" : lead + " ") +
            $"Осталось: {what}.");
        FeatureHintGo.Visibility = Visibility.Visible;
        FeatureHint.Visibility = Visibility.Visible;
        _pendingReminder = true;
        StartHintTimer(15);
    }

    private FrameworkElement PendingCard(string glyph, Brush tone, string title, string sub, Button action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var stripe = new Border { Background = tone, CornerRadius = new CornerRadius(10, 0, 0, 10) };
        Grid.SetColumn(stripe, 0);
        grid.Children.Add(stripe);

        var dp = new DockPanel { Margin = new Thickness(16, 13, 16, 13), LastChildFill = true };
        if (action != null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            action.Margin = new Thickness(16, 0, 0, 0);
            DockPanel.SetDock(action, Dock.Right);
            dp.Children.Add(action);
        }

        var ic = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)FindResource("Icons"),
            FontSize = 17,
            Foreground = tone,
            Width = 24,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 10, 0)
        };
        DockPanel.SetDock(ic, Dock.Left);
        dp.Children.Add(ic);

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14.5,
            Foreground = B("Text"),
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrEmpty(sub))
            sp.Children.Add(new TextBlock
            {
                Text = Typo.Nbsp(sub),
                Style = (Style)FindResource("SubText"),
                Margin = new Thickness(0, 3, 0, 0),
                MaxWidth = 720,
                HorizontalAlignment = HorizontalAlignment.Left
            });
        dp.Children.Add(sp);

        Grid.SetColumn(dp, 1);
        grid.Children.Add(dp);

        return new Border
        {
            Background = B("Panel"),
            BorderBrush = B("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = grid
        };
    }

    private Button PendingButton(string text, bool primary, RoutedEventHandler click)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(14, 6, 14, 6)
        };
        b.Style = (Style)FindResource("Outline");   // залитая кнопка на Главной одна — «Оптимизировать»
        b.Click += click;
        return b;
    }
}
