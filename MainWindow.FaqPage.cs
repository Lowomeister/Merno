// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Merno.Core;

namespace Merno;

// «Частые вопросы» на Главной.
public partial class MainWindow
{
    // ---------- частые вопросы на главной ----------
    private static readonly (string Q, string A)[] Faq =
    {
        ("С чего начать после переустановки Windows?", "На «Главной» в карточке «Настройка Windows» пройдите шаги: режим, программы, внешний вид, драйверы. Потом перезагрузите компьютер и поставьте обновления Windows."),
        ("Что изменит «Настройка Windows»?", "Только то, что вы отметили на шагах. Пока не нажмёте «Начать», ничего не меняется. Ваши файлы и игры Merno не трогает."),
        ("Какой режим выбрать?", "«Обычный» подходит почти всем. «Игровой» подойдёт, если много играете. «Бережный» меняет меньше всего. Если не уверены, оставьте «Обычный»."),
        ("Как вернуть всё как было?", "«Настройки» → «Откат изменений»: выберите запись и нажмите «Вернуть как было», затем перезагрузите компьютер."),
        ("Почему галочка не срабатывает сразу?", "В «Автозагрузке», «Настройках Windows» и «Контекстном меню» галочка показывает, что включено сейчас. Поменяйте нужные и нажмите «Применить». Число в скобках показывает количество изменений. «Отменить изменения» вернёт галочки как были."),
        ("Как убрать программу из автозагрузки?", "В «Автозагрузке» снимите галочку и нажмите «Применить». Программа останется на компьютере, но перестанет запускаться вместе с Windows. Если сомневаетесь, оставьте как есть."),
        ("Как почистить компьютер от мусора?", "Откройте «Очистку»: Merno сам посчитает, сколько места освободится. Отметьте нужное и нажмите «Очистить отмеченное». Личные файлы не удаляются."),
        ("Как установить, обновить или удалить программы?", "В «Программах» отметьте нужные и нажмите «Установить». Там же можно обновить установленные программы и удалить ненужные."),
        ("Игра не запускается и пишет про «.dll»", "Откройте «Компоненты» и поставьте то, чего не хватает. Обычно это Visual C++, DirectX или .NET."),
        ("Как обновить драйверы?", "В «Драйверах» Merno покажет вашу видеокарту, звук и другие устройства. «Скачать» откроет страницу загрузки у производителя."),
        ("Как поставить обновления Windows?", "Обновления системы и драйверов находятся в разделе «Обновления Windows». Merno ставит их в фоне и показывает, что сейчас делается."),
        ("Безопасно ли это для игр и защиты от читов?", "Да, безопасно. Merno не меняет игры и защиту от читов (FACEIT, Vanguard, EAC). Такие программы в «Автозагрузке» лучше не выключать."),
        ("Станет ли больше кадров в играх?", "Игры обычно идут ровнее, но чудес не будет: многое зависит от видеокарты, процессора и настроек самой игры."),
        ("Где «Настройки Windows» и «Контекстное меню»?", "Это разделы для опытных. Поставьте галочку «Расширенный режим» в «Настройках», чтобы они появились в меню. Обычно хватает карточки «Настройка Windows» на «Главной»."),
        ("Как перенести настройки на другой компьютер?", "В «Настройках» справа есть карточка «Перенос». Сохраните файл на одном компьютере и загрузите его на другом."),
        ("Как обновить сам Merno?", "В «Настройках» справа, в карточке «Версия», нажмите «Проверить обновления». Без вашего согласия ничего не ставится."),
        ("Почему Merno не закрывается крестиком?", "Крестик прячет Merno в область уведомлений рядом с часами. Чтобы закрыть совсем, щёлкните по значку Merno там правой кнопкой и выберите «Выход»."),
        ("Зачем Merno права администратора?", "Без них Windows не даёт менять настройки, чистить системные папки и ставить драйверы."),
        ("Нужен ли отдельный антивирус?", "Обычно нет: в Windows уже есть бесплатная «Безопасность Windows»."),
    };

    // Ссылка в каждом ответе — прямо на нужную функцию (по порядку вопросов в Faq; null — ссылка не нужна).
    // Page — как в ShowPageByName, Target — x:Name элемента, который подсветить. Особые: "tour" — обучение, "uri:…" — открыть адрес.
    private static readonly (string Text, string Page, string Target)[] FaqLinks =
    {
        ("Открыть «Настройку Windows»", "PageHome", "HomeSetup"),
        ("Открыть «Настройку Windows»", "PageHome", "HomeSetup"),
        ("Открыть «Настройку Windows»", "PageHome", "HomeSetup"),
        ("Открыть «Откат изменений»", "PageRollback", "BakList"),
        ("Открыть «Автозагрузку»", "PageStartup", "StartList"),
        ("Открыть «Автозагрузку»", "PageStartup", "StartList"),
        ("Открыть «Очистку»", "PageClean", "CleanList"),
        ("Открыть «Программы»", "PageApps", "AppsList"),
        ("Открыть «Компоненты»", "PageRuntime", null),
        ("Открыть «Драйверы»", "PageDrivers", "DrvCardGpu"),
        ("Открыть «Обновления Windows»", "Windows", null),
        (null, null, null),
        (null, null, null),
        ("Включить «Расширенный режим»", "PageSettings", "SetAdvanced"),
        ("Открыть «Настройки»", "PageSettings", "SetPanelMain"),
        ("Открыть «Настройки»", "PageSettings", "SetAutoUpdate"),
        (null, null, null),
        (null, null, null),
        ("Открыть «Безопасность Windows»", "uri:windowsdefender:", null),
    };

    private void OpenFeature(string page, string target)
    {
        if (page == "tour") { StartTour(); return; }
        if (page.StartsWith("uri:")) { Sh.Open(page[4..]); return; }
        try { ShowPageByName(page); } catch (Exception ex) { Log.Warn("Переход: " + ex.Message); return; }
        if (string.IsNullOrEmpty(target)) return;
        string hint = target switch
        {
            "HomeSetup" => "Пройдите шаги по порядку и нажмите «Начать». Лишний шаг можно пропустить.",
            "BakList" => "Выберите запись и нажмите «Вернуть как было».",
            "BtnSysRestore" => "В открывшемся окне восстановления Windows выберите точку, созданную до появления проблемы.",
            "StartList" => "Снимите галочки у ненужных программ и нажмите «Применить». Эти программы перестанут запускаться вместе с Windows.",
            "SetPanelMain" => "Карточка «Перенос» находится справа.",
            "SetAutoUpdate" => "Включите проверку обновлений. Для ручной проверки нажмите «Проверить обновления» справа.",
            "DrvCardGpu" => "В этой карточке находятся драйверы для вашей видеокарты. «Скачать» откроет страницу загрузки на сайте производителя.",
            "CleanList" => "Отметьте мусор и нажмите «Очистить отмеченное» справа.",
            "SetAdvanced" => "Чтобы открыть ручные настройки, поставьте галочку и подтвердите выбор.",
            "AppsList" => "Выберите программу и нажмите «Установить». Галочки слева позволяют поставить или удалить несколько сразу.",
            _ => null
        };
        // некоторые разделы строятся в фоне (драйверы, программы) — ждём нужный элемент до 10 секунд,
        // иначе подсказка «где исправить» молча не появится
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        int tries = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                if (FindName(target) is FrameworkElement el && el.IsVisible)
                {
                    timer.Stop();
                    el.BringIntoView();
                    Highlight(el, hint);
                }
                else if (++tries >= 33) timer.Stop();
            }
            catch { timer.Stop(); }
        };
        timer.Start();
    }

    /// <summary>Начало ответа для свёрнутой плашки: первое предложение, не длиннее ~110 знаков (по границе слова).
    /// Заголовок Expander меряется без ограничения ширины, поэтому обрезку «…» делаем сами.</summary>
    private static string FaqPreview(string a)
    {
        var s = (a ?? "").Replace('\n', ' ').Trim();
        int dot = s.IndexOfAny(new[] { '.', '!', '?' });
        if (dot > 20 && dot < 110) return s[..(dot + 1)];
        if (s.Length <= 110) return s;
        int cut = s.LastIndexOf(' ', 105);
        return s[..(cut > 40 ? cut : 105)].TrimEnd(',', ';', ':', '—', ' ') + "…";
    }

    /// <summary>Частые вопросы — страница «Частые вопросы» (PageHelp), над подробной инструкцией.</summary>
    private bool _faqWired;
    private void BuildFaq()
    {
        FaqBox.Children.Clear();
        if (!_faqWired) { _faqWired = true; FaqFilter.TextChanged += (_, _) => ApplyFilter(FaqFilter, FaqFilterHint, FaqBox); }
        for (int i = 0; i < Faq.Length; i++)
        {
            var (q, a) = Faq[i];
            var answer = new TextBlock { Style = (Style)FindResource("SubText"), Margin = new Thickness(18, 8, 0, 2), LineHeight = 21 };
            answer.Inlines.Add(new System.Windows.Documents.Run(Typo.Nbsp(a)));
            var link = i < FaqLinks.Length ? FaqLinks[i] : default;
            if (link.Text != null)
            {
                var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(link.Text + " →")) { Foreground = B("Accent"), Cursor = System.Windows.Input.Cursors.Hand };
                var (pg, tg) = (link.Page, link.Target);
                h.Click += (_, _) => OpenFeature(pg, tg);
                answer.Inlines.Add(new System.Windows.Documents.LineBreak());
                answer.Inlines.Add(h);
            }
            // в свёрнутом виде под вопросом — начало ответа одной бледной строкой: плашка не пустая и сразу видно, о чём ответ
            var head = new StackPanel();
            head.Children.Add(new TextBlock { Text = q, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = B("Text"), TextWrapping = TextWrapping.Wrap });
            var preview = new TextBlock { Text = Typo.Nbsp(FaqPreview(a)), FontSize = 12.5, Foreground = B("Sub"), Opacity = 0.8,
                                          TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            head.Children.Add(preview);
            var exp = new Expander { Header = head, Content = answer, VerticalAlignment = VerticalAlignment.Center };
            exp.Expanded += (_, _) => preview.Visibility = Visibility.Collapsed;
            exp.Collapsed += (_, _) => preview.Visibility = Visibility.Visible;
            // раскрывается нажатием на всю плашку, а не только на текст вопроса; высота — по содержимому, без пустоты снизу
            var row = new Border { Style = (Style)FindResource("Plate"), Child = exp, Cursor = System.Windows.Input.Cursors.Hand, MinHeight = 0 };
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is System.Windows.ContentElement) return;   // ссылка в ответе
                for (var d = e.OriginalSource as DependencyObject; d != null && d != row; d = VisualTreeHelper.GetParent(d))
                    if (d is System.Windows.Controls.Primitives.ToggleButton || d == answer) return;   // заголовок раскрывает сам; текст ответа — не сворачивать
                exp.IsExpanded = !exp.IsExpanded;
            };
            row.Tag = q + " " + a;   // по этому тексту ищет поле поиска
            FaqBox.Children.Add(row);
        }
    }

}
