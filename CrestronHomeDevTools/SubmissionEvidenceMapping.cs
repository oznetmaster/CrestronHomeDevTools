// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record SubmissionRequirementMapping (
	string SourceRequirementId, string DestinationRequirementId, string SourceTarget, string DestinationTarget, string Rationale);
public sealed record SubmissionEvidenceMappingPlan (
	int SchemaVersion, SubmissionEvidenceIdentity SourceIdentity, SubmissionEvidenceIdentity DestinationIdentity,
	string SourceObservationsSha256, IReadOnlyList<SubmissionRequirementMapping> Requirements, string SourceFormat = "document",
	SubmissionEvidenceFile? SourceWorker = null);
public sealed record SubmissionEvidenceMappingReport (
	string MappingSha256, SubmissionEvidenceDocument? Observations, SubmissionEvidenceReport Source,
	SubmissionEvidenceReport? Destination, IReadOnlyList<string> UnmappedRequirementIds)
	{
	public bool MappingChecksPassed => Observations != null && Source.EvidenceChecksPassed && Destination?.EvidenceChecksPassed == true;
	}

/// <summary>Imports explicitly reviewed one-to-one evidence mappings without changing measured facts.
/// The caller must authenticate the source producer and approve behavioral equivalence before pinning the mapping.
/// This operation does not approve a policy, combine partial tests, authenticate a producer or authorize submission.</summary>
public static class SubmissionEvidenceMapping
	{
	/// <summary>All paths except evidenceDirectory are relative to that retained evidence tree.
	/// The expected mapping digest must come from the trusted review, not from producer output.
	/// Original documents remain unchanged and are referenced by every derived observation.</summary>
	public static SubmissionEvidenceMappingReport MapFiles (
		string evidenceDirectory, string mappingPath, string expectedMappingSha256, string sourcePolicyPath,
		string sourceObservationsPath, string destinationPolicyPath, DateTimeOffset now, CancellationToken cancellationToken = default)
		{
		var root = Path.GetFullPath (evidenceDirectory);
		var inputs = new List<FileStream> ();
		try
			{
			(byte[] Bytes, SubmissionEvidenceFile File) Read (string relative, string expected)
				{
				cancellationToken.ThrowIfCancellationRequested ();
				if (expected?.Length != 64 || !expected.All (char.IsAsciiHexDigit) ||
					!SubmissionEvidence.SafeEvidencePath (root, relative, out var path))
					throw new ArgumentException ("Mapping inputs require safe retained paths and independently reviewed SHA-256 digests.");
				var input = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
				inputs.Add (input);
				if (input.Length > 16 * 1024 * 1024)
					throw new ArgumentException ("Mapping JSON inputs must not exceed 16 MiB.");
				var bytes = new byte[checked((int)input.Length)];
				input.ReadExactly (bytes);
				var digest = Convert.ToHexStringLower (SHA256.HashData (bytes));
				if (!digest.Equals (expected, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException ("Mapping input differs from its reviewed digest.");
				return (bytes, new (relative, digest));
				}
			var mapping = Read (mappingPath, expectedMappingSha256);
			var plan = SubmissionValidation.Read<SubmissionEvidenceMappingPlan> (mapping.Bytes);
			if (plan.SchemaVersion != 1 || plan.SourceFormat is not ("document" or "endurance-export") ||
				(plan.SourceWorker != null && plan.SourceFormat != "endurance-export") || plan.Requirements.Count == 0 || plan.Requirements.Any (row => row == null ||
				string.IsNullOrWhiteSpace (row.SourceRequirementId) || string.IsNullOrWhiteSpace (row.DestinationRequirementId) ||
				string.IsNullOrWhiteSpace (row.SourceTarget) || string.IsNullOrWhiteSpace (row.DestinationTarget) || string.IsNullOrWhiteSpace (row.Rationale)) ||
				plan.Requirements.Select (row => row.SourceRequirementId).Distinct (StringComparer.Ordinal).Count () != plan.Requirements.Count ||
				plan.Requirements.Select (row => row.DestinationRequirementId).Distinct (StringComparer.Ordinal).Count () != plan.Requirements.Count)
				throw new ArgumentException ("Use explicit one-to-one requirement mappings with targets and reviewed behavioral rationales.");
			var sourceIdentity = plan.SourceIdentity;
			var destinationIdentity = plan.DestinationIdentity;
			if (!sourceIdentity.PackageSha256.Equals (destinationIdentity.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
				!sourceIdentity.SourceCommit.Equals (destinationIdentity.SourceCommit, StringComparison.OrdinalIgnoreCase) ||
				!sourceIdentity.TemplateSha256.Equals (destinationIdentity.TemplateSha256, StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException ("Mapping cannot transfer evidence to another package, source commit or official form revision.");
			var sourcePolicy = Read (sourcePolicyPath, sourceIdentity.PolicySha256);
			var sourceObservations = Read (sourceObservationsPath, plan.SourceObservationsSha256);
			var destinationPolicy = Read (destinationPolicyPath, destinationIdentity.PolicySha256);
			SubmissionEvidenceFile? workerFile = null;
			SubmissionEvidencePolicy source;
			if (plan.SourceWorker is { } workerReference)
				{
				// Some reviewed collector policies describe behavioral scope; the original worker binds executable rules.
				// Preserve both documents, rather than inventing a replacement policy with the old digest.
				if (SubmissionValidation.Read<JsonElement> (sourcePolicy.Bytes).ValueKind != JsonValueKind.Object)
					throw new ArgumentException ("The reviewed collection policy must be a JSON object.");
				var retainedWorker = Read (workerReference.RelativePath, workerReference.Sha256);
				var worker = SubmissionValidation.Read<SubmissionEnduranceWorkerPlan> (retainedWorker.Bytes, pascalCase: true);
				SubmissionEndurance.ValidatePlan (worker.Plan);
				var identity = worker.Plan.Identity;
				if (!identity.PackageSha256.Equals (sourceIdentity.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
					!identity.SourceCommit.Equals (sourceIdentity.SourceCommit, StringComparison.OrdinalIgnoreCase) ||
					!identity.PolicySha256.Equals (sourceIdentity.PolicySha256, StringComparison.OrdinalIgnoreCase) ||
					!identity.TemplateSha256.Equals (sourceIdentity.TemplateSha256, StringComparison.OrdinalIgnoreCase) ||
					SubmissionEnduranceProcessProbe.GetProducerId (worker.Probe) != worker.Plan.ProducerId)
					throw new ArgumentException ("The retained worker must bind the original evidence identity and exact producer inventory.");
				source = new (1, [worker.Plan.Requirement]);
				workerFile = retainedWorker.File;
				}
			else
				source = SubmissionValidation.Read<SubmissionEvidencePolicy> (sourcePolicy.Bytes);
			var document = plan.SourceFormat == "endurance-export"
				? new SubmissionEvidenceDocument (1, [SubmissionValidation.Read<SubmissionObservation> (sourceObservations.Bytes, pascalCase: true)])
				: SubmissionValidation.Read<SubmissionEvidenceDocument> (sourceObservations.Bytes);
			var destination = SubmissionValidation.Read<SubmissionEvidencePolicy> (destinationPolicy.Bytes);
			if (source.SchemaVersion != 1 || document.SchemaVersion != 1 || destination.SchemaVersion != 1)
				throw new ArgumentException ("Unsupported evidence document schema.");
			// Validate the entire source run: never choose a passing row over a failed or duplicate observation.
			var sourceReport = SubmissionEvidence.Evaluate (sourceIdentity, source.Requirements, document.Observations, root, now, cancellationToken);
			// Validate the full destination policy even when only a subset is imported.
			SubmissionEvidence.Evaluate (destinationIdentity, destination.Requirements, [], root, now, cancellationToken);
			var unmapped = destination.Requirements.Select (row => row.Id).Except (plan.Requirements.Select (row => row.DestinationRequirementId), StringComparer.Ordinal).ToArray ();
			if (!sourceReport.EvidenceChecksPassed)
				return new (mapping.File.Sha256, null, sourceReport, null, unmapped);
			var observations = new List<SubmissionObservation> ();
			var selected = new List<SubmissionRequirement> ();
			foreach (var row in plan.Requirements)
				{
				cancellationToken.ThrowIfCancellationRequested ();
				var from = source.Requirements.SingleOrDefault (item => item.Id == row.SourceRequirementId);
				var to = destination.Requirements.SingleOrDefault (item => item.Id == row.DestinationRequirementId);
				if (from?.Execution == null || to?.Execution == null || from.Execution.Target != row.SourceTarget ||
					to.Execution.Target != row.DestinationTarget || from.Execution.Method != to.Execution.Method)
					throw new ArgumentException ("Mapping requires existing scoped requirements, exact reviewed targets and the same observation method.");
				var original = document.Observations.Single (item => item.RequirementId == row.SourceRequirementId);
				var provenance = new List<SubmissionEvidenceFile> { mapping.File, sourcePolicy.File, sourceObservations.File, destinationPolicy.File };
				if (workerFile != null)
					provenance.Add (workerFile);
				var files = original.Files.Concat (provenance).ToArray ();
				if (files.GroupBy (file => file.RelativePath, StringComparer.OrdinalIgnoreCase).Any (group =>
					group.Select (file => file.Sha256.ToLowerInvariant ()).Distinct ().Count () != 1))
					throw new ArgumentException ("Evidence paths have conflicting digests.");
				observations.Add (original with
					{
					RequirementId = to.Id, Identity = destinationIdentity,
					Execution = original.Execution! with { Target = to.Execution.Target },
					Files = files.DistinctBy (file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray (),
					Rationale = original.Rationale + "\nReviewed mapping: " + row.Rationale
					});
				selected.Add (to);
				}
			var destinationReport = SubmissionEvidence.Evaluate (destinationIdentity, selected, observations, root, now, cancellationToken);
			return new (mapping.File.Sha256, destinationReport.EvidenceChecksPassed ? new (1, observations.AsReadOnly ()) : null,
				sourceReport, destinationReport, unmapped);
			}
		finally
			{
			foreach (var input in inputs)
				input.Dispose ();
			}
		}
	}