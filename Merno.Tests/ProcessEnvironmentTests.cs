// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright © 2026 Merno contributors

using System.Diagnostics;
using System.IO;
using Merno.Core;

namespace Merno.Tests;

public class ProcessEnvironmentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PowerShellSetsSystemModulesBeforeExecutingScript(bool stream)
    {
        var script = Sh.PowerShellScript("Get-AppxPackage", stream);
        Assert.StartsWith("$env:PSModulePath='" + Sh.Esc(ProcessEnvironment.ModulePath) + "';", script);
        Assert.EndsWith("Get-AppxPackage", script);
    }
    [Theory]
    [InlineData("COR_ENABLE_PROFILING")]
    [InlineData("coreclr_profiler_path")]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    [InlineData("DOTNET_BUNDLE_EXTRACT_BASE_DIR")]
    [InlineData("COMPLUS_ReadyToRun")]
    public void RuntimeInjectionIsRemoved(string name)
    {
        var env = new Dictionary<string, string> { [name] = "untrusted", ["PATH"] = "keep" };
        ProcessEnvironment.Sanitize(env, @"C:\protected\temp", @"C:\Windows\modules");
        Assert.DoesNotContain(name, env.Keys);
        Assert.Equal("keep", env["PATH"]);
    }

    [Fact]
    public void BothTempVariablesAndModulePathAreReplacedRegardlessOfCase()
    {
        var env = new Dictionary<string, string> { ["temp"] = "user", ["Tmp"] = "user", ["psmodulepath"] = "user" };
        ProcessEnvironment.Sanitize(env, "protected", "system-modules");
        Assert.Equal(3, env.Count);
        Assert.Equal("protected", env["TEMP"]);
        Assert.Equal("protected", env["TMP"]);
        Assert.Equal("system-modules", env["PSModulePath"]);
    }

    [Fact]
    public void ProcessStartInfoGetsTheSamePolicyWithoutChangingParentEnvironment()
    {
        string original = Environment.GetEnvironmentVariable("TEMP");
        var info = new ProcessStartInfo { UseShellExecute = false };
        info.Environment["DOTNET_STARTUP_HOOKS"] = "injected.dll";
        ProcessEnvironment.Sanitize(info.Environment, "protected", ProcessEnvironment.ModulePath);
        Assert.False(info.Environment.ContainsKey("DOTNET_STARTUP_HOOKS"));
        Assert.Equal(original, Environment.GetEnvironmentVariable("TEMP"));
        Assert.Equal("protected", info.Environment["TEMP"]);
        Assert.Equal(string.Join(";", Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\Modules"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"WindowsPowerShell\Modules")),
            info.Environment["PSModulePath"]);
    }

    [Fact]
    public async Task PowerShellActuallyUsesSystemModulesAndSanitizedEnvironment()
    {
        string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        string script = Sh.PowerShellScript("[Console]::Write($env:PSModulePath + '|' + $env:TEMP + '|' + $env:COR_ENABLE_PROFILING)", false);
        var info = new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -EncodedCommand " +
            Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.SystemDirectory,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        info.Environment["PSModulePath"] = @"C:\untrusted-modules";
        info.Environment["COR_ENABLE_PROFILING"] = "1";
        ProcessEnvironment.Sanitize(info.Environment, ProcessEnvironment.UserTemp, ProcessEnvironment.ModulePath);
        using var process = Process.Start(info);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("PowerShell test timed out"); }
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(ProcessEnvironment.ModulePath + "|" + ProcessEnvironment.UserTemp + "|", await output);
        Assert.Equal("", await error);
    }
}
