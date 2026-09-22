// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[Platform ("Win")]
public sealed class PowerShellRuntimeTests
	{
	[Test]
	public async Task SupportedPowerShellRunsTheOperation ()
		{
		var result = await Run (PowerShellRuntime.LocalExecutable);
		Assert.That (result.ExitCode, Is.Zero, result.Error);
		Assert.That (result.Output, Does.Contain ("OPERATION_EXECUTED"));
		}

	[Test]
	public async Task WindowsPowerShellIsRejectedBeforeTheOperation ()
		{
		string legacy = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.System),
			"WindowsPowerShell", "v1.0", "powershell.exe");
		if (!File.Exists (legacy))
			Assert.Ignore ("Windows PowerShell is absent; only used here to verify refusal of the old engine.");
		var result = await Run (legacy);
		Assert.That (result.ExitCode, Is.Not.Zero);
		Assert.That (result.Output, Does.Not.Contain ("OPERATION_EXECUTED"));
		Assert.That (result.Error, Does.Contain ("PowerShell 7.6 or later is required"));
		}

	private static async Task<(int ExitCode, string Output, string Error)> Run (string executable)
		{
		var start = new ProcessStartInfo (executable)
			{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
			};
		start.Environment.Remove ("PSModulePath");
		foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand",
			Convert.ToBase64String (Encoding.Unicode.GetBytes (PowerShellRuntime.RequireSupportedVersion (
				"Write-Output 'OPERATION_EXECUTED'"))) })
			start.ArgumentList.Add (argument);
		using var process = Process.Start (start)!;
		Task<string> output = process.StandardOutput.ReadToEndAsync ();
		Task<string> error = process.StandardError.ReadToEndAsync ();
		try
			{
			using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (30));
			await process.WaitForExitAsync (timeout.Token);
			return (process.ExitCode, await output, await error);
			}
		finally
			{
			if (!process.HasExited)
				{
				process.Kill (entireProcessTree: true);
				await process.WaitForExitAsync ();
				}
			}
		}
	}
