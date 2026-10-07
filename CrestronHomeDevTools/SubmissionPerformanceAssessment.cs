// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools;

/// <summary>A phase-two assessment of observed behavior, not a numerical timing tolerance.</summary>
public sealed record SubmissionPerformanceFinding(string Pair, string Control,
    bool? FunctionalityPreserved, bool? NoPerformanceDegradation, bool? FunctionsImmediate,
    bool? FeedbackPromptAndAccurate, string Rationale,
    SubmissionEvidenceFile[] BeforeEvidence, SubmissionEvidenceFile[] AfterEvidence);
public sealed record SubmissionPerformanceAssessment(int SchemaVersion, SubmissionEvidenceIdentity Identity,
    string RequirementId, DateTimeOffset EnduranceStartedUtc, DateTimeOffset EnduranceFinishedUtc,
    string Reviewer, DateTimeOffset ReviewedUtc, SubmissionPerformanceFinding[] Findings);

public static class SubmissionPerformanceReview
{
    /// <summary>Validates coverage and returns the explicit assessment. Callers verify all referenced evidence bytes.</summary>
    public static SubmissionEvidenceOutcome Assess(SubmissionPerformanceAssessment assessment,
        SubmissionEvidenceIdentity identity, string requirementId, DateTimeOffset start, DateTimeOffset finish,
        IReadOnlyDictionary<string, SubmissionResponseComparisonReport> comparisons)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (assessment.SchemaVersion != 1 || assessment.Identity != identity || assessment.RequirementId != requirementId ||
            start == default || finish <= start || assessment.EnduranceStartedUtc != start || assessment.EnduranceFinishedUtc != finish ||
            string.IsNullOrWhiteSpace(assessment.Reviewer) || assessment.ReviewedUtc < finish ||
            assessment.Findings is not { Length: > 0 } || comparisons.Count == 0)
            throw new InvalidDataException("Performance assessment must identify the candidate, requirement, completed interval and reviewer.");
        var expected = comparisons.SelectMany(p => p.Value.Differences.Select(d => (Pair:p.Key, Control:d.Id))).ToHashSet();
        var actual = assessment.Findings.Select(f => f == null ? default : (f.Pair, f.Control)).ToArray();
        if (actual.Length != expected.Count || actual.Distinct().Count() != actual.Length || !expected.SetEquals(actual))
            throw new InvalidDataException("Performance assessment must cover every measured device/control exactly once.");
        foreach (var finding in assessment.Findings)
        {
            if (string.IsNullOrWhiteSpace(finding.Rationale) || !Evidence(finding.BeforeEvidence) || !Evidence(finding.AfterEvidence))
                throw new InvalidDataException("Each performance finding needs a reason and original before/after supporting evidence.");
        }
        var decisions = assessment.Findings.SelectMany(f => new[] { f.FunctionalityPreserved, f.NoPerformanceDegradation,
            f.FunctionsImmediate, f.FeedbackPromptAndAccurate }).ToArray();
        return decisions.Any(v => v == false) ? SubmissionEvidenceOutcome.Failed :
            decisions.Any(v => v == null) ? SubmissionEvidenceOutcome.Inconclusive : SubmissionEvidenceOutcome.Passed;
    }
    private static bool Evidence(SubmissionEvidenceFile[] files) => files is { Length: > 0 } &&
        files.All(f => f != null && !string.IsNullOrWhiteSpace(f.RelativePath) && f.Sha256 is { Length: 64 } && f.Sha256.All(Uri.IsHexDigit)) &&
        files.Select(f => f.RelativePath).Distinct(StringComparer.Ordinal).Count() == files.Length;
}
