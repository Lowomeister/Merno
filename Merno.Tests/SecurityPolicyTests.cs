// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using System.Security.Cryptography;
using Merno.Core;

namespace Merno.Tests;

public class SecurityPolicyTests
{
    [Fact]
    public void CompactExceptionPreservesCauseBeforeFrameworkStack()
    {
        string text = "InvalidOperationException: important cause\n   at Merno.Window.Action()\n" +
            string.Concat(Enumerable.Repeat("   at System.Windows.Dispatcher.Invoke()\n", 500));
        string compact = CrashReport.CompactException(text);
        Assert.StartsWith("InvalidOperationException: important cause", compact);
        Assert.Contains("Merno.Window.Action", compact);
        Assert.DoesNotContain("System.Windows.Dispatcher", compact);
        Assert.True(compact.Length <= 1500);
    }
    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("payload/../../outside.exe")]
    [InlineData("C:/outside.exe")]
    [InlineData("payload\\outside.exe")]
    public void InstalledKitRejectsPathsOutsideDestination(string entry)
    {
        string zip = Path.GetTempFileName();
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Update))
                archive.CreateEntry(entry);
            Assert.Throws<InvalidDataException>(() => Updater.ExtractKit(zip, destination));
            Assert.False(Directory.Exists(destination));
        }
        finally { File.Delete(zip); if (Directory.Exists(destination)) Directory.Delete(destination, true); }
    }

    [Theory]
    [InlineData("C:/Users/HiddenName/AppData/Temp/x")]
    [InlineData(@"\\server\private\file")]
    [InlineData("user@example.com")]
    [InlineData("https://example.com/?token=secret")]
    [InlineData(@"DOMAIN\HiddenName")]
    public void LogsRemoveCommonPrivateIdentifiers(string source)
    {
        var text = CrashReport.Sanitize(source, 10000);
        Assert.DoesNotContain("HiddenName", text);
        Assert.DoesNotContain("private", text);
        Assert.DoesNotContain("user@example.com", text);
        Assert.DoesNotContain("secret", text);
    }

    [Fact]
    public async Task ProcessOutputIsDrainedButBounded()
    {
        var result = await BoundedOutput.ReadAsync(new StringReader(new string('a', 50000)), limit: 1000);
        Assert.True(result.Truncated);
        Assert.Equal(1000, result.Text.Length);
    }

    [Fact]
    public void AllMetadataFieldsAreSignedAndExpiredReleaseIsRejected()
    {
        using var key = RSA.Create(2048);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var info = new UpdateInfo { Version = "1.0.0", Url = "Merno-1.0.0.bin", Sha256 = new string('a',64), Size = 100,
            Format = "installed-kit-v1", Published = now, Expires = now + 100, Notes = "release" };
        info.SigMeta = Convert.ToBase64String(key.SignData(UpdateMetadata.Payload(info), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.True(UpdateMetadata.Verify(info,key,now));
        info.Notes = "tampered";
        Assert.False(UpdateMetadata.Verify(info,key,now));
        info.Notes = "release";
        Assert.False(UpdateMetadata.Verify(info,key,now + 101));
        info.Url = "https://untrusted/payload";
        Assert.False(UpdateMetadata.Verify(info,key,now));
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.0.0.1", "1.0.0", false)]   // номер только X.Y.Z
    [InlineData(" 1.0.1\r\n", "1.0.0", true)]
    [InlineData("1.0.1", "1.0.1", false)]   // уже установлено
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("1.0.1; del", "1.0.0", false)]
    [InlineData("", "1.0.0", false)]
    [InlineData(null, "1.0.0", false)]
    public void FailedUpdateIsReportedOnlyWhenOlderVersionRuns(string pending, string current, bool failed) =>
        Assert.Equal(failed, Updater.FailedUpdate(pending, System.Version.Parse(current)) != null);

    static UpdateInfo SignedMeta(RSA key, long published, long expires)
    {
        var info = new UpdateInfo { Version = "1.0.1", Url = "Merno-1.0.1.bin", Sha256 = new string('b',64), Size = 100,
            Format = "installed-kit-v1", Published = published, Expires = expires, Notes = "release" };
        info.SigMeta = Convert.ToBase64String(key.SignData(UpdateMetadata.Payload(info), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return info;
    }

    [Fact]
    public void ExpiredSignatureIsAcceptedOnlyOverPinnedHttps()
    {
        using var key = RSA.Create(2048);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var info = SignedMeta(key, now - 200 * 86400L, now - 120 * 86400L);   // выпуск подписан давно, более новых нет
        var check = UpdateMetadata.Check(info, key, now);
        Assert.Equal(MetaCheck.Expired, check);
        Assert.True(UpdateMetadata.Accept(check, pinned: true));
        Assert.False(UpdateMetadata.Accept(check, pinned: false));
        Assert.False(UpdateMetadata.Verify(info, key, now));
    }

    [Fact]
    public void ClockBehindIsAcceptedOnlyOverPinnedHttps()
    {
        using var key = RSA.Create(2048);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var info = SignedMeta(key, now + 3600, now + 30 * 86400L);   // часы ПК отстают на час от выпуска
        var check = UpdateMetadata.Check(info, key, now);
        Assert.Equal(MetaCheck.ClockBehind, check);
        Assert.True(UpdateMetadata.Accept(check, pinned: true));
        Assert.False(UpdateMetadata.Accept(check, pinned: false));
        Assert.Equal(MetaCheck.Ok, UpdateMetadata.Check(info, key, now + 3600));
    }

    [Fact]
    public void ForgedOrOverlongMetadataIsNeverAccepted()
    {
        using var key = RSA.Create(2048);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expired = SignedMeta(key, now - 200 * 86400L, now - 120 * 86400L);
        expired.Expires = now + 86400;   // подпись не сходится: срок изменён
        Assert.Equal(MetaCheck.Invalid, UpdateMetadata.Check(expired, key, now));
        Assert.False(UpdateMetadata.Accept(UpdateMetadata.Check(expired, key, now), pinned: true));

        using var other = RSA.Create(2048);
        var foreign = SignedMeta(other, now - 200 * 86400L, now - 120 * 86400L);   // просрочен и подписан другим ключом
        Assert.Equal(MetaCheck.Invalid, UpdateMetadata.Check(foreign, key, now));
        Assert.False(UpdateMetadata.Accept(UpdateMetadata.Check(foreign, key, now), pinned: true));

        var overlong = SignedMeta(key, now, now + UpdateMetadata.MaxLifetime + 1);   // срок длиннее MaxLifetime: такой выпуск отклоняется
        Assert.Equal(MetaCheck.Invalid, UpdateMetadata.Check(overlong, key, now));

        var fresh = SignedMeta(key, now, now + 86400);
        Assert.Equal(MetaCheck.Ok, UpdateMetadata.Check(fresh, key, now));
        Assert.True(UpdateMetadata.Accept(MetaCheck.Ok, pinned: false));
    }
}
