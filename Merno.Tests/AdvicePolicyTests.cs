// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using Merno.Core;

namespace Merno.Tests;

public class AdvicePolicyTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(159L << 20, false)]
    [InlineData(1L << 30, false)]
    [InlineData(1_127_428_914, false)]
    [InlineData(1_127_428_915, false)]
    [InlineData(1_127_428_916, true)]
    [InlineData(2L << 30, true)]
    public void CleanupAppearsOnlyAboveOnePointZeroFiveGb(long bytes, bool expected) =>
        Assert.Equal(expected, AdvicePolicy.SuggestCleanup(bytes));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(524_287_999, false)]
    [InlineData(524_288_000, false)]
    [InlineData(524_288_001, true)]
    public void RecycleAppearsOnlyAboveFiveHundredMb(long bytes, bool expected) =>
        Assert.Equal(expected, AdvicePolicy.SuggestRecycle(bytes));

    private static StartupItem StartupEntry(string name = "Discord", string hive = "HKCU") => new()
    {
        Name = name, Hive = hive, Scope = "Пользователь", Kind = StartKind.Registry,
        RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Enabled = true
    };

    [Fact]
    public void ChoicesSurviveSettingsReloadAndApplyToFreshlyDetectedEntries()
    {
        string path = Path.GetTempFileName();
        try
        {
            var original = StartupEntry();
            var settings = new AppSettings
            {
                StartupAdviceIgnored = new() { AdvicePolicy.StartupKey(original) },
                KeptDriverVersions = new() { [@"PCI\GPU-A"] = "32.0.15.6636" }
            };
            settings.SaveTo(path);
            var reloaded = AppSettings.LoadFrom(path, message => throw new Exception(message));
            var detected = StartupEntry("DISCORD", "hkcu");
            detected.Command = @"C:\Apps\Discord\new-version\Discord.exe --start";
            detected.Title = "Новое название Discord";
            Assert.NotSame(settings, reloaded);
            Assert.True(AdvicePolicy.StartupIgnored(reloaded, detected));
            Assert.True(AdvicePolicy.DriverKept(reloaded, @"pci\gpu-a", "32.0.15.6636"));
            Assert.False(AdvicePolicy.DriverKept(reloaded, @"PCI\GPU-A", "32.0.15.9999"));
            Assert.False(AdvicePolicy.DriverKept(reloaded, @"PCI\GPU-B", "32.0.15.6636"));
            Assert.False(AdvicePolicy.StartupIgnored(reloaded, StartupEntry(hive: "HKLM")));
            Assert.False(AdvicePolicy.StartupIgnored(reloaded, StartupEntry(" Discord")));

            reloaded.StartupAdviceIgnored.Clear();
            reloaded.KeptDriverVersions.Clear();
            reloaded.SaveTo(path);
            var reset = AppSettings.LoadFrom(path, message => throw new Exception(message));
            Assert.False(AdvicePolicy.StartupIgnored(reset, detected));
            Assert.False(AdvicePolicy.DriverKept(reset, @"PCI\GPU-A", "32.0.15.6636"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void StartupCountOmitsChosenProtectedAndDisabledEntries()
    {
        var chosen = StartupEntry();
        var protectedEntry = StartupEntry("Antivirus"); protectedEntry.Protected = true;
        var disabled = StartupEntry("Disabled"); disabled.Enabled = false;
        var settings = new AppSettings { StartupAdviceIgnored = new() { AdvicePolicy.StartupKey(chosen) } };
        Assert.Equal(1, AdvicePolicy.StartupCandidates(settings, new[] { chosen, protectedEntry, disabled, StartupEntry("Other") }));
    }

    [Fact]
    public void DriverChoiceHidesOnlyAgeAdviceForThatDeviceAndVersion()
    {
        var settings = new AppSettings { KeptDriverVersions = new() { ["gpu-a"] = "566.36" } };
        var ignored = new Issue { Kind = "gpu_driver_age", DeviceId = "gpu-a", DriverVersion = "566.36", Severity = 1 };
        var newVersion = new Issue { Kind = "gpu_driver_age", DeviceId = "gpu-a", DriverVersion = "567.00", Severity = 1 };
        var otherGpu = new Issue { Kind = "gpu_driver_age", DeviceId = "gpu-b", DriverVersion = "566.36", Severity = 1 };
        var critical = new Issue { Kind = "gpu_driver_age", DeviceId = "gpu-a", DriverVersion = "566.36", Severity = 2 };
        var deviceError = new Issue { Kind = "device_error", DeviceId = "gpu-a", DriverVersion = "566.36", Severity = 1 };
        Assert.Equal(new[] { newVersion, otherGpu, critical, deviceError }, AdvicePolicy.VisibleIssues(settings, new[] { ignored, newVersion, otherGpu, critical, deviceError }));
    }

    [Fact]
    public void OldOrNullSettingsHaveNoImplicitExceptions()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"StartupAdviceIgnored\":null,\"KeptDriverVersions\":null}");
        Assert.False(AdvicePolicy.StartupIgnored(settings, StartupEntry()));
        Assert.False(AdvicePolicy.DriverKept(settings, "gpu", "566.36"));
        Assert.False(AdvicePolicy.DriverKept(new AppSettings(), "", ""));
    }

    [Fact]
    public void RecycleShortcutNeverIncludesOtherCheckedCategories()
    {
        var cache = new CleanTarget { Id = "cache" };
        var bin = new CleanTarget { Id = "recycle", Recycle = true };
        Assert.Equal(new[] { bin }, AdvicePolicy.CleanupSelection(new[] { cache, bin }, bin, _ => true));
        Assert.Equal(new[] { cache }, AdvicePolicy.CleanupSelection(new[] { cache, bin }, null, target => target == cache));
    }

    [Fact]
    public void ПослеПримененияНастроекОстальныеНеПредлагаются()
    {
        Assert.False(AdvicePolicy.TweaksChosen(Array.Empty<BackupData>()));
        // копия от очистки или автозагрузки — без настроек Windows: советы остаются
        Assert.False(AdvicePolicy.TweaksChosen(new[] { new BackupData { Title = "Очистка" }, null }));
        Assert.True(AdvicePolicy.TweaksChosen(new[] { new BackupData(), new BackupData { Tweaks = { "ads" } } }));
    }
}
