// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeDevTools;

public sealed record SubmissionExecutionRequirements (
	string Target, string Method, SubmissionEvidenceOutcome RequiredOutcome, int? ResponseLimitSeconds,
	bool Restore, int? MaximumSampleGapSeconds = null);
public sealed record SubmissionResponseObservation (
	DateTimeOffset TriggeredUtc, DateTimeOffset ObservedUtc, string TriggerEvidence, string ResponseEvidence);
public sealed record SubmissionRestorationObservation (
	DateTimeOffset OriginalCapturedUtc, DateTimeOffset ActionStartedUtc, DateTimeOffset RestoredUtc,
	DateTimeOffset VerifiedUtc, bool MatchesOriginal, string OriginalEvidence, string VerificationEvidence);
public sealed record SubmissionFunctionalSample (
	DateTimeOffset ObservedUtc, SubmissionEvidenceOutcome Outcome, string Evidence);
public sealed record SubmissionExecutionObservation (
	string Target, string Method, SubmissionResponseObservation? Response = null,
	SubmissionRestorationObservation? Restoration = null, IReadOnlyList<SubmissionFunctionalSample>? Samples = null);

/// <summary>Checks scoped measurement records. The calling workflow must authenticate producers and approve the policy.</summary>
internal static class SubmissionExecution
	{
	internal static void ValidatePolicy (SubmissionRequirement requirement)
		{
		var contract = requirement.Execution;
		if (contract == null)
			return;
		if (string.IsNullOrWhiteSpace (contract.Target) ||
			contract.Method is not ("android" or "configuration" or "combined" or "absence" or "outage" or "endurance") ||
			contract.RequiredOutcome is not (SubmissionEvidenceOutcome.Passed or SubmissionEvidenceOutcome.NotApplicable) ||
			(contract.RequiredOutcome == SubmissionEvidenceOutcome.NotApplicable && !requirement.AllowNotApplicable) ||
			(contract.Method == "absence") != (contract.RequiredOutcome == SubmissionEvidenceOutcome.NotApplicable) ||
			contract.ResponseLimitSeconds is <= 0 || contract.MaximumSampleGapSeconds is <= 0)
			throw new ArgumentException ("Execution policy requires a target, supported method, consistent outcome and positive measurement limits.");
		}

	internal static void Evaluate (SubmissionRequirement requirement, SubmissionObservation observation, List<SubmissionEvidenceIssue> issues)
		{
		var contract = requirement.Execution;
		if (contract == null)
			return;
		void Issue (string code, string message) => issues.Add (new (requirement.Id, code, message));
		bool InWindow (DateTimeOffset value) => value != default && value >= observation.StartedUtc && value <= observation.FinishedUtc;
		bool Retained (string? path) => !string.IsNullOrWhiteSpace (path) &&
			(observation.Files?.Any (file => file != null && file.RelativePath == path) ?? false);
		if (observation.Outcome != contract.RequiredOutcome)
			Issue ("execution-outcome", "The observation does not have the outcome required by its scoped execution policy.");
		if (observation.Files == null || observation.Files.Count == 0)
			Issue ("execution-files", "Every scoped observation, including non-applicability, requires retained evidence.");
		var execution = observation.Execution;
		if (execution == null)
			{
			Issue ("execution-missing", "The observation has no scoped execution measurements.");
			return;
			}
		if (execution.Target != contract.Target || execution.Method != contract.Method)
			Issue ("execution-scope", "The measurements describe a different target or observation method.");
		if (contract.ResponseLimitSeconds is int limit)
			{
			var response = execution.Response;
			if (response == null)
				Issue ("response-missing", "A measured trigger and response are required.");
			else
				{
				if (!InWindow (response.TriggeredUtc) || !InWindow (response.ObservedUtc) || response.ObservedUtc < response.TriggeredUtc)
					Issue ("response-time", "Response timestamps must be ordered within the observation interval.");
				else if (response.ObservedUtc - response.TriggeredUtc > TimeSpan.FromSeconds (limit))
					Issue ("response-deadline", "The observed response exceeded the policy deadline.");
				if (!Retained (response.TriggerEvidence) || !Retained (response.ResponseEvidence))
					Issue ("response-evidence", "Trigger and response must reference files retained by this observation.");
				}
			}
		if (contract.Restore)
			{
			var restoration = execution.Restoration;
			if (restoration == null)
				Issue ("restoration-missing", "Original state, action, restoration and independent verification are required.");
			else
				{
				if (!restoration.MatchesOriginal)
					Issue ("restoration-unconfirmed", "The restored state was not confirmed to match the captured original state.");
				if (!InWindow (restoration.OriginalCapturedUtc) || !InWindow (restoration.ActionStartedUtc) ||
					!InWindow (restoration.RestoredUtc) || !InWindow (restoration.VerifiedUtc) ||
					restoration.OriginalCapturedUtc > restoration.ActionStartedUtc || restoration.ActionStartedUtc > restoration.RestoredUtc ||
					restoration.RestoredUtc > restoration.VerifiedUtc ||
					(execution.Response != null && (execution.Response.TriggeredUtc < restoration.ActionStartedUtc || execution.Response.ObservedUtc > restoration.RestoredUtc)))
					Issue ("restoration-time", "Capture must precede the action and verification must follow restoration within the observation interval.");
				if (!Retained (restoration.OriginalEvidence) || !Retained (restoration.VerificationEvidence))
					Issue ("restoration-evidence", "Original and verified restored state must reference retained evidence files.");
				}
			}
		if (contract.Method == "endurance" && contract.MaximumSampleGapSeconds == null)
			Issue ("sampling-policy", "Endurance requires a reviewed maximum gap between functional observations.");
		if (contract.MaximumSampleGapSeconds is int maximumGap)
			{
			var samples = execution.Samples;
			if (samples == null || samples.Count < 2)
				{
				Issue ("samples-missing", "Periodic functional samples are required; elapsed time alone is insufficient.");
				return;
				}
			var previous = observation.StartedUtc;
			var first = true;
			foreach (var sample in samples)
				{
				if (sample == null)
					{
					Issue ("sample-invalid", "Functional samples must not be null.");
					continue;
					}
				if (!InWindow (sample.ObservedUtc) || sample.ObservedUtc < previous || (!first && sample.ObservedUtc == previous))
					Issue ("sample-time", "Functional samples must have strictly increasing timestamps within the observation interval.");
				if (sample.ObservedUtc - previous > TimeSpan.FromSeconds (maximumGap))
					Issue ("sample-gap", "The functional observation gap exceeds the policy limit.");
				if (sample.Outcome != SubmissionEvidenceOutcome.Passed || !Retained (sample.Evidence))
					Issue ("sample-result", "Each functional sample must pass and reference retained evidence.");
				previous = sample.ObservedUtc;
				first = false;
				}
			if (samples[0]?.ObservedUtc != observation.StartedUtc || samples[^1]?.ObservedUtc != observation.FinishedUtc)
				Issue ("sample-coverage", "Functional samples must cover the start and end of the claimed observation interval.");
			}
		}
	}