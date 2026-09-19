// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeDevTools;

/// <summary>A completed scheduler receipt, copied without opening the collector journal.</summary>
public sealed record SubmissionEnduranceSchedulerSnapshot (int SchemaVersion, DateTimeOffset ObservedUtc,
	string State, int ExitCode, SubmissionEnduranceMonitorStatus? Collector);

/// <summary>
/// A trusted independent observer supplies current reachability/task state and the completed receipt.
/// CheckedUtc is the observation time, not the time an old snapshot was copied into another directory.
/// </summary>
public sealed record SubmissionEnduranceHealthSnapshot (DateTimeOffset CheckedUtc, bool WorkerReachable,
	bool TaskPresent, bool TaskEnabled, string TaskState, long LastTaskResult, bool AttentionPresent,
	SubmissionEnduranceSchedulerSnapshot? Scheduler);

public enum SubmissionEnduranceHealthState { Collecting, Completed, AttentionRequired }

/// <summary>Operational monitoring only. Completed is not independently validated submission evidence.</summary>
public sealed record SubmissionEnduranceHealthReport (SubmissionEnduranceHealthState State,
	IReadOnlyList<string> Reasons, DateTimeOffset EvaluatedUtc, DateTimeOffset? LastSampleUtc)
	{
	public bool RequiresAttention => State == SubmissionEnduranceHealthState.AttentionRequired;
	}

/// <summary>
/// Classifies passive scheduler observations without taking collector locks, contacting hardware,
/// restarting anything or sending notifications. Run the observer independently of the monitored worker.
/// </summary>
public static class SubmissionEnduranceHealth
	{
	public static SubmissionEnduranceHealthReport Evaluate (SubmissionEndurancePlan plan,
		SubmissionEnduranceHealthSnapshot snapshot, DateTimeOffset now,
		TimeSpan maximumStatusAge, TimeSpan clockTolerance)
		{
		ArgumentNullException.ThrowIfNull (snapshot);
		SubmissionEndurance.ValidatePlan (plan);
		if (maximumStatusAge <= TimeSpan.Zero || maximumStatusAge > TimeSpan.FromDays (1))
			throw new ArgumentOutOfRangeException (nameof (maximumStatusAge));
		if (clockTolerance < TimeSpan.Zero || clockTolerance > TimeSpan.FromMinutes (1))
			throw new ArgumentOutOfRangeException (nameof (clockTolerance));
		var reasons = new List<string> ();
		void Fresh (DateTimeOffset value, string prefix, TimeSpan maximumAge)
			{
			if (value == default || value > now + clockTolerance) reasons.Add (prefix + "-invalid-time");
			else if (now - value > maximumAge) reasons.Add (prefix + "-stale");
			}
		Fresh (snapshot.CheckedUtc, "observer", maximumStatusAge);
		if (!snapshot.WorkerReachable) reasons.Add ("worker-unreachable");
		if (!snapshot.TaskPresent) reasons.Add ("task-missing");
		if (snapshot.AttentionPresent) reasons.Add ("attention-latched");
		var receipt = snapshot.Scheduler;
		var checkpoint = receipt?.Collector?.Checkpoint;
		bool completed = receipt?.State == "Passed" && receipt.ExitCode == 0 &&
			receipt.Collector?.ReservationState == "Released" && checkpoint?.State == SubmissionEnduranceState.Passed;
		if (!snapshot.TaskEnabled && !completed) reasons.Add ("task-disabled");
		if (snapshot.TaskState is not ("Ready" or "Running") && !(completed && snapshot.TaskState == "Disabled"))
			reasons.Add ("task-state-unconfirmed");
		// SCHED_S_TASK_RUNNING is an in-progress result, not a completed task failure.
		if (snapshot.LastTaskResult != 0 && !(snapshot.TaskState == "Running" && snapshot.LastTaskResult == 267009))
			reasons.Add ("task-result-failed");
		if (receipt == null) reasons.Add ("scheduler-receipt-missing");
		else
			{
			if (receipt.SchemaVersion != 1) reasons.Add ("scheduler-schema-unsupported");
			Fresh (receipt.ObservedUtc, "scheduler", maximumStatusAge);
			if (receipt.ObservedUtc > snapshot.CheckedUtc + clockTolerance) reasons.Add ("scheduler-after-observer");
			if (receipt.ExitCode != 0) reasons.Add ("scheduler-result-failed");
			if (!completed && (receipt.State != "Collecting" || receipt.Collector?.ReservationState != "Held" ||
				checkpoint?.State != SubmissionEnduranceState.Collecting)) reasons.Add ("collection-state-unconfirmed");
			}
		DateTimeOffset? lastSample = null;
		if (checkpoint == null) reasons.Add ("checkpoint-missing");
		else
			{
			if (checkpoint.SchemaVersion != 1 || checkpoint.PlanSha256 != SubmissionEndurance.PlanDigest (plan))
				reasons.Add ("checkpoint-identity-mismatch");
			if (checkpoint.UpdatedUtc == default || checkpoint.UpdatedUtc > now + clockTolerance || checkpoint.UpdatedUtc > receipt!.ObservedUtc + clockTolerance)
				reasons.Add ("checkpoint-time-invalid");
			if (checkpoint.Samples == null || checkpoint.Samples.Count == 0) reasons.Add ("samples-missing");
			else
				{
				var maximumGap = TimeSpan.FromSeconds (plan.Requirement.Execution!.MaximumSampleGapSeconds!.Value);
				DateTimeOffset? previous = null;
				string? boot = null;
				foreach (var sample in checkpoint.Samples)
					{
					if (sample == null) { reasons.Add ("sample-invalid"); continue; }
					if (sample.Outcome != SubmissionEvidenceOutcome.Passed) reasons.Add ("sample-not-passed");
					if (sample.ObservedUtc == default || sample.ObservedUtc > now + clockTolerance || sample.ObservedUtc > checkpoint.UpdatedUtc + clockTolerance ||
						(previous.HasValue && sample.ObservedUtc <= previous.Value)) reasons.Add ("sample-time-invalid");
					if (previous.HasValue && sample.ObservedUtc - previous.Value > maximumGap) reasons.Add ("sample-gap");
					if (string.IsNullOrWhiteSpace (sample.BootIdentity) || (boot != null && sample.BootIdentity != boot)) reasons.Add ("sample-boot-changed");
					boot ??= sample.BootIdentity;
					previous = sample.ObservedUtc;
					}
				lastSample = previous;
				if (!completed && lastSample.HasValue) Fresh (lastSample.Value, "sample", maximumGap);
				DateTimeOffset? firstSample = checkpoint.Samples[0]?.ObservedUtc;
				if (completed && (!lastSample.HasValue || !firstSample.HasValue || lastSample.Value - firstSample.Value < plan.Requirement.MinimumDuration))
					reasons.Add ("completed-duration-short");
				}
			}
		return new (reasons.Count != 0 ? SubmissionEnduranceHealthState.AttentionRequired :
			completed ? SubmissionEnduranceHealthState.Completed : SubmissionEnduranceHealthState.Collecting,
			reasons.Distinct (StringComparer.Ordinal).ToArray (), now, lastSample);
		}
	}