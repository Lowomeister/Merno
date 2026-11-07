// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Merno;

/// <summary>
/// ScrollViewer с полосой «Auto» при каждой раскладке меряет содержимое дважды: сначала без полосы, потом с ней.
/// На длинных списках это половина всей работы при растягивании окна и сворачивании меню.
/// Если список заведомо длиннее двух экранов, полоса будет видна при любой ширине — ставим её «всегда видна»
/// (вид тот же, мерка одна). Короче двух экранов — «Auto».
/// </summary>
internal static class ScrollOnce
{
    public static void Attach(ScrollViewer sv)
    {
        if (sv.VerticalScrollBarVisibility != ScrollBarVisibility.Auto) return;
        bool ours = false;
        sv.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0 && e.ViewportWidthChange == 0) return;
            bool longList = sv.ViewportHeight > 0 && sv.ExtentHeight > sv.ViewportHeight * 2;
            if (!ours && longList && sv.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && sv.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            { ours = true; sv.VerticalScrollBarVisibility = ScrollBarVisibility.Visible; }
            else if (ours && !longList && sv.VerticalScrollBarVisibility == ScrollBarVisibility.Visible)
            { ours = false; sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
        };
    }
}

/// <summary>Часть страницы в каркасе.</summary>
public enum ShellPart { None, Header, Toolbar, Main, Side }

/// <summary>Режим ширины: узко (окно у минимальной ширины), обычно, широко (2K/4K).</summary>
public enum ShellMode { Narrow, Normal, Wide }

/// <summary>
/// Общий каркас страницы: шапка, полоса инструментов, основная колонка и боковая панель справа.
/// Дети объявляются прямо в MainWindow.xaml (имена остаются видны FindName) и помечаются PageShell.Part.
///
/// Ширина содержимого (окно минус меню слева):
///   узко  &lt; 760  — боковая панель уходит под основную колонку, страница прокручивается целиком;
///   обычно 760–1439 — боковая панель справа: 256 (до 960) или 288;
///   широко ≥ 1440 — боковая панель 336, вся страница не шире 1600 и стоит по центру.
/// Scrolling=true — основная и боковая колонки прокручиваются сами (шапка стоит на месте);
/// false — страница целиком внутри внешнего ScrollViewer (Главная).
/// </summary>
public class PageShell : Grid
{
    public const double NarrowBelow = 760, CompactBelow = 960, WideFrom = 1440, MaxContent = 1600;

    public static readonly DependencyProperty PartProperty =
        DependencyProperty.RegisterAttached("Part", typeof(ShellPart), typeof(PageShell), new PropertyMetadata(ShellPart.None));
    public static ShellPart GetPart(UIElement e) => (ShellPart)e.GetValue(PartProperty);
    public static void SetPart(UIElement e, ShellPart v) => e.SetValue(PartProperty, v);

    public bool Scrolling { get; set; } = true;

    private bool _sideHidden;
    /// <summary>Спрятать боковую панель (вкладки «Настроек»: карточки справа только у вкладки «Настройки») — основная колонка во всю ширину.</summary>
    public bool SideHidden
    {
        get => _sideHidden;
        set
        {
            if (_sideHidden == value) return;
            _sideHidden = value;
            foreach (var c in (_g ?? (Grid)this).Children.OfType<UIElement>().Where(c => GetPart(c) == ShellPart.Side))
                c.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            Relayout();
        }
    }

    public ShellMode Mode { get; private set; } = ShellMode.Normal;
    public event Action<ShellMode> ModeChanged;

    private readonly ColumnDefinition _main = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly ColumnDefinition _gap = new() { Width = new GridLength(24) };
    private readonly ColumnDefinition _side = new() { Width = new GridLength(288) };
    private readonly RowDefinition _rBody = new();
    private readonly RowDefinition _rUnder = new() { Height = GridLength.Auto };
    private FrameworkElement _host;
    private bool _built;

    // У страниц со своей прокруткой раскладка живёт во внутренней сетке _g внутри _sv.
    // Обычно _sv не прокручивает (колонки прокручиваются сами); в узком окне прокручивает всю страницу целиком —
    // чтобы не было двух маленьких окошек с отдельными полосами прокрутки.
    private ScrollViewer _sv;
    private Grid _g;
    private Grid L => _g ?? this;
    private bool _whole;

    public PageShell()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Loaded += (_, _) => { Build(); Attach(); Relayout(); };
        IsVisibleChanged += (_, _) => { if (IsVisible && _dirty) Relayout(); };
    }

    private void Build()
    {
        if (_built) return;
        _built = true;
        if (Scrolling)
        {
            _g = new Grid();
            var kids = Children.OfType<UIElement>().ToList();
            Children.Clear();
            foreach (var k in kids) _g.Children.Add(k);
            _sv = new ScrollViewer { Content = _g, Focusable = false,
                                     HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                                     VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            // вложенные списки забирают колесо себе — в режиме «целиком» крутим всю страницу
            _sv.PreviewMouseWheel += (_, e) =>
            {
                if (!_whole) return;
                _sv.ScrollToVerticalOffset(_sv.VerticalOffset - e.Delta * 0.4);
                e.Handled = true;
            };
            Children.Add(_sv);
        }
        foreach (var sv in L.Children.OfType<ScrollViewer>()) ScrollOnce.Attach(sv);
        var g = L;
        g.ColumnDefinitions.Clear(); g.RowDefinitions.Clear();
        g.ColumnDefinitions.Add(_main); g.ColumnDefinitions.Add(_gap); g.ColumnDefinitions.Add(_side);
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // шапка
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // инструменты
        _rBody.Height = Scrolling ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        g.RowDefinitions.Add(_rBody);                                            // основное + боковая
        g.RowDefinitions.Add(_rUnder);                                           // боковая в узком режиме
        foreach (UIElement c in g.Children)
        {
            switch (GetPart(c))
            {
                case ShellPart.Header: SetRow(c, 0); SetColumn(c, 0); SetColumnSpan(c, 3); break;
                case ShellPart.Toolbar: SetRow(c, 1); SetColumn(c, 0); SetColumnSpan(c, 1); break;   // только над списком: боковая панель стоит рядом с поиском
                case ShellPart.Main: SetRow(c, 2); SetColumn(c, 0); break;
            }
        }
    }

    /// <summary>Ширину берём у родителя: сама страница сжимается до MaxWidth.</summary>
    private void Attach()
    {
        if (_host != null) return;
        _host = Parent as FrameworkElement;
        if (_host != null) _host.SizeChanged += (_, _) => Relayout();
    }

    /// <summary>Есть полоса инструментов (поиск): тогда боковая панель начинается рядом с ней, иначе — рядом с основной колонкой.</summary>
    private bool HasToolbar => L.Children.OfType<UIElement>().Any(c => GetPart(c) == ShellPart.Toolbar && c.Visibility != Visibility.Collapsed);

    private bool _dirty;

    private void Relayout()
    {
        if (!_built || _host == null) return;
        // Скрытая страница не перекладывается: иначе при сворачивании меню или растягивании окна все уже открытые
        // разделы вставали в очередь раскладки WPF и мерились заново — тормоза росли с каждым открытым разделом.
        // Переложим, когда страницу покажут.
        if (!IsVisible) { _dirty = true; return; }
        _dirty = false;
        double total = _host.ActualWidth;
        if (_host is ScrollViewer) total -= 14;   // место под полосу прокрутки
        if (total <= 0) return;
        var mode = total < NarrowBelow ? ShellMode.Narrow : total < WideFrom ? ShellMode.Normal : ShellMode.Wide;
        bool compact = total < CompactBelow;
        double pad = mode == ShellMode.Wide ? 40 : compact ? 24 : 32;
        double top = 24;   // «Назад» находится в боковом меню — над заголовком только обычный отступ
        double inner = total - pad * 2;
        double side = Math.Max(pad, (inner > MaxContent ? (total - MaxContent) / 2 : pad));

        bool narrow = mode == ShellMode.Narrow;
        // узко: у страниц со своей прокруткой прокручивается вся страница
        _whole = narrow && _sv != null;
        if (_sv != null)
        {
            _sv.VerticalScrollBarVisibility = _whole ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            if (!_whole) _sv.ScrollToVerticalOffset(0);
            _rBody.Height = _whole ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        }
        L.Margin = new Thickness(side, top, _whole ? Math.Max(8, side - 14) : side, Scrolling && !_whole ? 0 : 24);

        // без боковой панели (Настройки) основная колонка занимает всю ширину
        bool noSide = _sideHidden || !L.Children.OfType<UIElement>().Any(c => GetPart(c) == ShellPart.Side);
        double sideW = mode == ShellMode.Wide ? 336 : compact ? 256 : 288;
        double gapW = mode == ShellMode.Wide ? 32 : compact ? 20 : 24;
        _side.Width = narrow || noSide ? new GridLength(0) : new GridLength(sideW);
        _gap.Width = narrow || noSide ? new GridLength(0) : new GridLength(gapW);
        foreach (UIElement c in L.Children)
        {
            if (GetPart(c) != ShellPart.Side) continue;
            if (narrow) { SetRow(c, 3); SetRowSpan(c, 1); SetColumn(c, 0); SetColumnSpan(c, 3); if (c is FrameworkElement f) f.Margin = new Thickness(0, 16, 0, 0); }
            else { bool tb = HasToolbar; SetRow(c, tb ? 1 : 2); SetRowSpan(c, tb ? 2 : 1); SetColumn(c, 2); SetColumnSpan(c, 1); if (c is FrameworkElement f) f.Margin = new Thickness(0); }
        }
        if (mode != Mode) { Mode = mode; ModeChanged?.Invoke(mode); }
    }
}
