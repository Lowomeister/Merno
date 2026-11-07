// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using Merno.Core;

namespace Merno.Tests;

public class BackupSafetyTests
{
    private static RegEntry Entry(int value = 1) => new() { Hive = "HKCU", Path = @"Control Panel\Desktop", Name = "MenuShowDelay", N = value, Tweak = "test" };

    [Fact]
    public void OriginalValueIsOnDiskBeforeMutationAndRecordedOnlyOnce()
    {
        using var area = new Area();
        using (Backup.SessionAt(area.File))
        {
            Backup.RecordEntry(Entry(1));
            Assert.Equal(1, Backup.ReadData(area.File).Reg.Single().N);
            Backup.RecordEntry(Entry(2));
            Assert.Equal(1, Backup.ReadData(area.File).Reg.Single().N);
        }
        Assert.Null(Backup.Current);
    }

    [Fact]
    public void CannotChangeAnythingWhenOriginalValueCannotBeSaved()
    {
        using var area = new Area();
        bool changed = false;
        using (Backup.SessionAt(Path.Combine(area.Root, "missing", "backup.json")))
            Assert.Throws<DirectoryNotFoundException>(() => { Backup.RecordEntry(Entry()); changed = true; });
        Assert.False(changed);
        Assert.Null(Backup.Current);
    }

    [Fact]
    public void AtomicReplacementFailurePreservesPreviousFile()
    {
        using var area = new Area();
        File.WriteAllText(area.File, "original");
        Assert.Throws<IOException>(() => AtomicFile.Write(area.File, "replacement", (_, _) => throw new IOException("failure")));
        Assert.Equal("original", File.ReadAllText(area.File));
        Assert.Empty(Directory.GetFiles(area.Root, "*.tmp"));
    }

    [Fact]
    public void FailedFinalSaveStillClosesSession()
    {
        using var area = new Area();
        var scope = Backup.SessionAt(area.File);
        Backup.RecordEntry(Entry());
        File.Delete(area.File);
        Directory.CreateDirectory(area.File);
        Assert.Throws<UnauthorizedAccessException>(() => scope.Dispose());
        Assert.Null(Backup.Current);
    }

    [Fact]
    public async Task ConcurrentOperationsHaveSeparateSessions()
    {
        using var one = new Area();
        using var two = new Area();
        using (Backup.SessionAt(one.File))
        {
            Backup.RecordEntry(Entry(1));
            await Task.Run(() =>
            {
                Backup.End(); // локальный контекст дочерней операции
                using (Backup.SessionAt(two.File)) Backup.RecordEntry(Entry(2));
            });
            Assert.Equal(1, Backup.Current.Reg.Single().N);
        }
        Assert.Equal(1, Backup.ReadData(one.File).Reg.Single().N);
        Assert.Equal(2, Backup.ReadData(two.File).Reg.Single().N);
    }

    [Fact]
    public void FailedReplayDoesNotArchiveOrLoseEntriesAndCanBeRetried()
    {
        using var area = new Area();
        Backup.WriteData(area.File, new BackupData { Reg = new() { Entry() } });
        Assert.Throws<IOException>(() => Backup.ReplayFile(area.File, _ => false, _ => true));
        Assert.True(File.Exists(area.File));
        Assert.Single(Backup.ReadData(area.File).Reg);
        Backup.ReplayFile(area.File, _ => true, _ => true);
        Assert.False(File.Exists(area.File));
        Assert.Single(Directory.GetFiles(area.Root, "rolledback-*.json"));
    }

    [Fact]
    public void TweakReplayUsesOldestValueAndRetainsAllSourcesOnFailure()
    {
        var old = new BackupData { Reg = new() { Entry(1) } };
        var newer = new BackupData { Reg = new() { Entry(2) } };
        var restored = new List<long>();
        Assert.Equal(1, Backup.ReplayTweak(new[] { old, newer }, "test", e => { restored.Add(e.N); return false; }, _ => true));
        Assert.Equal(new long[] { 1 }, restored);
        Assert.Single(old.Reg); Assert.Single(newer.Reg);
    }

    [Fact]
    public void OldBackupNullCollectionsAndLegacyExtrasAreSupported()
    {
        var old = new BackupData { Reg = null, Tasks = null, Extras = null, PowerUndo = null, Tweaks = null, PowerScheme = "scheme" };
        Assert.Equal(0, Backup.Replay(old, _ => true, _ => true));
        Assert.Equal("power_high", old.Extras.Single().Tweak);
        Assert.Equal(1, Backup.Replay(new BackupData { Tweaks = new() { "defender_off" } }, _ => true, _ => true));
    }

    // Удаление лишних приложений не сохраняет копию: вернуть нечего, но откат всей копии не должен падать.
    [Fact]
    public void TweakWithoutSnapshotIsNotAFailureWhenHandled()
    {
        var data = new BackupData { Tweaks = new() { "debloat", "defender_off" } };
        var asked = new List<string>();
        Assert.Equal(0, Backup.Replay(data, _ => true, _ => true, id => { asked.Add(id); return true; }));
        Assert.Equal(new[] { "debloat", "defender_off" }, asked);
        Assert.Equal(1, Backup.Replay(data, _ => true, _ => true, id => id == "debloat"));
    }

    [Fact]
    public void LegacyRestoreForOneTweakRunsOnceAcrossOldCopies()
    {
        var first = new BackupData { Tweaks = new() { "classic_menu" } };
        var second = new BackupData { Tweaks = new() { "classic_menu" } };
        int calls = 0;
        Assert.Equal(0, Backup.ReplayTweak(new[] { first, second }, "classic_menu", _ => true, _ => true, _ => { calls++; return true; }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void DefenderSnapshotInNewerCopyMakesLegacyRestoreUnnecessary()
    {
        var legacy = new BackupData { Tweaks = new() { "defender_off" } };
        var newer = new BackupData { Tweaks = new() { "defender_off" },
            Extras = new() { new TweakExtra { Kind = "defender", Value = "{}", Tweak = "defender_off" } } };
        int legacyCalls = 0;
        Assert.Equal(0, Backup.ReplayTweak(new[] { legacy, newer }, "defender_off", _ => true, _ => true, _ => { legacyCalls++; return true; }));
        Assert.Equal(0, legacyCalls);
    }

    [Fact]
    public void SingleTweakRevertRestoresRegistryNewestFirst()
    {
        var data = new BackupData { Reg = new() { Entry(1), Entry(2), Entry(3) } };
        foreach (var e in data.Reg) e.Tweak = "test";
        data.Reg[1].Name = "B"; data.Reg[2].Name = "C";
        var order = new List<long>();
        Backup.ReplayTweak(new[] { data }, "test", e => { order.Add(e.N); return true; }, _ => true);
        Assert.Equal(new long[] { 3, 2, 1 }, order);
    }

    [Fact]
    public void RegistryReplayOnlyAcceptsKnownLocations()
    {
        Assert.True(Backup.EntryAllowed(Entry()));
        Assert.False(Backup.EntryAllowed(new RegEntry { Hive = "HKLM", Path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", Name = "Shell" }));
        Assert.False(Backup.EntryAllowed(new RegEntry { Hive = "anything", Path = Entry().Path, Name = Entry().Name }));
        foreach (var tweak in Catalog.All())
            foreach (var op in tweak.Reg)
                Assert.True(Backup.EntryAllowed(new RegEntry { Hive = op.Hive, Path = op.Path, Name = op.Name }));
    }

    [Fact]
    public void DefenderRestoresSavedPreferencesInsteadOfDefaults()
    {
        const string snapshot = "{\"DisableRealtimeMonitoring\":true,\"DisableBehaviorMonitoring\":false,\"DisableIOAVProtection\":true,\"MAPSReporting\":0,\"SubmitSamplesConsent\":2}";
        string args = DefenderSnapshot.Arguments(snapshot);
        Assert.Contains("-DisableRealtimeMonitoring $true", args);
        Assert.Contains("-MAPSReporting 0", args);
        Assert.Contains("-SubmitSamplesConsent 2", args);
        Assert.ThrowsAny<Exception>(() => DefenderSnapshot.Arguments("{}"));
    }

    [Fact]
    public void RegistryMultiStringsAreComparedByContents()
    {
        Assert.False(Reg.Equal(new[] { "a" }, new[] { "b" }));
        Assert.True(Reg.Equal(new[] { "a", "b" }, new[] { "a", "b" }));
        Assert.True(Reg.Equal(unchecked((int)0x80000001), 0x80000001L));
    }

    private sealed class Area : IDisposable
    {
        internal string Root { get; } = Path.Combine(AppContext.BaseDirectory, "backup-test-" + Guid.NewGuid().ToString("N"));
        internal string File => Path.Combine(Root, "backup-test.json");
        internal Area() => Directory.CreateDirectory(Root);
        public void Dispose() => SafeFs.DeleteTree(Root, SafeFs.RootFinal(AppContext.BaseDirectory), true);
    }
}
