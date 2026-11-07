// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Merno;

// Меню слева в невысоком окне. При масштабе Windows 150 % на ноутбуке 1920×1080 окну остаётся около 640 точек
// по высоте, а меню нужно около 680: пункты ниже «Обновлений Windows» срезались без намёка, что их можно прокрутить.
// Сначала пункты становятся ниже (34 вместо 40); если и так не влезает, сверху/снизу
// появляется затухание со стрелкой. Полоса прокрутки остаётся скрытой: с ней меняется ширина меню и раскладка
// зацикливается (см. MainWindow.xaml). Затухание лежит поверх списка и ширину не меняет.
public partial class MainWindow
{
    private const double NavNormalHeight = 40, NavCompactHeight = 34;
    private bool _navCompact, _navFitBusy;
    /// <summary>Высота меню, при которой обычные пункты помещаются целиком (замерена, когда они не поместились).</summary>
    private double _navNeed;

    private void InitNavFit()
    {
        var side = ((SolidColorBrush)B("Side")).Color;
        NavFadeTopBg.Background = Fade(side, opaqueAtTop: true);
        NavFadeBottomBg.Background = Fade(side, opaqueAtTop: false);
        NavScroll.ScrollChanged += (_, _) => UpdateNavFit();
        SideBar.SizeChanged += (_, _) => UpdateNavFit();
        NavMore.Click += (_, _) => NavScroll.ScrollToVerticalOffset(NavScroll.VerticalOffset + NavScroll.ViewportHeight * 0.8);
        NavLess.Click += (_, _) => NavScroll.ScrollToVerticalOffset(NavScroll.VerticalOffset - NavScroll.ViewportHeight * 0.8);
    }

    /// <summary>Затухание к краю меню: у края сплошной фон (на нём стрелка), дальше прозрачно (пункт уходит под него).</summary>
    private static LinearGradientBrush Fade(Color c, bool opaqueAtTop)
    {
        var solid = Color.FromArgb(255, c.R, c.G, c.B);
        var clear = Color.FromArgb(0, c.R, c.G, c.B);
        var stops = opaqueAtTop
            ? new GradientStopCollection { new(solid, 0), new(solid, 0.45), new(clear, 1) }
            : new GradientStopCollection { new(clear, 0), new(solid, 0.55), new(solid, 1) };
        return new LinearGradientBrush(stops, 90);
    }

    private void UpdateNavFit()
    {
        if (_navFitBusy || NavScroll.ViewportHeight <= 0) return;
        _navFitBusy = true;
        try
        {
            double over = NavScroll.ExtentHeight - NavScroll.ViewportHeight;
            if (!_navCompact && over > 0.5)
            {
                // запас по высоте запоминаем до сжатия: вернуть обычные пункты, только когда они точно поместятся
                _navCompact = true;
                _navNeed = SideBar.ActualHeight + over;
                SideBar.Resources["NavItemHeight"] = NavCompactHeight;
            }
            else if (_navCompact && SideBar.ActualHeight >= _navNeed)
            {
                _navCompact = false;
                SideBar.Resources["NavItemHeight"] = NavNormalHeight;
            }
            bool below = NavScroll.VerticalOffset < NavScroll.ScrollableHeight - 0.5;
            bool above = NavScroll.VerticalOffset > 0.5;
            NavFadeBottom.Visibility = below ? Visibility.Visible : Visibility.Collapsed;
            NavFadeTop.Visibility = above ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _navFitBusy = false; }
    }

    /// <summary>Для снимков (--shot): плотное ли меню и есть ли скрытые пункты.</summary>
    private string NavFitState() =>
        (_navCompact ? "плотное" : "обычное") + (NavFadeBottom.Visibility == Visibility.Visible ? ", ниже есть ещё" : "");
}
