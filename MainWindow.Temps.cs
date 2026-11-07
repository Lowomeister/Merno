// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// Температура: блок страницы «Драйверы».
public partial class MainWindow : Window
{
    // ---------- температура ----------

    // Датчики обновляются сами, пока карточка видна на экране: видеокарта — каждые 2 секунды
    // (nvidia-smi отвечает быстро), диски — раз в 30 секунд (их опрос заметно тяжелее).
    private const int TempGpuEveryMs = 2000, TempDiskEvery = 15;   // 15 × 2 с = 30 с

    private void AddTempsBlock(StackPanel column)
    {
        var (card, box) = DrvCard("Температура", Glyph.Thermo);
        var rows = new StackPanel();
        box.Children.Add(rows);
        var live = new TextBlock { Text = "Обновляется само, пока открыт этот раздел", Style = (Style)FindResource("SubText"), FontSize = 12.5, Margin = new Thickness(12, 6, 12, 0) };
        box.Children.Add(live);
        column.Children.Add(card);
        rows.Children.Add(DrvRow("Читаю датчики…", "Обычно пара секунд."));

        var gpu = new List<TempReading>();
        var disks = new List<TempReading>();
        var values = new Dictionary<string, (TextBlock Value, TextBlock Sub)>();
        string layout = null;
        bool reading = false, first = true;
        int tick = 0;

        // уровень («норма/тепло/горячо») виден по цвету градусов — в подписи только загрузка и память
        static string Sub(TempReading t) => t.C.HasValue ? (t.Extra ?? "") : (t.Why ?? "нет данных");

        // Своя строка датчика: название и подпись слева (одна строка, без переноса — строка не прыгает по высоте),
        // градусы справа фиксированной ширины. Обновляются только тексты, строка не перестраивается.
        (Border Row, TextBlock Value, TextBlock Sub) SensorRow(TempReading t)
        {
            var row = new Border { Style = (Style)FindResource("Row"), MinHeight = 0, Padding = new Thickness(12, 8, 12, 8) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold, FontSize = 13.5, Foreground = B("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
            var sub = new TextBlock { Text = Sub(t), Style = (Style)FindResource("SubText"), FontSize = 12.5, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            sub.Visibility = sub.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            left.Children.Add(sub);
            var value = new TextBlock { Text = Temps.Text(t.C), FontSize = 15, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right,
                                        Foreground = TempPaint(t.C), VerticalAlignment = VerticalAlignment.Center, FontFamily = (FontFamily)FindResource("Display") };
            Grid.SetColumn(value, 1);
            g.Children.Add(left); g.Children.Add(value);
            row.Child = g;
            return (row, value, sub);
        }

        void Show()
        {
            var all = gpu.Concat(disks).ToList();
            string now = string.Join("|", all.Select(t => t.Name));
            if (now == layout)
            {   // те же датчики — меняем только числа, без перерисовки строк
                // по порядковому номеру: у двух одинаковых дисков имена совпадают
                for (int i = 0; i < all.Count; i++)
                    if (values.TryGetValue(i.ToString(), out var v) && all[i] is var t)
                    {
                        v.Value.Text = Temps.Text(t.C); v.Value.Foreground = TempPaint(t.C);
                        v.Sub.Text = Sub(t); v.Sub.Visibility = v.Sub.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                    }
                return;
            }
            layout = now;
            rows.Children.Clear(); values.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                var (row, value, sub) = SensorRow(all[i]);
                values[i.ToString()] = (value, sub);
                rows.Children.Add(row);
            }
            if (all.All(t => !t.C.HasValue))
                rows.Children.Add(DrvRow("Windows не отдаёт температуру на этом компьютере",
                    "Это не поломка. Температуру покажет OCCT («Программы» → «Проверка железа и разгон»).", null, B("Sub"), Glyph.Info));
            else
                // процессор и плата: Windows их не отдаёт без отдельного драйвера
                rows.Children.Add(DrvRow("Процессор и материнская плата",
                    "Windows не отдаёт их температуру. Её покажет OCCT («Программы» → «Проверка железа и разгон»).", null, B("Sub"), Glyph.Info));
        }

        async void Tick()
        {
            if (reading || WindowState == WindowState.Minimized || !card.IsVisible) return;
            reading = true;
            bool readDisks = first || tick % TempDiskEvery == 0;
            tick++;
            try
            {
                var r = await Task.Run(() => Temps.Read(gpu: true, disks: readDisks));
                gpu = r.Items.Where(i => i.Gpu).ToList();
                if (readDisks) disks = r.Items.Where(i => !i.Gpu).ToList();
                Show();
            }
            catch (Exception ex) { Log.Skip("Температура: " + ex.Message); }
            finally { reading = false; first = false; }
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TempGpuEveryMs) };
        timer.Tick += (_, _) => Tick();
        // Таймер работает, только пока карточка на экране: ушли со страницы или свернули в трей — опрос стоит
        card.IsVisibleChanged += (_, _) => { if (card.IsVisible) { timer.Start(); Tick(); } else timer.Stop(); };
        card.Unloaded += (_, _) => timer.Stop();
        if (card.IsVisible) { timer.Start(); }
        _ = card.Dispatcher.BeginInvoke(new Action(Tick), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>TextBlock с нужным текстом внутри строки.</summary>
    private static TextBlock FindText(DependencyObject root, string text)
    {
        if (root is TextBlock tb && tb.Text == text) return tb;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var f = FindText(VisualTreeHelper.GetChild(root, i), text);
            if (f != null) return f;
        }
        if (root is FrameworkElement fe)
            foreach (var c in LogicalTreeHelper.GetChildren(fe).OfType<DependencyObject>())
            {
                var f = FindText(c, text);
                if (f != null) return f;
            }
        return null;
    }

    Brush TempPaint(int? c) => c == null ? B("Sub") : c < 70 ? B("Ok") : c < 85 ? B("Warn") : B("Bad");
}
