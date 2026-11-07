// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;

namespace Merno;

/// <summary>
/// Строка «название + метки»: первый ребёнок — название (TextBlock с обрезкой «…»), остальные — метки
/// и состояние («Системная», «Осторожно», «● Установлена»). Метки не сжимаются и стоят сразу за названием;
/// не хватает места — сначала ужимается название. Если название стало бы совсем коротким,
/// метки переходят на вторую строку и сами переносятся, но за край не вылезают.
/// Метка с Tag = TitleLine.DotOnly («● Установлена») перед переносом сначала сжимается до одной точки
/// (слово — в подсказке): в узком окне так название не теряет места.
/// </summary>
public class TitleLine : Panel
{
    /// <summary>Сколько места оставить названию, прежде чем переносить метки вниз.</summary>
    public double MinTitle { get; set; } = 120;
    /// <summary>Tag метки-TextBlock: в тесноте оставить только первый кусок текста (точку), остальное — в подсказку.</summary>
    public const string DotOnly = "dot";
    private bool _compact;
    private double _fullTagsW;
    private bool _wrap;
    // кэш полной ширины названия; _titleInf — название сейчас измерено без ограничения ширины
    private double _full = -1, _h = double.NaN;
    private bool _titleInf;
    private string _sig;
    private bool _lazy;          // последняя мерка пропущена: строка вне видимой области
    private ScrollViewer _sv;
    private bool _posOk;         // только что показана (фильтр, смена страницы) — прежнему положению не верим
    public TitleLine() { IsVisibleChanged += (_, _) => _posOk = false; }

    public TitleLine(UIElement title, params UIElement[] tags) : this()
    {
        Children.Add(title);
        foreach (var t in tags) if (t != null) Children.Add(t);
    }

    private IEnumerable<UIElement> Tags => InternalChildren.Cast<UIElement>().Skip(1).Where(c => c.Visibility != Visibility.Collapsed);

    protected override Size MeasureOverride(Size avail)
    {
        if (InternalChildren.Count == 0) return new Size();
        var title = InternalChildren[0];
        double tagsW = 0, tagsH = 0;
        foreach (var t in Tags)
        {
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tagsW += t.DesiredSize.Width; tagsH = Math.Max(tagsH, t.DesiredSize.Height);
        }
        // Полную ширину названия меряем только при смене текста: иначе название форматировалось бы дважды
        // при каждом шаге растягивания окна (без ограничения и с ограничением) — на сотнях строк это заметно.
        // Название помещается — меряем его «без ограничения» (тот же размер, WPF берёт готовый результат).
        string sig = (title as TextBlock)?.Text;
        if (_titleInf || _full < 0 || sig != _sig || !title.IsMeasureValid || avail.Height != _h)
        {
            title.Measure(new Size(double.PositiveInfinity, avail.Height));
            _full = title.DesiredSize.Width; _titleInf = true; _sig = sig; _h = avail.Height;
        }
        double full = _full;
        double w = avail.Width;
        // в тесноте «● Установлена» → «●». Ширину полного вида помним, чтобы решать без перемерки
        // (смена текста в каждой мерке зациклила бы раскладку)
        if (!_compact) _fullTagsW = tagsW;
        bool tight = !double.IsInfinity(w) && _fullTagsW > 0 && full + _fullTagsW > w && w - _fullTagsW < Math.Min(full, MinTitle);
        bool wantCompact = tight && Tags.Any(IsDotOnly);
        if (wantCompact != _compact)
        {
            _compact = wantCompact;
            tagsW = 0; tagsH = 0;
            foreach (var t in Tags)
            {
                if (IsDotOnly(t)) SetCompact((TextBlock)t, _compact);
                t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                tagsW += t.DesiredSize.Width; tagsH = Math.Max(tagsH, t.DesiredSize.Height);
            }
        }
        bool wasWrap = _wrap;
        _wrap = !double.IsInfinity(w) && tagsW > 0 && full + tagsW > w && w - tagsW < Math.Min(full, MinTitle);
        _lazy = false;
        if (!_wrap)
        {
            double tw = double.IsInfinity(w) ? double.PositiveInfinity : Math.Max(0, w - tagsW);
            // строка далеко за видимой областью: высота от ширины не зависит — обрезку названия пересчитаем, когда подъедет
            bool trust = _posOk; _posOk = true;
            if (trust && !wasWrap && !double.IsInfinity(w) && title.IsMeasureValid && Offscreen.Check(this, ref _sv)) { _lazy = true; Offscreen.MarkStale(this, _sv); }
            else if (full <= tw) { if (!_titleInf) { title.Measure(new Size(double.PositiveInfinity, avail.Height)); _titleInf = true; } }
            else { title.Measure(new Size(tw, avail.Height)); _titleInf = false; }
            double width = title.DesiredSize.Width + tagsW;
            return new Size(double.IsInfinity(w) ? width : Math.Min(width, w), Math.Max(title.DesiredSize.Height, tagsH));
        }
        // метки — со второй строки, переносятся как слова
        title.Measure(new Size(w, avail.Height)); _titleInf = false;
        double h = title.DesiredSize.Height, x = 0, lineH = 0;
        foreach (var t in Tags)
        {
            var d = t.DesiredSize;
            if (x > 0 && x + d.Width > w) { h += lineH; x = 0; lineH = 0; }
            if (d.Width > w) t.Measure(new Size(w, double.PositiveInfinity));
            x += Math.Min(t.DesiredSize.Width, w); lineH = Math.Max(lineH, t.DesiredSize.Height);
        }
        h += lineH + 2;
        return new Size(w, h);
    }

    private static bool IsDotOnly(UIElement t) => t is TextBlock tb && tb.Tag as string == DotOnly && tb.Inlines.Count >= 2;

    private static void SetCompact(TextBlock tb, bool on)
    {
        var parts = tb.Inlines.Skip(1).OfType<System.Windows.Documents.Run>().ToList();
        foreach (var r in parts)
        {
            r.Tag ??= r.Text;   // полный текст храним в самом куске
            string want = on ? "" : (string)r.Tag;
            if (r.Text != want) r.Text = want;
        }
        tb.ToolTip = on ? string.Concat(parts.Select(r => (string)r.Tag)).Trim() : null;
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (InternalChildren.Count == 0) return final;
        var title = InternalChildren[0];
        double w = final.Width;
        if (_lazy) return final;   // за видимой областью: название и метки стоят, где стояли (переложим, когда строка подъедет)
        if (!_wrap)
        {
            double tagsW = Tags.Sum(t => t.DesiredSize.Width);
            double h = final.Height;
            double tw = Math.Min(title.DesiredSize.Width, Math.Max(0, w - tagsW));
            title.Arrange(new Rect(0, (h - title.DesiredSize.Height) / 2, tw, title.DesiredSize.Height));
            double x = tw;
            foreach (var t in Tags)
            {
                var d = t.DesiredSize;
                double cw = Math.Min(d.Width, Math.Max(0, w - x));   // крайний случай: даже без названия не влезает — обрезаем, а не вылезаем
                t.Arrange(new Rect(x, (h - d.Height) / 2, cw, d.Height));
                x += cw;
            }
            return final;
        }
        title.Arrange(new Rect(0, 0, w, title.DesiredSize.Height));
        double y = title.DesiredSize.Height + 2, lx = 0, lh = 0;
        foreach (var t in Tags)
        {
            var d = t.DesiredSize;
            if (lx > 0 && lx + d.Width > w) { y += lh; lx = 0; lh = 0; }
            // в начале строки левый отступ метки не нужен — метка встаёт ровно под названием
            double shift = lx == 0 && t is FrameworkElement fe ? fe.Margin.Left : 0;
            double cw = Math.Min(d.Width, w + shift);
            t.Arrange(new Rect(lx - shift, y, cw, d.Height));
            lx += cw - shift; lh = Math.Max(lh, d.Height);
        }
        return final;
    }
}
