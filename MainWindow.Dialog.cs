// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Тёмные диалоги вместо системного MessageBox: в стиле программы, с понятными подписями кнопок
public partial class MainWindow
{
    /// <summary>Текст ошибки для человека: свои сообщения Merno и Windows — по-русски, их показываем как есть;
    /// английский текст .NET (бывает с путями) — только в журнал, на экран — fallback.</summary>
    internal static string ForPerson(Exception ex, string fallback = "Не получилось")
    {
        string m = ex?.Message?.Trim() ?? "";
        return m.Length > 0 && m.Length <= 300 && m.Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё') ? m : fallback;
    }

    /// <summary>
    /// Диалог Merno. cancel == null — одна кнопка (сообщение). safe — по умолчанию фокус на «Отмене»
    /// (для предупреждений, чтобы случайный Enter ничего не сломал).
    /// </summary>
    private bool MernoDialog(string text, string title, string ok, string cancel, bool safe, Brush accent = null, string warning = null)
    {
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var head = new TextBlock { Text = title, Style = (Style)FindResource("H2"), FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        if (accent != null) head.Foreground = accent;
        root.Children.Add(head);
        // предупреждение красным — над основным текстом
        if (warning != null)
            root.Children.Add(new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap, Foreground = B("Bad"), FontWeight = FontWeights.SemiBold, LineHeight = 21, Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = B("Text"), LineHeight = 21 }
        });

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var okBtn = new Button { Content = ok, Style = (Style)FindResource("Primary"), MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
        Button cancelBtn = null;
        if (cancel != null)
        {
            cancelBtn = new Button { Content = cancel, MinWidth = 96, IsCancel = true };
            btns.Children.Add(cancelBtn);
        }
        else okBtn.IsCancel = true;   // Esc закрывает и сообщение
        okBtn.IsDefault = !safe || cancel == null;
        btns.Children.Add(okBtn);
        root.Children.Add(btns);

        var win = new Window
        {
            Title = title, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 500, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Background = B("Bg"), Foreground = B("Text"), FontFamily = FontFamily, FontSize = 14,
            Content = root
        };
        if (IsLoaded && IsVisible) win.Owner = this; else win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        win.SourceInitialized += (_, _) => DarkTitleBar(win);
        okBtn.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => (safe && cancelBtn != null ? cancelBtn : okBtn).Focus();
        try { return win.ShowDialog() == true; }
        catch (Exception ex) { Log.Warn("Диалог: " + ex.Message); return false; }
    }

    private bool Ask(string text, string title = "Merno", MessageBoxImage icon = MessageBoxImage.Question, string yes = "ОК", string no = "Отмена") =>
        MernoDialog(text, title, yes, no, icon == MessageBoxImage.Warning, icon == MessageBoxImage.Warning ? B("Warn") : null);

    private void Info(string text, string title = "Merno") => MernoDialog(text, title, "ОК", null, false);

    /// <summary>Сообщение об ошибке. После закрытия окна ошибка остаётся в строке внизу — красным, до следующего действия.</summary>
    private void Warn(string text, string title = "Merno") { MernoDialog(text, title, "ОК", null, false, B("Warn")); SetError(text); }
}
