// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Text.Json;

namespace Merno.Core;

internal static class DefenderSnapshot
{
    private static readonly string[] Booleans = { "DisableRealtimeMonitoring", "DisableBehaviorMonitoring", "DisableIOAVProtection" };
    /// <summary>Значения Windows по умолчанию: для копий отката из прежних версий Merno, где снимка Защитника нет.</summary>
    internal const string WindowsDefaults =
        "{\"DisableRealtimeMonitoring\":false,\"DisableBehaviorMonitoring\":false,\"DisableIOAVProtection\":false,\"MAPSReporting\":2,\"SubmitSamplesConsent\":1}";
    internal static string Capture()
    {
        var result = Sh.PS("Get-MpPreference -ErrorAction Stop | Select-Object DisableRealtimeMonitoring,DisableBehaviorMonitoring,DisableIOAVProtection,MAPSReporting,SubmitSamplesConsent | ConvertTo-Json -Compress", 30_000);
        if (result.Code != 0) throw new IOException("Не удалось сохранить настройки Защитника. Изменения отменены.");
        _ = Arguments(result.Output);
        return result.Output;
    }
    internal static string Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var parts = Booleans.Select(name => $"-{name} ${(root.GetProperty(name).GetBoolean() ? "true" : "false")}").ToList();
        foreach (var name in new[] { "MAPSReporting", "SubmitSamplesConsent" })
        {
            int value = root.GetProperty(name).GetInt32();
            if (value < 0 || value > (name == "MAPSReporting" ? 2 : 3)) throw new InvalidDataException("Неверное значение настройки Защитника.");
            parts.Add($"-{name} {value}");
        }
        return string.Join(" ", parts);
    }
    internal static bool Restore(string json)
    {
        var result = Sh.PS("Set-MpPreference " + Arguments(json) + " -ErrorAction Stop; 'MERNO_RESTORED'", 60_000);
        return result.Code == 0 && result.Output.Split('\n').Any(s => s.Trim() == "MERNO_RESTORED");
    }
}
