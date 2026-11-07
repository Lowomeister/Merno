// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Diagnostics;

namespace Merno.Core;

/// <summary>Окружение повышенного процесса и его дочерних программ.</summary>
internal static class ProcessEnvironment
{
    // Запоминаем до замены TEMP/TMP; очистка пользовательских файлов не должна чистить установщики Merno.
    internal static readonly string UserTemp = Path.GetTempPath();
    private static readonly object Gate = new();
    private static string _temp;

    internal static string TempDirectory
    {
        get
        {
            lock (Gate) return _temp ??= Log.NewWorkDir("process-temp");
        }
    }

    internal static string ModulePath => string.Join(";",
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\Modules"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"WindowsPowerShell\Modules"));

    internal static bool IsRuntimeOverride(string name) =>
        name.StartsWith("COR_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("COMPLUS_", StringComparison.OrdinalIgnoreCase);

    // Отдельный чистый метод: тесты не создают Log и не меняют окружение тестового хоста.
    internal static void Sanitize(IDictionary<string, string> env, string temp, string modules)
    {
        foreach (var key in env.Keys.Where(k => IsRuntimeOverride(k) ||
                     k.Equals("TEMP", StringComparison.OrdinalIgnoreCase) ||
                     k.Equals("TMP", StringComparison.OrdinalIgnoreCase) ||
                     k.Equals("PSModulePath", StringComparison.OrdinalIgnoreCase)).ToArray())
            env.Remove(key);
        env["TEMP"] = temp;
        env["TMP"] = temp;
        env["PSModulePath"] = modules;
    }

    internal static void Configure(ProcessStartInfo info) => Sanitize(info.Environment, TempDirectory, ModulePath);

    internal static void Initialize()
    {
        _ = UserTemp;
        var env = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var key in env.Keys.Where(IsRuntimeOverride)) Environment.SetEnvironmentVariable(key, null);
        Environment.SetEnvironmentVariable("TEMP", TempDirectory);
        Environment.SetEnvironmentVariable("TMP", TempDirectory);
        Environment.SetEnvironmentVariable("PSModulePath", ModulePath);
    }
}
