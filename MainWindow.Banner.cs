// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Merno.Core;

namespace Merno;

/// <summary>
/// Баннер Главной в пиксельном стиле старых аркадных игр: сцена рисуется по точкам (130×70) и растягивается
/// без сглаживания (NearestNeighbor). Небо, звёзды, луна и горы неподвижны — двигается только вода:
/// отражение луны мерцает, по воде бегает рябь (кадры по кругу, 8 в секунду — как в старых играх).
/// Цвета — из палитры темы (ActiveTheme.Map), поэтому баннер перекрашивается вместе с Merno.
/// Экономия: кадры воды готовятся один раз, таймер работает только пока баннер виден. В режиме снимков — без анимации.
/// </summary>
public partial class MainWindow
{
    private const int PxW = 130, PxH = 70, Horizon = 48, WaterFrames = 16;
    private WriteableBitmap[] _waterFrames;
    private DispatcherTimer _waterTimer;
    private int _waterFrame;
    private bool _bannerRunning;

    private void InitBanner()
    {
        try
        {
            BannerScene.Source = DrawScene();
            _waterFrames = Enumerable.Range(0, WaterFrames).Select(DrawWater).ToArray();
            BannerWater.Source = _waterFrames[0];
        }
        catch (Exception ex) { Log.Skip("Баннер: " + ex.Message); return; }
        if (ShotDir != null) return;

        _waterTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(125) };
        _waterTimer.Tick += (_, _) => { _waterFrame = (_waterFrame + 1) % WaterFrames; BannerWater.Source = _waterFrames[_waterFrame]; };
        HomeStateCard.IsVisibleChanged += (_, _) => UpdateBanner();
        StateChanged += (_, _) => UpdateBanner();
        IsVisibleChanged += (_, _) => UpdateBanner();
        Closed += (_, _) => _waterTimer?.Stop();
        UpdateBanner();
    }

    private void UpdateBanner()
    {
        bool want = _waterTimer != null && HomeStateCard.IsVisible && IsVisible && WindowState != WindowState.Minimized;
        if (want == _bannerRunning) return;
        _bannerRunning = want;
        if (want) _waterTimer.Start(); else _waterTimer.Stop();
    }

    // ---------------------------------------------------------------- палитра
    private Color Pal(string hex) => ActiveTheme.Map((Color)ColorConverter.ConvertFromString(hex));

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Упорядоченный дизеринг 4×4 (Байер): плавный переход двумя цветами «в клеточку», как в 16-битных играх.</summary>
    private static readonly int[,] Bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
    private static bool Dither(int x, int y, double t) => t * 16 > Bayer[y & 3, x & 3] + 0.5;

    /// <summary>Полосатый градиент: 4 ступени цвета, на стыках — дизеринг.</summary>
    private static Color Bands(Color top, Color bottom, int x, int y, int y0, int y1)
    {
        double t = Math.Clamp((y - y0) / (double)Math.Max(1, y1 - y0), 0, 1) * 3;   // 4 ступени
        int step = (int)Math.Floor(t); double frac = t - step;
        var c0 = Mix(top, bottom, Math.Min(step, 3) / 3.0);
        var c1 = Mix(top, bottom, Math.Min(step + 1, 3) / 3.0);
        return frac > 0.55 && Dither(x, y, (frac - 0.55) / 0.45) ? c1 : c0;
    }

    private static void Put(byte[] px, int x, int y, Color c, double alpha = 1)
    {
        if (x < 0 || y < 0 || x >= PxW || y >= PxH) return;
        int i = (y * PxW + x) * 4;
        if (alpha >= 1) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255; return; }
        // смешивание поверх уже нарисованного (или прозрачного — тогда просто полупрозрачная точка)
        byte a0 = px[i + 3];
        if (a0 == 0) { px[i] = (byte)(c.B * alpha); px[i + 1] = (byte)(c.G * alpha); px[i + 2] = (byte)(c.R * alpha); px[i + 3] = (byte)(255 * alpha); return; }
        px[i] = (byte)(px[i] + (c.B - px[i]) * alpha); px[i + 1] = (byte)(px[i + 1] + (c.G - px[i + 1]) * alpha); px[i + 2] = (byte)(px[i + 2] + (c.R - px[i + 2]) * alpha);
    }

    private static WriteableBitmap ToBitmap(byte[] px)
    {
        // Pbgra32 — значения уже умножены на прозрачность (Put так и пишет)
        var wb = new WriteableBitmap(PxW, PxH, 96, 96, PixelFormats.Pbgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, PxW, PxH), px, PxW * 4, 0);
        wb.Freeze();
        return wb;
    }

    // ---------------------------------------------------------------- неподвижная сцена
    private const int MoonX = 65, MoonY = 21;

    private static double MountainH(int x, (int X, int Y)[] pts)
    {
        for (int i = 1; i < pts.Length; i++)
            if (x <= pts[i].X)
            {
                var (x0, y0) = pts[i - 1]; var (x1, y1) = pts[i];
                return y0 + (y1 - y0) * (x - x0) / (double)Math.Max(1, x1 - x0);
            }
        return pts[^1].Y;
    }

    private WriteableBitmap DrawScene()
    {
        var px = new byte[PxW * PxH * 4];
        Color skyTop = Pal("#162224"), skyLow = Pal("#24393B"), glow = Pal("#5CB8A7");
        Color farTop = Pal("#3A5A56"), farLow = Pal("#1E2C2E"), farRidge = Pal("#4E7570");
        Color nearC = Pal("#141E20"), nearRidge = Pal("#22312F");
        Color waterTop = Pal("#1C2D2E"), waterLow = Pal("#101719");
        Color moon = Pal("#E8F4F0"), moonShade = Pal("#B9D0CA"), moonDark = Pal("#93AFA8");
        Color star = Pal("#CFE2DD"), starDim = Pal("#6F8D88");

        // небо полосами + ровное сияние вокруг луны: два чистых круга с чёткой границей, отцентрованные по луне.
        // Круги, а не кольца с дизерингом: у дизеринга края «в клеточку», и сияние выглядит кривым пятном.
        for (int y = 0; y < Horizon; y++)
            for (int x = 0; x < PxW; x++)
            {
                var c = Bands(skyTop, skyLow, x, y, 0, Horizon - 1);
                double dx = x - MoonX + 0.5, dy = y - MoonY + 0.5;   // та же точка центра, что у самой луны
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d <= 12.5) c = Mix(c, glow, 0.16);
                else if (d <= 16.5) c = Mix(c, glow, 0.07);
                Put(px, x, y, c);
            }

        // звёзды: всегда в одних и тех же местах, подальше от луны
        var rnd = new Random(1987);
        for (int i = 0; i < 46; i++)
        {
            int x = rnd.Next(PxW), y = rnd.Next(2, Horizon - 12);
            if (Math.Abs(x - MoonX) < 18 && Math.Abs(y - MoonY) < 19) continue;
            bool bright = rnd.Next(5) == 0;
            Put(px, x, y, bright ? star : starDim);
            if (bright && rnd.Next(2) == 0) { Put(px, x - 1, y, starDim); Put(px, x + 1, y, starDim); Put(px, x, y - 1, starDim); Put(px, x, y + 1, starDim); }
        }

        // луна: серп — круг без смещённого круга; тень у внутреннего края и пара «кратеров»
        for (int y = MoonY - 10; y <= MoonY + 10; y++)
            for (int x = MoonX - 10; x <= MoonX + 10; x++)
            {
                double dx = x - MoonX + 0.5, dy = y - MoonY + 0.5;
                double r = Math.Sqrt(dx * dx + dy * dy);
                double cx = x - (MoonX + 4) + 0.5, cy = y - (MoonY - 3) + 0.5;
                double rc = Math.Sqrt(cx * cx + cy * cy);
                if (r > 9 || rc < 8) continue;
                var c = rc < 9.3 ? moonShade : moon;   // тень вдоль выреза
                if (r > 8.2 && dx < 0) c = moonShade;  // край диска
                Put(px, x, y, c);
            }
        foreach (var (x, y) in new[] { (MoonX - 6, MoonY + 1), (MoonX - 5, MoonY + 2), (MoonX - 3, MoonY + 6), (MoonX - 7, MoonY - 3) })
            Put(px, x, y, moonDark);

        // дальние горы: ступенчатые склоны, светлый гребень
        var far1 = new (int, int)[] { (0, 40), (11, 36), (19, 40), (31, 29), (42, 38), (50, 34), (62, 48) };
        var far2 = new (int, int)[] { (68, 48), (80, 38), (88, 41), (99, 30), (109, 37), (120, 31), (130, 36) };
        for (int x = 0; x < PxW; x++)
        {
            double h = x <= 62 ? MountainH(x, far1) : x >= 68 ? MountainH(x, far2) : Horizon;
            int top = (int)Math.Round(h);
            for (int y = top; y < Horizon; y++)
                Put(px, x, y, y == top ? farRidge : Bands(farTop, farLow, x, y, top, Horizon - 1));
        }
        // ближние горы — тёмный силуэт
        var near1 = new (int, int)[] { (0, 42), (8, 40), (18, 44), (28, 39), (42, 48) };
        var near2 = new (int, int)[] { (84, 48), (97, 41), (106, 44), (118, 38), (130, 42) };
        for (int x = 0; x < PxW; x++)
        {
            double h = x <= 42 ? MountainH(x, near1) : x >= 84 ? MountainH(x, near2) : Horizon;
            int top = (int)Math.Round(h);
            for (int y = top; y < Horizon; y++) Put(px, x, y, y == top ? nearRidge : nearC);
        }

        // вода — полосами; линия горизонта в цвет акцента
        for (int y = Horizon; y < PxH; y++)
            for (int x = 0; x < PxW; x++)
                Put(px, x, y, Bands(waterTop, waterLow, x, y, Horizon, PxH - 1));
        for (int x = 0; x < PxW; x++) Put(px, x, Horizon, Mix(waterTop, glow, 0.45));
        return ToBitmap(px);
    }

    // ---------------------------------------------------------------- кадры воды
    private WriteableBitmap DrawWater(int f)
    {
        var px = new byte[PxW * PxH * 4];
        Color refl = Pal("#D6ECE7"), ripple = Pal("#3E5654"), rippleHi = Pal("#557371"), glow = Pal("#5CB8A7");
        double ph = f * Math.PI * 2 / WaterFrames;

        // отражение луны: полоски через ряд, ширина «дышит» у каждой со своим сдвигом, ниже — бледнее и уже
        int row = 0;
        for (int y = Horizon + 2; y < PxH - 1; y += 2, row++)
        {
            double depth = (y - Horizon) / (double)(PxH - Horizon);
            double w = (9 - depth * 5) * (0.75 + 0.35 * Math.Sin(ph * (row % 2 == 0 ? 1 : 2) + row * 1.3));
            int half = Math.Max(1, (int)Math.Round(w / 2));
            int shift = (int)Math.Round(Math.Sin(ph + row * 0.9) * 1.2);   // полоски чуть «гуляют» вбок
            double a = 0.85 - depth * 0.7;
            for (int x = MoonX - half + shift; x <= MoonX + half + shift; x++)
            {
                bool tip = x == MoonX - half + shift || x == MoonX + half + shift;
                Put(px, x, y, refl, tip ? a * 0.5 : a);
            }
        }

        // рябь: короткие штрихи плывут — верхние медленнее и в одну сторону, нижние быстрее и в другую
        var rnd = new Random(42);
        for (int i = 0; i < 18; i++)
        {
            int y = Horizon + 3 + rnd.Next(PxH - Horizon - 4);
            int len = 3 + rnd.Next(7);
            int x0 = rnd.Next(PxW);
            // штрих покачивается туда-обратно (у дальних — меньше): круг кадров замыкается без скачка
            double amp = (y - Horizon) < 10 ? 2 : 4;
            int dir = i % 2 == 0 ? 1 : -1;
            int x = ((x0 + dir * (int)Math.Round(Math.Sin(ph + i * 0.7) * amp)) % PxW + PxW) % PxW;
            bool hi = (f + i) % 4 == 0;   // штрих иногда вспыхивает светлее
            for (int k = 0; k < len; k++)
            {
                int xx = (x + k) % PxW;
                if (Math.Abs(xx - MoonX) < 7) continue;   // не перекрываем отражение
                Put(px, xx, y, hi ? rippleHi : ripple, 0.9);
            }
        }

        // блики у горизонта: пара точек в цвет акцента мигает по очереди
        foreach (var (x, n) in new[] { (22, 0), (47, 5), (92, 10), (113, 3) })
            if ((f + n) % 8 < 3) Put(px, x, Horizon + 1, glow, 0.7);
        return ToBitmap(px);
    }
}
