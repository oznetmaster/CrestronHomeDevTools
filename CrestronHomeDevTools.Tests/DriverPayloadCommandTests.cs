// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class DriverPayloadCommandTests
	{
	[TestCase ("missing-driver", "Missing --driver.")]
	[TestCase ("missing-package", "Missing --package.")]
	[TestCase ("missing-pin", "Missing --package-sha256.")]
	[TestCase ("invalid-pin", "64 hexadecimal characters")]
	[TestCase ("invalid-timeout", "at most 600 seconds")]
	[TestCase ("missing-file", "package file does not exist")]
	public async Task InvalidArgumentsFailBeforeAnyProfileOrConnection (string scenario, string diagnostic)
		{
		var arguments = new List<string> { "compare-payload" };
		if (scenario != "missing-driver") arguments.AddRange (["--driver", "example.platform.ip.developer"]);
		if (scenario != "missing-package") arguments.AddRange (["--package", Path.Combine (TestContext.CurrentContext.WorkDirectory, Guid.NewGuid ().ToString ("N") + ".pkg")]);
		if (scenario != "missing-pin") arguments.AddRange (["--package-sha256", scenario == "invalid-pin" ? "not-a-sha256" : new string ('0', 64)]);
		if (scenario == "invalid-timeout") arguments.AddRange (["--timeout", "601"]);
		// If argument validation ever moves after profile loading, this test fails.
		arguments.AddRange (["--settings", Path.Combine (TestContext.CurrentContext.WorkDirectory, Guid.NewGuid ().ToString ("N") + ".json")]);
		var result = await Run (arguments);
		Assert.That (result.Exit, Is.EqualTo (2));
		Assert.That (result.Error, Does.Contain (diagnostic));
		Assert.That (result.Output, Is.Empty);
		}

	[Test]
	public async Task HelpExplainsComparisonWithoutClaimingLoadedMemory ()
		{
		var result = await Run (["--help"]);
		Assert.That (result.Exit, Is.Zero);
		Assert.That (result.Output, Does.Contain ("compare-payload --package FILE --package-sha256 SHA256 --driver ID"));
		Assert.That (result.Output, Does.Contain ("Does not install, update, reload or attest loaded memory."));
		}

	private static async Task<(int Exit, string Output, string Error)> Run (IEnumerable<string> arguments)
		{
		var start = new ProcessStartInfo ("dotnet")
			{ UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
		start.ArgumentList.Add (Path.Combine (AppContext.BaseDirectory, "CrestronHomeDevTools.Console.dll"));
		foreach (var argument in arguments) start.ArgumentList.Add (argument);
		using var process = Process.Start (start)!;
		var output = process.StandardOutput.ReadToEndAsync ();
		var error = process.StandardError.ReadToEndAsync ();
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (30));
		try { await process.WaitForExitAsync (deadline.Token); }
		finally
			{
			if (!process.HasExited)
				{
				process.Kill (true);
				await process.WaitForExitAsync ();
				}
			}
		return (process.ExitCode, await output, await error);
		}
	}