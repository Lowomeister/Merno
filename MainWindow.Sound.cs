// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Звук: блок на странице «Драйверы». Выключить и включить устройство можно в любой момент —
// ничего не удаляется, поэтому звук всегда возвращается обратно.
public partial class MainWindow : Window
{
    private List<SoundDevice> _sound = new();

    /// <summary>Блок «Звук» на странице «Драйверы».</summary>
    private async Task AddSoundBlockAsync(StackPanel column)
    {
        List<SoundDevice> list = new();
        try { await Task.Run(() => list = Sound.List()); }
        catch (Exception ex) { Log.Warn("Звук: " + ex.Message); return; }
        if (list == null || list.Count == 0) return;   // звуковых устройств нет — карточка не нужна

        _sound = list;
        var (card, body) = DrvCard("Звук", Glyph.Sound,
            "Пропал звук на колонках после установки драйверов? Windows иногда переключает его на HDMI. Выключите звук видеокарты, чтобы вернуть звук на колонки или наушники.");

        foreach (var d in _sound.OrderBy(x => x.Video).ThenBy(x => x.Name))
        {
            // наушники и микрофон на одном устройстве — отдельные галочки, чтобы не выключать всё сразу
            var parts = Sound.Parts(d);
            if (parts.Count > 1) body.Children.Add(SoundPartsRow(d, parts));
            else body.Children.Add(SoundRow(d));
        }

        var open = new Button
        {
            Content = "Настройки звука Windows", Margin = new Thickness(12, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Выбрать, какое устройство звучит по умолчанию"
        };
        open.Click += (_, _) => Sh.Open(Sh.Sys32("mmsys.cpl"));   // полный путь: короткое имя Windows искала бы и рядом с Merno
        body.Children.Add(open);
        column.Children.Add(card);
    }

    private Border SoundRow(SoundDevice d)
    {
        var row = new Border { Style = (Style)FindResource("Row") };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // галочка слева
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(sp, 1);
        sp.Children.Add(new TextBlock
        {
            Text = d.Name, FontWeight = FontWeights.SemiBold, FontSize = 14,
            Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis
        });
        var sub = new TextBlock
        {
            Text = Sound.Advice(d), FontSize = 12.5, Foreground = B("Sub"),
            TextWrapping = TextWrapping.Wrap, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0)
        };
        sp.Children.Add(sub);

        var state = new TextBlock
        {
            FontSize = 13.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Text = Sound.State(d), Foreground = Paint(d)
        };
        Grid.SetColumn(state, 2);

        var cb = new CheckBox
        {
            Style = (Style)FindResource("Check"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
            IsChecked = !d.Disabled, IsEnabled = d.Present
        };
        Grid.SetColumn(cb, 0);

        bool guard = false;
        async void Apply()
        {
            bool want = cb.IsChecked == true;
            if (guard || _busy) { guard = true; cb.IsChecked = !d.Disabled; guard = false; return; }
            if (!d.Present) { guard = true; cb.IsChecked = !d.Disabled; guard = false; return; }
            // нельзя оставить компьютер совсем без звука: вернуть его будет нечем
            if (!want && !d.Disabled && Sound.EnabledCount(_sound) <= 1)
            {
                guard = true; cb.IsChecked = true; guard = false;
                Warn("Это последнее звуковое устройство.\n\nЕсли его выключить, звука не будет ни в играх, ни в браузере.");
                return;
            }
            guard = true; cb.IsEnabled = false;
            (bool Ok, bool Reboot, string Error) r = (false, false, null);
            await Busy(want ? "Включаю звук…" : "Выключаю звук…", () => r = Sound.SetEnabled(d, want));
            cb.IsEnabled = d.Present; guard = false;
            if (!r.Ok)
            {
                guard = true; cb.IsChecked = !want; guard = false;
                SetStatus(r.Error ?? "Не получилось изменить звук. Попробуйте ещё раз.");
                return;
            }
            d.Disabled = !want; d.Error = false; d.Pending = r.Reboot;
            d.Status = d.Disabled ? "Disabled" : "OK";
            state.Text = Sound.State(d);
            state.Foreground = Paint(d);
            sub.Text = Sound.Advice(d);
            SetStatus(r.Reboot ? $"Устройство {(want ? "включится" : "выключится")} после перезагрузки компьютера"
                               : d.Disabled ? "Устройство выключено" : "Устройство включено");
        }
        cb.Checked += (_, _) => Apply();
        cb.Unchecked += (_, _) => Apply();
        row.Cursor = d.Present ? Cursors.Hand : Cursors.Arrow;
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (_busy || !d.Present) return;
            if (e.OriginalSource is FrameworkElement fe && IsInside(fe, cb)) return;
            cb.IsChecked = !cb.IsChecked;
        };

        g.Children.Add(sp); g.Children.Add(state); g.Children.Add(cb);
        row.Child = g;
        return row;
    }

    /// <summary>Устройство с несколькими входами и выходами: название и по галочке на каждый («Динамики», «Микрофон»).</summary>
    private Border SoundPartsRow(SoundDevice d, List<AudioEndpoint> parts)
    {
        var row = new Border { Style = (Style)FindResource("Row") };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = d.Name, FontWeight = FontWeights.SemiBold, FontSize = 14,
            Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis
        });
        sp.Children.Add(new TextBlock
        {
            Text = d.Error ? Sound.Advice(d) : "Выход и вход можно отключать по отдельности.", FontSize = 12.5, Foreground = B("Sub"),
            TextWrapping = TextWrapping.Wrap, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 4)
        });
        foreach (var e in parts) sp.Children.Add(SoundPartLine(d, e));
        row.Child = sp;
        return row;
    }

    private FrameworkElement SoundPartLine(SoundDevice d, AudioEndpoint e)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cb = new CheckBox
        {
            Style = (Style)FindResource("Check"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
            IsChecked = !e.Off
        };
        var name = new TextBlock
        {
            Text = Sound.PartName(e) + (e.Capture ? " · запись" : " · звук"), FontSize = 13.5, Foreground = B("Text"),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(name, 1);
        var state = new TextBlock { FontSize = 13.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(state, 2);
        void Show(bool off)
        {
            state.Text = off ? "Отключено" : e.State == 8 ? "Не подключено" : "Работает";
            state.Foreground = off || e.State == 8 ? B("Sub") : B("Ok");
        }
        Show(e.Off);

        bool guard = false;
        async void Apply()
        {
            if (guard) return;
            bool want = cb.IsChecked == true;
            if (_busy) { guard = true; cb.IsChecked = !want; guard = false; return; }
            // нельзя оставить компьютер совсем без звука: вернуть его будет нечем
            if (!want && !e.Capture && Sound.OutputsOn(_sound) <= 1)
            {
                guard = true; cb.IsChecked = true; guard = false;
                Warn("Это последний выход звука.\n\nЕсли его отключить, звука не будет ни в играх, ни в браузере.");
                return;
            }
            cb.IsEnabled = false;
            (bool Ok, string Error) r = (false, null);
            await Busy(want ? "Включаю…" : "Отключаю…", () => r = Sound.SetPart(d, e, want));
            cb.IsEnabled = true;
            var fresh = d.Endpoints.FirstOrDefault(x => x.Id == e.Id) ?? e;
            guard = true; cb.IsChecked = !fresh.Off; guard = false;
            Show(fresh.Off);
            if (!r.Ok) { SetStatus(r.Error ?? "Не получилось. Попробуйте ещё раз."); return; }
            SetStatus($"{Sound.PartName(e)}: {(want ? "включено" : "отключено")}");
        }
        cb.Checked += (_, _) => Apply();
        cb.Unchecked += (_, _) => Apply();
        g.MouseLeftButtonUp += (_, ev) =>
        {
            if (_busy) return;
            if (ev.OriginalSource is FrameworkElement fe && IsInside(fe, cb)) return;
            cb.IsChecked = !cb.IsChecked;
        };
        g.Children.Add(cb); g.Children.Add(name); g.Children.Add(state);
        return g;
    }

    Brush Paint(SoundDevice d) => !d.Present ? B("Sub") : d.Pending ? B("Warn") : d.Disabled ? B("Sub") : d.Error ? B("Warn") : B("Ok");
}
