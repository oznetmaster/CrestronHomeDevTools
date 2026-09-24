// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionEnduranceMonitorTests
	{
	private string _directory = null!;
	private SubmissionEndurancePlan _plan = null!;
	private readonly SubmissionEnduranceProcessor _processor = new ("processor.invalid", "pinned-ssh-identity");
	private Clock _clock = null!;
	private bool _held;
	private int _acquires, _resumes, _releases, _probes;
	private bool _uncertainRelease;
	private string? _wrongOwner;

	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (TestContext.CurrentContext.WorkDirectory, "monitor-" + Guid.NewGuid ().ToString ("N"));
		_plan = new (new (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64)),
			new ("endurance", TimeSpan.FromSeconds (2), Execution: new ("gateway", "endurance", SubmissionEvidenceOutcome.Passed, null, false, 4)),
			"processor", "instance", Guid.NewGuid ().ToString ("N"), "synthetic-producer", TimeSpan.FromSeconds (1), TimeSpan.FromSeconds (1));
		_clock = new ();
		_held = _uncertainRelease = false;
		_acquires = _resumes = _releases = _probes = 0;
		_wrongOwner = null;
		}
	[TearDown]
	public void TearDown () { if (Directory.Exists (_directory)) Directory.Delete (_directory, true); }
	private Task<IProcessorOperationLease> Acquire (CancellationToken token)
		{
		_acquires++;
		if (_held) throw new ProcessorBusyException ();
		_held = true;
		return Task.FromResult<IProcessorOperationLease> (new Lease (this, _plan.ReservationId));
		}
	private Task<IProcessorOperationLease> Resume (CancellationToken token)
		{
		_resumes++;
		if (!_held) throw new IOException ("Synthetic lost reservation.");
		return Task.FromResult<IProcessorOperationLease> (new Lease (this, _wrongOwner ?? _plan.ReservationId));
		}
	private Task<SubmissionEnduranceProbeResult> Probe (CancellationToken token)
		{
		_probes++;
		return Task.FromResult (new SubmissionEnduranceProbeResult (_plan.Identity, _plan.ProcessorIdentity, _plan.InstallationIdentity,
			_plan.ReservationId, _plan.ProducerId, "same-boot", SubmissionEvidenceOutcome.Passed, "Synthetic only"u8.ToArray ()));
		}
	private Task Start () => SubmissionEnduranceMonitor.StartCoreAsync (_directory, _plan, _processor, Acquire);
	private Task<SubmissionEnduranceCheckpoint> Collect (Func<CancellationToken, Task<SubmissionEnduranceProbeResult>>? probe = null) =>
		SubmissionEnduranceMonitor.CollectCoreAsync (_directory, _plan, _processor, Resume, probe ?? Probe, _clock);
	private Task Finish () => SubmissionEnduranceMonitor.FinishCoreAsync (_directory, _plan, _processor, Resume);
	private async Task Complete ()
		{
		await Collect ();
		_clock.Advance (1); await Collect ();
		_clock.Advance (1);
		Assert.That ((await Collect ()).State, Is.EqualTo (SubmissionEnduranceState.Passed));
		}

	[Test]
	public async Task SeparateInvocationsKeepOneReservationUntilCompletedAndExplicitlyFinished ()
		{
		await Start ();
		Assert.That (_held, Is.True, "Transport disposal must not release the persistent reservation.");
		await Complete ();
		Assert.That (_held, Is.True);
		Assert.That (_resumes, Is.EqualTo (6), "Ownership must be checked before and after each of three samples.");
		await Finish ();
		Assert.That (_held, Is.False);
		await Finish ();
		await Collect ();
		Assert.Multiple (() =>
			{
			Assert.That (_acquires, Is.EqualTo (1));
			Assert.That (_releases, Is.EqualTo (1));
			Assert.That (_probes, Is.EqualTo (3));
			Assert.That (SubmissionEndurance.Export (SubmissionEnduranceMonitor.GetEvidenceDirectory (_directory), _plan, _clock.GetUtcNow ()).Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Passed));
			});
		}

	[Test]
	public async Task OperatorStopRetainsPassingSamplesWithoutClaimingCompletedInterval ()
		{
		await Start (); await Collect ();
		var before = SubmissionEndurance.ReadCheckpoint (SubmissionEnduranceMonitor.GetEvidenceDirectory (_directory), _plan)!;
		await SubmissionEnduranceMonitor.StopCoreAsync (_directory, _plan, _processor, Resume);
		var after = SubmissionEnduranceMonitor.ReadStatus (_directory, _plan, _processor);
		Assert.That (after.ReservationState, Is.EqualTo ("Released"));
		Assert.That (after.Checkpoint!.State, Is.EqualTo (SubmissionEnduranceState.Failed));
		Assert.That (after.Checkpoint.Reason, Is.EqualTo ("operator-stopped"));
		Assert.That (after.Checkpoint.Samples, Is.EqualTo (before.Samples));
		Assert.That (after.Checkpoint.PlanSha256, Is.EqualTo (before.PlanSha256));
		await SubmissionEnduranceMonitor.StopCoreAsync (_directory, _plan, _processor, Resume);
		await Collect ();
		Assert.That (_releases, Is.EqualTo (1));
		Assert.That (_probes, Is.EqualTo (1));
		Assert.Throws<InvalidOperationException> (() => SubmissionEndurance.Export (SubmissionEnduranceMonitor.GetEvidenceDirectory (_directory), _plan, _clock.GetUtcNow ()));
		}

	[Test]
	public async Task OperatorStopPreservesAlreadyCompletedOutcome ()
		{
		await Start (); await Complete ();
		await SubmissionEnduranceMonitor.StopCoreAsync (_directory, _plan, _processor, Resume);
		Assert.That (SubmissionEnduranceMonitor.ReadStatus (_directory, _plan, _processor).Checkpoint!.State, Is.EqualTo (SubmissionEnduranceState.Passed));
		Assert.That (_releases, Is.EqualTo (1));
		}

	[Test]
	public async Task OperatorStopRefusesInterruptedProbeWithoutReleasing ()
		{
		await Start ();
		await Collect (_ => throw new OperationCanceledException ());
		Assert.ThrowsAsync<InvalidOperationException> (async () => await SubmissionEnduranceMonitor.StopCoreAsync (_directory, _plan, _processor, Resume));
		Assert.That (_releases, Is.Zero);
		}

	[Test]
	public async Task OfflineStatusDistinguishesPassedObservationFromReservationCleanup ()
		{
		Assert.That (SubmissionEnduranceMonitor.ReadStatus (_directory, _plan, _processor).ReservationState, Is.EqualTo ("NotStarted"));
		await Start (); await Complete ();
		var held = SubmissionEnduranceMonitor.ReadStatus (_directory, _plan, _processor);
		Assert.That (held.ReservationState, Is.EqualTo ("Held"));
		Assert.That (held.Checkpoint!.State, Is.EqualTo (SubmissionEnduranceState.Passed));
		int resumes = _resumes;
		await Finish ();
		Assert.That (SubmissionEnduranceMonitor.ReadStatus (_directory, _plan, _processor).ReservationState, Is.EqualTo ("Released"));
		Assert.That (_resumes, Is.EqualTo (resumes + 1), "Offline status must not make a network connection.");
		}

	[Test]
	public async Task EarlySchedulerTickDoesNotProbeOrReconnect ()
		{
		await Start (); await Collect (); await Collect ();
		Assert.That (_probes, Is.EqualTo (1));
		Assert.That (_resumes, Is.EqualTo (2));
		}

	[Test]
	public void CollectBeforeStartCannotAcquireImplicitly ()
		{
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Collect ());
		Assert.That (_acquires + _resumes + _probes, Is.Zero);
		}

	[Test]
	public async Task RestartCannotStartAnExistingMonitorAgain ()
		{
		await Start ();
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Start ());
		Assert.That (_acquires, Is.EqualTo (1));
		Assert.That (_held, Is.True);
		}

	[Test]
	public void UncertainAcquisitionIsNotReplayedAfterRestart ()
		{
		Assert.ThrowsAsync<IOException> (async () => await SubmissionEnduranceMonitor.StartCoreAsync (_directory, _plan, _processor,
			async token => { _ = await Acquire (token); throw new IOException ("Synthetic lost response after acquisition."); }));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Start ());
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Collect ());
		Assert.That (_acquires, Is.EqualTo (1));
		Assert.That (_held, Is.True);
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task EndpointOrPlanChangeIsRejectedBeforeNetworkOrProbe (bool changePlan)
		{
		await Start ();
		Assert.ThrowsAsync<InvalidDataException> (async () => await SubmissionEnduranceMonitor.CollectCoreAsync (_directory,
			changePlan ? _plan with { ProducerId = "different-producer" } : _plan,
			changePlan ? _processor : _processor with { Host = "other.invalid" }, Resume, Probe, _clock));
		Assert.That (_resumes + _probes, Is.Zero);
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task MissingOrWrongReservationCannotProducePassingEvidence (bool wrongOwner)
		{
		await Start ();
		if (wrongOwner) _wrongOwner = Guid.NewGuid ().ToString ("N"); else _held = false;
		Assert.That ((await Collect ()).State, Is.EqualTo (SubmissionEnduranceState.Failed));
		Assert.That (_probes, Is.Zero);
		Assert.That (_releases, Is.Zero);
		}

	[Test]
	public async Task ReservationLostDuringProbeInvalidatesItsOtherwisePassingResult ()
		{
		await Start ();
		var result = await Collect (async token => { var value = await Probe (token); _held = false; return value; });
		Assert.That (result.State, Is.EqualTo (SubmissionEnduranceState.Failed));
		Assert.That (result.Samples, Is.Empty);
		Assert.That (_resumes, Is.EqualTo (2));
		}

	[Test]
	public async Task ConcurrentWorkerCannotEnterWhileFirstProbeIsRunning ()
		{
		await Start ();
		var entered = new TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously);
		var proceed = new TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously);
		var first = Collect (async token => { entered.SetResult (); await proceed.Task; return await Probe (token); });
		try
			{
			await entered.Task.WaitAsync (TimeSpan.FromSeconds (5));
			Assert.ThrowsAsync<IOException> (async () => await Collect ());
			Assert.ThrowsAsync<IOException> (async () => await Finish ());
			}
		finally { proceed.SetResult (); await first; }
		Assert.That (_probes, Is.EqualTo (1));
		}

	[Test]
	public async Task InterruptedProbeRetainsReservationForInspection ()
		{
		await Start ();
		var result = await Collect (_ => Task.FromException<SubmissionEnduranceProbeResult> (new OperationCanceledException ()));
		Assert.That (result.State, Is.EqualTo (SubmissionEnduranceState.Interrupted));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Finish ());
		await Collect ();
		Assert.That (_resumes, Is.EqualTo (1));
		Assert.That (_held, Is.True);
		}

	[Test]
	public async Task ActiveObservationIntervalCannotBeFinishedEarly ()
		{
		await Start (); await Collect ();
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Finish ());
		Assert.That (_held, Is.True);
		}

	[Test]
	public async Task FailedReadOnlyFunctionCanReleaseWithoutBecomingPassed ()
		{
		await Start ();
		var failed = await Collect (async token => (await Probe (token)) with { Outcome = SubmissionEvidenceOutcome.Failed });
		Assert.That (failed.State, Is.EqualTo (SubmissionEnduranceState.Failed));
		await Finish ();
		Assert.That (_held, Is.False);
		Assert.That ((await Collect ()).State, Is.EqualTo (SubmissionEnduranceState.Failed));
		Assert.Throws<InvalidOperationException> (() => SubmissionEndurance.Export (SubmissionEnduranceMonitor.GetEvidenceDirectory (_directory), _plan, _clock.GetUtcNow ()));
		}

	[Test]
	public async Task UncertainReleaseIsNeverRepeatedByTheNextWorker ()
		{
		await Start (); await Complete (); _uncertainRelease = true;
		Assert.ThrowsAsync<IOException> (async () => await Finish ());
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Finish ());
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Collect ());
		Assert.That (_releases, Is.EqualTo (1));
		Assert.That (_held, Is.False, "The first release happened, but its response was uncertain.");
		}

	private sealed class Lease (SubmissionEnduranceMonitorTests test, string owner) : IProcessorOperationLease
		{
		public string Owner => owner;
		public Task ReleaseAsync (CancellationToken token)
			{
			test._releases++; test._held = false;
			if (test._uncertainRelease) throw new IOException ("Synthetic lost release response.");
			return Task.CompletedTask;
			}
		public void Dispose () { }
		}
	private sealed class Clock : TimeProvider
		{
		private DateTimeOffset _now = new (2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		public void Advance (int seconds) => _now = _now.AddSeconds (seconds);
		public override DateTimeOffset GetUtcNow () => _now;
		public override long GetTimestamp () => _now.UtcTicks;
		public override long TimestampFrequency => TimeSpan.TicksPerSecond;
		}
	}
