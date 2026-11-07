// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Diagnostics;
using System.IO;
using Merno.Core;

namespace Merno.Tests;

public class CleanerTests
{
    private const string Profile = @"C:\Users\Example";
    private static List<CleanTarget> Catalog() => Cleaner.Targets(Profile, @"C:\Windows", @"C:\ProgramData");
    private static string[] Allowed() => Catalog().SelectMany(t => t.Paths).ToArray();

    [Theory]
    [InlineData(@"C:\Windows\Installer")]
    [InlineData(@"C:\ProgramData\Merno")]
    [InlineData(@"D:\любая\tmp")]
    [InlineData(@"D:\Temp")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Users\Example\AppData\Local\Documents")]
    [InlineData(@"C:\Users\Example\AppData\Local")]
    [InlineData(@"C:\Users\Example\AppData\Local\Temp\subfolder")]
    [InlineData(@"C:\Windows\Temp-other")]
    [InlineData(@"C:\Windows\Temp\..\Installer")]
    [InlineData(@"C:\")]
    [InlineData(@"C:Windows\Temp")]
    [InlineData(@"\\server\share\Temp")]
    [InlineData(@"\\?\C:\Windows\Temp")]
    [InlineData(@"C:\Windows\Temp:stream")]
    [InlineData(@"C:\Windows\Temp.")]
    [InlineData(@"C:\Windows\Temp ")]
    [InlineData("")]
    [InlineData(null)]
    public void OnlyExactCatalogRootsAreAllowed(string path) => Assert.False(CleanupPaths.Allowed(path, Allowed()));

    [Theory]
    [InlineData(@"C:\Windows\Temp")]
    [InlineData(@"c:\windows\TEMP\")]
    [InlineData(@"C:\Windows\SoftwareDistribution\Download")]
    [InlineData(@"C:\Users\Example\AppData\Local\Temp")]
    [InlineData(@"C:\Users\Example\AppData\Local\Microsoft\Windows\Explorer")]
    public void CatalogRootsAreAllowed(string path) => Assert.True(CleanupPaths.Allowed(path, Allowed()));

    [Fact]
    public void TempAndAppDataAreDerivedFromProfileInsteadOfEnvironment()
    {
        Assert.Equal(Profile + @"\AppData\Local\Temp", Catalog().Single(t => t.Id == "tmp_user").Paths.Single());
        Assert.Equal(Profile + @"\AppData\Roaming\discord", CleanupPaths.Expand(@"%APPDATA%\discord", Profile, @"C:\Windows", @"C:\ProgramData"));
        Assert.Equal(Profile + @"\AppData\Local\Temp\Battlestate Games", CleanupPaths.Expand(@"%TEMP%\Battlestate Games", Profile, @"C:\Windows", @"C:\ProgramData"));
        Assert.Throws<ArgumentException>(() => CleanupPaths.Expand(@"%UNKNOWN%\Temp", Profile, @"C:\Windows", @"C:\ProgramData"));
    }

    [Fact]
    public void MutableTargetCannotOverridePathFilterAgeOrOperation()
    {
        var catalog = Catalog();
        var requested = new CleanTarget { Id = "thumbs", Filter = "*", Paths = new[] { @"C:\Windows\Installer" }, Dism = true };
        var canonical = Cleaner.CanonicalTarget(requested, catalog);
        Assert.Equal("thumbcache_*.db", canonical.Filter);
        Assert.False(canonical.Dism);
        Assert.Equal(Profile + @"\AppData\Local\Microsoft\Windows\Explorer", canonical.Paths.Single());
        Assert.Null(Cleaner.CanonicalTarget(new CleanTarget { Id = "unknown" }, catalog));
        Assert.Null(Cleaner.CanonicalTarget(null, catalog));
        Assert.Equal(168, Cleaner.CanonicalTarget(new CleanTarget { Id = "logs", MinAgeHours = 0 }, catalog).MinAgeHours);
    }

    [Theory]
    [InlineData("logs")]
    [InlineData("dumps")]
    [InlineData("leftovers")]
    public void DiagnosticAndPersonalDataRequireExplicitSelection(string id) => Assert.False(Catalog().Single(t => t.Id == id).Default);

    [Fact]
    public void RealProfileComesFromWindowsToken()
    {
        Assert.NotNull(CleanupPaths.Normalize(CleanupPaths.Profile));
        Assert.True(Directory.Exists(CleanupPaths.Profile));
    }

    [Fact]
    public void CleanupDeletesOldFilesButPreservesFreshAndLockedFiles()
    {
        using var area = new TestArea();
        string old = area.File("nested/old.tmp", 10);
        string fresh = area.File("nested/fresh.tmp", 0);
        string locked = area.File("locked.tmp", 10);
        using var held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        var target = new CleanTarget { Paths = new[] { area.Root }, SkipBusy = true };
        Cleaner.CleanFiles(target, new[] { area.Root });
        Assert.False(System.IO.File.Exists(old));
        Assert.True(System.IO.File.Exists(fresh));
        Assert.True(System.IO.File.Exists(locked));
        Assert.True(Directory.Exists(Path.Combine(area.Root, "nested")));
    }

    [Fact]
    public void AgeLimitSurvivesFinalDirectoryCleanupEvenWithoutSkipBusy()
    {
        using var area = new TestArea();
        string old = area.File("old.dmp", 24 * 8);
        string fresh = area.File("nested/recent.dmp", 24);
        var target = new CleanTarget { Paths = new[] { area.Root }, MinAgeHours = 168 };
        Cleaner.CleanFiles(target, new[] { area.Root });
        Assert.False(System.IO.File.Exists(old));
        Assert.True(System.IO.File.Exists(fresh));
    }

    [Fact]
    public void FileMaskAndRootBoundaryArePreserved()
    {
        using var area = new TestArea();
        string thumbnail = area.File("thumbcache_1.db", 10);
        string other = area.File("important.db", 10);
        string outside = area.File("other/cache.tmp", 10);
        var target = new CleanTarget { Paths = new[] { area.Root }, Filter = "thumbcache_*.db" };
        Cleaner.CleanFiles(target, new[] { area.Root });
        Assert.False(System.IO.File.Exists(thumbnail));
        Assert.True(System.IO.File.Exists(other));
        Cleaner.CleanFiles(new CleanTarget { Paths = new[] { Path.Combine(area.Root, "other") } }, new[] { area.Root });
        Assert.True(System.IO.File.Exists(outside));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JunctionInRootOrParentCannotRedirectCleanup(bool parent)
    {
        using var area = new TestArea();
        string sentinel = area.File("outside/Temp/keep.tmp", 24 * 8);
        string alias = Path.Combine(area.Root, "alias");
        string target = Path.Combine(area.Root, "outside", parent ? "" : "Temp");
        CreateJunction(alias, target);
        string root = parent ? Path.Combine(alias, "Temp") : alias;
        Assert.False(Cleaner.Safe(root, new[] { root }));
        Cleaner.CleanFiles(new CleanTarget { Paths = new[] { root } }, new[] { root });
        Assert.True(System.IO.File.Exists(sentinel));
    }

    [Fact]
    public void JunctionInsideAllowedRootIsNotTraversed()
    {
        using var area = new TestArea();
        string sentinel = area.File("outside/keep.tmp", 24 * 8);
        string old = area.File("clean/delete.tmp", 24 * 8);
        string clean = Path.Combine(area.Root, "clean");
        string link = Path.Combine(clean, "link");
        CreateJunction(link, Path.Combine(area.Root, "outside"));
        Cleaner.CleanFiles(new CleanTarget { Paths = new[] { clean } }, new[] { clean });
        Assert.False(System.IO.File.Exists(old));
        Assert.True(System.IO.File.Exists(sentinel));
        Assert.True(Directory.Exists(link));
    }

    private static void CreateJunction(string alias, string target)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Arguments = $"/d /c mklink /J \"{alias}\" \"{target}\"";
        using (var process = Process.Start(start))
        {
            if (!process.WaitForExit(10_000)) { process.Kill(true); throw new TimeoutException("Junction creation timed out"); }
            Assert.Equal(0, process.ExitCode);
        }
    }

    private sealed class TestArea : IDisposable
    {
        // Только выделенная папка рядом с тестовой сборкой, никакие настоящие кэши Windows не затрагиваются.
        internal string Root { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "cleanup-test-" + Guid.NewGuid().ToString("N")));
        internal TestArea() => Directory.CreateDirectory(Root);
        internal string File(string relative, int ageHours)
        {
            string path = Path.GetFullPath(Path.Combine(Root, relative));
            if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(nameof(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            System.IO.File.WriteAllText(path, "test");
            System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-ageHours));
            return path;
        }
        public void Dispose() => SafeFs.DeleteTree(Root, SafeFs.RootFinal(AppContext.BaseDirectory), true);
    }
}
