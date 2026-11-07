// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Merno.Core;

namespace Merno;

// Окно «Что нового»: список изменений по версиям со ссылками на разделы
public partial class MainWindow
{
    /// Since — номер доработки (например, 1.0.0.1), с которой пункт появился: только такие пункты открывают окно после доработки.
    private sealed class WnItem { public string Text; public string Page; public string Target; public bool Removed; public string Since; }
    private sealed class WnVersion { public string Version; public string Date; public List<WnItem> Items = new(); }

    private static string WnSeenFile => System.IO.Path.Combine(Log.DataDir, "whatsnew-seen.txt");

    private void InitWhatsNew()
    {
        BtnWhatsNew.Click += (_, _) => ShowWhatsNew();
        string seen = "";
        try { if (File.Exists(WnSeenFile)) seen = File.ReadAllText(WnSeenFile).Trim(); } catch { }
        if (seen == App.Version) return;
        // доработка той же версии (1.0.0 → 1.0.0.2) — окно не открываем, если в ней нет пункта, помеченного "since" новее виденного
        if (System.Version.TryParse(seen, out var sv) && System.Version.TryParse(App.Version, out var cv)
            && sv.Major == cv.Major && sv.Minor == cv.Minor && sv.Build == cv.Build
            && !LoadWhatsNew().SelectMany(x => x.Items).Any(it => System.Version.TryParse(it.Since, out var iv) && iv > sv))
        {
            try { File.WriteAllText(WnSeenFile, App.Version); } catch { }
            return;
        }
        _wnPending = true;
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            _wnPending = false;
            try { File.WriteAllText(WnSeenFile, App.Version); } catch { }
            ShowWhatsNew();
        }));
    }

    private static List<WnVersion> LoadWhatsNew()
    {
        var res = new List<WnVersion>();
        try
        {
            using var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("whatsnew.json");
            if (st == null) return res;
            using var doc = JsonDocument.Parse(st);
            foreach (var v in doc.RootElement.EnumerateArray())
            {
                var ver = new WnVersion
                {
                    Version = v.TryGetProperty("version", out var a) ? a.GetString() : "",
                    Date = v.TryGetProperty("date", out var d) ? d.GetString() : ""
                };
                if (v.TryGetProperty("items", out var its))
                    foreach (var it in its.EnumerateArray())
                        ver.Items.Add(new WnItem
                        {
                            Text = it.TryGetProperty("text", out var t) ? t.GetString() : "",
                            Page = it.TryGetProperty("page", out var p) ? p.GetString() : null,
                            Target = it.TryGetProperty("target", out var g) ? g.GetString() : null,
                            Removed = it.TryGetProperty("removed", out var r) && r.ValueKind == JsonValueKind.True,
                            Since = it.TryGetProperty("since", out var sn) ? sn.GetString() : null
                        });
                res.Add(ver);
            }
        }
        catch (Exception ex) { Log.Warn("Что нового: " + ex.Message); }
        return res;
    }

    private void ShowWhatsNew()
    {
        var versions = LoadWhatsNew();
        var win = new Window
        {
            Title = "Что нового", WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 640, Height = 600, MinWidth = 420, MinHeight = 300, ShowInTaskbar = false,
            Background = B("Bg"), Foreground = B("Text"), FontFamily = FontFamily, FontSize = 14
        };
        if (IsLoaded && IsVisible) win.Owner = this; else win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        win.SourceInitialized += (_, _) => DarkTitleBar(win);
        win.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; win.Close(); } };

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(new TextBlock { Text = "Что нового", Style = (Style)FindResource("H2"), FontSize = 22, Margin = new Thickness(0, 0, 0, 12) });

        bool first = true;
        foreach (var v in versions)
        {
            var list = new StackPanel { Margin = new Thickness(4, 6, 0, 10) };
            foreach (var it in v.Items) list.Children.Add(WnRow(win, it));
            // «next» — черновик до выпуска (номер ставится при выпуске)
            string head = v.Version == "next" ? "Готовится к выпуску" : "Версия " + v.Version;
            var exp = new Expander
            {
                Header = new TextBlock { Text = head, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = first ? B("Accent") : B("Text") },
                Content = list, IsExpanded = first, Margin = new Thickness(0, 0, 0, 6)
            };
            root.Children.Add(exp);
            first = false;
        }

        win.Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root
        };
        _wnOpen = true;
        try { win.ShowDialog(); }
        catch (Exception ex) { Log.Warn("Что нового: " + ex.Message); }
        finally { _wnOpen = false; }
        // предложение отчёта о сбое ждало, пока окно закроют
        if (_crashOfferDeferred) { _crashOfferDeferred = false; OfferCrashReport(); }
    }

    /// <summary>Открыто окно «Что нового» (ShowDialog крутит свой цикл — поверх него другие окна не показываем).</summary>
    private bool _wnOpen;
    /// <summary>«Что нового» откроется само после запуска, но ещё не открылось.</summary>
    private bool _wnPending;
    /// <summary>Предложение отчёта о сбое отложено до закрытия «Что нового».</summary>
    private bool _crashOfferDeferred;

    private UIElement WnRow(Window win, WnItem it)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = "•", Foreground = it.Removed ? B("Sub") : B("Accent") });

        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
        Grid.SetColumn(tb, 1);
        if (it.Removed)
        {
            tb.Text = it.Text + " (убрано)";
            tb.Foreground = B("Sub");
            tb.TextDecorations = TextDecorations.Strikethrough;
        }
        else if (!string.IsNullOrEmpty(it.Page))
        {
            var run = new Run(it.Text);
            var link = new Hyperlink(run)
            {
                Foreground = B("Accent"), TextDecorations = null, Cursor = Cursors.Hand
            };
            link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (_, _) => link.TextDecorations = null;
            link.Click += (_, _) => WnGo(win, it);
            tb.Inlines.Add(link);
        }
        else
        {
            tb.Text = it.Text;
            tb.Foreground = B("Text");
        }
        row.Children.Add(tb);
        return row;
    }

    private void WnGo(Window win, WnItem it)
    {
        win.Close();
        if (it.Page == "tour") { StartTour(); return; }   // «пройти обучение заново»
        try { ShowPageByName(it.Page); } catch (Exception ex) { Log.Warn("Что нового: " + ex.Message); return; }
        if (string.IsNullOrEmpty(it.Target)) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try { if (FindName(it.Target) is FrameworkElement el) Highlight(el); } catch { }
        };
        timer.Start();
    }
}
