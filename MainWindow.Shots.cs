// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Merno.Core;

namespace Merno;

// Служебный режим для проверки внешнего вида: Merno.exe --shot <папка> [страницы через запятую]
// Окно ставится за пределы экрана, каждая страница рисуется при нескольких ширинах и сохраняется в PNG, затем программа закрывается.
// Ничего в Windows не меняет: только открывает страницы.
public partial class MainWindow
{
    internal static string ShotDir, ShotPages;
    private static bool ShotScaling;

    /// <summary>Замер скорости раскладки (--shot &lt;папка&gt; perf:PageA,PageB): для каждой страницы 10 раз меняем ширину
    /// (как при растягивании окна) и 10 раз сворачиваем/разворачиваем меню; пишем миллисекунды в perf.txt.</summary>
    private async Task RunPerfAsync(string[] pages)
    {
        var sb = new System.Text.StringBuilder();
        var root = (FrameworkElement)Content;
        foreach (var p in pages)
        {
            ShowPageByName(p);
            await Task.Delay(p == "PageHome" ? 1500 : 7000);
            double Lay(double w)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                root.Measure(new Size(w, 900)); root.Arrange(new Rect(0, 0, w, 900)); root.UpdateLayout();
                return sw.Elapsed.TotalMilliseconds;
            }
            Lay(1366); Lay(1300);   // прогрев
            var widths = new List<double>();
            for (int i = 0; i < 10; i++) widths.Add(Lay(i % 2 == 0 ? 1366 : 1300 - i * 7));
            var toggles = new List<double>(); var applyOnly = new List<double>();
            for (int i = 0; i < 10; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                ApplySide(i % 2 == 1, false);
                applyOnly.Add(sw.Elapsed.TotalMilliseconds);
                root.Measure(new Size(1366, 900)); root.Arrange(new Rect(0, 0, 1366, 900)); root.UpdateLayout();
                toggles.Add(sw.Elapsed.TotalMilliseconds);
            }
            sb.Append($"[само переключение {applyOnly.Average():0.0} мс] ");
            ApplySide(true, false);
            int rows = CountDescendants<ExpandText>(root);
            sb.AppendLine($"{p}: ширина ср {widths.Average():0.0} мс (макс {widths.Max():0.0}), меню ср {toggles.Average():0.0} мс (макс {toggles.Max():0.0}), ExpandText: {rows}");
        }
        File.WriteAllText(Path.Combine(ShotDir, "perf.txt"), sb.ToString());
    }

    private static int CountDescendants<T>(DependencyObject d)
    {
        int n = d is T ? 1 : 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) n += CountDescendants<T>(VisualTreeHelper.GetChild(d, i));
        return n;
    }

    private async Task RunShotsAsync()
    {
        try
        {
            Directory.CreateDirectory(ShotDir);
            _settings.Advanced = true;   // только в памяти: чтобы открылись «Настройки Windows» и «Контекстное меню»
            Left = -20000; Top = 0; WindowState = WindowState.Normal;
            // проверка связи с GitHub Releases (--shot <папка> updcheck): пишет итог в updcheck.txt, ничего не ставит
            if (string.Equals(ShotPages, "updcheck", StringComparison.OrdinalIgnoreCase))
            {
                var r = await Updater.CheckAsync();
                File.WriteAllText(Path.Combine(ShotDir, "updcheck.txt"), $"{r.Message}\n{r.Info?.Version} {r.Info?.ExeUri}");
                return;
            }
            if ((ShotPages ?? "").StartsWith("scale:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = ShotPages.Split(':', 3);
                if (parts.Length != 3 || !int.TryParse(parts[1], out int scale) || scale < 50 || scale > 200) throw new ArgumentException("Неверный масштаб снимка.");
                _settings.UiScale = scale;
                ShotScaling = true;
                ApplyUiScale(resize: false);
                ShotPages = parts[2];
            }
            bool perf = (ShotPages ?? "").StartsWith("perf:", StringComparison.OrdinalIgnoreCase);
            var pages = (perf ? ShotPages[5..] : ShotPages ?? "PageHome,PageApps,PageRuntime").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // ждём, пока соберутся сведения и списки
            for (int i = 0; i < 120 && !_ready; i++) await Task.Delay(500);
            await Task.Delay(1500);
            if (perf) { await RunPerfAsync(pages); return; }
            foreach (var (w, h) in new[] { (860, 600), (1100, 700), (1366, 768), (1920, 1080), (2560, 1440) })
            {
                Width = w; Height = h;
                foreach (var p in pages)
                {
                    ShowPageByName(p);
                    if (p == "PageApps" && !_appsBuilt) { }
                    await Task.Delay(p == "PageHome" ? 800 : 6000);   // списки программ догружаются в фоне
                    // для разбора: Windows может не дать окну за экраном нужную ширину, тогда меню свернётся само
                    File.AppendAllText(Path.Combine(ShotDir, "shots.txt"), $"{p}-{w}: окно {ActualWidth:0}×{ActualHeight:0}, меню {(_sideShown ? "раскрыто" : "свёрнуто")} ({NavFitState()}), масштаб {_uiScale:0.00}\n");
                    // раскладываем содержимое ровно под нужный размер — окно может не дать стать шире экрана
                    var root = (FrameworkElement)Content;
                    root.Measure(new Size(w, h));
                    root.Arrange(new Rect(0, 0, w, h));
                    root.UpdateLayout();
                    await Task.Delay(300);
                    root.Measure(new Size(w, h));
                    root.Arrange(new Rect(0, 0, w, h));
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(Background is SolidColorBrush sb && sb.Color.A > 0 ? sb : B("Bg"), null, new Rect(0, 0, w, h));
                        dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
                    }
                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using var fs = File.Create(Path.Combine(ShotDir, $"{p}-{w}.png"));
                    enc.Save(fs);
                }
            }
        }
        catch (Exception ex) { try { File.WriteAllText(Path.Combine(ShotDir ?? ".", "shot-error.txt"), ex.ToString()); } catch { } }
        finally { App.ForceExit = true; Application.Current.Shutdown(); }
    }
}
