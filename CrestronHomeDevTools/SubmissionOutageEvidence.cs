// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

public sealed record SubmissionOutageEvidenceResult (SubmissionOutageMeasurementReport Measurements,
	 SubmissionEvidenceDocument Observations);

/// <summary>Imports a bounded outage record into the normal evidence pipeline. The caller must
/// approve the plan's hardware scope and authenticate the record's producer independently.</summary>
public static class SubmissionOutageEvidence
	{
	private static readonly JsonSerializerOptions Json = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		RespectRequiredConstructorParameters = true,
		RespectNullableAnnotations = true,
		AllowDuplicateProperties = false,
		Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
		};
	/// <summary>Inputs and raw captures stay under the retained evidence root. This never changes
	/// an incomplete measurement into a pass, operates hardware, or authorizes submission.</summary>
	public static SubmissionOutageEvidenceResult ImportFiles (string evidenceDirectory,
		 string planRelativePath, string expectedPlanSha256, string recordRelativePath, string expectedRecordSha256,
		 string policyRelativePath, DateTimeOffset now, CancellationToken cancellationToken = default)
		{
		cancellationToken.ThrowIfCancellationRequested ();
		string root = Path.GetFullPath (evidenceDirectory);
		byte[] Read (string relative, string digest)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (digest?.Length != 64 || !digest.All (char.IsAsciiHexDigit) ||
				 !SubmissionEvidence.SafeEvidencePath (root, relative, out var full))
				throw new InvalidDataException ("Outage inputs require retained paths and independently pinned digests.");
			using var input = new FileStream (full, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (input.Length > 16 * 1024 * 1024)
				throw new InvalidDataException ("Outage JSON exceeds 16 MiB.");
			var bytes = new byte[checked((int)input.Length)];
			input.ReadExactly (bytes);
			if (!string.Equals (Convert.ToHexStringLower (SHA256.HashData (bytes)), digest, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("Outage input differs from its pinned digest.");
			return bytes;
			}
		T Parse<T> (byte[] bytes) => JsonSerializer.Deserialize<T> (bytes, Json) ?? throw new InvalidDataException ("Empty outage input.");
		var plan = Parse<SubmissionOutageMeasurementPlan> (Read (planRelativePath, expectedPlanSha256));
		var record = Parse<SubmissionOutageMeasurementRecord> (Read (recordRelativePath, expectedRecordSha256));
		var measurements = SubmissionOutageMeasurements.Assess (plan, record, root, now, cancellationToken);
		var policy = Parse<SubmissionEvidencePolicy> (Read (policyRelativePath, plan.Identity.PolicySha256));
		var matches = policy.Requirements?.Where (r => r?.Id == plan.RequirementId).ToArray ();
		if (policy.SchemaVersion != 1 || matches is not { Length: 1 })
			throw new InvalidDataException ("Pinned policy must contain exactly one matching outage requirement.");
		var requirement = matches[0];
		SubmissionExecution.ValidatePolicy (requirement);
		var contract = requirement.Execution;
		if (contract is not { Method: "outage", RequiredOutcome: SubmissionEvidenceOutcome.Passed, Restore: true, ResponseLimitSeconds: > 0 } ||
			 requirement.MinimumDuration > plan.MinimumInterruption || plan.RecoveryLimit.TotalSeconds > contract.ResponseLimitSeconds ||
			 requirement.PriorEvidence != null)
			throw new InvalidDataException ("Outage measurement scope must meet the pinned duration, restoration and recovery policy.");

		var captures = new List<SubmissionOutageCapture> { record.OriginalState, record.VerifiedState };
		captures.AddRange (record.Interruptions.SelectMany (i => new[] { i.Interrupted, i.Restored }));
		captures.AddRange (record.Functions.Select (f => f.Observation));
		if (record.ProgramLoaded != null)
			captures.Add (record.ProgramLoaded);
		var files = new List<SubmissionEvidenceFile>{new(planRelativePath,expectedPlanSha256),
				new(recordRelativePath,expectedRecordSha256),new(policyRelativePath,plan.Identity.PolicySha256)};
		files.AddRange (captures.Select (c => c.Evidence));
		// Avoid duplicate references without silently choosing between conflicting provenance.
		var unique = new Dictionary<string, SubmissionEvidenceFile> (StringComparer.OrdinalIgnoreCase);
		foreach (var file in files)
			{
			var key = file.RelativePath.Replace ('\\', '/');
			if (unique.TryGetValue (key, out var previous) && !string.Equals (previous.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("Conflicting retained outage evidence references.");
			unique.TryAdd (key, file);
			}
		SubmissionResponseObservation? response = null;
		if (measurements.MaximumRecoverySeconds != null)
			{
			// Use the conservative endpoints, never midpoint or the operator's later reply time.
			var trigger = plan.RecoveryClock == SubmissionOutageRecoveryClock.ProgramLoaded ? record.ProgramLoaded! :
				 record.Interruptions.MaxBy (i => i.Restored.EarliestUtc)!.Restored;
			var recovered = record.Functions.MaxBy (f => f.Observation.LatestUtc)!.Observation;
			response = new (trigger.EarliestUtc, recovered.LatestUtc, trigger.Evidence.RelativePath, recovered.Evidence.RelativePath);
			}
		SubmissionRestorationObservation? restoration = null;
		if (record.Interruptions.Length > 0)
			restoration = new (record.OriginalState.EarliestUtc, record.Interruptions.Min (i => i.Interrupted.EarliestUtc),
				 record.VerifiedState.EarliestUtc, record.VerifiedState.LatestUtc, record.MatchesOriginal,
				 record.OriginalState.Evidence.RelativePath, record.VerifiedState.Evidence.RelativePath);
		var observation = new SubmissionObservation (plan.RequirementId, plan.Identity, measurements.Outcome,
			 record.OriginalState.EarliestUtc, record.VerifiedState.LatestUtc, unique.Values.ToArray (),
			 "Bounded outage measurement; retained plan defines the reviewed components, functions and clock. " +
			 "This is not full submission acceptance. Issues: " + string.Join (", ", measurements.Issues),
			 new (contract.Target, "outage", response, restoration));
		// A passing measurement must also satisfy the normal execution/evidence contract.
		if (measurements.MeasurementChecksPassed &&
			 !SubmissionEvidence.Evaluate (plan.Identity, [requirement], [observation], root, now, cancellationToken).EvidenceChecksPassed)
			throw new InvalidDataException ("Passing outage measurement did not satisfy the normal scoped evidence contract.");
		return new (measurements, new (1, [observation]));
		}
	}