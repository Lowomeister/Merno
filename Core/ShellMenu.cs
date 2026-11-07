// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Merno.Core;

/// <summary>Пункт контекстного меню Проводника.</summary>
public class MenuItemInfo
{
    public string Name;        // как видно в меню
    public string Where;       // «Файлы», «Папки», «Картинки»…
    public string Desc;        // для известных пунктов Windows
    public bool System;        // известный пункт Windows (из списка) или найденный пункт программы
    public string Clsid;       // для обработчиков — скрываем через Shell Extensions\Blocked
    /// <summary>Команды — пути в HKCR (один пункт часто прописан у нескольких типов файлов), скрываем значением ProgrammaticAccessOnly.</summary>
    public List<string> VerbKeys = new();
    public bool Hidden;
    /// <summary>Входит в «Убрать лишнее».</summary>
    public bool Junk;
}

/// <summary>Показ и скрытие пунктов контекстного меню. Ничего не удаляется: любой пункт можно вернуть.</summary>
public static class ShellMenu
{
    const string Blocked = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
    // ProgrammaticAccessOnly прячет команду из меню, но программы по-прежнему могут её вызывать
    const string HideValue = "ProgrammaticAccessOnly";

    // Известные пункты Windows: (название, где, пояснение, CLSID или путь команды)
    static readonly (string Name, string Where, string Desc, string Clsid, string Verb)[] Known =
    {
        ("Передать на устройство", "Файлы", "Показывает видео или музыку на телевизоре.", "{7AD84985-87B4-4a16-BE58-8B72A5B390F7}", null),
        ("Предоставить доступ к", "Файлы и папки", "Открывает доступ к файлу другим компьютерам в домашней сети.", "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}", null),
        ("Восстановить прежнюю версию", "Файлы и папки", "Возвращает старую версию файла. Список версий почти всегда пуст.", "{596AB062-B4D2-4215-9F74-E9109B0A8153}", null),
        ("Поделиться", "Файлы", "Отправляет файл по почте или на соседнее устройство.", "{e2bf9676-5f8f-435c-97eb-11607a5bedf7}", null),
        ("Изменить с помощью приложения «Фотографии»", "Картинки", "Открывает редактор приложения «Фотографии».", "{BFE0E2A4-C70C-4AD7-AC3D-10D1ECEBB5B4}", null),
        ("Изменить в Clipchamp", "Видео и картинки", "Открывает видеоредактор Clipchamp.", "{8AB635F8-9A67-4698-AB99-784AD929F3B4}", null),
        ("Редактирование с помощью Paint", "Картинки", "Открывает картинку в Paint.", "{2430F218-B743-4FD6-97BF-5C76541B4AE9}", null),
        ("Изменить в Блокноте", "Файлы", "Открывает файл в Блокноте.", "{CA6CC9F1-867A-481E-951E-A28C5E4F01EA}", null),
        ("Закрепить на начальном экране", "Файлы и папки", "Добавляет плитку в меню «Пуск».", "{470C0EBD-5D73-4d58-9CED-E91E22E23282}", null),
        ("Проверить с помощью Microsoft Defender", "Файлы и папки", "Проверяет файл антивирусом. Защита работает и без этого пункта.", "{09A47860-11B0-4DA5-AFA5-26D86198A780}", null),
        ("Добавить в библиотеку", "Папки", "Добавляет папку в библиотеку Проводника. Нужен редко.", "{3dad6c5d-2167-4cae-9914-f99e41c12cfa}", null),
        ("Удалить объект («Фотографии»)", "Картинки", "Стирает лишнее с фото в «Фотографиях».", "{1100CBCD-B822-43F0-84CB-16814C2F6B44}", null),
        ("Создать в Designer", "Картинки", "Открывает картинку в Microsoft Designer.", "{7A53B94A-4E6E-4826-B48E-535020B264E5}", null),
        ("Визуальный поиск с Bing", "Картинки", "Ищет похожие картинки в Bing.", "{9AAFEDA2-97B6-43EA-9466-9DE90501B1B6}", null),
        ("Открыть в Терминале", "Папки", "Открывает командную строку в этой папке.", "{9F156763-7844-4DC4-B2B1-901F640F5155}", null),
        ("Спросить Copilot", "Файлы", "Отправляет файл ИИ-помощнику Microsoft.", "{CB3B0003-8088-4EDE-8769-8B354AB2FF8C}", null),
        ("Отправить", "Файлы и папки", "Отправляет файл на флешку, рабочий стол или в архив.", "{7BA4C740-9E81-11CF-99D3-00AA004AE837}", null),
        ("Добавить в избранное", "Файлы", "Закрепляет файл в разделе «Главная» Проводника.", null, @"*\shell\pintohomefile"),
        ("Сделать фоном рабочего стола", "Картинки", "Ставит картинку обоями.", null, @"SystemFileAssociations\image\shell\setdesktopwallpaper"),
        ("Печать (для картинок)", "Картинки", "Отправляет картинку на печать.", null, @"SystemFileAssociations\image\shell\print"),
    };

    // Где искать пункты программ
    static readonly (string Key, string Where)[] Places =
    {
        ("*", "Файлы"), ("AllFilesystemObjects", "Файлы и папки"), ("Directory", "Папки"), ("Folder", "Папки"),
        ("Directory\\Background", "Фон папки"), ("Drive", "Диски"), ("SystemFileAssociations\\image", "Картинки"),
        ("SystemFileAssociations\\video", "Видео"), ("SystemFileAssociations\\audio", "Музыка"),
    };
    // Команды Windows, которые прятать нельзя или бессмысленно
    static readonly HashSet<string> CoreVerbs = new(StringComparer.OrdinalIgnoreCase)
    { "open", "opennewwindow", "opennewprocess", "opennewtab", "explore", "find", "runasuser", "printto",
      "pintohome", "cmd", "powershell", "Powershell", "OpenWith", "openas", "UpdateEncryptionSettings",
      "UpdateEncryptionSettingsWork", "change-passphrase", "change-pin", "encrypt-bde", "encrypt-bde-elev", "manage-bde", "resume-bde",
      "resume-bde-elev", "unlock-bde", "format", "eject", "properties" };
    // Обработчики Windows, без которых ломается Проводник
    static readonly HashSet<string> CoreHandlers = new(StringComparer.OrdinalIgnoreCase)
    { "{09799AFB-AD67-11d1-ABCD-00C04FC30936}", "{a2a9545d-a0c2-42b4-9708-a0b2badd77c8}", "{474C98EE-CF3D-41f5-80E3-4AAB0AB04301}",
      "{596AB062-B4D2-4215-9F74-E9109B0A8153}", "{3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}", "{e82a2d71-5b2f-43a0-97b8-81be15854de8}",
      "{D969A300-E7FF-11d0-A93B-00A0C90F2719}" /* «Создать» */ };

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int SHLoadIndirectString(string src, StringBuilder buf, int size, IntPtr reserved);

    static string Resolve(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (s.StartsWith("@"))
        {
            var sb = new StringBuilder(512);
            if (SHLoadIndirectString(s, sb, sb.Capacity, IntPtr.Zero) == 0) s = sb.ToString(); else return null;
        }
        return s.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").Trim();
    }

    static bool IsBlocked(string clsid)
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var k = root.OpenSubKey(Blocked);
            if (k != null && k.GetValueNames().Any(n => n.Equals(clsid, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    // Что убирает кнопка «Убрать лишнее»: дубли «Открыть с помощью», реклама сервисов Microsoft и редко нужное.
    // «Отправить», «Открыть в Терминале», «Проверить с помощью Defender» и «Сделать фоном» оставляем — ими пользуются.
    public static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Передать на устройство", "Предоставить доступ к", "Восстановить прежнюю версию", "Поделиться",
        "Изменить с помощью приложения «Фотографии»", "Изменить в Clipchamp", "Редактирование с помощью Paint", "Изменить в Блокноте",
        "Закрепить на начальном экране", "Добавить в библиотеку", "Спросить Copilot", "Удалить объект («Фотографии»)",
        "Создать в Designer", "Визуальный поиск с Bing", "Добавить в избранное", "Печать (для картинок)"
    };

    // Типы файлов, у которых программы чаще всего добавляют свои пункты
    static readonly (string Ext, string Where)[] Exts =
    {
        (".jpg","Картинки"),(".jpeg","Картинки"),(".png","Картинки"),(".bmp","Картинки"),(".gif","Картинки"),(".webp","Картинки"),
        (".heic","Картинки"),(".tif","Картинки"),(".tiff","Картинки"),(".ico","Картинки"),
        (".mp4","Видео"),(".mkv","Видео"),(".avi","Видео"),(".mov","Видео"),(".mp3","Музыка"),(".flac","Музыка"),(".wav","Музыка"),
        (".txt","Текст"),(".log","Текст"),(".pdf","PDF"),(".zip","Архивы"),(".rar","Архивы"),(".7z","Архивы"),
    };
    // Понятные имена для команд без подписи
    static readonly Dictionary<string, string> VerbNames = new(StringComparer.OrdinalIgnoreCase)
    { ["edit"] = "Изменить", ["print"] = "Печать", ["play"] = "Воспроизвести", ["enqueue"] = "Добавить в список воспроизведения", ["preview"] = "Просмотр" };

    /// <summary>Все места в HKCR, откуда Проводник собирает меню: общие ключи, ключи расширений и программы, открывающие эти файлы.</summary>
    static List<(string Key, string Where)> AllPlaces()
    {
        var res = new List<(string, string)>(Places);
        var seen = new HashSet<string>(Places.Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
        void Add(string k, string w) { if (!string.IsNullOrEmpty(k) && seen.Add(k)) res.Add((k, w)); }
        foreach (var (ext, where) in Exts)
        {
            Add(@"SystemFileAssociations\" + ext, where);
            Add(ext, where);   // HKCR\.ext\shell и .ext\shellex — туда тоже пишут программы
            using (var e = Registry.ClassesRoot.OpenSubKey(ext)) Add(e?.GetValue("") as string, where);
            // программы из «Открыть с помощью» тоже могут добавлять свои пункты
            using (var ow = Registry.ClassesRoot.OpenSubKey(ext + @"\OpenWithProgids"))
                if (ow != null) foreach (var pid in ow.GetValueNames()) if (pid.Length > 0) Add(pid, where);
            // программа, выбранная пользователем «по умолчанию»
            foreach (var ucName in new[] { "UserChoice", "UserChoiceLatest" })   // Windows 11 24H2+ хранит выбор в UserChoiceLatest
                using (var uc = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\{ucName}"))
                {
                    var pid = uc?.GetValue("ProgId") as string;
                    if (pid == null && uc != null) { using var pk = uc.OpenSubKey("ProgId"); pid = pk?.GetValue("ProgId") as string; }
                    Add(pid, where);
                }
        }
        return res;
    }

    static bool VerbHidden(RegistryKey v) => v.GetValue(HideValue) != null || v.GetValue("LegacyDisable") != null;

    public static List<MenuItemInfo> Scan()
    {
        var list = new List<MenuItemInfo>();
        var knownClsids = new HashSet<string>(Known.Where(k => k.Clsid != null).Select(k => k.Clsid), StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, MenuItemInfo>(StringComparer.OrdinalIgnoreCase);
        MenuItemInfo KnownItem(string name) => list.FirstOrDefault(i => i.System && i.Name == name);

        foreach (var k in Known)
        {
            var it = new MenuItemInfo { Name = k.Name, Where = k.Where, Desc = k.Desc, System = true, Junk = JunkNames.Contains(k.Name), Clsid = k.Clsid };
            if (k.Verb != null) { using var key = Registry.ClassesRoot.OpenSubKey(k.Verb); if (key != null) it.VerbKeys.Add(k.Verb); }
            list.Add(it);
        }

        var seenH = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (place, where) in AllPlaces())
        {
            bool image = where == "Картинки";
            using (var shell = Registry.ClassesRoot.OpenSubKey(place + @"\shell"))
                if (shell != null)
                    foreach (var verb in shell.GetSubKeyNames())
                    {
                        if (CoreVerbs.Contains(verb)) continue;
                        // команда по умолчанию (двойной щелчок) у ProgID — не трогаем
                        var defVerb = shell.GetValue("") as string;
                        if (!place.Contains('\\') && place != "*" && (string.IsNullOrEmpty(defVerb) ? false : defVerb.Split(',')[0].Trim().Equals(verb, StringComparison.OrdinalIgnoreCase))) continue;
                        using var v = shell.OpenSubKey(verb);
                        if (v == null) continue;
                        if (v.GetValue("Extended") != null && !VerbHidden(v)) continue;  // и так видно только по Shift
                        var raw = Resolve(v.GetValue("MUIVerb") as string) ?? Resolve(v.GetValue("") as string);
                        if (raw == null && verb.Equals("runas", StringComparison.OrdinalIgnoreCase)) continue;   // «Запуск от имени администратора»
                        var name = raw ?? (VerbNames.TryGetValue(verb, out var vn) ? vn : verb);
                        var path = place + @"\shell\" + verb;

                        // привязываем к известным пунктам Windows, если это они
                        MenuItemInfo target = null;
                        if (verb.Equals("setdesktopwallpaper", StringComparison.OrdinalIgnoreCase)) target = KnownItem("Сделать фоном рабочего стола");
                        else if (verb.Equals("pintohomefile", StringComparison.OrdinalIgnoreCase)) target = KnownItem("Добавить в избранное");
                        else if (image && verb.Equals("print", StringComparison.OrdinalIgnoreCase)) target = KnownItem("Печать (для картинок)");
                        else if (image && name.Contains("Paint", StringComparison.OrdinalIgnoreCase) || image && verb.Equals("edit", StringComparison.OrdinalIgnoreCase) && raw == null)
                            target = KnownItem("Редактирование с помощью Paint");
                        else if (name.Contains("Фотограф", StringComparison.OrdinalIgnoreCase) || name.Contains("Photos", StringComparison.OrdinalIgnoreCase))
                            target = KnownItem("Изменить с помощью приложения «Фотографии»");
                        else if (name.Contains("Блокнот", StringComparison.OrdinalIgnoreCase) && !name.Contains("++"))
                            target = KnownItem("Изменить в Блокноте");

                        if (target == null)
                        {
                            // один и тот же пункт у разных типов файлов — одна строка
                            if (!byName.TryGetValue(name, out target))
                            {
                                target = new MenuItemInfo { Name = name, Where = where };
                                byName[name] = target; list.Add(target);
                            }
                            else if (!target.Where.Split(", ").Contains(where)) target.Where += ", " + where;
                        }
                        if (!target.VerbKeys.Contains(path, StringComparer.OrdinalIgnoreCase)) target.VerbKeys.Add(path);
                    }
            using (var hs = Registry.ClassesRoot.OpenSubKey(place + @"\shellex\ContextMenuHandlers"))
                if (hs != null)
                    foreach (var h in hs.GetSubKeyNames())
                    {
                        using var hk = hs.OpenSubKey(h);
                        var clsid = hk?.GetValue("") as string;
                        if (string.IsNullOrEmpty(clsid) || !clsid.StartsWith("{")) clsid = h.StartsWith("{") ? h : null;
                        if (clsid == null || knownClsids.Contains(clsid) || CoreHandlers.Contains(clsid) || !seenH.Add(clsid)) continue;
                        string name = h.StartsWith("{") ? null : h;
                        using (var cl = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid))
                        {
                            var n = Resolve(cl?.GetValue("") as string);
                            if (!string.IsNullOrEmpty(n)) name = n;
                            using var ip = cl?.OpenSubKey("InprocServer32");
                            var dll = ip?.GetValue("") as string ?? "";
                            if (name == null && dll.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase)) continue;
                        }
                        list.Add(new MenuItemInfo { Name = name ?? h, Where = where, Clsid = clsid });
                    }
        }

        // известные пункты, которых на этой Windows нет, не показываем
        list.RemoveAll(i => i.System && i.VerbKeys.Count == 0 && (i.Clsid == null || (!ClsidExists(i.Clsid) && !IsBlocked(i.Clsid))));
        foreach (var i in list) i.Hidden = StateHidden(i);
        return list;
    }

    static bool ClsidExists(string clsid) { using var cl = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid); return cl != null; }

    static bool StateHidden(MenuItemInfo i)
    {
        bool any = false, all = true;
        if (i.Clsid != null && (ClsidExists(i.Clsid) || IsBlocked(i.Clsid))) { any = true; all &= IsBlocked(i.Clsid); }
        foreach (var p in i.VerbKeys)
        {
            using var v = Registry.ClassesRoot.OpenSubKey(p);
            if (v == null) continue;
            any = true; all &= VerbHidden(v);
        }
        return any && all;
    }

    /// <summary>Показать или скрыть пункт. Ничего не удаляет.</summary>
    public static void SetHidden(MenuItemInfo it, bool hide)
    {
        int failed = 0;
        if (it.Clsid != null)
            // и для всех пользователей, и для текущего: часть обработчиков смотрит только один из списков
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var k = root.CreateSubKey(Blocked, true);
                    if (hide) k.SetValue(it.Clsid, it.Name ?? "", RegistryValueKind.String);
                    else foreach (var n in k.GetValueNames().Where(n => n.Equals(it.Clsid, StringComparison.OrdinalIgnoreCase))) k.DeleteValue(n, false);
                }
                catch (Exception ex) { failed++; Log.Warn($"Меню: «{it.Name}»: не получилось изменить ({ex.Message})"); }
            }
        foreach (var p in it.VerbKeys)
        {
            try
            {
                using var k = Registry.ClassesRoot.OpenSubKey(p, true);
                if (k == null) continue;
                if (hide) k.SetValue(HideValue, "", RegistryValueKind.String);
                else { k.DeleteValue(HideValue, false); k.DeleteValue("LegacyDisable", false); }
            }
            catch (Exception ex) { failed++; Log.Warn($"Меню: «{it.Name}»: не получилось изменить ({ex.Message})"); }
        }
        it.Hidden = failed == 0 ? hide : StateHidden(it);
        if (failed == 0 || hide) Remember(it, hide);
        if (failed > 0) Log.Warn($"Меню: пункт «{it.Name}» изменён не до конца: часть записей Windows защищает от изменений (или их блокирует антивирус)");
        Log.Write($"Меню: «{it.Name}»: {(hide ? "убран" : "возвращён")}");
    }

    // Что именно скрыл Merno — чтобы «Вернуть как было» не трогал то, что было скрыто до него
    static readonly string StateFile = Path.Combine(Log.DataDir, "context-menu.json");
    static HashSet<string> LoadState()
    {
        try { return File.Exists(StateFile) ? System.Text.Json.JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(StateFile)) ?? new() : new(); }
        catch { return new(); }
    }
    static void SaveState(HashSet<string> st) => AtomicFile.Write(StateFile, System.Text.Json.JsonSerializer.Serialize(st));
    static void Remember(MenuItemInfo it, bool hide)
    {
        var st = LoadState();
        var keys = new List<string>();
        if (it.Clsid != null) keys.Add("clsid:" + it.Clsid);
        keys.AddRange(it.VerbKeys.Select(v => "verb:" + v));
        foreach (var k in keys) { if (hide) st.Add(k); else st.Remove(k); }
        SaveState(st);
    }

    /// <summary>Вернуть всё, что скрыл Merno. Возвращает число восстановленных записей.</summary>
    public static int RestoreAll()
    {
        var st = LoadState(); int n = 0;
        foreach (var rec in st.ToList())
        {
            try
            {
                if (rec.StartsWith("clsid:"))
                {
                    var clsid = rec[6..];
                    foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                    {
                        using var k = root.OpenSubKey(Blocked, true);
                        if (k == null) continue;
                        foreach (var name in k.GetValueNames().Where(x => x.Equals(clsid, StringComparison.OrdinalIgnoreCase))) k.DeleteValue(name, false);
                    }
                }
                else if (rec.StartsWith("verb:"))
                {
                    using var k = Registry.ClassesRoot.OpenSubKey(rec[5..], true);
                    k?.DeleteValue(HideValue, false);
                }
                else throw new InvalidDataException("Неизвестная запись контекстного меню.");
                st.Remove(rec);
                n++;
            }
            catch (Exception ex) { Log.Warn($"Не удалось вернуть {rec}: {ex.Message}"); }
        }
        SaveState(st);
        if (st.Count == 0) Log.Ok("Меню возвращено как было");
        else Log.Warn($"Не удалось вернуть пунктов: {st.Count}. Записи сохранены для повторной попытки.");
        return n;
    }

    // Классическое меню Windows 10 в Windows 11: пустой InprocServer32 у этого CLSID отключает новое меню
    const string ClassicKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    public static bool ClassicEnabled() { using var k = Registry.CurrentUser.OpenSubKey(ClassicKey + @"\InprocServer32"); return k != null; }
    public static void SetClassic(bool on)
    {
        if (on) { using var k = Registry.CurrentUser.CreateSubKey(ClassicKey + @"\InprocServer32", true); k.SetValue("", "", RegistryValueKind.String); }
        else Registry.CurrentUser.DeleteSubKeyTree(ClassicKey, false);
        Log.Write("Меню правой кнопки: " + (on ? "как в Windows 10" : "как в Windows 11"));
    }

    /// <summary>Перезапуск Проводника, чтобы скрытые обработчики пропали сразу.</summary>
    public static void RestartExplorer()
    {
        Sh.Run(Sh.Sys32("taskkill.exe"), "/f /im explorer.exe", 20_000);
        // Windows сама перезапускает оболочку; запускать explorer из программы с правами администратора нельзя —
        // он стал бы «админским». Если за 6 секунд не поднялся — запускаем с обычными правами.
        for (int i = 0; i < 12; i++)
        {
            Thread.Sleep(500);
            var ps = System.Diagnostics.Process.GetProcessesByName("explorer");
            foreach (var p in ps) p.Dispose();
            if (ps.Length > 0) return;
        }
        Sh.Run(Sh.Sys32("runas.exe"), "/trustlevel:0x20000 explorer.exe", 20_000);
    }
}
