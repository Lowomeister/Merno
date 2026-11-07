// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using Merno.Core;

namespace Merno.Tests;

public class ReliabilityTests
{
    [Fact]
    public async Task DownloadEnforcesSizeAndIdleTimeout()
    {
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => DownloadTransfer.CopyAsync(new MemoryStream(new byte[12]), destination, 10, TimeSpan.FromSeconds(1), default));
        await Assert.ThrowsAsync<TimeoutException>(() => DownloadTransfer.CopyAsync(new SilentStream(), destination, 10, TimeSpan.FromMilliseconds(50), default));
    }

    [Fact]
    public void CorruptSettingsArePreservedBeforeDefaultsAreSaved()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(path, "broken");
            string warning = null;
            var settings = AppSettings.LoadFrom(path, s => warning = s);
            Assert.NotNull(warning);
            Assert.False(settings.CrashReports);
            Assert.Equal("broken", File.ReadAllText(Assert.Single(Directory.GetFiles(root, "*.corrupt-*"))));
            settings.SaveTo(path);
            Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)));
        }
        finally { SafeFs.DeleteTree(root, SafeFs.RootFinal(AppContext.BaseDirectory), true); }
    }

    private sealed class SilentStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(new TaskCompletionSource<int>().Task);
    }
}
