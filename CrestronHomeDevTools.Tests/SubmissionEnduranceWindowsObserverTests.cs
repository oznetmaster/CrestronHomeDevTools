// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionEnduranceWindowsObserverTests
	{
	private static readonly SubmissionEndurancePlan Plan = new (
		new (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64)),
		new ("periodic", TimeSpan.FromMinutes (2), Execution: new ("gateway", "endurance", SubmissionEvidenceOutcome.Passed, null, false, 120)),
		"processor", "instance", "reservation", "producer", TimeSpan.FromSeconds (30), TimeSpan.FromSeconds (30));
	[Test]
	public void EncodedCommandPreservesTheBundledReaderAndLiteralParameters ()
		{
		var task = new SubmissionEnduranceWindowsTask ("Candidate 'one'; $env:PATH", "C:\\Private\\Unicode-é\\state");
		string command = SubmissionEnduranceWindowsObserver.EncodedCommand (task);
		Assert.That (command.Length, Is.LessThan (7901));
		string bootstrap = Encoding.Unicode.GetString (Convert.FromBase64String (command.Split (' ').Last ()));
		string payload = Regex.Match (bootstrap, "FromBase64String\\('([A-Za-z0-9+/=]+)'\\)").Groups[1].Value;
		using var gzip = new GZipStream (new MemoryStream (Convert.FromBase64String (payload)), CompressionMode.Decompress);
		using var reader = new StreamReader (gzip, Encoding.UTF8);
		Assert.That (reader.ReadToEnd (), Is.EqualTo (SubmissionEnduranceWindowsObserver.Script (task)));
		Assert.That (SubmissionEnduranceWindowsObserver.Script (task), Does.Contain ("-TaskName 'Candidate ''one''; $env:PATH'"));
		Assert.That (bootstrap, Does.Contain ("$ProgressPreference='SilentlyContinue'"));
		}
	[TestCase ("relative")]
	[TestCase ("\\\\server\\share")]
	[TestCase ("C:\\private\r\nWrite-Output injected")]
	public void InvalidRemotePathsAreRejectedBeforeAnyConnection (string path) =>
		Assert.Throws<ArgumentException> (() => SubmissionEnduranceWindowsObserver.EncodedCommand (new ("Candidate", path)));
	[TestCase (3, "{}", "")]
	[TestCase (0, "null", "")]
	[TestCase (0, "{PRIVATE-SECRET", "")]
	[TestCase (0, "{}", "PRIVATE-SECRET")]
	public void InvalidObservationsDoNotLeakRawOutput (int exitCode, string output, string error)
		{
		var failure = Assert.Throws<InvalidDataException> (() => SubmissionEnduranceWindowsObserver.Parse (exitCode, output, error));
		Assert.That (failure!.Message, Does.Not.Contain ("PRIVATE-SECRET"));
		}
	[Test]
	public async Task FailedObservationProducesFreshBoundAttentionAndDoesNotRetry ()
		{
		int calls = 0;
		var report = await SubmissionEnduranceWindowsObserver.AssessCoreAsync (Plan,
			_ => { calls++; throw new IOException ("PRIVATE-SECRET"); }, CancellationToken.None);
		Assert.That (calls, Is.EqualTo (1));
		Assert.That (report.RequiresAttention, Is.True);
		Assert.That (report.Reasons, Is.EqualTo (new[] { "observer-query-failed" }));
		Assert.That (report.PlanSha256, Is.EqualTo (SubmissionEndurance.PlanDigest (Plan)));
		Assert.That (report.LastSampleUtc, Is.Null);
		}
	[TestCase (3, "{}", "")]
	[TestCase (0, "null", "")]
	[TestCase (0, "{PRIVATE-SECRET", "")]
	[TestCase (0, "{}", "PRIVATE-SECRET")]
	public async Task FailedSnapshotParsingProducesAttentionInsteadOfEscapingTheObserver (int exitCode, string output, string error)
		{
		int calls = 0;
		var before = DateTimeOffset.UtcNow;
		var report = await SubmissionEnduranceWindowsObserver.AssessCoreAsync (Plan,
			_ => { calls++; return Task.FromResult (SubmissionEnduranceWindowsObserver.Parse (exitCode, output, error)); }, CancellationToken.None);
		Assert.That (calls, Is.EqualTo (1));
		Assert.That (report.State, Is.EqualTo (SubmissionEnduranceHealthState.AttentionRequired));
		Assert.That (report.Reasons, Is.EqualTo (new[] { "observer-query-failed" }));
		Assert.That (report.PlanSha256, Is.EqualTo (SubmissionEndurance.PlanDigest (Plan)));
		Assert.That (report.EvaluatedUtc, Is.InRange (before, DateTimeOffset.UtcNow));
		Assert.That (report.LastSampleUtc, Is.Null);
		}
	[Test]
	public void ExplicitCancellationDoesNotBecomeAHealthyOrSyntheticObservation ()
		{
		using var cancelled = new CancellationTokenSource ();
		cancelled.Cancel ();
		int calls = 0;
		Assert.ThrowsAsync<OperationCanceledException> (() => SubmissionEnduranceWindowsObserver.AssessCoreAsync (Plan,
			_ => { calls++; throw new IOException (); }, cancelled.Token));
		Assert.That (calls, Is.Zero);
		}
	[Test]
	public async Task EmbeddedReaderExecutesWithLiteralTaskNameAndNoInjectedCommands ()
		{
		if (!OperatingSystem.IsWindows ())
			Assert.Ignore ("Windows PowerShell integration.");
		string root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "observer-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (root);
		try
			{
			string marker = Path.Combine (root, "injected.txt");
			string name = "Candidate'; [IO.File]::WriteAllText('" + marker + "','injected'); #";
			static string Quote (string value) => "'" + value.Replace ("'", "''", StringComparison.Ordinal) + "'";
			string mock = "function Get-ScheduledTask { param($TaskPath) [pscustomobject]@{TaskName=" + Quote (name) + ";State='Ready'} };" +
				"function Get-ScheduledTaskInfo { param([Parameter(ValueFromPipeline)]$InputObject) process { [pscustomobject]@{LastTaskResult=0} } };";
			string bootstrap = Encoding.Unicode.GetString (Convert.FromBase64String (SubmissionEnduranceWindowsObserver.EncodedCommand (new (name, root)).Split (' ').Last ()));
			var start = new ProcessStartInfo (PowerShellRuntime.LocalExecutable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
			foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String (Encoding.Unicode.GetBytes (mock + bootstrap)) })
				start.ArgumentList.Add (arg);
			using var process = Process.Start (start)!;
			var stdout = process.StandardOutput.ReadToEndAsync ();
			var stderr = process.StandardError.ReadToEndAsync ();
			using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (20));
			try
				{
				await process.WaitForExitAsync (deadline.Token);
				}
			finally { if (!process.HasExited) { process.Kill (true); await process.WaitForExitAsync (); } }
			var snapshot = SubmissionEnduranceWindowsObserver.Parse (process.ExitCode, await stdout, await stderr);
			Assert.That (snapshot.TaskPresent, Is.True);
			Assert.That (snapshot.TaskState, Is.EqualTo ("Ready"));
			Assert.That (snapshot.Scheduler, Is.Null);
			Assert.That (File.Exists (marker), Is.False);
			}
		finally { Directory.Delete (root, true); }
		}
	}