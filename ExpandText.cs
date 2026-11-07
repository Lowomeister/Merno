// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace Merno;

/// <summary>
/// Строка списка далеко за краем видимой области прокрутки (с запасом в пол-экрана).
/// Таким строкам при растягивании окна не пересчитываем обрезку текста — высота у них от этого не меняется,
/// а увидеть их нельзя. Пересчёт — как только строка подъедет к видимой области (прокрутка, смена размера).
/// </summary>
internal static class Offscreen
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, HashSet<FrameworkElement>> _stale = new();

    public static bool Check(FrameworkElement e, ref ScrollViewer sv)
    {
        if (e.ActualHeight <= 0 || !e.IsVisible) return false;
        if (sv == null)
        {
            for (var p = System.Windows.Media.VisualTreeHelper.GetParent(e); p != null; p = System.Windows.Media.VisualTreeHelper.GetParent(p))
                if (p is ScrollViewer s) { sv = s; break; }
            if (sv == null) return false;
        }
        if (sv.ViewportHeight <= 0 || !sv.IsAncestorOf(e)) { sv = null; return false; }
        double y;
        try { y = e.TransformToAncestor(sv).Transform(new Point()).Y; } catch { return false; }
        double m = sv.ViewportHeight * 0.5;
        return y + e.ActualHeight < -m || y > sv.ViewportHeight + m;
    }

    public static void MarkStale(FrameworkElement e, ScrollViewer sv)
    {
        if (!_stale.TryGetValue(sv, out var set))
        {
            set = new HashSet<FrameworkElement>();
            _stale.Add(sv, set);
            sv.ScrollChanged += (_, _) => Refresh(sv, set);
        }
        set.Add(e);
    }

    private static void Refresh(ScrollViewer sv, HashSet<FrameworkElement> set)
    {
        if (set.Count == 0) return;
        List<FrameworkElement> wake = null;
        foreach (var e in set) { var s = sv; if (!Check(e, ref s)) (wake ??= new List<FrameworkElement>()).Add(e); }
        if (wake == null) return;
        foreach (var e in wake) { set.Remove(e); e.InvalidateMeasure(); e.InvalidateArrange(); }
    }
}

/// <summary>Описание раскрывается нажатием по всей строке. Повторное нажатие сворачивает текст.</summary>
public class ExpandText : Decorator
{
    private readonly TextBlock _tb = new() { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private string _text = "", _flat = "", _fontKey;
    private bool _expanded, _over;
    private double _naturalWidth;
    public TextBlock Inner => _tb;
    public ExpandText() : this("") { }
    public ExpandText(string text)
    {
        _tb.SetResourceReference(StyleProperty, "SubText");
        Child = _tb;
        Text = text;
        MouseLeftButtonUp += (_, e) => { if (Toggle()) e.Handled = true; };
        KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space && Toggle()) e.Handled = true; };
    }
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            _flat = _text.ReplaceLineEndings(" ");
            _expanded = false; _fontKey = null;
            System.Windows.Automation.AutomationProperties.SetName(this, _text);
            InvalidateMeasure();
        }
    }
    public bool IsExpanded
    {
        get => _expanded;
        set { if (_expanded != value) { _expanded = value; InvalidateMeasure(); } }
    }
    private bool Toggle()
    {
        if (!_over && !_expanded) return false;
        IsExpanded = !_expanded;
        return true;
    }
    protected override Size MeasureOverride(Size available)
    {
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;
        string font = $"{_tb.FontFamily.Source}|{_tb.FontStyle}|{_tb.FontWeight}|{_tb.FontStretch}|{_tb.FontSize}|{dpi}";
        if (_fontKey != font)
        {
            var face = new System.Windows.Media.Typeface(_tb.FontFamily, _tb.FontStyle, _tb.FontWeight, _tb.FontStretch);
            var measured = new System.Windows.Media.FormattedText(_flat, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, face, _tb.FontSize, System.Windows.Media.Brushes.Black, dpi);
            _naturalWidth = measured.WidthIncludingTrailingWhitespace;
            _fontKey = font;
        }
        _over = _naturalWidth > available.Width + 0.5 || _text != _flat;
        _tb.Text = _expanded ? _text : _flat;
        _tb.TextWrapping = _expanded ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _tb.TextTrimming = _expanded ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        Cursor = _over || _expanded ? Cursors.Hand : null;
        Focusable = _over || _expanded;
        ToolTip = _over || _expanded ? (_expanded ? "Нажмите, чтобы свернуть текст" : "Нажмите, чтобы раскрыть текст") : null;
        _tb.Measure(available);
        return _tb.DesiredSize;
    }
    protected override System.Windows.Media.HitTestResult HitTestCore(System.Windows.Media.PointHitTestParameters point) =>
        new Rect(RenderSize).Contains(point.HitPoint) ? new System.Windows.Media.PointHitTestResult(this, point.HitPoint) : null;
}
