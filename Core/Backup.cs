// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.Json;
using Microsoft.Win32;

namespace Merno.Core;

public class RegEntry
{
    public string Hive { get; set; }
    public string Path { get; set; }
    public string Name { get; set; }
    public bool Exists { get; set; }
    public bool KeyExisted { get; set; } = true;
    public string Kind { get; set; }
    public string S { get; set; }
    public long N { get; set; }
    public string B64 { get; set; }
    public string[] M { get; set; }
    /// <summary>Id твика, из-за которого сделана запись (null — старые бэкапы).</summary>
    public string Tweak { get; set; }
}

/// <summary>Нереестровое изменение твика: kind = task | powerundo | powerscheme | hibernate.</summary>
public class TweakExtra
{
    public string Tweak { get; set; }
    public string Kind { get; set; }
    public string Value { get; set; }
}

public class BackupData
{
    public string Version { get; set; } = App.Version;
    public DateTime Date { get; set; } = DateTime.Now;
    public string Computer { get; set; } = Environment.MachineName;
    public List<RegEntry> Reg { get; set; } = new();
    public List<string> Tasks { get; set; } = new();
    public List<string> Tweaks { get; set; } = new();
    public string PowerScheme { get; set; }
    public bool HibernateWasOn { get; set; }
    /// <summary>Аргументы powercfg для отката (выполняются в обратном порядке).</summary>
    public List<string> PowerUndo { get; set; } = new();
    /// <summary>Те же задачи/powercfg/гибернация, но с привязкой к твику (для отката одного твика).</summary>
    public List<TweakExtra> Extras { get; set; } = new();
    /// <summary>Подпись для «Отката» и «Последних действий», если это не настройки (например, очистка остатков программ).</summary>
    public string Title { get; set; }
    public bool IsEmpty => Reg.Count == 0 && Tasks.Count == 0 && PowerScheme == null && !HibernateWasOn && Tweaks.Count == 0 && PowerUndo.Count == 0 && Extras.Count == 0;
}

/// <summary>Работа с реестром с записью исходных значений в бэкап.</summary>
public static class Reg
{
    public static RegistryKey Root(string hive) => RegistryKey.OpenBaseKey(hive switch
    {
        "HKLM" => RegistryHive.LocalMachine,
        "HKCU" => RegistryHive.CurrentUser,
        _ => throw new ArgumentException("Недопустимая ветвь реестра.", nameof(hive))
    }, RegistryView.Registry64);

    public static object Get(string hive, string path, string name)
    {
        try
        {
            using var root = Root(hive);
            using var k = root.OpenSubKey(path);
            return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch { return null; }
    }

    public static bool KeyExists(string hive, string path)
    {
        try { using var root = Root(hive); using var k = root.OpenSubKey(path); return k != null; } catch { return false; }
    }

    public static bool Has(string hive, string path, string name)
    {
        try
        {
            using var root = Root(hive);
            using var k = root.OpenSubKey(path);
            return k != null && k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) != null;
        }
        catch { return false; }
    }

    public static bool Equal(object cur, object want)
    {
        if (cur == null) return false;
        if (cur is byte[] a && want is byte[] b) return a.SequenceEqual(b);
        if (cur is string[] sa && want is string[] sb) return sa.SequenceEqual(sb, StringComparer.Ordinal);
        if (cur is int ci) return Convert.ToInt64(want) == (uint)ci || Convert.ToInt64(want) == ci;
        if (cur is long cl) return Convert.ToInt64(want) == cl;
        return string.Equals(cur.ToString(), want?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool Set(string hive, string path, string name, object value, RegistryValueKind kind = RegistryValueKind.DWord)
        => SetCore(hive, path, name, value, kind, true);

    /// <summary>Запись без попадания в общий бэкап (у вызывающего свой способ отката).</summary>
    public static bool SetNoBackup(string hive, string path, string name, object value, RegistryValueKind kind = RegistryValueKind.DWord)
        => SetCore(hive, path, name, value, kind, false);

    static bool SetCore(string hive, string path, string name, object value, RegistryValueKind kind, bool backup)
    {
        try
        {
            using var root = Root(hive);
            if (backup)
            {
                using var previous = root.OpenSubKey(path);
                Backup.Record(hive, path, name, previous);
            }
            using var k = root.CreateSubKey(path, true);
            if (k == null) throw new UnauthorizedAccessException();
            object v = kind switch
            {
                RegistryValueKind.DWord => unchecked((int)Convert.ToInt64(value)),
                RegistryValueKind.QWord => Convert.ToInt64(value),
                _ => value
            };
            k.SetValue(name, v, kind);
            return true;
        }
        catch
        {
            Log.Warn($"Не удалось изменить настройку: {name}");
            return false;
        }
    }

    public static void Delete(string hive, string path, string name)
    {
        try { using var root = Root(hive); using var k = root.OpenSubKey(path, true); k?.DeleteValue(name, false); } catch { }
    }

    /// <summary>Тип запуска службы: 2 — авто, 3 — вручную, 4 — отключена.</summary>
    public static int? ServiceStart(string svc)
    {
        var v = Get("HKLM", $@"SYSTEM\CurrentControlSet\Services\{svc}", "Start");
        return v is int i ? i : null;
    }

    /// <summary>false — записать не удалось (службы нет — это не ошибка).</summary>
    public static bool SetService(string svc, int start)
    {
        if (ServiceStart(svc) == null) return true;
        if (!Set("HKLM", $@"SYSTEM\CurrentControlSet\Services\{svc}", "Start", start)) return false;
        if (start == 4) Sh.Run(Sh.Sys32("sc.exe"), $"stop {svc}", 15_000);
        return true;
    }
}

public static class Backup
{
    private sealed class Recording
    {
        internal BackupData Data = new();
        internal string File, Tweak;
        internal HashSet<string> Seen = new();
    }
    private static readonly AsyncLocal<Recording> Active = new();
    public static BackupData Current => Active.Value?.Data;
    public static string CurrentTweak { get => Active.Value?.Tweak; set { if (Active.Value != null) Active.Value.Tweak = value; } }
    private static string _file => Active.Value?.File;
    private static HashSet<string> _seen => Active.Value.Seen;

    public static void Begin() => BeginAt(System.IO.Path.Combine(Log.DataDir, $"backup-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json"));
    private static void BeginAt(string file)
    {
        if (Current != null) throw new InvalidOperationException("Другая запись отката ещё не завершена.");
        Active.Value = new Recording { File = file };
    }
    public static IDisposable Session() => new RecordingScope(null);
    internal static IDisposable SessionAt(string file) => new RecordingScope(file);
    private sealed class RecordingScope : IDisposable
    {
        private readonly Recording recording;
        internal RecordingScope(string file) { if (file == null) Begin(); else BeginAt(file); recording = Active.Value; }
        public void Dispose()
        {
            if (!ReferenceEquals(Active.Value, recording)) return;
            try { Persist(); } finally { End(); }
        }
    }

    static string SeenId(string hive, string path, string name) => $"{hive}\\{path}\\{name}".ToLowerInvariant();

    public static void Record(string hive, string path, string name, RegistryKey k)
    {
        if (Current == null) throw new InvalidOperationException("Для изменения требуется запись отката.");
        var e = new RegEntry { Hive = hive, Path = path, Name = name, Tweak = CurrentTweak, KeyExisted = k != null };
        var v = k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (v != null) // GetValue регистронезависим, в отличие от GetValueNames().Contains
        {
            e.Exists = true;
            var kind = k.GetValueKind(name);
            e.Kind = kind.ToString();
            switch (v)
            {
                case byte[] b: e.B64 = Convert.ToBase64String(b); break;
                case string[] m: e.M = m; break;
                case int i: e.N = (uint)i; break;
                case long l: e.N = l; break;
                default: e.S = v?.ToString(); break;
            }
        }
        RecordEntry(e);
    }

    internal static void RecordEntry(RegEntry entry)
    {
        var cur = Current ?? throw new InvalidOperationException("Запись отката не начата.");
        lock (cur)
        {
            string id = SeenId(entry.Hive, entry.Path, entry.Name);
            if (_seen.Contains(id)) return;
            cur.Reg.Add(entry);
            try { Persist(); _seen.Add(id); }
            catch { cur.Reg.Remove(entry); throw; }
        }
    }

    private static void Persist()
    {
        var cur = Current;
        if (cur == null) return;
        lock (cur)
        {
            if (!cur.IsEmpty) WriteData(_file, cur);
        }
    }
    internal static void WriteData(string file, BackupData data) =>
        AtomicFile.Write(file, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));

    public static void Save()
    {
        Persist();
        if (Current != null && !Current.IsEmpty) Log.Ok($"Резервная копия сохранена: {System.IO.Path.GetFileName(_file)}");
    }

    public static void End() => Active.Value = null;

    public static List<(string File, BackupData Data)> List()
    {
        var res = new List<(string, BackupData)>();
        if (!Directory.Exists(Log.DataDir)) return res;
        foreach (var f in Directory.GetFiles(Log.DataDir, "backup-*.json").OrderByDescending(x => x))
        {
            try { res.Add((f, ReadData(f))); } catch { }
        }
        return res;
    }


    // ---- запись нереестровых изменений с привязкой к твику ----
    static void AddExtra(string kind, string value)
    {
        if (Current == null) return;
        lock (Current) { Current.Extras.Add(new TweakExtra { Tweak = CurrentTweak, Kind = kind, Value = value }); Persist(); }
    }
    public static void AddTask(string task)
    {
        if (Current == null) return;
        lock (Current) Current.Tasks.Add(task);
        AddExtra("task", task);
    }
    public static void AddPowerUndo(string args)
    {
        if (Current == null) return;
        lock (Current) Current.PowerUndo.Add(args);
        AddExtra("powerundo", args);
    }
    public static void SetPowerScheme(string scheme)
    {
        if (Current == null || scheme == null) throw new IOException("Не удалось сохранить текущую схему питания.");
        if (Current.PowerScheme != null) { Persist(); return; }
        lock (Current) Current.PowerScheme = scheme;
        AddExtra("powerscheme", scheme);
    }
    public static void MarkHibernate()
    {
        if (Current == null) return;
        lock (Current) Current.HibernateWasOn = true;
        AddExtra("hibernate", "on");
    }

    // Значения из файла копии идут в командную строку schtasks/powercfg/sc — пропускаем только то, что Merno сам туда пишет:
    // имя задачи без кавычек, «/setacvalueindex схема раздел параметр число», схема/служба — буквы, цифры, {}-._
    static bool TaskOk(string t) => !string.IsNullOrWhiteSpace(t) && t.Length < 260 && t.IndexOfAny(new[] { '"', '\r', '\n' }) < 0;
    static bool PowerUndoOk(string a) => a != null && System.Text.RegularExpressions.Regex.IsMatch(a, @"^/setacvalueindex [\w{}\-]+ [\w{}\-]+ [\w{}\-]+ \d+$");
    static bool NameOk(string s) => s != null && System.Text.RegularExpressions.Regex.IsMatch(s, @"^[\w{}.\-]{1,80}$");

    internal static bool EntryAllowed(RegEntry e)
    {
        if (e == null || e.Hive is not ("HKLM" or "HKCU") || e.Path == null || e.Name == null ||
            e.Path.Split('\\').Any(part => part is "" or "." or "..") || e.Path.Contains('/') || e.Path.Any(char.IsControl)) return false;
        bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        foreach (var tweak in Catalog.All())
        {
            if (tweak.Reg.Any(r => r.Hive == e.Hive && Eq(r.Path, e.Path) && Eq(r.Name, e.Name))) return true;
            if (e.Hive == "HKLM" && Eq(e.Name, "Start") && tweak.Svc.Any(svc => Eq(e.Path, @"SYSTEM\CurrentControlSet\Services\" + svc.Svc))) return true;
        }
        if (e.Hive == "HKCU" && Eq(e.Path, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32") && e.Name == "") return true;
        if (e.Hive == "HKCU" && Eq(e.Path, @"Software\Microsoft\DirectX\UserGpuPreferences") && Eq(e.Name, "DirectXUserGlobalSettings")) return true;
        if (e.Hive == "HKLM" && (Eq(e.Path, @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection") &&
            new[] { "DisableRealtimeMonitoring", "DisableBehaviorMonitoring", "DisableOnAccessProtection", "DisableScanOnRealtimeEnable", "DisableIOAVProtection" }.Contains(e.Name, StringComparer.OrdinalIgnoreCase) ||
            Eq(e.Path, @"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet") && new[] { "SpynetReporting", "SubmitSamplesConsent" }.Contains(e.Name, StringComparer.OrdinalIgnoreCase))) return true;
        const string approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        if (new[] { "Run", "Run32", "StartupFolder" }.Any(end => Eq(e.Path, approved + end))) return true;
        foreach (var uninstall in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" })
            if (e.Path.StartsWith(uninstall, StringComparison.OrdinalIgnoreCase) && !e.Path[uninstall.Length..].Contains('\\') && e.Path.Length > uninstall.Length) return true;
        return false;
    }

    public static void RecordDefender() => AddExtra("defender", DefenderSnapshot.Capture());

    static bool RestoreEntry(RegEntry e)
    {
        try
        {
            if (!EntryAllowed(e)) return false;
            using var root = Reg.Root(e.Hive);
            if (!e.Exists)
            {
                bool empty;
                using (var previous = root.OpenSubKey(e.Path, true))
                {
                    previous?.DeleteValue(e.Name, false);
                    empty = previous != null && previous.ValueCount == 0 && previous.SubKeyCount == 0;
                }
                if (!e.KeyExisted && empty) root.DeleteSubKey(e.Path, false);
                return true;
            }
            using var k = root.CreateSubKey(e.Path, true);
            var kind = Enum.Parse<RegistryValueKind>(e.Kind);
            object v = kind switch
            {
                RegistryValueKind.Binary => Convert.FromBase64String(e.B64 ?? ""),
                RegistryValueKind.MultiString => e.M ?? Array.Empty<string>(),
                RegistryValueKind.DWord => unchecked((int)(uint)e.N),
                RegistryValueKind.QWord => e.N,
                _ => e.S ?? ""
            };
            k.SetValue(e.Name, v, kind);
            return true;
        }
        catch { Log.Warn($"Не удалось вернуть настройку: {e.Name}"
); return false; }
    }

    static void StripTweak(BackupData d, string id)
    {
        if (d == null) return;
        lock (d)
        {
            if (ReferenceEquals(d, Current))
                lock (_seen)
                    foreach (var e in d.Reg.Where(e => e.Tweak == id)) _seen.Remove(SeenId(e.Hive, e.Path, e.Name));
            d.Reg.RemoveAll(e => e.Tweak == id);
            foreach (var x in d.Extras.Where(x => x.Tweak == id).ToList())
            {
                switch (x.Kind)
                {
                    case "task": d.Tasks.Remove(x.Value); break;
                    case "powerundo": d.PowerUndo.Remove(x.Value); break;
                    case "powerscheme": if (d.PowerScheme == x.Value) d.PowerScheme = null; break;
                    case "hibernate": d.HibernateWasOn = false; break;
                }
            }
            d.Extras.RemoveAll(x => x.Tweak == id);
            d.Tweaks.RemoveAll(t => t == id);
        }
    }

    /// <summary>Исходные значения берём от старых копий к новым; при отказе копии не удаляются.</summary>
    public static (bool Ok, string Message) RevertTweak(Tweak t, Ctx c)
    {
        if (!t.CanRevert) return (false, "Это действие нельзя отменить автоматически.");
        try
        {
            var files = List().Where(x => x.Data != null).ToList();
            var sources = files.Where(x => x.Data.Reg.Any(e => e.Tweak == t.Id) ||
                x.Data.Extras.Any(e => e.Tweak == t.Id) || x.Data.Tweaks.Contains(t.Id)).ToList();
            // копии нет: настройку включили до Merno или в самой Windows — возвращаем как в Windows по умолчанию
            if (sources.Count == 0) return RevertToDefaults(t, c);
            int failed = ReplayTweak(Enumerable.Reverse(sources).Select(s => s.Data), t.Id, RestoreWithService, RestoreExtra, RestoreWithoutSnapshot);
            if (failed != 0) return (false, $"Не всё удалось вернуть ({failed}). Копии сохранены; можно повторить откат.");
            // Новые копии убираем первыми: при сбое записи старейшее исходное значение остаётся доступным.
            foreach (var (file, data) in sources)
            {
                StripTweak(data, t.Id);
                if (data.IsEmpty) File.Delete(file); else WriteData(file, data);
                if (file == _file) StripTweak(Current, t.Id);
            }
            Log.Ok($"Откат: «{t.Name}» возвращён к исходному состоянию");
            return (true, "Настройка возвращена.");
        }
        catch (Exception ex)
        {
            Log.Warn("Откат не завершён: " + ex.Message);
            return (false, "Откат не завершён. Оставшиеся копии сохранены; повторите попытку.");
        }
    }

    /// <summary>Выключить настройку без копии «как было»: значения Windows по умолчанию (Catalog.DefaultsPlan).</summary>
    private static (bool Ok, string Message) RevertToDefaults(Tweak t, Ctx c)
    {
        var plan = Catalog.DefaultsPlan(t, c?.S);
        if (plan == null)
            return (false, "Её включили до Merno, и Merno не знает, какой она была. Верните её в параметрах Windows или через точку восстановления.");
        foreach (var (op, value) in plan)
        {
            if (ReferenceEquals(value, Catalog.Absent)) Reg.Delete(op.Hive, op.Path, op.Name);
            else if (!Reg.SetNoBackup(op.Hive, op.Path, op.Name, value, op.Kind))
                return (false, "Не получилось вернуть значение Windows по умолчанию. Попробуйте ещё раз.");
        }
        if (t.IsApplied(c)) return (false, "Не получилось вернуть значение Windows по умолчанию. Попробуйте ещё раз.");
        Log.Ok($"Откат: настройка «{t.Name}» возвращена к значению Windows по умолчанию (копии не было)");
        return (true, "Настройка возвращена к значению Windows по умолчанию.");
    }

    public static (bool Found, string Name, DateTime When) LastRestorePoint()
    {
        try
        {
            var (_, o) = Sh.PS("$r = Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Sort-Object SequenceNumber | Select-Object -Last 1; " +
                               "if ($r) { '{0}|{1:o}' -f $r.Description, [System.Management.ManagementDateTimeConverter]::ToDateTime($r.CreationTime) }", 60_000);
            foreach (var line in (o ?? "").Split('\n').Select(l => l.Trim()))
            {
                int i = line.LastIndexOf('|');
                if (i <= 0) continue;
                if (DateTime.TryParse(line[(i + 1)..], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var when))
                    return (true, line[..i], when);
            }
        }
        catch { }
        return (false, "", default);
    }

    public static void Rollback(string file)
    {
        var data = ReadData(file);
        Log.Head($"Откат изменений от {data.Date:dd.MM.yyyy HH:mm}");
        ReplayFile(file, RestoreWithService, RestoreExtra, RestoreWithoutSnapshot);
        Log.Ok("Настройки возвращены. Перезагрузите компьютер. Удалённые приложения автоматически не восстанавливаются.");
    }

    internal static void ReplayFile(string file, Func<RegEntry, bool> registry, Func<TweakExtra, bool> extra, Func<string, bool> withoutSnapshot = null)
    {
        var data = ReadData(file);
        int failed = Replay(data, registry, extra, withoutSnapshot);
        if (failed != 0) throw new IOException($"Не всё удалось вернуть ({failed}). Копия сохранена; повторите откат.");
        string archived = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file),
            System.IO.Path.GetFileName(file).Replace("backup-", "rolledback-", StringComparison.Ordinal));
        File.Move(file, archived);
    }

    /// <param name="withoutSnapshot">Что делать с настройкой, для которой в копии нет исходного состояния:
    /// true — обработано (нечего возвращать или вернули прежним способом). null — это ошибка (так в тестах).</param>
    internal static int Replay(BackupData data, Func<RegEntry, bool> registry, Func<TweakExtra, bool> extra, Func<string, bool> withoutSnapshot = null)
    {
        Normalize(data);
        int failed = 0;
        foreach (var entry in Enumerable.Reverse(data.Reg))
            try { if (!registry(entry)) failed++; } catch { failed++; }
        foreach (var item in Enumerable.Reverse(data.Extras))
            try { if (!extra(item)) failed++; } catch { failed++; }
        foreach (var tweak in data.Tweaks.Distinct())
            if (NeedsMissingSnapshot(data, tweak) && !Handled(withoutSnapshot, tweak)) failed++;
        return failed;
    }

    internal static int ReplayTweak(IEnumerable<BackupData> sources, string id, Func<RegEntry, bool> restoreRegistry, Func<TweakExtra, bool> restoreExtra,
        Func<string, bool> withoutSnapshot = null)
    {
        var registry = new HashSet<string>();
        var extras = new HashSet<string>();
        int failed = 0;
        var all = sources.Select(Normalize).ToList();
        // есть копия со снимком Защитника: она и вернёт его настройки, прежний способ (значения по умолчанию) не нужен
        bool legacyDone = id == "defender_off" && all.Any(d => d.Extras.Any(x => x.Kind == "defender" && x.Tweak == id));
        foreach (var source in all)
        {
            var data = Normalize(source);
            // с конца, как при полном откате: первая запись ключа (KeyExisted=false) восстанавливается последней,
            // когда ключ уже пуст, и он удаляется, а не остаётся пустым
            foreach (var entry in Enumerable.Reverse(data.Reg.Where(e => e.Tweak == id).ToList()))
                if (registry.Add(SeenId(entry.Hive, entry.Path, entry.Name)))
                    try { if (!restoreRegistry(entry)) failed++; } catch { failed++; }
            foreach (var extra in Enumerable.Reverse(data.Extras.Where(x => x.Tweak == id).ToList()))
                if (extras.Add(ExtraId(extra)))
                    try { if (!restoreExtra(extra)) failed++; } catch { failed++; }
            if (data.Tweaks.Contains(id) && NeedsMissingSnapshot(data, id))
            {
                // прежний способ отката достаточно выполнить один раз, даже если старых копий несколько
                if (legacyDone) continue;
                if (Handled(withoutSnapshot, id)) legacyDone = true; else failed++;
            }
        }
        return failed;
    }

    private static bool Handled(Func<string, bool> withoutSnapshot, string id)
    {
        if (withoutSnapshot == null) return false;
        try { return withoutSnapshot(id); } catch { return false; }
    }

    /// <summary>Настройка без сохранённого исходного состояния.
    /// Необратимые (удаление приложений и т.п.) возвращать нечего: это не ошибка отката, о них предупреждает сообщение.
    /// Копии из прежних версий Merno не хранили снимок для Защитника и классического меню: для них прежний способ отката.</summary>
    private static bool RestoreWithoutSnapshot(string id)
    {
        var tweak = Catalog.All().FirstOrDefault(t => t.Id == id);
        if (tweak != null && !tweak.CanRevert) return true;
        switch (id)
        {
            case "defender_off":
                bool ok = DefenderSnapshot.Restore(DefenderSnapshot.WindowsDefaults);
                if (ok) Log.Write("Откат: Защитник Windows возвращён к настройкам по умолчанию (копия из прежней версии Merno)");
                return ok;
            case "classic_menu":
                using (var root = Reg.Root("HKCU"))
                    root.DeleteSubKeyTree(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                return true;
            default:
                return false;
        }
    }

    private static bool NeedsMissingSnapshot(BackupData data, string id) =>
        !data.Reg.Any(x => x.Tweak == id) && !data.Extras.Any(x => x.Tweak == id) ||
        id == "defender_off" && !data.Extras.Any(x => x.Kind == "defender" && x.Tweak == id) ||
        id == "classic_menu" && !data.Reg.Any(x => x.Tweak == id);

    private static string ExtraId(TweakExtra x) => x.Kind is "powerscheme" or "hibernate" or "defender" ? x.Kind :
        x.Kind == "powerundo" ? System.Text.RegularExpressions.Regex.Replace(x.Value ?? "", @"\s\d+$", "") : x.Kind + "|" + x.Value;

    private static bool RestoreWithService(RegEntry entry)
    {
        if (!RestoreEntry(entry)) return false;
        if (entry.Exists && entry.Name == "Start" && entry.N == 2 && entry.Hive == "HKLM" &&
            entry.Path.StartsWith(@"SYSTEM\CurrentControlSet\Services\", StringComparison.OrdinalIgnoreCase))
        {
            string service = entry.Path.Split('\\').Last();
            if (!NameOk(service)) return false;
            var result = Sh.Run(Sh.Sys32("sc.exe"), $"start {service}", 15_000);
            return result.Code is 0 or 1056;
        }
        return true;
    }

    private static bool RestoreExtra(TweakExtra item) => item.Kind switch
    {
        "task" => TaskOk(item.Value) && Sh.Run(Sh.Sys32("schtasks.exe"), $"/Change /TN \"{item.Value}\" /Enable", 20_000).Code == 0,
        "powerundo" => PowerUndoOk(item.Value) && Sh.Run(Sh.Sys32("powercfg.exe"), item.Value).Code == 0 &&
            Sh.Run(Sh.Sys32("powercfg.exe"), "/setactive SCHEME_CURRENT").Code == 0,
        "powerscheme" => NameOk(item.Value) && Sh.Run(Sh.Sys32("powercfg.exe"), $"/setactive {item.Value}").Code == 0,
        "hibernate" => item.Value == "on" && Sh.Run(Sh.Sys32("powercfg.exe"), "/hibernate on").Code == 0,
        "defender" => DefenderSnapshot.Restore(item.Value),
        _ => false
    };

    internal static BackupData ReadData(string file) => Normalize(JsonSerializer.Deserialize<BackupData>(File.ReadAllText(file))
        ?? throw new InvalidDataException("Запись отката повреждена."));

    internal static BackupData Normalize(BackupData data)
    {
        data.Reg ??= new(); data.Tasks ??= new(); data.Tweaks ??= new(); data.PowerUndo ??= new(); data.Extras ??= new();
        foreach (var task in data.Tasks.Where(t => !data.Extras.Any(e => e.Kind == "task" && e.Value == t)))
            data.Extras.Add(new TweakExtra { Kind = "task", Value = task, Tweak = data.Tweaks.Count == 1 ? data.Tweaks[0] : null });
        foreach (var undo in data.PowerUndo.Where(t => !data.Extras.Any(e => e.Kind == "powerundo" && e.Value == t)))
            data.Extras.Add(new TweakExtra { Kind = "powerundo", Value = undo, Tweak = "usb_pcie_power" });
        if (data.PowerScheme != null && !data.Extras.Any(e => e.Kind == "powerscheme")) data.Extras.Add(new TweakExtra { Kind = "powerscheme", Tweak = "power_high", Value = data.PowerScheme });
        if (data.HibernateWasOn && !data.Extras.Any(e => e.Kind == "hibernate")) data.Extras.Add(new TweakExtra { Kind = "hibernate", Tweak = "hibernate_off", Value = "on" });
        return data;
    }

    public static bool CreateRestorePoint()
    {
        Log.Write("Создание точки восстановления…");
        const string SR = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", FREQ = "SystemRestorePointCreationFrequency";
        var prevFreq = Reg.Get("HKLM", SR, FREQ);
        Reg.SetNoBackup("HKLM", SR, FREQ, 0);
        (int, string) res;
        try
        {
            res = Sh.PS($"Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" -ErrorAction SilentlyContinue; " +
                                 $"Checkpoint-Computer -Description 'Merno {DateTime.Now:yyyy-MM-dd HH:mm}' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'OK'", 300_000);
        }
        finally
        {
            // возвращаем прежнее ограничение частоты (по умолчанию значения нет — 1 точка в сутки)
            if (prevFreq == null) Reg.Delete("HKLM", SR, FREQ);
            else Reg.SetNoBackup("HKLM", SR, FREQ, prevFreq, prevFreq is int ? RegistryValueKind.DWord : RegistryValueKind.String);
        }
        var (code, outp) = res;
        outp ??= "";
        if (outp.Split('\n').Any(l => l.Trim() == "OK"))
        {
            var (found, name, when) = LastRestorePoint();
            if (found && name.StartsWith("Merno", StringComparison.OrdinalIgnoreCase) && (DateTime.Now - when).TotalMinutes < 10)
            {
                Log.Ok($"Точка восстановления «{name}» создана в {when:HH:mm}");
                return true;
            }
            Log.Warn("Точка восстановления не появилась. Возможно, Windows создаёт не больше одной в день");
            return false;
        }
        Log.Warn("Точку восстановления создать не удалось. Включите защиту системы: Пуск → «Создание точки восстановления»");
        return false;
    }
}
