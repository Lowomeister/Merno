// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.RegularExpressions;

namespace Merno.Core;

internal static class SafeBoot
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    internal static bool NormalBoot => GetSystemMetrics(67) == 0;
    internal static string PendingFile => Path.Combine(Log.DataDir, "safe-boot-entry.txt");
    internal static void Remember(string id) => AtomicFile.Write(PendingFile, id);
    internal static bool CleanupStored()
    {
        if (!File.Exists(PendingFile)) return true;
        bool removed = Cleanup(command => Sh.Run(Sh.Sys32("bcdedit.exe"), command, 20_000), File.ReadAllText(PendingFile).Trim(), NormalBoot);
        if (removed) File.Delete(PendingFile);
        return removed;
    }
    internal static bool Cleanup(Func<string, (int Code, string Output)> run, string id, bool normal)
    {
        if (!normal) return false;
        if (!Regex.IsMatch(id ?? "", @"\A\{[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\}\z"))
            throw new IOException("Сведения о прошлом запуске в безопасном режиме повреждены.");
        var current = run("/enum {current} /v");
        var manager = run("/enum {bootmgr} /v");
        if (current.Code != 0 || manager.Code != 0 || current.Output.Contains(id, StringComparison.OrdinalIgnoreCase) || manager.Output.Contains(id, StringComparison.OrdinalIgnoreCase)) return false;
        var entry = run($"/enum {id} /v");
        if (entry.Code != 0)
        {
            var all = run("/enum all /v");
            return all.Code == 0 && !all.Output.Contains(id, StringComparison.OrdinalIgnoreCase);
        }
        if (!entry.Output.Contains("Merno Safe Mode", StringComparison.Ordinal)) return false;
        return run($"/delete {id}").Code == 0;
    }
    // Отдельная запись BCD запускается через одноразовый bootsequence. Обычная запись не меняется,
    // возврат не зависит от входа пользователя и выполнения RunOnce в безопасном режиме.
    internal static string Prepare(Func<string, (int Code, string Output)> run, Action<string> remember = null)
    {
        var copy = run("/copy {current} /d \"Merno Safe Mode\"");
        var match = Regex.Match(copy.Output ?? "", @"\{[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\}");
        if (copy.Code != 0 || !match.Success) throw new IOException("Не удалось подготовить запуск в безопасном режиме.");
        string id = match.Value;
        try
        {
            remember?.Invoke(id);
            foreach (var command in new[] { $"/set {id} safeboot minimal", $"/displayorder {id} /remove", $"/bootsequence {id}" })
                if (run(command).Code != 0) throw new IOException("Не удалось настроить запуск в безопасном режиме.");
            return id;
        }
        catch
        {
            if (run($"/delete {id}").Code != 0)
                throw new IOException("Подготовка безопасного режима не завершена. Не перезагружайте компьютер и проверьте параметры загрузки Windows.");
            throw;
        }
    }
}
