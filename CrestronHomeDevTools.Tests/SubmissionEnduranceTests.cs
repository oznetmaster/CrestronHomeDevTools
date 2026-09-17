// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json.Nodes;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionEnduranceTests
	{
	private string _directory = null!;
	private Clock _clock = null!;
	private int _calls;
	private static readonly SubmissionEvidenceIdentity Identity = new (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64));
	private static SubmissionEndurancePlan Plan => new (Identity,
		new ("endurance", TimeSpan.FromMinutes (1), Execution: new ("gateway", "endurance", SubmissionEvidenceOutcome.Passed, null, false, 40)),
		"processor-identity", "installed-instance", "reservation", "trusted-producer", TimeSpan.FromSeconds (30), TimeSpan.FromSeconds (5));
	private static SubmissionEnduranceProbeResult Result => new (Identity, Plan.ProcessorIdentity, Plan.InstallationIdentity,
		Plan.ReservationId, Plan.ProducerId, "boot-identity", SubmissionEvidenceOutcome.Passed,
		"Synthetic observation: not hardware or certification evidence"u8.ToArray ());

	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (TestContext.CurrentContext.WorkDirectory, "endurance-" + Guid.NewGuid ().ToString ("N"));
		_clock = new ();
		_calls = 0;
		}

	[TearDown]
	public void TearDown ()
		{
		if (Directory.Exists (_directory)) Directory.Delete (_directory, true);
		}

	private Task<SubmissionEnduranceCheckpoint> Collect (SubmissionEnduranceProbeResult? result = null, SubmissionEndurancePlan? plan = null) =>
		SubmissionEndurance.CollectAsync (_directory, plan ?? Plan, _ => { _calls++; return Task.FromResult (result ?? Result); }, _clock);

	[Test]
	public async Task IndependentInvocationsResumeAndExportOnlyAfterFullObservedInterval ()
		{
		Assert.That ((await Collect ()).State, Is.EqualTo (SubmissionEnduranceState.Collecting));
		Assert.Throws<InvalidOperationException> (() => SubmissionEndurance.Export (_directory, Plan, _clock.GetUtcNow ()));
		await Collect ();
		Assert.That (_calls, Is.EqualTo (1), "A scheduler tick before the sample is due must not call the producer.");
		_clock.Advance (30);
		await Collect ();
		_clock.Advance (30);
		Assert.That ((await Collect ()).State, Is.EqualTo (SubmissionEnduranceState.Passed));
		var observation = SubmissionEndurance.Export (_directory, Plan, _clock.GetUtcNow ());
		Assert.That (SubmissionEvidence.Evaluate (Identity, [Plan.Requirement], [observation], _directory, _clock.GetUtcNow ()).EvidenceChecksPassed, Is.True);
		Assert.That (observation.Execution!.Samples, Has.Count.EqualTo (3));
		await Collect ();
		Assert.That (_calls, Is.EqualTo (3), "A completed run is never sampled again.");
		}

	[Test]
	public async Task MonitorDowntimeBeyondApprovedGapPermanentlyFailsWithoutAnotherProbe ()
		{
		await Collect ();
		_clock.Advance (41);
		var checkpoint = await Collect ();
		Assert.That (checkpoint.Reason, Is.EqualTo ("sample-gap"));
		Assert.That (checkpoint.State, Is.EqualTo (SubmissionEnduranceState.Failed));
		await Collect ();
		Assert.That (_calls, Is.EqualTo (1));
		Assert.Throws<InvalidOperationException> (() => SubmissionEndurance.Export (_directory, Plan, _clock.GetUtcNow ()));
		}

	[TestCase ("identity", "probe-identity-mismatch")]
	[TestCase ("processor", "probe-identity-mismatch")]
	[TestCase ("instance", "probe-identity-mismatch")]
	[TestCase ("reservation", "probe-identity-mismatch")]
	[TestCase ("producer", "probe-identity-mismatch")]
	[TestCase ("reboot", "processor-restarted-or-unknown")]
	[TestCase ("boot-missing", "processor-restarted-or-unknown")]
	[TestCase ("functional", "functional-check-not-passed")]
	public async Task ChangedEnvironmentOrFailedFunctionCannotBeCredited (string defect, string reason)
		{
		await Collect ();
		_clock.Advance (30);
		var result = defect switch
			{
				"identity" => Result with { Identity = Identity with { PackageSha256 = new ('f', 64) } },
				"processor" => Result with { ProcessorIdentity = "other" },
				"instance" => Result with { InstallationIdentity = "other" },
				"reservation" => Result with { ReservationId = "other" },
				"producer" => Result with { ProducerId = "other" },
				"reboot" => Result with { BootIdentity = "rebooted" },
				"boot-missing" => Result with { BootIdentity = "" },
				_ => Result with { Outcome = SubmissionEvidenceOutcome.Failed }
				};
		var checkpoint = await Collect (result);
		Assert.That (checkpoint.State, Is.EqualTo (SubmissionEnduranceState.Failed));
		Assert.That (checkpoint.Reason, Is.EqualTo (reason));
		Assert.That (checkpoint.Samples, Has.Count.EqualTo (2), "Retain the failed measurement too.");
		}

	[Test]
	public async Task DifferentPlanCannotReusePreviousTime ()
		{
		await Collect ();
		Assert.ThrowsAsync<InvalidDataException> (async () => await Collect (plan: Plan with { ProducerId = "changed" }));
		Assert.That (_calls, Is.EqualTo (1));
		}

	[Test]
	public async Task CrashWithPendingProbeIsNotAutomaticallyReplayed ()
		{
		await Collect ();
		var path = Path.Combine (_directory, "checkpoint.json");
		var checkpoint = JsonNode.Parse (File.ReadAllText (path))!;
		checkpoint["state"] = "ProbePending";
		File.WriteAllText (path, checkpoint.ToJsonString ());
		Assert.That ((await Collect ()).State, Is.EqualTo (SubmissionEnduranceState.Interrupted));
		Assert.That (_calls, Is.EqualTo (1));
		}

	[Test]
	public async Task MissingOrChangedEvidenceStopsBeforeNextObservation ()
		{
		var checkpoint = await Collect ();
		File.WriteAllText (Path.Combine (_directory, checkpoint.Samples[0].File.RelativePath), "changed");
		_clock.Advance (30);
		Assert.ThrowsAsync<InvalidDataException> (async () => await Collect ());
		Assert.That (_calls, Is.EqualTo (1));
		}

	[Test]
	public async Task CheckpointCannotMoveAnObservationToHideAMissedInterval ()
		{
		await Collect ();
		var path = Path.Combine (_directory, "checkpoint.json");
		var checkpoint = JsonNode.Parse (File.ReadAllText (path))!;
		checkpoint["samples"]![0]!["observedUtc"] = _clock.GetUtcNow ().AddSeconds (25).ToString ("O");
		File.WriteAllText (path, checkpoint.ToJsonString ());
		_clock.Advance (60);
		Assert.ThrowsAsync<InvalidDataException> (async () => await Collect ());
		Assert.That (_calls, Is.EqualTo (1));
		}

	[Test]
	public async Task RetainedObservationContainsObservedIdentityAndOriginalProducerEvidence ()
		{
		var checkpoint = await Collect ();
		var retained = JsonNode.Parse (File.ReadAllText (Path.Combine (_directory, checkpoint.Samples[0].File.RelativePath)))!;
		Assert.That (retained["probe"]!["processorIdentity"]!.GetValue<string> (), Is.EqualTo (Plan.ProcessorIdentity));
		Assert.That (retained["probe"]!["identity"]!["packageSha256"]!.GetValue<string> (), Is.EqualTo (Identity.PackageSha256));
		Assert.That (Convert.FromBase64String (retained["probe"]!["evidence"]!.GetValue<string> ()), Is.EqualTo (Result.Evidence));
		}

	[Test]
	public async Task CancelledProbeCannotBeResumedAsIfItNeverRan ()
		{
		var checkpoint = await SubmissionEndurance.CollectAsync (_directory, Plan,
			_ => throw new OperationCanceledException (), _clock);
		Assert.That (checkpoint.State, Is.EqualTo (SubmissionEnduranceState.Interrupted));
		await Collect ();
		Assert.That (_calls, Is.Zero);
		}

	[Test]
	public async Task ConcurrentCollectorCannotInvokeSecondProbe ()
		{
		var entered = new TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously);
		var finish = new TaskCompletionSource<SubmissionEnduranceProbeResult> (TaskCreationOptions.RunContinuationsAsynchronously);
		var first = SubmissionEndurance.CollectAsync (_directory, Plan, _ => { entered.SetResult (); return finish.Task; }, _clock);
		await entered.Task;
		try { Assert.ThrowsAsync<IOException> (async () => await Collect ()); }
		finally { finish.SetResult (Result); await first; }
		Assert.That (_calls, Is.Zero);
		}

	[Test]
	public async Task ProbeExceptionIsDurableAndDoesNotExposeItsMessage ()
		{
		var checkpoint = await SubmissionEndurance.CollectAsync (_directory, Plan,
			_ => throw new InvalidOperationException ("secret response"), _clock);
		Assert.That (checkpoint.State, Is.EqualTo (SubmissionEnduranceState.Failed));
		Assert.That (File.ReadAllText (Path.Combine (_directory, "checkpoint.json")), Does.Not.Contain ("secret response"));
		await Collect ();
		Assert.That (_calls, Is.Zero);
		}

	[Test]
	public async Task LateProbeCannotTurnIntoPassingSample ()
		{
		var checkpoint = await SubmissionEndurance.CollectAsync (_directory, Plan,
			_ => { _clock.Advance (6); return Task.FromResult (Result); }, _clock);
		Assert.That (checkpoint.Reason, Is.EqualTo ("probe-timeout"));
		Assert.That (checkpoint.State, Is.EqualTo (SubmissionEnduranceState.Failed));
		}

	[Test]
	public async Task ClockReversalStopsBeforeAnotherProbe ()
		{
		await Collect ();
		_clock.Advance (-1);
		Assert.That ((await Collect ()).Reason, Is.EqualTo ("clock-reversed"));
		Assert.That (_calls, Is.EqualTo (1));
		}

	[Test]
	public void PolicyMustLeaveTimeForFunctionalObservation () => Assert.ThrowsAsync<ArgumentException> (async () =>
		await Collect (plan: Plan with { SampleInterval = TimeSpan.FromSeconds (40) }));

	private sealed class Clock : TimeProvider
		{
		private DateTimeOffset _now = new (2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow () => _now;
		public override long GetTimestamp () => _now.Ticks;
		public override long TimestampFrequency => TimeSpan.TicksPerSecond;
		internal void Advance (int seconds) => _now = _now.AddSeconds (seconds);
		}
	}