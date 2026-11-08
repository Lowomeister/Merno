// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Merno.Core;

/// <summary>
/// Темы оформления. Базовая палитра — Themes/Dark.xaml (бирюзовая). Другие темы получаются перекраской её цветов:
/// нейтральные (фон, панели, линии, текст) берут оттенок темы, акцентные бирюзовые — цвет акцента темы.
/// Цвета состояний (зелёный «готово», жёлтый «совет», красный «ошибка») не меняются — их смысл важнее красоты.
/// Тема применяется при запуске, до создания окна; при смене на ходу палитра подменяется и окно пересоздаётся
/// в том же процессе (MainWindow.Look.cs: ApplyThemeLive) — без перезапуска программы.
/// </summary>
public sealed class Theme
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Hint { get; init; }
    /// <summary>Оттенок нейтральных цветов (0–360) или null — оставить как есть.</summary>
    public double? NeutralHue { get; init; }
    /// <summary>Насыщенность нейтральных цветов, множитель (0 — чистый серый).</summary>
    public double NeutralSat { get; init; } = 1;
    /// <summary>Яркость тёмных цветов (фон, панели), множитель. Меньше 1 — темнее.</summary>
    public double NeutralDark { get; init; } = 1;
    /// <summary>Оттенок акцента (0–360) или null — бирюзовый.</summary>
    public double? AccentHue { get; init; }
    public double AccentSat { get; init; } = 1;
    /// <summary>Добавка к яркости акцента (−0.2…+0.2).</summary>
    public double AccentLight { get; init; }

    public bool IsBase => Id == "teal";

    public static readonly Theme[] All =
    {
        new() { Id = "teal", Name = "Бирюзовая", Hint = "Основная тема: графит и бирюза" },
        new() { Id = "black", Name = "Чёрная", Hint = "Почти чёрный фон для OLED и тёмных комнат", NeutralSat = 0, NeutralDark = 0.55 },
        new() { Id = "graphite", Name = "Графит", Hint = "Без цвета: серый на сером", NeutralSat = 0, AccentSat = 0, AccentLight = 0.12 },
        new() { Id = "ocean", Name = "Синяя ночь", Hint = "Тёмно-синий фон, голубой акцент", NeutralHue = 215, NeutralSat = 1.25, AccentHue = 210, AccentSat = 1.1, AccentLight = 0.03 },
        new() { Id = "lavender", Name = "Лаванда", Hint = "Тёмно-фиолетовый фон, сиреневый акцент", NeutralHue = 258, NeutralSat = 1.1, AccentHue = 262, AccentSat = 0.95, AccentLight = 0.06 },
        new() { Id = "sakura", Name = "Сакура", Hint = "Тёплый тёмный фон, розовый акцент", NeutralHue = 335, NeutralSat = 0.9, AccentHue = 340, AccentSat = 1.0, AccentLight = 0.06 },
    };

    public static Theme Get(string id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];

    // цвета состояний — не трогаем
    static readonly HashSet<uint> Keep = new() { 0x7DC48E, 0xDDB866, 0xE58376 };

    /// <summary>Перекраска одного цвета палитры в эту тему.</summary>
    public Color Map(Color c)
    {
        if (IsBase || Keep.Contains((uint)(c.R << 16 | c.G << 8 | c.B))) return c;
        var (h, s, l) = ToHsl(c);
        bool accent = s >= 0.3 && h >= 150 && h <= 190;
        if (accent)
        {
            if (AccentHue.HasValue) h = AccentHue.Value;
            s = Math.Clamp(s * AccentSat, 0, 1);
            l = Math.Clamp(l + (l > 0.3 ? AccentLight : 0), 0, 1);
        }
        else if (s < 0.3)
        {
            if (NeutralHue.HasValue) h = NeutralHue.Value;
            s = Math.Clamp(s * NeutralSat, 0, 1);
            if (l < 0.45) l *= NeutralDark;
        }
        else return c;
        var r = FromHsl(h, s, l);
        return Color.FromArgb(c.A, r.R, r.G, r.B);
    }

    internal static readonly Regex Hex = new(@"#(?<a>[0-9A-Fa-f]{2})?(?<rgb>[0-9A-Fa-f]{6})(?![0-9A-Fa-f])", RegexOptions.Compiled);

    /// <summary>Перекрашивает все цвета #RRGGBB / #AARRGGBB в тексте XAML.</summary>
    public string MapXaml(string xaml) => Hex.Replace(xaml, m =>
    {
        byte a = m.Groups["a"].Success ? Convert.ToByte(m.Groups["a"].Value, 16) : (byte)255;
        uint rgb = Convert.ToUInt32(m.Groups["rgb"].Value, 16);
        var c = Map(Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        return m.Groups["a"].Success ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    });

    /// <summary>Подменяет палитру приложения на выбранную тему. Вызывать до создания главного окна.
    /// live — смена темы на ходу: базовую палитру тоже подставляем заново (при запуске она уже стоит).</summary>
    public static bool Apply(Application app, string id, bool live = false)
    {
        var t = Get(id);
        if (t.IsBase && !live) return true;
        try
        {
            using var st = typeof(Theme).Assembly.GetManifestResourceStream("theme.xaml");
            if (st == null) return false;
            using var rd = new StreamReader(st);
            var dict = (ResourceDictionary)XamlReader.Parse(t.MapXaml(rd.ReadToEnd()));
            var merged = app.Resources.MergedDictionaries;
            if (merged.Count > 0) merged[0] = dict; else merged.Add(dict);
            return true;
        }
        catch (Exception ex) { Log.Warn("Тема оформления не применилась: " + ex.Message); return false; }
    }

    /// <summary>Значок в цветах темы: каждый пиксель — через Map, как пиксельный баннер (значок нарисован его палитрой);
    /// луна и блики (светлее 80%) остаются белыми.</summary>
    public BitmapSource Logo(BitmapSource src)
    {
        if (IsBase || src == null) return src;
        var b = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = b.PixelWidth, h = b.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        b.CopyPixels(px, stride, 0);
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] == 0) continue;
            var src0 = Color.FromRgb(px[i + 2], px[i + 1], px[i]);
            if (ToHsl(src0).L > 0.8) continue;   // луна и блики остаются светлыми
            var c = Map(src0);
            px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R;
        }
        var res = BitmapSource.Create(w, h, b.DpiX, b.DpiY, PixelFormats.Bgra32, null, px, stride);
        res.Freeze();
        return res;
    }

    // ---------- HSL ----------
    static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, h = 0, s = 0;
        if (max - min > 1e-9)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    static Color FromHsl(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double F(double t)
        {
            t = (t % 1 + 1) % 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        double hh = h / 360;
        byte B(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return s < 1e-9 ? Color.FromRgb(B(l), B(l), B(l)) : Color.FromRgb(B(F(hh + 1.0 / 3)), B(F(hh)), B(F(hh - 1.0 / 3)));
    }
}
