// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;

namespace CrestronHomeDevTools;

public sealed record SubmissionEvidenceCompositionPlan (
	int SchemaVersion, SubmissionEvidenceIdentity Identity, IReadOnlyList<SubmissionEvidenceFile> Sources);
public sealed record SubmissionEvidenceCompositionReport (
	string PlanSha256, SubmissionEvidenceDocument Observations, SubmissionEvidenceReport Evidence)
	{
	public bool CompositionChecksPassed => Evidence.EvidenceChecksPassed;
	}

/// <summary>Combines original scoped observations without selecting winners, changing outcomes or inferring assertions.
/// Callers must independently approve the complete policy, source inventory and evidence producers.</summary>
public static class SubmissionEvidenceComposition
	{
	/// <summary>Paths are relative to evidenceDirectory. The plan digest must come from the trusted coordinator.
	/// Original observations are retained even when validation fails; an incomplete report never authorizes submission.</summary>
	public static SubmissionEvidenceCompositionReport CombineFiles (string evidenceDirectory, string planPath,
		string expectedPlanSha256, string policyPath, DateTimeOffset now, CancellationToken cancellationToken = default)
		{
		var root = Path.GetFullPath (evidenceDirectory);
		var inputs = new List<FileStream> ();
		long totalBytes = 0;
		try
			{
			(byte[] Bytes, SubmissionEvidenceFile File) Read (string relative, string expected)
				{
				cancellationToken.ThrowIfCancellationRequested ();
				if (expected?.Length != 64 || !expected.All (char.IsAsciiHexDigit) ||
					!SubmissionEvidence.SafeEvidencePath (root, relative, out var path))
					throw new ArgumentException ("Composition inputs require safe retained paths and independently reviewed SHA-256 digests.");
				var input = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
				inputs.Add (input);
				if (input.Length > 16 * 1024 * 1024 || (totalBytes += input.Length) > 64 * 1024 * 1024)
					throw new ArgumentException ("Composition JSON inputs are limited to 16 MiB each and 64 MiB in total.");
				var bytes = new byte[checked((int)input.Length)];
				input.ReadExactly (bytes);
				string digest = Convert.ToHexStringLower (SHA256.HashData (bytes));
				if (!digest.Equals (expected, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException ("Composition input differs from its reviewed digest.");
				return (bytes, new (relative, digest));
				}
			var retainedPlan = Read (planPath, expectedPlanSha256);
			var plan = SubmissionValidation.Read<SubmissionEvidenceCompositionPlan> (retainedPlan.Bytes);
			if (plan.SchemaVersion != 1 || plan.Sources.Count is < 1 or > 1024 || plan.Sources.Any (file => file == null) ||
				plan.Sources.Select (file => file.RelativePath.Replace ('\\', '/')).Distinct (StringComparer.OrdinalIgnoreCase).Count () != plan.Sources.Count)
				throw new ArgumentException ("Composition requires schema 1 and a nonempty, unique source inventory of at most 1024 documents.");
			var retainedPolicy = Read (policyPath, plan.Identity.PolicySha256);
			var policy = SubmissionValidation.Read<SubmissionEvidencePolicy> (retainedPolicy.Bytes);
			if (policy.SchemaVersion != 1) throw new ArgumentException ("Unsupported evidence policy schema.");
			// Validate the entire policy before reading sources; missing observations are expected at this point.
			SubmissionEvidence.Evaluate (plan.Identity, policy.Requirements, [], root, now, cancellationToken);
			var combined = new List<SubmissionObservation> ();
			var sourceIssues = new List<SubmissionEvidenceIssue> ();
			foreach (var source in plan.Sources)
				{
				var retained = Read (source.RelativePath, source.Sha256);
				var document = SubmissionValidation.Read<SubmissionEvidenceDocument> (retained.Bytes);
				if (document.SchemaVersion != 1 || document.Observations.Count == 0 || document.Observations.Any (item => item == null))
					throw new ArgumentException ("Each source must contain an original, nonempty schema 1 observation document.");
				var sourceReport = SubmissionEvidence.Evaluate (plan.Identity, policy.Requirements, document.Observations, root, now, cancellationToken);
				// Other documents may supply missing scopes, but provenance files must never repair an invalid original observation.
				var invalidSource = sourceReport.Issues.Where (issue => issue.Code != "missing-observation").ToArray ();
				sourceIssues.AddRange (invalidSource);
				foreach (var observation in document.Observations)
					{
					// Never change identity, scope, outcome, timestamps, measurements or rationale.
					// Retain duplicate/conflicting observations so the normal evaluator rejects them.
					var files = invalidSource.Length == 0
						? observation.Files.Concat ([retainedPlan.File, retainedPolicy.File, retained.File]).ToArray ()
						: observation.Files;
					combined.Add (observation with { Files = files });
					}
				}
			var result = new SubmissionEvidenceDocument (1, combined.AsReadOnly ());
			var report = SubmissionEvidence.Evaluate (plan.Identity, policy.Requirements, result.Observations, root, now, cancellationToken);
			return new (retainedPlan.File.Sha256, result, new (sourceIssues.Concat (report.Issues).Distinct ().ToArray ()));
			}
		finally
			{
			foreach (var input in inputs) input.Dispose ();
			}
		}
	}