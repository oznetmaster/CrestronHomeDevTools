// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;

namespace CrestronHomeDevTools;

public enum SubmissionOutageRecoveryClock
	{
	NetworkRestored, ProgramLoaded
	}
/// <summary>Bounds on the physical event time, not the later time an operator replied.</summary>
public sealed record SubmissionOutageCapture (DateTimeOffset EarliestUtc, DateTimeOffset LatestUtc,
	 SubmissionEvidenceFile Evidence);
public sealed record SubmissionComponentInterruption (string Component,
	 SubmissionOutageCapture Interrupted, SubmissionOutageCapture Restored);
public sealed record SubmissionOutageFunction (string Id, SubmissionEvidenceOutcome Outcome,
	 SubmissionOutageCapture Observation);
/// <summary>Independently review and pin the scope and functional assertions before the test.</summary>
public sealed record SubmissionOutageMeasurementPlan (SubmissionEvidenceIdentity Identity,
	 string RequirementId, string[] RequiredComponents, string[] RequiredFunctions,
	 TimeSpan MinimumInterruption, TimeSpan RecoveryLimit, SubmissionOutageRecoveryClock RecoveryClock,
	 string? ProgramComponent = null);
public sealed record SubmissionOutageMeasurementRecord (int SchemaVersion, SubmissionEvidenceIdentity Identity,
	 SubmissionComponentInterruption[] Interruptions, SubmissionOutageCapture? ProgramLoaded,
	 SubmissionOutageFunction[] Functions, SubmissionOutageCapture OriginalState,
	 SubmissionOutageCapture VerifiedState, bool MatchesOriginal);
public sealed record SubmissionOutageMeasurementReport (SubmissionEvidenceIdentity Identity,
	 string RequirementId, SubmissionEvidenceOutcome Outcome, double? GuaranteedInterruptionSeconds,
	 double? MinimumRecoverySeconds, double? MaximumRecoverySeconds, string[] Issues)
	{
	public bool MeasurementChecksPassed => Outcome == SubmissionEvidenceOutcome.Passed;
	}

/// <summary>
/// Assesses bounded outage measurements and retained file integrity. Does not interrupt equipment,
/// infer functionality from reachability, authenticate producer assertions, or approve checklist scope.
/// </summary>
public static class SubmissionOutageMeasurements
	{
	internal static void ValidatePlan (SubmissionOutageMeasurementPlan plan)
		{
		ArgumentNullException.ThrowIfNull (plan);
		static bool Names (string[]? names) => names is { Length: > 0 and <= 128 } &&
			 names.All (n => !string.IsNullOrWhiteSpace (n) && n.Length <= 256) &&
			 names.Distinct (StringComparer.Ordinal).Count () == names.Length;
		static bool Hex (string? value, int length) => value?.Length == length && value.All (char.IsAsciiHexDigit);
		if (plan.Identity == null || !Hex (plan.Identity.PackageSha256, 64) || !Hex (plan.Identity.PolicySha256, 64) ||
			 !Hex (plan.Identity.TemplateSha256, 64) || !Hex (plan.Identity.SourceCommit, 40) ||
			 string.IsNullOrWhiteSpace (plan.RequirementId) || !Names (plan.RequiredComponents) || !Names (plan.RequiredFunctions) ||
			 plan.MinimumInterruption <= TimeSpan.Zero || plan.RecoveryLimit <= TimeSpan.Zero || !Enum.IsDefined (plan.RecoveryClock) ||
			 (plan.RecoveryClock == SubmissionOutageRecoveryClock.ProgramLoaded &&
				  !plan.RequiredComponents.Contains (plan.ProgramComponent, StringComparer.Ordinal)) ||
			 (plan.RecoveryClock == SubmissionOutageRecoveryClock.NetworkRestored && plan.ProgramComponent != null))
			throw new InvalidDataException ("Outage measurements require the pinned identity, explicit scope, functions and positive timing limits.");
		}
	public static SubmissionOutageMeasurementReport Assess (SubmissionOutageMeasurementPlan plan,
		 SubmissionOutageMeasurementRecord record, string evidenceDirectory, DateTimeOffset now,
		 CancellationToken cancellationToken = default)
		{
		ValidatePlan (plan);
		ArgumentNullException.ThrowIfNull (record);
		cancellationToken.ThrowIfCancellationRequested ();
		static bool Hex (string? value, int length) => value?.Length == length && value.All (char.IsAsciiHexDigit);
		if (record.SchemaVersion != 1 || record.Identity != plan.Identity || now == default ||
			 record.Interruptions is not { Length: <= 128 } || record.Functions is not { Length: <= 128 })
			throw new InvalidDataException ("Outage measurements require the pinned identity, explicit scope, functions and positive timing limits.");
		string root = Path.GetFullPath (evidenceDirectory);
		var files = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);
		void Capture (SubmissionOutageCapture? capture)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (capture == null || capture.EarliestUtc == default || capture.LatestUtc < capture.EarliestUtc ||
				 capture.LatestUtc > now || capture.Evidence == null || !Hex (capture.Evidence.Sha256, 64) ||
				 !SubmissionEvidence.SafeEvidencePath (root, capture.Evidence.RelativePath, out string path))
				throw new InvalidDataException ("A capture requires ordered event bounds and a retained evidence file.");
			if (files.TryGetValue (capture.Evidence.RelativePath, out var hash))
				{
				if (!string.Equals (hash, capture.Evidence.Sha256, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException ("Conflicting capture digests.");
				return;
				}
			using var input = File.OpenRead (path);
			if (input.Length > 64 * 1024 * 1024 || !string.Equals (Convert.ToHexStringLower (SHA256.HashData (input)),
					  capture.Evidence.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("Capture evidence changed or exceeds its size limit.");
			files.Add (capture.Evidence.RelativePath, capture.Evidence.Sha256);
			}
		Capture (record.OriginalState);
		Capture (record.VerifiedState);
		if (record.OriginalState.LatestUtc > record.VerifiedState.EarliestUtc)
			throw new InvalidDataException ("Original capture must precede restoration verification.");
		var components = new HashSet<string> (StringComparer.Ordinal);
		foreach (var interruption in record.Interruptions)
			{
			if (interruption == null || !plan.RequiredComponents.Contains (interruption.Component, StringComparer.Ordinal) || !components.Add (interruption.Component))
				throw new InvalidDataException ("Unexpected or duplicate interruption component; preserve and review changed test scope.");
			Capture (interruption.Interrupted);
			Capture (interruption.Restored);
			if (record.OriginalState.LatestUtc > interruption.Interrupted.EarliestUtc ||
				 interruption.Interrupted.LatestUtc > interruption.Restored.EarliestUtc ||
				 interruption.Restored.LatestUtc > record.VerifiedState.EarliestUtc)
				throw new InvalidDataException ("Interruption and restoration bounds must be ordered inside the captured test.");
			}
		var functions = new HashSet<string> (StringComparer.Ordinal);
		foreach (var function in record.Functions)
			{
			if (function == null || !plan.RequiredFunctions.Contains (function.Id, StringComparer.Ordinal) || !functions.Add (function.Id) ||
				 function.Outcome is not (SubmissionEvidenceOutcome.Passed or SubmissionEvidenceOutcome.Failed or SubmissionEvidenceOutcome.Partial or SubmissionEvidenceOutcome.Inconclusive or SubmissionEvidenceOutcome.NotTested))
				throw new InvalidDataException ("Unexpected, duplicate or invalid functional result.");
			Capture (function.Observation);
			if (function.Observation.EarliestUtc < record.OriginalState.LatestUtc || function.Observation.LatestUtc > record.VerifiedState.EarliestUtc)
				throw new InvalidDataException ("Functional results must be inside the captured test.");
			}
		if (record.ProgramLoaded != null)
			{
			Capture (record.ProgramLoaded);
			if (record.ProgramLoaded.EarliestUtc < record.OriginalState.LatestUtc || record.ProgramLoaded.LatestUtc > record.VerifiedState.EarliestUtc)
				throw new InvalidDataException ("Program-load event is outside the captured test.");
			}
		var issues = new List<string> ();
		bool failed = false;
		foreach (var name in plan.RequiredComponents.Except (components, StringComparer.Ordinal))
			issues.Add ("component-not-observed:" + name);
		foreach (var name in plan.RequiredFunctions.Except (functions, StringComparer.Ordinal))
			issues.Add ("function-not-observed:" + name);
		foreach (var function in record.Functions.Where (f => f.Outcome != SubmissionEvidenceOutcome.Passed))
			{
			issues.Add ("function-not-passed:" + function.Id);
			failed |= function.Outcome == SubmissionEvidenceOutcome.Failed;
			}
		if (!record.MatchesOriginal)
			{
			issues.Add ("original-state-not-restored");
			failed = true;
			}
		double? interruptionSeconds = null, minimumRecovery = null, maximumRecovery = null;
		DateTimeOffset? clockEarliest = null, clockLatest = null;
		if (components.Count == plan.RequiredComponents.Length)
			{
			// All selected components must share the minimum outage interval.
			DateTimeOffset lossLatest = record.Interruptions.Max (i => i.Interrupted.LatestUtc);
			DateTimeOffset restoreEarliest = record.Interruptions.Min (i => i.Restored.EarliestUtc);
			interruptionSeconds = Math.Max (0, (restoreEarliest - lossLatest).TotalSeconds);
			if (interruptionSeconds < plan.MinimumInterruption.TotalSeconds)
				issues.Add ("minimum-interruption-unproven");
			if (plan.RecoveryClock == SubmissionOutageRecoveryClock.NetworkRestored)
				{
				clockEarliest = record.Interruptions.Max (i => i.Restored.EarliestUtc);
				clockLatest = record.Interruptions.Max (i => i.Restored.LatestUtc);
				}
			else if (record.ProgramLoaded is { } load)
				{
				// A program-load marker from before the measured outage cannot start this recovery clock.
				if (load.EarliestUtc < lossLatest)
					throw new InvalidDataException ("Program-load marker precedes the measured outage.");
				var processor = record.Interruptions.Single (i => i.Component == plan.ProgramComponent);
				if (load.EarliestUtc < processor.Restored.LatestUtc)
					issues.Add ("program-load-not-after-power-restoration");
				clockEarliest = load.EarliestUtc;
				clockLatest = load.LatestUtc;
				}
			}
		if (plan.RecoveryClock == SubmissionOutageRecoveryClock.ProgramLoaded && record.ProgramLoaded == null)
			issues.Add ("program-load-time-not-observed");
		if (clockEarliest != null && clockLatest != null && functions.Count == plan.RequiredFunctions.Length)
			{
			DateTimeOffset allRestored = record.Interruptions.Max (i => i.Restored.LatestUtc);
			if (record.Functions.Any (f => f.Observation.EarliestUtc < clockLatest || f.Observation.EarliestUtc < allRestored))
				issues.Add ("functional-evidence-not-after-restoration");
			else
				{
				minimumRecovery = (record.Functions.Max (f => f.Observation.EarliestUtc) - clockLatest.Value).TotalSeconds;
				maximumRecovery = (record.Functions.Max (f => f.Observation.LatestUtc) - clockEarliest.Value).TotalSeconds;
				if (minimumRecovery > plan.RecoveryLimit.TotalSeconds)
					{
					issues.Add ("recovery-deadline-exceeded");
					failed = true;
					}
				else if (maximumRecovery > plan.RecoveryLimit.TotalSeconds)
					issues.Add ("recovery-deadline-unproven");
				}
			}
		return new (plan.Identity, plan.RequirementId, failed ? SubmissionEvidenceOutcome.Failed :
			 issues.Count == 0 ? SubmissionEvidenceOutcome.Passed : SubmissionEvidenceOutcome.Partial,
			 interruptionSeconds, minimumRecovery, maximumRecovery, issues.ToArray ());
		}
	}