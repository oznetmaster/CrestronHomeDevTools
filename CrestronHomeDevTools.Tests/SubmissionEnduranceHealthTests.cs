// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionEnduranceHealthTests
	{
	private static readonly DateTimeOffset Now = new (2026, 9, 19, 6, 0, 0, TimeSpan.Zero);
	private static readonly SubmissionEndurancePlan Plan = new (
		new (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64)),
		new ("periodic", TimeSpan.FromMinutes (2), Execution: new ("gateway", "endurance", SubmissionEvidenceOutcome.Passed, null, false, 120)),
		"processor", "instance", "reservation", "producer", TimeSpan.FromSeconds (30), TimeSpan.FromSeconds (30));
	private static SubmissionEnduranceSample Sample (int seconds) => new (Now.AddSeconds (seconds),
		SubmissionEvidenceOutcome.Passed, new ("sample.json", new ('e', 64)), "boot");
	private static SubmissionEnduranceHealthSnapshot Snapshot () => new (Now, true, true, true, "Ready", 0, false,
		new (1, Now, "Collecting", 0, new ("Held", new (1, SubmissionEndurance.PlanDigest (Plan),
			SubmissionEnduranceState.Collecting, Now, [Sample (-120), Sample (-60), Sample (0)]))));
	private static SubmissionEnduranceHealthSnapshot WithCheckpoint (SubmissionEnduranceHealthSnapshot snapshot,
		Func<SubmissionEnduranceCheckpoint, SubmissionEnduranceCheckpoint> change) => snapshot with
		{
		Scheduler = snapshot.Scheduler! with { Collector = snapshot.Scheduler.Collector! with { Checkpoint = change (snapshot.Scheduler.Collector.Checkpoint!) } }
		};
	private static SubmissionEnduranceHealthReport Evaluate (SubmissionEnduranceHealthSnapshot snapshot, DateTimeOffset? now = null) =>
		SubmissionEnduranceHealth.Evaluate (Plan, snapshot, now ?? Now, TimeSpan.FromSeconds (60), TimeSpan.FromSeconds (2));
	private static SubmissionEnduranceHealthSnapshot Completed ()
		{
		var snapshot = WithCheckpoint (Snapshot (), c => c with { State = SubmissionEnduranceState.Passed });
		return snapshot with { Scheduler = snapshot.Scheduler! with { State = "Passed", Collector = snapshot.Scheduler.Collector! with { ReservationState = "Released" } } };
		}

	[TestCase ("Ready", 0)]
	[TestCase ("Running", 0)]
	[TestCase ("Running", 267009)]
	public void NormalScheduledInvocationsDoNotCreateAnAlert (string state, long result)
		{
		var report = Evaluate (Snapshot () with { TaskState = state, LastTaskResult = result });
		Assert.That (report.State, Is.EqualTo (SubmissionEnduranceHealthState.Collecting));
		Assert.That (report.Reasons, Is.Empty);
		}

	[Test]
	public void OfflineWorkerIsNotHiddenByAnOldPassingReceipt ()
		{
		var report = Evaluate (Completed () with { WorkerReachable = false });
		Assert.That (report.RequiresAttention, Is.True);
		Assert.That (report.Reasons, Does.Contain ("worker-unreachable"));
		}

	[Test]
	public void RecopyingAnOldHealthySnapshotDoesNotRenewItsFreshness ()
		{
		var report = Evaluate (Snapshot (), Now.AddMinutes (3));
		Assert.That (report.Reasons, Does.Contain ("observer-stale").And.Contain ("scheduler-stale").And.Contain ("sample-stale"));
		}

	[Test]
	public void FreshWrapperCannotConcealStoppedSampling ()
		{
		var snapshot = WithCheckpoint (Snapshot (), c => c with { Samples = [Sample (-250), Sample (-190), Sample (-130)], UpdatedUtc = Now.AddSeconds (-130) });
		Assert.That (Evaluate (snapshot).Reasons, Does.Contain ("sample-stale"));
		}

	[Test]
	public void LaterPassingSampleDoesNotEraseAnEarlierFailureOrGap ()
		{
		var snapshot = WithCheckpoint (Snapshot (), c => c with { Samples = [Sample (-181) with { Outcome = SubmissionEvidenceOutcome.Failed }, Sample (-60), Sample (0)] });
		Assert.That (Evaluate (snapshot).Reasons, Does.Contain ("sample-not-passed").And.Contain ("sample-gap"));
		}

	[Test]
	public void CopiedReceiptFromAnotherCandidateCannotReportHealthy ()
		{
		var snapshot = WithCheckpoint (Snapshot (), c => c with { PlanSha256 = new ('f', 64) });
		Assert.That (Evaluate (snapshot).Reasons, Does.Contain ("checkpoint-identity-mismatch"));
		}

	[Test]
	public void LatchedAttentionSurvivesAHealthyFollowingReceipt () =>
		Assert.That (Evaluate (Snapshot () with { AttentionPresent = true }).Reasons, Does.Contain ("attention-latched"));

	[Test]
	public void DisabledOrMissingTaskRequiresAttentionBeforeCompletion ()
		{
		var report = Evaluate (Snapshot () with { TaskPresent = false, TaskEnabled = false, TaskState = "Disabled" });
		Assert.That (report.Reasons, Does.Contain ("task-missing").And.Contain ("task-disabled"));
		}

	[TestCase ("Ready", 267009)]
	[TestCase ("Running", 1)]
	[TestCase ("Ready", 3)]
	[TestCase ("Ready", 4)]
	public void FailedOrInconsistentTaskResultIsNotHealthy (string state, long result) =>
		Assert.That (Evaluate (Snapshot () with { TaskState = state, LastTaskResult = result }).Reasons, Does.Contain ("task-result-failed"));

	[Test]
	public void ExportGuardContentionAfterCompletionDoesNotInventATestFailure ()
		{
		var completed = Completed () with { LastTaskResult = 4 };
		Assert.That (Evaluate (completed).State, Is.EqualTo (SubmissionEnduranceHealthState.Completed));
		Assert.That (Evaluate (completed with { LastTaskResult = 3 }).Reasons, Does.Contain ("task-result-failed"));
		Assert.That (Evaluate (completed with { AttentionPresent = true }).Reasons, Does.Contain ("attention-latched"));
		Assert.That (Evaluate (completed, Now.AddMinutes (2)).Reasons, Does.Contain ("scheduler-stale"));
		Assert.That (Evaluate (WithCheckpoint (completed, c => c with { PlanSha256 = new ('f', 64) })).Reasons, Does.Contain ("checkpoint-identity-mismatch"));
		Assert.That (Evaluate (WithCheckpoint (completed, c => c with { Samples = [Sample (-120) with { Outcome = SubmissionEvidenceOutcome.Failed }, Sample (0)] })).Reasons, Does.Contain ("sample-not-passed"));
		}

	[Test]
	public void CompletionRequiresReleasedOwnershipAndCompleteDuration ()
		{
		var completed = Completed ();
		Assert.That (Evaluate (completed).State, Is.EqualTo (SubmissionEnduranceHealthState.Completed));
		Assert.That (Evaluate (completed with { TaskEnabled = false, TaskState = "Disabled" }).RequiresAttention, Is.False);
		Assert.That (Evaluate (completed with { Scheduler = completed.Scheduler! with { Collector = completed.Scheduler.Collector! with { ReservationState = "Held" } } }).RequiresAttention, Is.True);
		Assert.That (Evaluate (WithCheckpoint (completed, c => c with { Samples = [Sample (-60), Sample (0)] })).Reasons, Does.Contain ("completed-duration-short"));
		}

	[Test]
	public void FutureClockValuesCannotKeepTheMonitorHealthy ()
		{
		var snapshot = Snapshot () with { CheckedUtc = Now.AddSeconds (3), Scheduler = Snapshot ().Scheduler! with { ObservedUtc = Now.AddSeconds (3) } };
		Assert.That (Evaluate (snapshot).Reasons, Does.Contain ("observer-invalid-time").And.Contain ("scheduler-invalid-time"));
		}

	[Test]
	public void EmptyInterruptedOrMalformedCollectionRequiresAttention ()
		{
		Assert.That (Evaluate (Snapshot () with { Scheduler = null }).RequiresAttention, Is.True);
		Assert.That (Evaluate (WithCheckpoint (Snapshot (), c => c with { Samples = [] })).Reasons, Does.Contain ("samples-missing"));
		Assert.That (Evaluate (WithCheckpoint (Snapshot (), c => c with { State = SubmissionEnduranceState.ProbePending })).RequiresAttention, Is.True);
		Assert.That (Evaluate (WithCheckpoint (Completed (), c => c with { Samples = [null!, Sample (0)] })).RequiresAttention, Is.True);
		}

	[Test]
	public void ChangedBootOrReversedSampleTimesRequireInspection ()
		{
		var snapshot = WithCheckpoint (Snapshot (), c => c with { Samples = [Sample (-60), Sample (-120) with { BootIdentity = "other-boot" }, Sample (0)] });
		Assert.That (Evaluate (snapshot).Reasons, Does.Contain ("sample-time-invalid").And.Contain ("sample-boot-changed"));
		}

	[Test]
	public void InvalidFreshnessPolicyIsRejected ()
		{
		Assert.Throws<ArgumentOutOfRangeException> (() => SubmissionEnduranceHealth.Evaluate (Plan, Snapshot (), Now, TimeSpan.Zero, TimeSpan.Zero));
		Assert.Throws<ArgumentOutOfRangeException> (() => SubmissionEnduranceHealth.Evaluate (Plan, Snapshot (), Now, TimeSpan.FromMinutes (1), TimeSpan.FromSeconds (-1)));
		}
	}
