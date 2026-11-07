// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.RegularExpressions;

namespace Merno.Core;

internal static class SystemVariables
{
    internal static string Expand(string command) => Expand(command, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        ["windir"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        ["SystemDrive"] = Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\'),
        ["ProgramFiles"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ["ProgramW6432"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ["ProgramFiles(x86)"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ["CommonProgramFiles"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
        ["CommonProgramFiles(x86)"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
        ["ProgramData"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
    });
    internal static string Expand(string command, IReadOnlyDictionary<string, string> system) =>
        Regex.Replace(command ?? "", "%([^%]+)%", match => system.TryGetValue(match.Groups[1].Value, out string value) && !string.IsNullOrEmpty(value)
            ? value : throw new IOException("Неизвестная переменная в системной команде удаления. Удалите программу через «Параметры» → «Приложения»."));
}
