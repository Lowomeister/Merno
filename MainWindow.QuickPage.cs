// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// «Быстрый доступ»: значки программ и вспомогательные элементы.
public partial class MainWindow
{
    // ---------- быстрый доступ к Windows ----------
    private void BuildQuick()
    {
        QuickAdvList.Children.Clear();
        BuildQuickAdv();   // окна Windows — MainWindow.Quick.cs
    }

    /// <summary>Значок программы из её файла; пока грузится (или если его нет) — плашка с первой буквой.</summary>
    private FrameworkElement FileIconBox(string name, string spec)
    {
        var tones = new[] { "#2F5D57", "#3B4F6B", "#5B4A6E", "#6B4F3A", "#3F5F3F", "#5E3F4A" };
        int h = 0; foreach (var c in name ?? "?") h = h * 31 + c;
        var letter = (name ?? "?").TrimStart('.', ' ').FirstOrDefault(char.IsLetterOrDigit);
        var box = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                               Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tones[Math.Abs(h % tones.Length)])),
                               Child = new TextBlock { Text = char.ToUpper(letter == default ? '?' : letter).ToString(), FontWeight = FontWeights.SemiBold, FontSize = 14,
                                                       Foreground = B("Text"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        if (!string.IsNullOrWhiteSpace(spec))
            _ = Task.Run(() => FileIcons.Get(spec)).ContinueWith(t =>
            {
                if (t.Status != TaskStatus.RanToCompletion || t.Result == null) return;
                box.Background = Brushes.Transparent;
                var img = new Image { Source = t.Result, Width = 28, Height = 28 };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                box.Child = img;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        return box;
    }

    private async Task LoadIcon(AppItem a, Border box)
    {
        try
        {
            var file = await Apps.IconAsync(a);
            if (file == null) return;
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(file); bmp.EndInit(); bmp.Freeze();
            box.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x35, 0x38));   // светлая подложка: тёмные значки (Ubisoft, Epic) не тонут в фоне
            box.Child = new Image { Source = bmp, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(box.Child, BitmapScalingMode.HighQuality);
        }
        catch { }
    }

    /// <summary>Меняет строку списка на новую на том же месте.</summary>
    private static void SwapRow(Border oldRow, Border newRow)
    {
        if (oldRow.Parent is not Panel p) return;
        int i = p.Children.IndexOf(oldRow);
        if (i < 0) return;
        newRow.Tag = oldRow.Tag;
        p.Children.RemoveAt(i); p.Children.Insert(i, newRow);
    }

}
