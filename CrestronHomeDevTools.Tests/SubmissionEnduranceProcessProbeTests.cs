// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionEnduranceProcessProbeTests
	{
	private string _root = null!;
	private SubmissionEnduranceProbeProgram _program = null!;
	private SubmissionEndurancePlan _plan = null!;
	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "probe-" + Guid.NewGuid ().ToString ("N"));
		string bundle = Path.Combine (_root, "bundle");
		Directory.CreateDirectory (bundle);
		var pins = new List<SubmissionEvidenceFile> ();
		foreach (var file in Directory.EnumerateFiles (Path.Combine (AppContext.BaseDirectory, "test-probe")))
			{
			string destination = Path.Combine (bundle, Path.GetFileName (file));
			File.Copy (file, destination);
			if (!OperatingSystem.IsWindows ()) File.SetUnixFileMode (destination, File.GetUnixFileMode (file));
			pins.Add (new (Path.GetFileName (file), Convert.ToHexString (SHA256.HashData (File.ReadAllBytes (file)))));
			}
		string settings = Path.Combine (_root, "private.json");
		File.WriteAllText (settings, "pass");
		_program = new (bundle, "CrestronHomeDevTools.Tests.Probe" + (OperatingSystem.IsWindows () ? ".exe" : ""), pins, settings);
		_plan = new (new (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64)),
			new ("synthetic-endurance", TimeSpan.FromSeconds (2), Execution: new ("gateway", "endurance", SubmissionEvidenceOutcome.Passed, null, false, 30)),
			"synthetic-processor", "synthetic-instance", Guid.NewGuid ().ToString ("N"), SubmissionEnduranceProcessProbe.GetProducerId (_program),
			TimeSpan.FromSeconds (1), TimeSpan.FromSeconds (10));
		}
	[TearDown]
	public async Task TearDown ()
		{
		if (File.Exists (_program.SettingsFile + ".pid")) AssertStopped ();
		// Process termination is asserted separately. Windows may briefly retain the
		// executable mapping after exit; retry only deletion of this test's own bundle.
		var elapsed = Stopwatch.StartNew ();
		while (Directory.Exists (_root))
			{
			try { Directory.Delete (_root, true); }
			catch (Exception error) when ((error is IOException or UnauthorizedAccessException) && elapsed.Elapsed < TimeSpan.FromSeconds (5))
				{
				await Task.Delay (100);
				}
			}
		}

	[Test]
	public async Task RealChildProcessReturnsBoundObservationWithoutShellOrVisibleWindow ()
		{
		var result = await SubmissionEnduranceProcessProbe.RunAsync (_program, _plan);
		Assert.Multiple (() =>
			{
			Assert.That (result.Identity, Is.EqualTo (_plan.Identity));
			Assert.That (result.ProducerId, Is.EqualTo (_plan.ProducerId));
			Assert.That (result.ReservationId, Is.EqualTo (_plan.ReservationId));
			Assert.That (result.BootIdentity, Is.EqualTo ("synthetic-boot"));
			Assert.That (result.Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Passed));
			});
		AssertStopped ();
		}
	[Test]
	public async Task ProcessObservationEntersTheCollectorAndIsRetainedWithItsPins ()
		{
		string evidence = Path.Combine (_root, "evidence");
		var checkpoint = await SubmissionEndurance.CollectAsync (evidence, _plan,
			ct => SubmissionEnduranceProcessProbe.RunAsync (_program, _plan, ct));
		Assert.That (checkpoint.State, Is.EqualTo (SubmissionEnduranceState.Collecting));
		Assert.That (checkpoint.Samples.Single ().Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Passed));
		Assert.That (SubmissionEndurance.ReadCheckpoint (evidence, _plan)!.Samples.Single ().File,
			Is.EqualTo (checkpoint.Samples.Single ().File));
		AssertStopped ();
		}
	[TestCase ("exit")]
	[TestCase ("malformed")]
	[TestCase ("stdout-limit")]
	[TestCase ("stderr-limit")]
	public async Task FailedOrExcessiveOutputStopsChildAndDoesNotLeakItsText (string mode)
		{
		File.WriteAllText (_program.SettingsFile!, mode);
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (20));
		var error = Assert.ThrowsAsync<InvalidDataException> (async () =>
			await SubmissionEnduranceProcessProbe.RunAsync (_program, _plan, deadline.Token));
		Assert.That (error!.ToString (), Does.Not.Contain ("PRIVATE-SECRET"));
		AssertStopped ();
		await Task.CompletedTask;
		}
	[Test]
	public async Task CancellationTerminatesTheRunningChildBeforeReturning ()
		{
		File.WriteAllText (_program.SettingsFile!, "hang");
		using var cancel = new CancellationTokenSource (TimeSpan.FromSeconds (20));
		var running = SubmissionEnduranceProcessProbe.RunAsync (_program, _plan, cancel.Token);
		try
			{
			while (!File.Exists (_program.SettingsFile + ".pid")) await Task.Delay (20, cancel.Token);
			cancel.Cancel ();
			Assert.ThrowsAsync<OperationCanceledException> (async () => await running);
			AssertStopped ();
			}
		finally { cancel.Cancel (); try { await running; } catch (OperationCanceledException) { } }
		}
	[TestCase ("changed")]
	[TestCase ("extra")]
	[TestCase ("missing")]
	public void ChangedMissingOrAddedBundleFilePreventsProcessStart (string change)
		{
		string file = Path.Combine (_program.Directory, _program.Files[0].RelativePath);
		if (change == "changed") File.AppendAllText (file, "changed");
		else if (change == "extra") File.WriteAllText (Path.Combine (_program.Directory, "unreviewed.dll"), "extra");
		else File.Delete (file);
		Assert.ThrowsAsync<InvalidDataException> (async () => await SubmissionEnduranceProcessProbe.RunAsync (_program, _plan));
		Assert.That (File.Exists (_program.SettingsFile + ".pid"), Is.False);
		}
	[Test]
	public void ChangedManifestCannotReplaceTheProducerOnAResumedPlan ()
		{
		var changed = _program with { SettingsFile = Path.Combine (_root, "another-private-file") };
		Assert.Throws<InvalidDataException> (() => SubmissionEnduranceProcessProbe.Validate (changed, _plan));
		}
	[Test]
	public void FileOrderingDoesNotChangeProducerIdentity () => Assert.That (
		SubmissionEnduranceProcessProbe.GetProducerId (_program with { Files = _program.Files.Reverse ().ToArray () }), Is.EqualTo (_plan.ProducerId));
	[Test]
	public void DuplicateManifestPathsAreRejected () => Assert.Throws<ArgumentException> (() =>
		SubmissionEnduranceProcessProbe.GetProducerId (_program with { Files = [.. _program.Files, _program.Files[0]] }));
	[Test]
	public void OfflineStatusNeedsNoCredentialsAndRejectsAChangedProducer ()
		{
		var worker = new SubmissionEnduranceWorkerPlan (_plan, new ("processor.invalid", "pin"), _program);
		string file = Path.Combine (_root, "worker.json");
		File.WriteAllText (file, JsonSerializer.Serialize (worker));
		Assert.That (EnduranceCommands.Read (file).Plan, Is.EqualTo (_plan));
		File.WriteAllText (file, JsonSerializer.Serialize (worker with { Probe = _program with { SettingsFile = null } }));
		Assert.Throws<ArgumentException> (() => EnduranceCommands.Read (file));
		}
	private void AssertStopped ()
		{
		int pid = int.Parse (File.ReadAllText (_program.SettingsFile + ".pid"));
		try { using var process = Process.GetProcessById (pid); Assert.That (process.HasExited, Is.True); }
		catch (ArgumentException) { }
		}
	[Test]
	public async Task ScheduledTickReportsUncertainOwnershipWithoutStartingProducerOrNetwork ()
		{
		string run = Path.Combine (_root, "uncertain-run");
		var endpoint = new SubmissionEnduranceProcessor ("processor.invalid", "pin");
		Assert.ThrowsAsync<IOException> (async () => await SubmissionEnduranceMonitor.StartCoreAsync (run, _plan, endpoint,
			_ => Task.FromException<IProcessorOperationLease> (new IOException ("Synthetic uncertain acquisition."))));
		int result = await EnduranceCommands.RunAsync ("endurance-tick", run, new (_plan, endpoint, _program), endpoint,
			new System.Net.NetworkCredential ("unused", "unused"), CancellationToken.None);
		Assert.That (result, Is.EqualTo (3));
		Assert.That (File.Exists (_program.SettingsFile + ".pid"), Is.False);
		}
	[TestCase (false)]
	[TestCase (true)]
	public async Task ActualStatusCliWorksOfflineAndReportsInvalidJournalWithoutLeakingIt (bool corrupt)
		{
		string run = Path.Combine (_root, "cli-run"), workerFile = Path.Combine (_root, "worker.json");
		Directory.CreateDirectory (run);
		File.WriteAllText (workerFile, JsonSerializer.Serialize (new SubmissionEnduranceWorkerPlan (_plan, new ("processor.invalid", "pin"), _program)));
		if (corrupt) File.WriteAllText (Path.Combine (run, "monitor.json"), "{\"privateValue\":\"PRIVATE-SECRET\"}");
		var start = new ProcessStartInfo ("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
		foreach (var argument in new[] { Path.Combine (AppContext.BaseDirectory, "CrestronHomeDevTools.Console.dll"), "endurance-status",
			"--worker", workerFile, "--run", run, "--profile", "nonexistent-endurance-test-profile" }) start.ArgumentList.Add (argument);
		using var process = Process.Start (start)!;
		var stdout = process.StandardOutput.ReadToEndAsync ();
		var stderr = process.StandardError.ReadToEndAsync ();
		using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (20));
		try { await process.WaitForExitAsync (timeout.Token); }
		finally { if (!process.HasExited) { process.Kill (true); await process.WaitForExitAsync (); } }
		Assert.That (process.ExitCode, Is.EqualTo (corrupt ? 3 : 0), await stderr);
		Assert.That ((await stdout) + (await stderr), Does.Not.Contain ("PRIVATE-SECRET"));
		if (!corrupt) Assert.That (await stdout, Does.Contain ("NotStarted"));
		Assert.That (File.Exists (_program.SettingsFile + ".pid"), Is.False);
		}
	}