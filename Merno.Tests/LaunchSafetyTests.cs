// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.IO;
using Merno.Core;

namespace Merno.Tests;

public class LaunchSafetyTests
{
    [Fact]
    public void MissingProgramDoesNotFallBackToPathOrCurrentDirectory()
    {
        Assert.Null(Sh.Resolve("merno-not-installed-test.exe", Array.Empty<string>()));
        Assert.Null(Sh.Resolve(@".\powershell.exe", Array.Empty<string>()));
        Assert.Null(Sh.Resolve(@"C:\missing-merno-test\winget.exe", Array.Empty<string>()));
        Assert.NotEqual(0, Sh.Run(null, "").Code);
        Assert.NotEqual(0, Sh.RunStream(null, "", null).Code);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), Sh.Resolve("cmd"));
    }

    private const string Id = "{12345678-1234-1234-1234-123456789abc}";

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, false, false)]
    public void CleanupOnlyDeletesOwnInactiveEntryAfterNormalBoot(bool normal, bool current, bool pending)
    {
        var calls = new List<string>();
        bool removed = SafeBoot.Cleanup(command => {
            calls.Add(command);
            return (0, command.Contains("{current}") ? (current ? Id : "ordinary") :
                command.Contains("{bootmgr}") ? (pending ? Id : "ordinary") : "Merno Safe Mode");
        }, Id, normal);
        Assert.Equal(normal && !current && !pending, removed);
        Assert.Equal(removed, calls.Contains($"/delete {Id}"));
    }
    [Fact]
    public void FailedDurableRecordRemovesNewEntryBeforeBootsequence()
    {
        var calls = new List<string>();
        Assert.Throws<IOException>(() => SafeBoot.Prepare(command => { calls.Add(command); return (0, Id); }, _ => throw new IOException("disk full")));
        Assert.Equal(new[] { "/copy {current} /d \"Merno Safe Mode\"", $"/delete {Id}" }, calls);
    }
    [Fact]
    public void CleanupRefusesMalformedOrUnownedEntries()
    {
        Assert.Throws<IOException>(() => SafeBoot.Cleanup(_ => throw new Exception("must not execute"), "{current} /delete", true));
        var calls = new List<string>();
        Assert.False(SafeBoot.Cleanup(command => { calls.Add(command); return (0, "another boot entry"); }, Id, true));
        Assert.DoesNotContain($"/delete {Id}", calls);
    }
    [Fact]
    public void FailedDeletionRemainsPending()
    {
        Assert.False(SafeBoot.Cleanup(command => command.StartsWith("/delete ") ? (1, "access denied") :
            (0, command == $"/enum {Id} /v" ? "Merno Safe Mode" : "ordinary"), Id, true));
    }
    [Theory]
    [InlineData(0, "ordinary", true)]
    [InlineData(0, Id, false)]
    [InlineData(1, "", false)]
    public void MissingEntryRequiresSuccessfulFullEnumerationBeforeClearingRecord(int code, string output, bool cleared)
    {
        Assert.Equal(cleared, SafeBoot.Cleanup(command => command == $"/enum {Id} /v" ? (1, "not found") :
            command == "/enum all /v" ? (code, output) : (0, "ordinary"), Id, true));
    }

    [Fact]
    public void SafeBootUsesOneTimeCopyAndNeverChangesCurrentEntry()
    {
        var calls = new List<string>();
        Assert.Equal(Id, SafeBoot.Prepare(command => { calls.Add(command); return (0, Id); }));
        Assert.Equal(new[] { "/copy {current} /d \"Merno Safe Mode\"", $"/set {Id} safeboot minimal", $"/displayorder {Id} /remove", $"/bootsequence {Id}" }, calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailedSafeBootPreparationRemovesCopyAndCannotReboot(int failedStep)
    {
        var calls = new List<string>();
        Assert.Throws<IOException>(() => SafeBoot.Prepare(command =>
        {
            calls.Add(command);
            return command.StartsWith("/delete ") ? (0, "") : (calls.Count - 1 == failedStep ? 1 : 0, Id);
        }));
        if (failedStep > 0) Assert.Equal($"/delete {Id}", calls.Last());
        Assert.DoesNotContain(calls, c => c.Contains("shutdown"));
        Assert.DoesNotContain("/set {current} safeboot minimal", calls);
    }
}
