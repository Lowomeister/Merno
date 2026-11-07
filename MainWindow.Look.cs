// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Merno.Core;

namespace Merno;

/// <summary>
/// «Настройки → Внешний вид»: выбор темы оформления. Тема перекрашивает палитру и значок (Core/Theme.cs)
/// и включается сразу: окно пересоздаётся в том же процессе (ApplyThemeLive). Перезапуск — только запасной путь.
/// </summary>
public partial class MainWindow
{
    private Theme ActiveTheme => Theme.Get(_activeThemeId ??= AppSettings.Load().Theme);
    private string _activeThemeId;

    /// <summary>Значки на выбор (Настройки → Внешний вид): id, название, файл. Первый — базовый, он же у exe.</summary>
    private static readonly (string Id, string Name, string File)[] Icons =
    {
        ("smooth", "Плавный", "logo.png"),
        ("pixel", "Пиксельный", "logo-pixel.png"),
    };

    /// <summary>Выбранный значок; неизвестное значение — базовый.</summary>
    /// Значок рисуется в конструкторе окна раньше, чем загружены настройки (там пока new AppSettings() со значением по умолчанию):
    /// без этой проверки после смены темы новое окно брало плавный значок, хотя выбран пиксельный.
    private string LogoId
    {
        get
        {
            string id = _settingsLoaded ? _settings.Logo : AppSettings.Load().Logo;
            return Icons.Any(i => i.Id == id) ? id : Icons[0].Id;
        }
    }
    private bool _settingsLoaded;

    private static BitmapSource LoadLogo(string file)
    {
        try { var b = new BitmapImage(new Uri("pack://application:,,,/Assets/" + file)); b.Freeze(); return b; }
        catch { return null; }
    }

    /// <summary>Выбранный значок до перекраски под тему.</summary>
    private BitmapSource BaseLogo() => LoadLogo(Icons.First(i => i.Id == LogoId).File);

    /// <summary>Значок нужно рисовать самим: тема не базовая или выбран не тот значок, что у exe.</summary>
    private bool CustomLogo => !ActiveTheme.IsBase || LogoId != Icons[0].Id;

    /// <summary>Выбранный значок в цветах темы.</summary>
    private BitmapSource CurrentLogo()
    {
        var b = BaseLogo();
        return ActiveTheme.IsBase ? b : ActiveTheme.Logo(b);
    }

    /// <summary>Значок в цветах темы: в меню, в «Настройках» и на панели задач.</summary>
    private void ApplyThemeLogo()
    {
        SetLogos();
        BtnLookRestart.Click += (_, _) => RestartMerno();
    }

    private void SetLogos()
    {
        var logo = CustomLogo ? CurrentLogo() : LoadLogo("logo.png");
        if (logo == null) return;
        SideLogo.Source = logo;
        if (CustomLogo) Icon = logo; else Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
    }

    /// <summary>Сменить значок сразу, без перезапуска: меню, «Настройки», панель задач и трей.</summary>
    private void ChooseIcon(string id)
    {
        if (LogoId == id) { SetStatus("Этот значок уже выбран"); return; }
        _settings.Logo = id; _settings.Save();
        try { SetLogos(); } catch (Exception ex) { Log.Skip("Значок: " + ex.Message); }
        try { if (_tray != null) { var old = _tray.Icon; _tray.Icon = LoadTrayIcon(); old?.Dispose(); } } catch (Exception ex) { Log.Skip("Значок трея: " + ex.Message); }
        BuildLook();
        SetStatus($"Значок: {Icons.First(i => i.Id == id).Name.ToLowerInvariant()}");
    }

    /// <summary>Плитки выбора значка под темами.</summary>
    private void BuildIconChoice()
    {
        LookIcons.Children.Clear();
        string chosen = LogoId;
        foreach (var (id, name, file) in Icons)
        {
            var src = LoadLogo(file);
            if (src != null && !ActiveTheme.IsBase) src = ActiveTheme.Logo(src);
            var img = new Image { Source = src, Width = 48, Height = 48, Margin = new Thickness(0, 0, 0, 8) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var sp = new StackPanel();
            sp.Children.Add(img);
            var title = new DockPanel();
            if (id == chosen)
            {
                var mark = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), Foreground = B("Accent"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(mark, Dock.Right); title.Children.Add(mark);
            }
            title.Children.Add(new TextBlock { Text = name, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = B("Text") });
            sp.Children.Add(title);
            var tile = new Button { Style = (Style)FindResource("Tile"), Content = sp, Width = 140, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(12),
                                    VerticalContentAlignment = VerticalAlignment.Top, HorizontalContentAlignment = HorizontalAlignment.Left };
            if (id == chosen) tile.BorderBrush = B("Accent");
            var cid = id;
            tile.Click += (_, _) => ChooseIcon(cid);
            LookIcons.Children.Add(tile);
        }
    }

    /// <summary>Значок в трее в цветах темы (null — тема базовая, берём обычный app.ico).</summary>
    private System.Drawing.Icon ThemedTrayIcon()
    {
        if (!CustomLogo) return null;
        try
        {
            var src = CurrentLogo();
            if (src == null) return null;
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms); ms.Position = 0;
            using var bmp = new System.Drawing.Bitmap(ms);
            var size = System.Windows.Forms.SystemInformation.SmallIconSize;
            using var small = new System.Drawing.Bitmap(bmp, size);
            var h = small.GetHicon();
            try
            {
                using var tmp = System.Drawing.Icon.FromHandle(h);
                return (System.Drawing.Icon)tmp.Clone();   // у копии свой значок — исходный можно освободить
            }
            finally { DestroyIconLook(h); }   // иначе значок остаётся в системе до выхода из Merno
        }
        catch (Exception ex) { Log.Skip("Значок трея в цветах темы: " + ex.Message); return null; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "DestroyIcon")] private static extern bool DestroyIconLook(IntPtr h);

    private void BuildLook()
    {
        LookList.Children.Clear();
        var chosen = _settings?.Theme ?? ActiveTheme.Id;
        var baseLogo = BaseLogo();
        foreach (var t in Theme.All)
        {
            Brush P(string hex) { var c = (Color)ColorConverter.ConvertFromString(hex); return new SolidColorBrush(t.Map(c)); }
            // маленький макет окна в цветах темы
            var mock = new Border { Height = 92, CornerRadius = new CornerRadius(8), Background = P("#14191B"), ClipToBounds = true, BorderBrush = P("#2B3538"), BorderThickness = new Thickness(1) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var side = new Border { Background = P("#0F1314"), CornerRadius = new CornerRadius(8, 0, 0, 8) };
            var logo = new Image { Source = t.Logo(baseLogo), Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            side.Child = logo;
            var body = new StackPanel { Margin = new Thickness(10, 10, 10, 0) };
            body.Children.Add(new Border { Height = 7, Width = 70, CornerRadius = new CornerRadius(3), Background = P("#E7EDEB"), HorizontalAlignment = HorizontalAlignment.Left });
            body.Children.Add(new Border { Height = 5, Width = 96, CornerRadius = new CornerRadius(2.5), Background = P("#A8B5B2"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.8 });
            var card = new Border { Height = 30, CornerRadius = new CornerRadius(5), Background = P("#1A2124"), Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(6) };
            card.Child = new Border { Width = 38, Height = 14, CornerRadius = new CornerRadius(4), Background = P("#5CB8A7"), HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(card);
            Grid.SetColumn(body, 1);
            g.Children.Add(side); g.Children.Add(body);
            mock.Child = g;

            var sp = new StackPanel();
            sp.Children.Add(mock);
            var title = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (t.Id == chosen)
            {
                var mark = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Icons"), Foreground = B("Accent"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(mark, Dock.Right); title.Children.Add(mark);
            }
            title.Children.Add(new TextBlock { Text = t.Name, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = B("Text") });
            sp.Children.Add(title);
            sp.Children.Add(new TextBlock { Text = Typo.Nbsp(t.Hint), Style = (Style)FindResource("SubText"), FontSize = 12.5, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });

            var tile = new Button { Style = (Style)FindResource("Tile"), Content = sp, Width = 212, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(12),
                                    VerticalContentAlignment = VerticalAlignment.Top };
            if (t.Id == chosen) tile.BorderBrush = B("Accent");
            var id = t.Id;
            tile.Click += (_, _) => ChooseTheme(id);
            LookList.Children.Add(tile);
        }
        LookRestart.Visibility = chosen != ActiveTheme.Id ? Visibility.Visible : Visibility.Collapsed;
        BuildIconChoice();
    }

    private void ChooseTheme(string id)
    {
        if (id == ActiveTheme.Id) { SetStatus("Это текущая тема"); return; }
        if (_busy) { SetStatus("Подождите, Merno ещё занят. Сменить тему можно после завершения текущего действия"); return; }
        // смена темы пересоздаёт окно: новое не знало бы о фоновой установке и не показало бы её ход
        if (BlockedByWinInstall("Смените тему")) return;
        _settings.Theme = id;
        _settings.Save();
        if (!ApplyThemeLive(id))
        {
            // не вышло на ходу — остаётся старый способ: перезапуск
            BuildLook();
            SetStatus($"Тема «{Theme.Get(id).Name}» включится после перезапуска");
        }
    }

    /// <summary>Открыть «Настройки → Внешний вид» сразу после загрузки (новое окно после смены темы).</summary>
    internal bool OpenLookOnStart;
    /// <summary>Окно закрывается из-за смены темы — не спрашивать про трей и не завершать программу.</summary>
    private bool _themeSwap;

    /// <summary>Смена темы без перезапуска: подменяем палитру и пересоздаём окно в том же процессе.
    /// Все цвета в разметке подставляются один раз при создании окна, поэтому старое окно перекрасить нельзя — только создать новое.</summary>
    private bool ApplyThemeLive(string id)
    {
        if (ShotDir != null) return false;
        if (!Theme.Apply(Application.Current, id, live: true)) return false;
        try
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            var w = new MainWindow { OpenLookOnStart = true, WindowStartupLocation = WindowStartupLocation.Manual };
            if (!bounds.IsEmpty && bounds.Width > 100) { w.Left = bounds.Left; w.Top = bounds.Top; w.Width = bounds.Width; w.Height = bounds.Height; }
            w.WindowState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            Application.Current.MainWindow = w;   // ShutdownMode=OnMainWindowClose: закрытие старого окна не завершит программу
            w.Show();
            Log.Write($"Тема оформления: «{Theme.Get(id).Name}» (без перезапуска)");
            _themeSwap = true;
            Close();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Смена темы на ходу: " + ex.Message);
            Application.Current.MainWindow = this;
            return false;
        }
    }

    /// <summary>Перезапуск Merno: новая копия дождётся, пока закроется эта (Program.cs, --restart).</summary>
    private void RestartMerno()
    {
        if (_busy) { SetStatus("Подождите, Merno ещё занят"); return; }
        if (BlockedByWinInstall("Перезапустите Merno")) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = true, Verb = "runas" });
        }
        catch (Exception ex) { Log.Warn("Перезапуск: " + ex.Message); SetStatus("Не удалось перезапустить. Закройте и откройте Merno сами."); return; }
        App.ForceExit = true;
        Application.Current.Shutdown();
    }
}
