// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using System.Text.RegularExpressions;

namespace Merno.Tests;

/// <summary>
/// Скрипты PowerShell внутри кода Merno. COM-объекты Windows Update создаются только через
/// New-Object -ComObject: без ключа PowerShell ищет .NET-тип и пишет «Не удается найти тип
/// [Microsoft.Update.UpdateColl]».
/// </summary>
public class PowerShellScriptTests
{
    private static string SourceRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Merno.csproj"))) return d.FullName;
        throw new DirectoryNotFoundException("Не найдена папка с Merno.csproj");
    }

    [Fact]
    public void ComОбъекты_создаются_с_ключом_ComObject()
    {
        var root = SourceRoot();
        var bad = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains("Merno.Tests")) continue;
            int n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                if (Regex.IsMatch(line, @"New-Object\s+(?!-ComObject)Microsoft\.Update\.", RegexOptions.IgnoreCase))
                    bad.Add(Path.GetRelativePath(root, file) + ":" + n);
            }
        }
        Assert.Empty(bad);
    }
}
