// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Общие элементы страниц: значки Segoe, карточки, строки и цветные метки.
public partial class MainWindow
{

    /// <summary>Значки Segoe Fluent Icons. Все проверены: шрифт Windows 11 их содержит.</summary>
    static class Glyph
    {
        public const string Gpu = "\uE950", Chip = "\uE964", Gear = "\uE713", Pulse = "\uE9D9",
                            Thermo = "\uE9CA", Trash = "\uE74D", Sound = "\uE7F3", Warn = "\uE7BA",
                            Sync = "\uE895", Key = "\uE8D7", Check = "\uE73E", Download = "\uE896",
                            Info = "\uE783";
    }
    /// <summary>Карточка раздела: заголовок со значком и место для строк.</summary>
    private (Border Card, StackPanel Body) DrvCard(string title, string glyph, string hint = null, Brush glyphBrush = null)
    {
        // заголовок переносится в узкой карточке, а не уходит за край
        var head = new DockPanel { Margin = new Thickness(18, 15, 18, 6) };
        if (!string.IsNullOrEmpty(glyph))
        {
            var hic = new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 15,
                Foreground = glyphBrush ?? B("Accent"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 10, 0)
            };
            DockPanel.SetDock(hic, Dock.Left);
            head.Children.Add(hic);
        }
        head.Children.Add(new TextBlock
        {
            Text = title, Style = (Style)FindResource("H2"), FontSize = 15.5,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        });

        var body = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
        var root = new StackPanel();
        root.Children.Add(head);
        if (!string.IsNullOrEmpty(hint))
            root.Children.Add(new TextBlock
            {
                Text = Typo.Nbsp(hint), Style = (Style)FindResource("SubText"),
                Margin = new Thickness(18, 0, 18, 8), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left
            });
        root.Children.Add(body);
        var card = new Border
        {
            Style = (Style)FindResource("CardBorder"), Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 14), Child = root
        };
        return (card, body);
    }

    /// <summary>Строка списка: текст слева, значение справа, значок — перед текстом.</summary>
    private Border DrvRow(string title, string sub, FrameworkElement right = null, Brush titleBrush = null, string glyph = null)
    {
        var row = new Border { Style = (Style)FindResource("Row") };
        var dp = new DockPanel();
        if (right != null) { right.VerticalAlignment = VerticalAlignment.Center; right.Margin = new Thickness(16, 0, 0, 0); DockPanel.SetDock(right, Dock.Right); dp.Children.Add(right); }
        if (!string.IsNullOrEmpty(glyph))
        {
            var ic = new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 15,
                Foreground = titleBrush ?? B("Sub"), Width = 20, TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
            };
            DockPanel.SetDock(ic, Dock.Left);
            dp.Children.Add(ic);
        }
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        // длинное название в узкой колонке переносится на следующую строку, а не прячется за «…»
        sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = titleBrush ?? B("Text"), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(sub)) sp.Children.Add(new TextBlock { Text = Typo.Nbsp(sub), Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 3, 0, 0) });
        dp.Children.Add(sp); row.Child = dp;
        // узкая колонка (1024×768, боковая панель): кнопка справа сжимала текст до буквы в строке — переносим её под текст
        if (right != null)
            dp.SizeChanged += (_, _) =>
            {
                bool narrow = dp.ActualWidth - right.DesiredSize.Width - (string.IsNullOrEmpty(glyph) ? 0 : 32) < 170;
                if (narrow == (DockPanel.GetDock(right) == Dock.Bottom)) return;
                DockPanel.SetDock(right, narrow ? Dock.Bottom : Dock.Right);
                right.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
                right.Margin = narrow ? new Thickness(string.IsNullOrEmpty(glyph) ? 0 : 32, 10, 0, 0) : new Thickness(16, 0, 0, 0);
            };
        return row;
    }

    /// <summary>Подзаголовок внутри карточки: значок, название и пояснение.</summary>
    private FrameworkElement DrvMicro(string title, string sub, string glyph = null)
    {
        var dp = new DockPanel { Margin = new Thickness(12, 14, 12, 2) };
        if (!string.IsNullOrEmpty(glyph))
        {
            var ic = new TextBlock
            {
                Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 14,
                Foreground = B("Sub"), Width = 20, TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0)
            };
            DockPanel.SetDock(ic, Dock.Left);
            dp.Children.Add(ic);
        }
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13.5, Foreground = B("Text") });
        if (!string.IsNullOrEmpty(sub))
            sp.Children.Add(new TextBlock { Text = Typo.Nbsp(sub), Style = (Style)FindResource("SubText"), Margin = new Thickness(0, 3, 0, 0) });
        dp.Children.Add(sp);
        return dp;
    }

    /// <summary>Итог проверки рядом с кнопками: значок и цвет зависят от результата.</summary>
    private void Note(string text, string tone = "sub")
    {
        DrvNote.Text = text ?? "";
        DrvNoteBox.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        DrvNoteIcon.Text = tone switch { "ok" => Glyph.Check, "warn" or "bad" => Glyph.Warn, _ => Glyph.Sync };
        DrvNoteIcon.Foreground = B(tone switch { "ok" => "Ok", "warn" => "Warn", "bad" => "Bad", _ => "Sub" });
    }
    // ---------- удалить программы ----------

    /// <summary>Цветная метка — только для важного: лишнее, системное, риск.</summary>
    private Border Chip(string text, string brush) => new Border
    {
        Background = new SolidColorBrush(Color.FromArgb(0x26, ((SolidColorBrush)B(brush)).Color.R, ((SolidColorBrush)B(brush)).Color.G, ((SolidColorBrush)B(brush)).Color.B)),
        CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = B(brush) }
    };

}
