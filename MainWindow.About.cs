// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Windows;
using System.Windows.Controls;
using Merno.Core;

namespace Merno;

// «Настройки» → вкладки «Настройки | Внешний вид | Связаться с нами» (Telegram, Core/Feedback.cs)
public partial class MainWindow
{
    private void InitAbout()
    {
        // вкладки «Настройки | Внешний вид | Связаться с нами» (авторы и лицензия — в карточке «Версия»)
        foreach (var (rb, panel) in new (RadioButton, FrameworkElement)[] { (SetTabMain, SetPanelMain), (SetTabLook, SetPanelLook), (SetTabContact, SetPanelContact) })
        {
            var p = panel;
            rb.Checked += (_, _) =>
            {
                foreach (var x in new FrameworkElement[] { SetPanelMain, SetPanelLook, SetPanelContact })
                    x.Visibility = x == p ? Visibility.Visible : Visibility.Collapsed;
                // карточки «Версия» и «Перенос» справа — только у вкладки «Настройки»
                SetShell.SideHidden = p != SetPanelMain;
                // шапка страницы: заголовок и одна строка о вкладке
                (SetTitle.Text, SetSubtitle.Text) = p == SetPanelLook ? ("Внешний вид", "Цвета программы и значка.")
                    : p == SetPanelContact ? ("Связаться с нами", "Нашли ошибку или есть идея? Напишите нам.")
                    : ("Настройки", "Как выглядит и работает Merno.");
                PageSettings.ScrollToTop();
                // подсветка в меню: «Связаться с нами» — своим пунктом, остальные вкладки — «Настройки»
                if (PageSettings.Visibility == Visibility.Visible)
                {
                    var nav = p == SetPanelContact ? NavContact : NavSettings;
                    if (nav.IsChecked != true) { _navSync = true; try { nav.IsChecked = true; } finally { _navSync = false; } }
                }
            };
        }

        BtnLicense.Click += (_, _) => Sh.Open("https://www.gnu.org/licenses/gpl-3.0.html");
        BtnFbTelegram.Click += (_, _) => OpenTelegram();
        BtnFbCopy.Click += (_, _) =>
        {
            if (TryCopy(Feedback.CollectInfo(_ctx.S?.OneLine()))) FbSay("Скопировано. Вставьте сведения в сообщение в Telegram.", "Ok");
            else FbSay("Не получилось скопировать. Попробуйте ещё раз.", "Warn");
        };

        // отчёт о сбое прошлого запуска — когда окно уже на экране; если открывается «Что нового», то после него
        Loaded += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(OfferCrashReport));
    }

    private void FbSay(string text, string brush) { FbStatus.Text = text; FbStatus.Foreground = B(brush); }

    private static void OpenTelegram() => Sh.Open(Feedback.TelegramUrl);

    /// <summary>Буфер обмена может быть занят другой программой (COMException) — пробуем ещё пару раз.</summary>
    private static bool TryCopy(string text)
    {
        for (int i = 0; i < 3; i++)
        {
            try { Clipboard.SetText(text); return true; }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException)
            {
                if (i == 2) Log.Warn("Буфер обмена: " + ex.Message);
                else Thread.Sleep(60);
            }
        }
        return false;
    }

    /// <summary>
    /// Прошлый запуск закончился сбоем — предлагаем скопировать отчёт (без журнала и личного) и написать в Telegram.
    /// Сам Merno ничего не отправляет. Одну и ту же ошибку предлагаем не чаще раза в сутки (Core/CrashReport.cs).
    /// </summary>
    private void OfferCrashReport()
    {
        try
        {
            if (_wnOpen || _wnPending) { _crashOfferDeferred = true; return; }   // не поверх «Что нового»
            string report = CrashReport.TakePending();
            if (report == null) return;
            if (!Ask("В прошлый раз Merno закрылся с ошибкой. Отчёт о ней без имён и путей можно скопировать и отправить нам в Telegram. Так мы быстрее её исправим.",
                     "Merno закрылся с ошибкой", yes: "Скопировать и написать", no: "Не сейчас")) return;
            if (TryCopy(report)) SetStatus("Отчёт скопирован. Вставьте его в сообщение в Telegram.");
            // ошибка есть и в errors.log — её соберёт «Скопировать сведения»
            else Warn("Не получилось скопировать отчёт. Откройте «Связаться с нами» и нажмите «Скопировать сведения».");
            OpenTelegram();
        }
        catch (Exception ex) { Log.Warn("Отчёт о сбое: " + ex.Message); }
    }
}
