// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeDevTools;

public enum SubmissionReviewMode { Complete, DeclaredGaps }
public enum SubmissionVerificationStatus { CompleteAgainstInterpretedRequirements, GapsDeclared, NeedsCorrection }
public enum SubmissionRequirementReviewStatus { VerifiedAgainstPlan, GapDeclared, GapUndeclared, InvalidEvidence, VerifiedPriorEvidence }
public sealed record SubmissionGapDeclaration (string RequirementId, string Reason);
public sealed record SubmissionRequirementReview (string RequirementId, SubmissionRequirementReviewStatus Status,
	SubmissionEvidenceOutcome? ObservedOutcome, string? DeclaredReason, IReadOnlyList<SubmissionEvidenceIssue> Issues);
public sealed record SubmissionReviewAssessmentReport (SubmissionReviewMode Mode, SubmissionVerificationStatus VerificationStatus,
	SubmissionEvidenceReport Evidence, IReadOnlyList<SubmissionRequirementReview> Requirements,
	IReadOnlyList<SubmissionEvidenceIssue> BlockingIssues)
	{
	/// <summary>Internal review eligibility only. Never authorizes signing, sending or a claim of Crestron acceptance.</summary>
	public bool ReadyForReview => VerificationStatus != SubmissionVerificationStatus.NeedsCorrection;
	}

/// <summary>Assesses results against the complete, independently reviewed interpretation of Crestron's requirements.
/// It never changes evidence outcomes or decides whether Crestron will accept a submission.</summary>
public static class SubmissionReviewAssessment
	{
	// Only known verification gaps may be acknowledged. New/unknown validation failures remain blocked.
	private static readonly HashSet<string> DeclarableGaps = new (StringComparer.Ordinal)
		{
		"missing-observation", "not-passed", "insufficient-duration", "missing-evidence-file",
		"execution-outcome", "execution-files", "execution-missing", "response-missing", "response-deadline", "response-evidence",
		"restoration-missing", "restoration-unconfirmed", "restoration-evidence", "sampling-policy",
		"samples-missing", "sample-gap", "sample-result", "sample-coverage"
		};

	/// <summary>Review the full policy and preserve the original validator result, including every failure.
	/// Declarations require exact scoped IDs and nonempty reasons. A reduced checklist does not establish completeness.
	/// Policy completeness, producer trust, document omissions and exact signing/delivery authorization remain separate.</summary>
	public static SubmissionReviewAssessmentReport Assess (SubmissionEvidenceIdentity identity,
		IReadOnlyList<SubmissionRequirement> requirements, IReadOnlyList<SubmissionObservation> observations,
		string evidenceDirectory, SubmissionReviewMode mode, IReadOnlyList<SubmissionGapDeclaration> declarations,
		DateTimeOffset now, CancellationToken cancellationToken = default)
		{
		if (!Enum.IsDefined (mode)) throw new ArgumentOutOfRangeException (nameof (mode));
		ArgumentNullException.ThrowIfNull (declarations);
		if (declarations.Any (item => item == null || string.IsNullOrWhiteSpace (item.RequirementId) || string.IsNullOrWhiteSpace (item.Reason)) ||
			declarations.Select (item => item.RequirementId).Distinct (StringComparer.Ordinal).Count () != declarations.Count)
			throw new ArgumentException ("Gap declarations require unique scoped requirement IDs and explicit reasons.", nameof (declarations));
		if (mode == SubmissionReviewMode.Complete && declarations.Count != 0)
			throw new ArgumentException ("Choose declared-gaps review explicitly before supplying gap declarations.", nameof (mode));
		var evidence = SubmissionEvidence.Evaluate (identity, requirements, observations, evidenceDirectory, now, cancellationToken);
		// The underlying evaluator treats every nonpassing value alike. Unknown enum values are malformed data,
		// not a genuine failed/partial/untested outcome that a developer can explain.
		var invalidOutcomes = observations.Where (item => !Enum.IsDefined (item.Outcome))
			.Select (item => new SubmissionEvidenceIssue (item.RequirementId, "invalid-outcome", "The recorded evidence outcome is not recognized.")).ToArray ();
		if (invalidOutcomes.Length != 0)
			evidence = new (evidence.Issues.Concat (invalidOutcomes).ToArray ());
		if (declarations.Any (item => !requirements.Any (requirement => requirement.Id == item.RequirementId)))
			throw new ArgumentException ("Every declaration must belong to the full reviewed requirement inventory.", nameof (declarations));
		var blocking = evidence.Issues.Where (issue => !DeclarableGaps.Contains (issue.Code)).ToList ();
		var rows = new List<SubmissionRequirementReview> ();
		foreach (var requirement in requirements)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			var issues = evidence.Issues.Where (issue => issue.RequirementId == requirement.Id).ToArray ();
			var declaration = declarations.SingleOrDefault (item => item.RequirementId == requirement.Id);
			var actual = observations.Where (item => item.RequirementId == requirement.Id).ToArray ();
			SubmissionEvidenceOutcome? outcome = actual.Length == 1 ? actual[0].Outcome : null;
			SubmissionRequirementReviewStatus status;
			if (issues.Length == 0)
				{
				if (declaration != null)
					throw new ArgumentException ("A declared gap no longer matches the evidence. Reconcile the declaration rather than silently dropping it.", nameof (declarations));
				status = outcome == SubmissionEvidenceOutcome.ReviewedPriorPass
					? SubmissionRequirementReviewStatus.VerifiedPriorEvidence : SubmissionRequirementReviewStatus.VerifiedAgainstPlan;
				}
			else if (issues.Any (issue => !DeclarableGaps.Contains (issue.Code)))
				status = SubmissionRequirementReviewStatus.InvalidEvidence;
			else if (mode == SubmissionReviewMode.DeclaredGaps && declaration != null)
				status = SubmissionRequirementReviewStatus.GapDeclared;
			else
				{
				status = SubmissionRequirementReviewStatus.GapUndeclared;
				blocking.Add (new (requirement.Id, "undeclared-gap", "This unmet requirement needs an explicit reason in declared-gaps mode, or completed verification."));
				}
			rows.Add (new (requirement.Id, status, outcome, declaration?.Reason, issues));
			}
		var decision = blocking.Count != 0 ? SubmissionVerificationStatus.NeedsCorrection
			: evidence.EvidenceChecksPassed ? SubmissionVerificationStatus.CompleteAgainstInterpretedRequirements
			: SubmissionVerificationStatus.GapsDeclared;
		return new (mode, decision, evidence, rows.AsReadOnly (), blocking.AsReadOnly ());
		}
	}