// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;

namespace Merno;

/// <summary>
/// Список, который на широком окне раскладывает строки в две колонки.
/// Строки — это Border со стилем «Row»; всё остальное (заголовки групп, панели кнопок, подсказки)
/// идёт во всю ширину и начинает новый ряд. Скрытые строки пропускаются — фильтр не оставляет дыр.
/// Наследуется от StackPanel, чтобы весь код, который работает со списками, не менялся.
/// </summary>
public class FlowColumns : StackPanel
{
    /// <summary>Минимальная ширина одной колонки: уже — строка становится тесной.</summary>
    public double MinColumnWidth { get; set; } = 460;
    public double Gap { get; set; } = 16;

    /// <summary>Строки не тянутся до высоты соседа: каждая встаёт в ту колонку, где сейчас ниже.
    /// Для настроек, где строки разной высоты, — иначе кнопки «уезжают» и остаются пустоты.</summary>
    public bool Masonry { get; set; }

    private Style _row, _plate;
    private bool IsRow(UIElement e)
    {
        _row ??= TryFindResource("Row") as Style;
        _plate ??= TryFindResource("Plate") as Style;
        return e is Border b && b.Style != null && (b.Style == _row || b.Style == _plate) && !GetFull(b);
    }

    public static readonly DependencyProperty FullProperty =
        DependencyProperty.RegisterAttached("Full", typeof(bool), typeof(FlowColumns), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static bool GetFull(UIElement e) => (bool)e.GetValue(FullProperty);

    /// <summary>Чётный ряд внутри группы — стиль Row даёт ему чуть более светлый фон (чередование через строку).</summary>
    public static readonly DependencyProperty AltProperty =
        DependencyProperty.RegisterAttached("Alt", typeof(bool), typeof(FlowColumns), new FrameworkPropertyMetadata(false));
    public static bool GetAlt(UIElement e) => (bool)e.GetValue(AltProperty);
    public static void SetAlt(UIElement e, bool v) => e.SetValue(AltProperty, v);

    /// <summary>Расставляет чередование: ряды считаются заново после каждого заголовка группы.</summary>
    private void MarkAlt(int cols)
    {
        int row = 0, col = 0;
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) continue;
            if (IsRow(c))
            {
                bool alt = !Masonry && row % 2 == 1;   // в «кирпичиках» ряды не совпадают — без чередования
                if (c is FrameworkElement fe) { var uid = alt ? "alt" : ""; if (fe.Uid != uid) fe.Uid = uid; }   // стиль Row красит Uid="alt"
                if (++col >= cols) { col = 0; row++; }
            }
            else { row = 0; col = 0; }
        }
    }
    public static void SetFull(UIElement e, bool v) => e.SetValue(FullProperty, v);

    private int Columns(double width) =>
        double.IsInfinity(width) ? 1 : Math.Max(1, Math.Min(2, (int)((width + Gap) / (MinColumnWidth + Gap))));

    protected override Size MeasureOverride(Size avail)
    {
        int cols = Columns(avail.Width);
        MarkAlt(cols);
        if (cols == 1) return base.MeasureOverride(avail);
        double colW = (avail.Width - Gap * (cols - 1)) / cols;
        if (Masonry) return new Size(avail.Width, LayMasonry(cols, colW, avail.Width, false));
        double y = 0, rowH = 0; int col = 0;
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) { c.Measure(new Size(0, 0)); continue; }
            if (IsRow(c))
            {
                c.Measure(new Size(colW, double.PositiveInfinity));
                rowH = Math.Max(rowH, c.DesiredSize.Height);
                if (++col == cols) { y += rowH; rowH = 0; col = 0; }
            }
            else
            {
                if (col > 0) { y += rowH; rowH = 0; col = 0; }
                c.Measure(new Size(avail.Width, double.PositiveInfinity));
                y += c.DesiredSize.Height;
            }
        }
        if (col > 0) y += rowH;
        return new Size(avail.Width, y);
    }

    /// <summary>Раскладка «кирпичиками»: строка — в более низкую колонку, заголовок во всю ширину начинает новый блок.
    /// arrange=false — только измерить; возвращает общую высоту.</summary>
    private double LayMasonry(int cols, double colW, double width, bool arrange)
    {
        var h = new double[cols];
        double top = 0;
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) { if (arrange) c.Arrange(new Rect()); else c.Measure(new Size(0, 0)); continue; }
            if (IsRow(c))
            {
                if (!arrange) c.Measure(new Size(colW, double.PositiveInfinity));
                int k = 0;
                for (int i = 1; i < cols; i++) if (h[i] < h[k] - 0.5) k = i;
                if (arrange) c.Arrange(new Rect(k * (colW + Gap), top + h[k], colW, c.DesiredSize.Height));
                h[k] += c.DesiredSize.Height;
            }
            else
            {
                top += h.Max(); Array.Clear(h);
                if (!arrange) c.Measure(new Size(width, double.PositiveInfinity));
                else c.Arrange(new Rect(0, top, width, c.DesiredSize.Height));
                top += c.DesiredSize.Height;
            }
        }
        return top + h.Max();
    }

    protected override Size ArrangeOverride(Size final)
    {
        int cols = Columns(final.Width);
        if (cols == 1) return base.ArrangeOverride(final);
        double colW = (final.Width - Gap * (cols - 1)) / cols;
        if (Masonry) return new Size(final.Width, Math.Max(LayMasonry(cols, colW, final.Width, true), final.Height));
        double y = 0, rowH = 0; int col = 0;
        var pending = new List<UIElement>();
        void Flush()
        {
            // строки одного ряда — одной высоты, чтобы карточки стояли ровно
            for (int i = 0; i < pending.Count; i++)
                pending[i].Arrange(new Rect(i * (colW + Gap), y, colW, rowH));
            y += rowH; rowH = 0; col = 0; pending.Clear();
        }
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) { c.Arrange(new Rect()); continue; }
            if (IsRow(c))
            {
                pending.Add(c);
                rowH = Math.Max(rowH, c.DesiredSize.Height);
                if (++col == cols) Flush();
            }
            else
            {
                if (col > 0) Flush();
                c.Arrange(new Rect(0, y, final.Width, c.DesiredSize.Height));
                y += c.DesiredSize.Height;
            }
        }
        if (col > 0) Flush();
        return new Size(final.Width, Math.Max(y, final.Height));   // меньший размер WPF поставил бы по центру — список «висел» бы посередине
    }
}
