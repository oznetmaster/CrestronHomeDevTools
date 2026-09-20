// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;

namespace CrestronHomeDevTools;

public enum SubmissionEvidenceOutcome
	{
	NotTested, Passed, Failed, Partial, Inconclusive, NotApplicable, ReviewedPriorPass
	}

public sealed record SubmissionEvidenceIdentity (string PackageSha256, string SourceCommit, string PolicySha256, string TemplateSha256);
public sealed record SubmissionRequirement (string Id, TimeSpan MinimumDuration, bool AllowNotApplicable = false,
	SubmissionExecutionRequirements? Execution = null)
	{
	public SubmissionPriorEvidenceRequirements? PriorEvidence { get; init; }
	}
public sealed record SubmissionEvidenceFile (string RelativePath, string Sha256);
public sealed record SubmissionObservation (
	string RequirementId, SubmissionEvidenceIdentity Identity, SubmissionEvidenceOutcome Outcome,
	DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc, IReadOnlyList<SubmissionEvidenceFile> Files, string Rationale = "",
	SubmissionExecutionObservation? Execution = null);
public sealed record SubmissionEvidenceIssue (string RequirementId, string Code, string Message);
public sealed record SubmissionEvidenceReport (IReadOnlyList<SubmissionEvidenceIssue> Issues)
	{
	public bool EvidenceChecksPassed => Issues.Count == 0;
	}

/// <summary>Checks evidence completeness and binding. Callers must separately authenticate its producer and approved policy.</summary>
public static class SubmissionEvidence
	{
	public static SubmissionEvidenceReport Evaluate (
		SubmissionEvidenceIdentity candidate, IReadOnlyList<SubmissionRequirement> requirements,
		IReadOnlyList<SubmissionObservation> observations, string evidenceDirectory, DateTimeOffset now,
		CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (candidate);
		ArgumentNullException.ThrowIfNull (requirements);
		ArgumentNullException.ThrowIfNull (observations);
		if (!ValidIdentity (candidate))
			throw new ArgumentException ("Candidate identity requires package, policy and template SHA-256 digests and a full source commit.", nameof (candidate));
		if (requirements.Count == 0 || requirements.Any (item => item == null || string.IsNullOrWhiteSpace (item.Id) || item.MinimumDuration < TimeSpan.Zero) ||
			requirements.Select (item => item.Id).Distinct (StringComparer.Ordinal).Count () != requirements.Count)
			throw new ArgumentException ("Provide a nonempty policy with unique requirement IDs and nonnegative durations.", nameof (requirements));
		var root = Path.TrimEndingDirectorySeparator (Path.GetFullPath (evidenceDirectory));
		if (!Directory.Exists (root))
			throw new DirectoryNotFoundException ("The evidence directory does not exist.");
		var issues = new List<SubmissionEvidenceIssue> ();
		void Issue (string id, string code, string message) => issues.Add (new (id, code, message));
		if (observations.Any (item => item == null))
			throw new ArgumentException ("Evidence observations must not be null.", nameof (observations));
		var prior = new SubmissionPriorEvidence.Context (candidate, observations, root, now, cancellationToken);
		foreach (var group in observations.GroupBy (item => item.RequirementId, StringComparer.Ordinal))
			{
			if (group.Count () > 1)
				Issue (group.Key, "duplicate-observation", "More than one observation claims this requirement; reconcile the run instead of choosing a passing result.");
			if (!requirements.Any (item => item.Id == group.Key))
				Issue (group.Key, "unknown-requirement", "The observation is not part of the approved requirement policy.");
			}
		foreach (var requirement in requirements)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			SubmissionExecution.ValidatePolicy (requirement);
			var matches = observations.Where (item => item.RequirementId == requirement.Id).ToArray ();
			if (matches.Length == 0)
				{
				Issue (requirement.Id, "missing-observation", "No evidence was supplied for this requirement.");
				continue;
				}
			foreach (var observation in matches)
				{
				if (observation.Outcome == SubmissionEvidenceOutcome.ReviewedPriorPass)
					prior.Evaluate (requirement, observation, issues);
				else
					SubmissionExecution.Evaluate (requirement, observation, issues);
				if (!ValidIdentity (observation.Identity) || !SameIdentity (candidate, observation.Identity))
					Issue (requirement.Id, "identity-mismatch", "Evidence belongs to another package, source commit, policy or official form revision.");
				if (observation.StartedUtc == default || observation.FinishedUtc < observation.StartedUtc || observation.FinishedUtc > now)
					Issue (requirement.Id, "invalid-time", "Evidence timestamps are missing, reversed or in the future.");
				if (observation.Outcome == SubmissionEvidenceOutcome.NotApplicable)
					{
					if (!requirement.AllowNotApplicable || string.IsNullOrWhiteSpace (observation.Rationale))
						Issue (requirement.Id, "invalid-not-applicable", "Non-applicability requires policy permission and an explicit rationale.");
					}
				else if (observation.Outcome is not (SubmissionEvidenceOutcome.Passed or SubmissionEvidenceOutcome.ReviewedPriorPass))
					Issue (requirement.Id, "not-passed", "The requirement is untested, partial, failed or inconclusive.");
				else
					{
					if (observation.Outcome == SubmissionEvidenceOutcome.Passed && observation.FinishedUtc - observation.StartedUtc < requirement.MinimumDuration)
						Issue (requirement.Id, "insufficient-duration", "The observed duration is shorter than this requirement's minimum.");
					if (observation.Files == null || observation.Files.Count == 0)
						Issue (requirement.Id, "missing-evidence-file", "A passing observation must reference retained evidence files.");
					}
				foreach (var file in observation.Files ?? [])
					{
					cancellationToken.ThrowIfCancellationRequested ();
					if (file == null || !IsHex (file.Sha256, 64) || !SafeEvidencePath (root, file.RelativePath, out var path))
						{
						Issue (requirement.Id, "invalid-evidence-file", "Evidence references need a SHA-256 digest and a safe relative path within the evidence directory.");
						continue;
						}
					try
						{
						using var input = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
						if (!Convert.ToHexString (SHA256.HashData (input)).Equals (file.Sha256, StringComparison.OrdinalIgnoreCase))
							Issue (requirement.Id, "evidence-digest", "An evidence file changed after its observation was recorded.");
						}
					catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
						{
						Issue (requirement.Id, "unavailable-evidence", "A referenced evidence file cannot be read.");
						}
					}
				}
			}
		return new (issues.AsReadOnly ());
		}

	private static bool IsHex (string? value, int length) => value?.Length == length && value.All (char.IsAsciiHexDigit);
	private static bool ValidIdentity (SubmissionEvidenceIdentity? identity) => identity != null && IsHex (identity.PackageSha256, 64) &&
		(IsHex (identity.SourceCommit, 40) || IsHex (identity.SourceCommit, 64)) && IsHex (identity.PolicySha256, 64) && IsHex (identity.TemplateSha256, 64);
	private static bool SameIdentity (SubmissionEvidenceIdentity left, SubmissionEvidenceIdentity right) =>
		left.PackageSha256.Equals (right.PackageSha256, StringComparison.OrdinalIgnoreCase) &&
		left.SourceCommit.Equals (right.SourceCommit, StringComparison.OrdinalIgnoreCase) &&
		left.PolicySha256.Equals (right.PolicySha256, StringComparison.OrdinalIgnoreCase) &&
		left.TemplateSha256.Equals (right.TemplateSha256, StringComparison.OrdinalIgnoreCase);

	internal static bool SafeEvidencePath (string root, string? relative, out string path)
		{
		path = string.Empty;
		if (string.IsNullOrWhiteSpace (relative) || Path.IsPathRooted (relative) || relative.Contains (':') ||
			relative.Replace ('\\', '/').Split ('/').Any (part => part is "" or "." or ".." || part.EndsWith ('.') || part.EndsWith (' ')))
			return false;
		try
			{
			root = Path.TrimEndingDirectorySeparator (Path.GetFullPath (root));
			path = Path.GetFullPath (Path.Combine (root, relative.Replace ('/', Path.DirectorySeparatorChar).Replace ('\\', Path.DirectorySeparatorChar)));
			var prefix = Path.TrimEndingDirectorySeparator (root) + Path.DirectorySeparatorChar;
			if (!path.StartsWith (prefix, OperatingSystem.IsWindows () ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
				return false;
			// Reject links/junctions rather than reading through them outside the evidence tree.
			var current = path;
			while (true)
				{
				if ((File.GetAttributes (current) & FileAttributes.ReparsePoint) != 0)
					return false;
				if (string.Equals (current, root, OperatingSystem.IsWindows () ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
					break;
				current = Path.GetDirectoryName (current)!;
				if (current == null)
					return false;
				}
			return true;
			}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
			{
			return false;
			}
		}
	}