// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

/// <summary>
/// «Настройки → Размер интерфейса». WPF и так учитывает масштаб Windows (125 %, 150 %…), но на 2K/4K
/// у многих в Windows стоит 100 % — и Merno выходит мелким. Тогда увеличиваем всё содержимое окна (LayoutTransform).
/// «Авто»: ширина монитора в единицах WPF ≥ 3400 — 150 %, ≥ 2400 — 125 %, иначе 100 %. Вручную — ползунок 50–200 %.
/// Всплывающие подсказки и меню («⋯») живут в отдельных окнах и не увеличиваются — это нормально.
/// </summary>
public partial class MainWindow
{
    private double _uiScale = 1;
    private bool _uiScaleGuard, _scaleApplied;

    /// <summary>Галочка «Авто» и ползунок 50–200 % с шагом 25. Пока «Авто» включено, ползунок показывает выбранный размер, но не двигается.</summary>
    private void InitUiScale()
    {
        void Show()
        {
            _uiScaleGuard = true;
            bool auto = _settings.UiScale == 0;
            UiScaleAuto.IsChecked = auto;
            UiScaleSlider.IsEnabled = !auto;
            int pct = (int)Math.Round(WantedScale() * 100);
            UiScaleSlider.Value = pct;
            UiScaleVal.Text = $"{pct} %";
            _uiScaleGuard = false;
        }
        // Окно при смене размера НЕ растягиваем: иначе ползунок уезжал из-под мыши, сам себя двигал и картинка дёргалась
        void Set(int val)
        {
            if (_uiScaleGuard || _settings.UiScale == val) return;
            _settings.UiScale = val; _settings.Save();
            ApplyUiScale(resize: false);
            Show();
            Log.Write($"Размер интерфейса: {(val == 0 ? "авто" : val + " %")}");
        }
        UiScaleAuto.Checked += (_, _) => Set(0);
        if (UiScaleAuto.Parent is FrameworkElement scaleBox) NarrowRow(scaleBox);   // в узком окне ползунок — под описанием
        UiScaleAuto.Unchecked += (_, _) => Set((int)Math.Round(WantedScale() * 100));
        // Мышь обрабатываем сами: ползунок берётся в любом месте дорожки (не только за кружок) и тянется.
        // Пока левая кнопка зажата — меняется только подпись «… %»; размер интерфейса — когда кнопку отпустили.
        // Иначе каждый щелчок по дорожке сразу менял бы масштаб, и всё прыгало бы под мышью.
        bool pressing = false;
        int ValueAt(MouseEventArgs e)
        {
            var track = UiScaleSlider.Template?.FindName("PART_Track", UiScaleSlider) as System.Windows.Controls.Primitives.Track;
            if (track == null || track.ActualWidth <= 0) return (int)UiScaleSlider.Value;
            double v = track.ValueFromPoint(e.GetPosition(track));
            double step = UiScaleSlider.TickFrequency > 0 ? UiScaleSlider.TickFrequency : 25;
            v = UiScaleSlider.Minimum + Math.Round((v - UiScaleSlider.Minimum) / step) * step;
            return (int)Math.Clamp(v, UiScaleSlider.Minimum, UiScaleSlider.Maximum);
        }
        // применить, только если левая кнопка действительно отпущена; иначе — проверим ещё раз чуть позже
        void ApplyWhenReleased()
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                Dispatcher.BeginInvoke(new Action(ApplyWhenReleased), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
            Set((int)UiScaleSlider.Value);
        }
        UiScaleSlider.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!UiScaleSlider.IsEnabled) return;
            pressing = true;
            UiScaleSlider.Focus();
            UiScaleSlider.CaptureMouse();
            UiScaleSlider.Value = ValueAt(e);
            e.Handled = true;   // кружок и кнопки дорожки сами ничего не делают — всё здесь
        };
        UiScaleSlider.PreviewMouseMove += (_, e) =>
        {
            if (pressing && e.LeftButton == MouseButtonState.Pressed) UiScaleSlider.Value = ValueAt(e);
        };
        UiScaleSlider.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!pressing) return;
            pressing = false;
            UiScaleSlider.ReleaseMouseCapture();
            e.Handled = true;
            ApplyWhenReleased();
        };
        // захват мыши потерян (Alt+Tab, окно свернули) — считаем, что кнопку отпустили
        UiScaleSlider.LostMouseCapture += (_, _) => { if (pressing) { pressing = false; ApplyWhenReleased(); } };
        UiScaleSlider.ValueChanged += (_, e) =>
        {
            if (_uiScaleGuard) return;
            UiScaleVal.Text = $"{(int)e.NewValue} %";
            // стрелки клавиатуры — сразу; мышь — только после отпускания кнопки
            if (!pressing && Mouse.LeftButton == MouseButtonState.Released) Set((int)e.NewValue);
        };
        Show();
        // окно перенесли на другой монитор или поменяли масштаб Windows — «Авто» пересчитывается
        // при перетаскивании — только когда окно перешло на другой монитор
        string lastScreen = null;
        LocationChanged += (_, _) =>
        {
            if (_settings.UiScale != 0) return;
            string scr;
            try { scr = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).DeviceName; } catch { return; }
            if (scr == lastScreen) return;
            lastScreen = scr;
            ApplyUiScale(resize: false);
        };
        DpiChanged += (_, _) => { if (_settings.UiScale == 0) ApplyUiScale(resize: false); };
        ApplyUiScale(resize: true);
    }

    /// <summary>Ширина монитора, на котором окно, в единицах WPF (с учётом масштаба Windows).</summary>
    private double MonitorWidthDip()
    {
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            var scr = h != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(h) : System.Windows.Forms.Screen.PrimaryScreen;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return scr.Bounds.Width / (dpi > 0 ? dpi : 1);
        }
        catch { return SystemParameters.PrimaryScreenWidth; }
    }

    private double WantedScale()
    {
        if (_settings.UiScale > 0) return _settings.UiScale / 100.0;
        double w = MonitorWidthDip();
        return w >= 3400 ? 1.5 : w >= 2400 ? 1.25 : 1.0;
    }

    /// <summary>resize — заодно увеличить окно, чтобы при крупном интерфейсе в него помещалось столько же.</summary>
    private void ApplyUiScale(bool resize)
    {
        if (Content is not FrameworkElement root || (ShotDir != null && !ShotScaling)) return;
        double s = WantedScale();
        // ничего не изменилось — ничего не трогаем: иначе при перетаскивании окна каждое движение мыши
        // перекладывало бы всё окно (лаги). Учитываем и 100 % (там Transform.Identity, а не ScaleTransform)
        if (_scaleApplied && Math.Abs(s - _uiScale) < 0.001) return;
        _scaleApplied = true;
        double old = _uiScale;
        _uiScale = s;
        root.LayoutTransform = Math.Abs(s - 1) < 0.001 ? Transform.Identity : new ScaleTransform(s, s);
        // минимальный размер окна растёт с масштабом, но не больше экрана — иначе окно не сузить и правая часть
        // (вместе с ползунком размера) уезжает за край
        var wa = SystemParameters.WorkArea;
        MinWidth = Math.Min(860 * s, wa.Width); MinHeight = Math.Min(540 * s, wa.Height);
        if (WindowState == WindowState.Normal && (Width > wa.Width || Height > wa.Height))
        {
            Width = Math.Min(Width, wa.Width); Height = Math.Min(Height, wa.Height);
            Left = Math.Max(wa.Left, Math.Min(Left, wa.Right - Width)); Top = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height));
        }
        if (resize && WindowState == WindowState.Normal)
        {
            var area = SystemParameters.WorkArea;
            double k = s / (old > 0 ? old : 1);
            double w = Math.Min(Math.Max(Width * k, MinWidth), area.Width), hgt = Math.Min(Math.Max(Height * k, MinHeight), area.Height);
            Width = w; Height = hgt;
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - w));
            Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - hgt));
        }
    }
}
