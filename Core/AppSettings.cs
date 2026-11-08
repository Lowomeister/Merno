// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.Json;

namespace Merno.Core;

/// <summary>Своя панель в «Быстром доступе»: название и что открыть (программа, файл, папка или адрес ms-settings:).</summary>
public class QuickCustom
{
    public string Name { get; set; }
    public string Target { get; set; }
}

/// <summary>Настройки самой программы (не компьютера).</summary>
public class AppSettings
{
    public bool Advanced { get; set; }
    public bool SideOpen { get; set; }
    public bool Onboarded { get; set; }
    /// <summary>Выбранная сортировка на страницах (номер варианта).</summary>
    public int SortUninstall { get; set; }
    public int SortStartup { get; set; }
    public int SortClean { get; set; }
    /// <summary>Проверять обновления при каждом запуске.</summary>
    public bool AutoUpdateCheck { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }
    /// <summary>Итог последней проверки — для строки в Настройках.</summary>
    public string LastUpdateResult { get; set; }
    /// <summary>Случайный номер установки из прежних версий. Не создаётся и не используется; оставлено, чтобы старый settings.json читался.</summary>
    public string InstallId { get; set; }
    /// <summary>Прежняя галочка «Отправлять отчёты об ошибках». Не используется: Merno сам ничего не отправляет, отчёт о сбое
    /// предлагается скопировать и написать в Telegram (Core/CrashReport.cs). Оставлено, чтобы старый settings.json читался.</summary>
    public bool CrashReports { get; set; }
    /// <summary>Что делать при закрытии окна: 0 — ещё не спрашивали, 1 — свернуть в трей, 2 — закрыть программу.</summary>
    public int CloseAction { get; set; }
    /// <summary>Размер интерфейса в процентах; 0 — «Авто» (по ширине монитора).</summary>
    public int UiScale { get; set; }
    /// <summary>Подсказку «Merno работает в трее» уже показывали.</summary>
    public bool TrayHintShown { get; set; }
    /// <summary>Запуск вместе с Windows (задание «Merno» в Планировщике).</summary>
    public bool Autostart { get; set; }
    /// <summary>«Быстрый доступ»: какие панели показывать (id из каталога). null — набор по умолчанию.</summary>
    public List<string> QuickPanels { get; set; }
    /// <summary>«Быстрый доступ»: свои панели пользователя.</summary>
    public List<QuickCustom> QuickCustom { get; set; } = new();
    /// <summary>Тема оформления (id из Theme.All).</summary>
    public string Theme { get; set; } = "teal";
    /// <summary>Значок в окне, на панели задач и в трее: "smooth" (базовый, он же у exe и ярлыка) или "pixel".</summary>
    public string Logo { get; set; } = "smooth";
    /// <summary>Пользователь нажал «Понятно» под итогами проверки — советы скрыты до следующей проверки.</summary>
    public bool CheckDismissed { get; set; }
    public List<string> StartupAdviceIgnored { get; set; } = new();
    public Dictionary<string, string> KeptDriverVersions { get; set; } = new();
    /// <summary>Мастер «Настройка» пройден (нажали «Начать»): «Главная» дальше открывается на «Проверке».</summary>
    public bool FirstRunDone { get; set; }
    /// <summary>Программы, которые winget «обновляет» без толку (ChatGPT и др.): Id → версия, на которой застряли.
    /// Пока версия та же — в «Обновлениях программ» они отдельно, без галочки.</summary>
    public Dictionary<string, string> UpdStuck { get; set; } = new();
    /// <summary>«Застрявшие» обновления, которые пользователь скрыл («✕»): Id → версия (To). Новая версия — снова показываем.</summary>
    public Dictionary<string, string> UpdStuckHidden { get; set; } = new();
    /// <summary>Ограничение скорости скачивания, Мбит/с; 0 — без ограничения (Core/NetLimit.cs).</summary>
    public int DownloadLimitMbps { get; set; }
    /// <summary>Способ скачивания winget «do» поставил Merno (для лимита) — при снятии лимита убрать.</summary>
    public bool WingetDoByMerno { get; set; }

    private static string FilePath => Path.Combine(Log.DataDir, "settings.json");

    private bool _saveBlocked;
    public static AppSettings Load() => LoadFrom(FilePath, Log.Warn, Log.Skip);
    /// <param name="warning">понятное сообщение (видно в строке состояния)</param>
    /// <param name="detail">подробности ошибки — только для журнала</param>
    internal static AppSettings LoadFrom(string path, Action<string> warning, Action<string> detail = null)
    {
        if (!File.Exists(path)) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Пустые настройки."); }
        catch (Exception ex)
        {
            bool preserved = false;
            try { File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"), false); preserved = true; } catch { }
            string reason = ex is JsonException or InvalidDataException ? "файл повреждён" : "файл недоступен";
            warning?.Invoke("Не удалось прочитать настройки: " + reason + ". " + (preserved
                ? "Его копия сохранена рядом, Merno начнёт с настройками по умолчанию."
                : "Merno начнёт с настройками по умолчанию и не будет их сохранять, чтобы не затереть этот файл."));
            detail?.Invoke("Настройки: " + ex.GetType().Name + ": " + ex.Message);
            return new AppSettings { _saveBlocked = !preserved };
        }
    }

    public static bool ReadOnly;
    public void Save()
    {
        if (ReadOnly) return;
        try { SaveTo(FilePath); }
        catch (Exception ex) { Log.Warn("Настройки не сохранены: " + ex.Message); }
    }
    internal void SaveTo(string path)
    {
        if (_saveBlocked) throw new IOException("Сохранение запрещено, пока исходный файл настроек недоступен.");
        AtomicFile.Write(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
