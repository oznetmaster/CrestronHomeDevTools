// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.
using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class DriverUpdatesCommandTests
    {
    [Test]
    public async Task HelpIncludesScanAndReviewedApplication ()
        {
        var result = await Run (["--help"]);
        Assert.That (result.Exit, Is.Zero);
        Assert.That (result.Output, Does.Contain ("driver-updates --output updates.json"));
        Assert.That (result.Output, Does.Contain ("update-drivers --plan updates.json --all true --journal NEW_DIRECTORY"));
        }

    [Test]
    public async Task MissingScanOutputFailsBeforeLoadingCredentials ()
        {
        var result = await Run (["driver-updates", "--settings", "nonexistent-private-settings.json"]);
        Assert.That (result.Exit, Is.EqualTo (2));
        Assert.That (result.Error, Does.Contain ("Missing --output"));
        }

    [TestCase ("no-selection")]
    [TestCase ("both")]
    [TestCase ("false")]
    [TestCase ("unknown-driver")]
    [TestCase ("missing-journal")]
    [TestCase ("used-journal")]
    public async Task InvalidBatchOptionsFailBeforeLoadingCredentials (string scenario)
        {
        var root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "update-cli-" + Guid.NewGuid ().ToString ("N"));
        Directory.CreateDirectory (root);
        try
            {
            var report = new DriverUpdateReport (1, new ("192.0.2.1", 443, 49000, new string ('A', 64)), DateTimeOffset.UtcNow, [], []);
            var path = Path.Combine (root, "report.json");
            File.WriteAllText (path, JsonSerializer.Serialize (report));
            var args = new List<string> { "update-drivers", "--plan", path, "--settings", "nonexistent-private-settings.json" };
            if (scenario is not ("no-selection" or "unknown-driver")) args.AddRange (["--all", scenario == "false" ? "false" : "true"]);
            if (scenario is "both" or "unknown-driver") args.AddRange (["--drivers", "not-in-report"]);
            if (scenario != "missing-journal") args.AddRange (["--journal", scenario == "used-journal" ? root : Path.Combine (root, "new-journal")]);
            var result = await Run (args);
            Assert.That (result.Exit, Is.EqualTo (2));
            Assert.That (result.Output, Is.Empty);
            Assert.That (result.Error, Does.Not.Contain ("Connection or file operation failed"));
            }
        finally { Directory.Delete (root, true); }
        }

    private static async Task<(int Exit, string Output, string Error)> Run (IEnumerable<string> args)
        {
        var start = new ProcessStartInfo ("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add (Path.Combine (AppContext.BaseDirectory, "CrestronHomeDevTools.Console.dll"));
        foreach (var arg in args) start.ArgumentList.Add (arg);
        using var process = Process.Start (start)!;
        var output = process.StandardOutput.ReadToEndAsync ();
        var error = process.StandardError.ReadToEndAsync ();
        using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (30));
        try { await process.WaitForExitAsync (timeout.Token); }
        finally { if (!process.HasExited) { process.Kill (true); await process.WaitForExitAsync (); } }
        return (process.ExitCode, await output, await error);
        }
    }
