// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Merno.Core;

internal static class AdvicePolicy
{
    // Same units as Sh.Size / Windows: 1 ГБ = 1024 МБ, 1 МБ = 1024 КБ.
    internal const long CleanupThreshold = 105L * (1L << 30) / 100;
    internal const long RecycleThreshold = 500L << 20;
    internal static bool SuggestCleanup(long bytes) => bytes > CleanupThreshold;
    internal static bool SuggestRecycle(long bytes) => bytes > RecycleThreshold;
    internal static string StartupKey(StartupItem item)
    {
        // Hash avoids persisting a command, file path or user name; entry identity survives app updates.
        string identity = JsonSerializer.Serialize(new[] { item.Kind.ToString(), item.Scope, item.Hive, item.RunKey, item.TaskPath, item.Name }
            .Select(value => (value ?? "").ToUpperInvariant()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
    internal static bool StartupIgnored(AppSettings settings, StartupItem item) =>
        settings.StartupAdviceIgnored?.Contains(StartupKey(item), StringComparer.OrdinalIgnoreCase) == true;
    internal static int StartupCandidates(AppSettings settings, IEnumerable<StartupItem> entries) =>
        entries.Count(item => item.Enabled && !item.Protected && !StartupIgnored(settings, item));
    internal static bool DriverKept(AppSettings settings, string deviceId, string version) =>
        !string.IsNullOrWhiteSpace(deviceId) && !string.IsNullOrWhiteSpace(version) && settings.KeptDriverVersions != null &&
        settings.KeptDriverVersions.Any(pair => string.Equals(pair.Key, deviceId, StringComparison.OrdinalIgnoreCase) && pair.Value == version);
    internal static IEnumerable<Issue> VisibleIssues(AppSettings settings, IEnumerable<Issue> issues) =>
        issues.Where(issue => issue.Kind != "gpu_driver_age" || issue.Severity >= 2 || !DriverKept(settings, issue.DeviceId, issue.DriverVersion));
    /// <summary>
    /// Человек уже применял настройки Windows через Merno (есть копия для отката с настройками) — значит, выбрал
    /// сам, что ему нужно. Тогда не уговариваем включить остальные.
    /// </summary>
    internal static bool TweaksChosen(IEnumerable<BackupData> backups) =>
        backups.Any(b => b?.Tweaks != null && b.Tweaks.Count > 0);
    internal static IEnumerable<CleanTarget> CleanupSelection(IEnumerable<CleanTarget> targets, CleanTarget only, Func<CleanTarget, bool> selected) =>
        targets.Where(target => only != null ? ReferenceEquals(target, only) : selected(target));
}
