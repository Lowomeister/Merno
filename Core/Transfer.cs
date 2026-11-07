// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.Json;

namespace Merno.Core;

/// <summary>Файл переноса настроек на другой компьютер (.merno).</summary>
public class TransferData
{
    public int version { get; set; } = 1;
    public string app { get; set; } = "Merno";
    public string appVersion { get; set; } = App.Version;
    public DateTime date { get; set; } = DateTime.Now;
    public List<string> appliedTweaks { get; set; } = new();
    public List<string> installedCatalogApps { get; set; } = new();
    /// <summary>Скрытые пункты контекстного меню: «Название|Где».</summary>
    public List<string> hiddenContextMenu { get; set; } = new();
}

public static class Transfer
{
    public const string Filter = "Перенос Merno (*.merno)|*.merno|Все файлы (*.*)|*.*";

    public static string MenuKey(MenuItemInfo it) => (it.Name ?? "") + "|" + (it.Where ?? "");

    /// <summary>Собирает текущее состояние. Долго (реестр, winget-каталог, меню) — вызывать не из UI-потока.</summary>
    public static TransferData Collect(Ctx c, IEnumerable<Tweak> tweaks)
    {
        var d = new TransferData();
        // рискованные и необратимые настройки (удаление приложений, отключение защиты) на другой ПК не переносим
        foreach (var t in tweaks.Where(t => t.Risk < 2 && t.CanRevert))
            try { if ((c.S == null || t.Applicable(c.S)) && t.IsApplied(c)) d.appliedTweaks.Add(t.Id); } catch { }
        try
        {
            var apps = Apps.Load().Where(a => a.tab == null && !Apps.IsYandex(a.name)).ToList();
            var inst = Apps.Installed(apps);
            d.installedCatalogApps.AddRange(apps.Where(a => inst.Contains(a.name)).Select(a => a.name));
        }
        catch (Exception ex) { Log.Warn("Перенос: список программ не собран: " + ex.Message); }
        try { d.hiddenContextMenu.AddRange(ShellMenu.Scan().Where(i => i.Hidden).Select(MenuKey).Distinct()); }
        catch (Exception ex) { Log.Warn("Перенос: меню не сохранено: " + ex.Message); }
        return d;
    }

    public static void Save(string file, TransferData d) =>
        File.WriteAllText(file, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));

    public static TransferData Load(string file)
    {
        var d = JsonSerializer.Deserialize<TransferData>(File.ReadAllText(file));
        if (d == null || d.app != "Merno") throw new InvalidDataException("Это не файл переноса Merno");
        d.appliedTweaks ??= new(); d.installedCatalogApps ??= new(); d.hiddenContextMenu ??= new();
        return d;
    }
}
