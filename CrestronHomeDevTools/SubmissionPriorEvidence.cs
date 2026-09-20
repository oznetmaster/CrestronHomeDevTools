// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;

namespace CrestronHomeDevTools;

/// <summary>Explicit permission in the independently pinned target policy to review prior passing evidence.
/// All paths are relative to the retained target evidence directory. No evidence is implicitly transferable.</summary>
public sealed record SubmissionPriorEvidenceRequirements (SubmissionEvidenceIdentity Identity,
	SubmissionEvidenceFile Policy, SubmissionEvidenceFile Observations, string EvidenceDirectory,
	SubmissionEvidenceFile ChangeReview);
public sealed record SubmissionChangeImpactDecision (string RequirementId, string Rationale,
	IReadOnlyList<SubmissionEvidenceFile> Evidence);
/// <summary>A review decision, not a test execution or automatically proven behavioral equivalence.
/// The trusted coordinator must approve the reviewer, scope, dependency analysis and supporting evidence.</summary>
public sealed record SubmissionChangeImpactReview (int SchemaVersion, SubmissionEvidenceIdentity SourceIdentity,
	string TargetPackageSha256, string TargetSourceCommit, DateTimeOffset ReviewedUtc, string Reviewer,
	IReadOnlyList<SubmissionChangeImpactDecision> Decisions);

/// <summary>Supports explicit, scoped change-impact reviews without relabelling old tests as new executions.
/// Original requirements, identities, timestamps, measurements and failures remain in retained source files.</summary>
public static class SubmissionPriorEvidence
	{
	/// <summary>File entry point with an independently approved candidate digest. Package and full-policy
	/// validation still belong to the normal review stage; this operation imports only the reviewed subset.</summary>
	public static SubmissionEvidenceDocument ImportFiles (string candidatePath, string expectedCandidateSha256,
		string policyPath, string evidenceDirectory, DateTimeOffset now, CancellationToken cancellationToken = default)
		{
		byte[] Read (string path, string digest)
			{
			if (digest?.Length != 64 || !digest.All (char.IsAsciiHexDigit)) throw new ArgumentException ("A trusted SHA-256 is required.");
			using var input = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (input.Length > 16 * 1024 * 1024) throw new ArgumentException ("Review JSON exceeds 16 MiB.");
			var bytes = new byte[checked((int)input.Length)];
			input.ReadExactly (bytes);
			if (!Same (Convert.ToHexStringLower (SHA256.HashData (bytes)), digest))
				throw new InvalidDataException ("Review input differs from its independently approved digest.");
			return bytes;
			}
		var candidate = SubmissionValidation.Read<SubmissionCandidate> (Read (candidatePath, expectedCandidateSha256));
		if (candidate.SchemaVersion != 1) throw new ArgumentException ("Unsupported candidate schema.");
		var policy = SubmissionValidation.Read<SubmissionEvidencePolicy> (Read (policyPath, candidate.Identity.PolicySha256));
		return Import (candidate.Identity, policy, evidenceDirectory, now, cancellationToken);
		}

	/// <summary>Prepare only scopes which explicitly permit prior evidence in the pinned target policy.
	/// Validate and compose the resulting document with fresh observations before form preparation.
	/// This method neither approves the change review nor establishes full submission completeness.</summary>
	public static SubmissionEvidenceDocument Import (SubmissionEvidenceIdentity target, SubmissionEvidencePolicy policy,
		string evidenceDirectory, DateTimeOffset now, CancellationToken cancellationToken = default)
		{
		if (policy.SchemaVersion != 1) throw new ArgumentException ("Unsupported evidence policy schema.");
		var root = Path.GetFullPath (evidenceDirectory);
		var context = new Context (target, [], root, now, cancellationToken);
		var observations = new List<SubmissionObservation> ();
		var retained = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		foreach (var requirement in policy.Requirements.Where (item => item.PriorEvidence != null))
			{
			var source = context.Load (requirement.PriorEvidence!);
			var decision = source.Review.Decisions.SingleOrDefault (item => item.RequirementId == requirement.Id)
				?? throw new ArgumentException ("No change-impact decision covers this requirement.");
			var original = source.Observations.Observations.Single (item => item.RequirementId == requirement.Id);
			// Shared original files need to appear once in the composed document. The validator
			// checks that every original reference remains retained, including unrelated failures.
			var files = source.RequiredFiles.Concat (source.Review.Decisions.SelectMany (item => item.Evidence))
				.Where (file => retained.Add (file.RelativePath)).ToList ();
			foreach (var file in source.Provenance.Concat (decision.Evidence))
				if (!files.Any (item => item.RelativePath == file.RelativePath)) files.Add (file);
			var rationale = $"Reviewed earlier passing evidence from package {original.Identity.PackageSha256}, " +
				$"source {original.Identity.SourceCommit}. Original execution dates and measurements are retained in the source observations. " +
				$"Change-impact review by {source.Review.Reviewer}: {decision.Rationale} This is not a new execution on the target package.";
			observations.Add (new (requirement.Id, target, SubmissionEvidenceOutcome.ReviewedPriorPass,
				source.Review.ReviewedUtc, source.Review.ReviewedUtc, files.AsReadOnly (), rationale));
			}
		if (observations.Count == 0) throw new ArgumentException ("The target policy has no explicitly reviewed prior-evidence scopes.");
		var selected = policy.Requirements.Where (item => item.PriorEvidence != null).ToArray ();
		var validation = SubmissionEvidence.Evaluate (target, selected, observations, root, now, cancellationToken);
		if (!validation.EvidenceChecksPassed)
			throw new InvalidDataException ("Prior evidence did not satisfy the reviewed scoped requirements: " +
				string.Join (", ", validation.Issues.Select (item => item.Code).Distinct ()));
		return new (1, observations.AsReadOnly ());
		}

	internal sealed class Context (SubmissionEvidenceIdentity target, IReadOnlyList<SubmissionObservation> observations,
		string root, DateTimeOffset now, CancellationToken cancellationToken)
		{
		private readonly Dictionary<SubmissionPriorEvidenceRequirements, Source> _sources = [];
		private readonly HashSet<SubmissionEvidenceFile> _retained = observations.SelectMany (item => item.Files ?? [])
			.Where (file => file?.Sha256 != null).Select (file => new SubmissionEvidenceFile (file.RelativePath, file.Sha256.ToLowerInvariant ())).ToHashSet ();

		internal void Evaluate (SubmissionRequirement requirement, SubmissionObservation observation, List<SubmissionEvidenceIssue> issues)
			{
			void Issue (string code, string message) => issues.Add (new (requirement.Id, code, message));
			if (requirement.PriorEvidence == null)
				{
				Issue ("prior-review-not-authorized", "The pinned policy does not permit prior evidence for this scope.");
				return;
				}
			try
				{
				var source = Load (requirement.PriorEvidence);
				var oldRule = source.Policy.Requirements.SingleOrDefault (item => item.Id == requirement.Id);
				if (oldRule == null || oldRule != (requirement with { PriorEvidence = null }))
					Issue ("prior-scope-changed", "Prior and target assertion, target, method, duration and measurement requirements must be identical.");
				var original = source.Observations.Observations.SingleOrDefault (item => item.RequirementId == requirement.Id);
				if (original?.Outcome != SubmissionEvidenceOutcome.Passed ||
					source.Validation.Issues.Any (item => item.RequirementId == requirement.Id))
					Issue ("prior-not-verified", "Only an originally passing assertion with fully validated measurements can be reviewed for reuse.");
				var decision = source.Review.Decisions.SingleOrDefault (item => item.RequirementId == requirement.Id);
				if (decision == null)
					Issue ("prior-decision-missing", "The independently pinned change review does not cover this requirement.");
				if (observation.Execution != null || observation.StartedUtc != source.Review.ReviewedUtc ||
					observation.FinishedUtc != source.Review.ReviewedUtc || string.IsNullOrWhiteSpace (observation.Rationale) ||
					original?.FinishedUtc > source.Review.ReviewedUtc)
					Issue ("prior-review-time", "Record the dated review separately from original physical execution measurements.");
				if (source.RequiredFiles.Concat (source.Review.Decisions.SelectMany (item => item.Evidence)).Any (file =>
					!_retained.Contains (new (file.RelativePath, file.Sha256.ToLowerInvariant ()))))
					Issue ("prior-provenance-missing", "Retain all original evidence and review references in the composed observation document.");
				}
			catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
				{
				Issue ("prior-review-invalid", "Prior evidence or its pinned change-impact review could not be validated.");
				}
			}

		internal Source Load (SubmissionPriorEvidenceRequirements prior)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (_sources.TryGetValue (prior, out var cached)) return cached;
			byte[] Read (SubmissionEvidenceFile reference)
				{
				if (reference == null || reference.Sha256?.Length != 64 || !reference.Sha256.All (char.IsAsciiHexDigit) ||
					!SubmissionEvidence.SafeEvidencePath (root, reference.RelativePath, out var path))
					throw new ArgumentException ("Prior-review files require safe retained paths and independently reviewed digests.");
				using var input = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
				if (input.Length > 16 * 1024 * 1024) throw new ArgumentException ("Prior-review JSON exceeds 16 MiB.");
				var bytes = new byte[checked((int)input.Length)];
				input.ReadExactly (bytes);
				if (!Same (Convert.ToHexStringLower (SHA256.HashData (bytes)), reference.Sha256))
					throw new InvalidDataException ("Prior-review file changed since independent review.");
				return bytes;
				}
			if (prior.Identity == null || !Same (prior.Identity.TemplateSha256, target.TemplateSha256) ||
				!Same (prior.Policy.Sha256, prior.Identity.PolicySha256) ||
				!SubmissionEvidence.SafeEvidencePath (root, prior.EvidenceDirectory, out var sourceRoot) || !Directory.Exists (sourceRoot))
				throw new ArgumentException ("Prior evidence must retain its original policy, same official form and contained evidence directory.");
			var policy = SubmissionValidation.Read<SubmissionEvidencePolicy> (Read (prior.Policy));
			var document = SubmissionValidation.Read<SubmissionEvidenceDocument> (Read (prior.Observations));
			var review = SubmissionValidation.Read<SubmissionChangeImpactReview> (Read (prior.ChangeReview));
			if (policy.SchemaVersion != 1 || document.SchemaVersion != 1 || review.SchemaVersion != 1 ||
				policy.Requirements.Any (item => item.PriorEvidence != null) ||
				document.Observations.Any (item => item.Outcome == SubmissionEvidenceOutcome.ReviewedPriorPass) ||
				review.SourceIdentity != prior.Identity || !Same (review.TargetPackageSha256, target.PackageSha256) ||
				!Same (review.TargetSourceCommit, target.SourceCommit) || string.IsNullOrWhiteSpace (review.Reviewer) ||
				review.ReviewedUtc == default || review.ReviewedUtc > now || review.Decisions.Count == 0 ||
				review.Decisions.Any (item => item == null || string.IsNullOrWhiteSpace (item.RequirementId) ||
					string.IsNullOrWhiteSpace (item.Rationale) || item.Evidence.Count == 0) ||
				review.Decisions.Select (item => item.RequirementId).Distinct (StringComparer.Ordinal).Count () != review.Decisions.Count)
				throw new ArgumentException ("Supply a dated, scoped and evidenced change-impact review of original executions. Nested carry-forward is not supported.");
			var validation = SubmissionEvidence.Evaluate (prior.Identity, policy.Requirements, document.Observations, sourceRoot, now, cancellationToken);
			// Other failed/partial requirements stay in the original document. Corruption or
			// ambiguous evidence anywhere in the retained source cannot be waived by selection.
			string[] structural = ["duplicate-observation", "unknown-requirement", "identity-mismatch", "invalid-time", "invalid-evidence-file", "unavailable-evidence", "evidence-digest"];
			if (validation.Issues.Any (item => structural.Contains (item.Code, StringComparer.Ordinal)) ||
				document.Observations.Any (item => !Enum.IsDefined (item.Outcome)))
				throw new InvalidDataException ("The original evidence document contains invalid or ambiguous evidence.");
			SubmissionEvidenceFile[] provenance = [prior.Policy, prior.Observations, prior.ChangeReview];
			var prefix = prior.EvidenceDirectory.Replace ('\\', '/').TrimEnd ('/') + "/";
			var files = document.Observations.SelectMany (item => item.Files ?? []).Select (file =>
				new SubmissionEvidenceFile (prefix + file.RelativePath.Replace ('\\', '/'), file.Sha256)).Concat (provenance).ToArray ();
			if (files.GroupBy (file => file.RelativePath, StringComparer.OrdinalIgnoreCase).Any (group =>
				group.Select (file => file.Sha256.ToLowerInvariant ()).Distinct ().Count () != 1))
				throw new InvalidDataException ("Conflicting retained source evidence digests.");
			var source = new Source (policy, document, review, validation, provenance,
				files.DistinctBy (file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray ());
			_sources.Add (prior, source);
			return source;
			}
		}

	internal sealed record Source (SubmissionEvidencePolicy Policy, SubmissionEvidenceDocument Observations,
		SubmissionChangeImpactReview Review, SubmissionEvidenceReport Validation,
		IReadOnlyList<SubmissionEvidenceFile> Provenance, IReadOnlyList<SubmissionEvidenceFile> RequiredFiles);
	private static bool Same (string left, string right) => string.Equals (left, right, StringComparison.OrdinalIgnoreCase);
	}