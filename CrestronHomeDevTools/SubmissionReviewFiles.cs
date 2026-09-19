// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;

namespace CrestronHomeDevTools;

public sealed record SubmissionGapDeclarations (int SchemaVersion, SubmissionEvidenceIdentity Identity,
	SubmissionReviewMode Mode, IReadOnlyList<SubmissionGapDeclaration> Declarations);
public sealed record SubmissionReviewFileReport (string DeclarationsSha256, SubmissionValidationReport Validation,
	SubmissionReviewAssessmentReport? Assessment)
	{
	/// <summary>Eligibility for further internal review only; not an approval to sign, submit or claim vendor acceptance.</summary>
	public bool ReadyForReview => Validation.Issues.Count == 0 && Validation.Package?.PackageChecksPassed == true && Assessment?.ReadyForReview == true;
	}

/// <summary>Candidate-bound declared-gap assessment. An independently reviewed full policy is always required.</summary>
public static class SubmissionReviewFiles
	{
	/// <summary>The candidate and declarations digests must come from the trusted review coordinator.
	/// A successful assessment retains the original validation failure for declared gaps. It does not relax
	/// package integrity, form rendering, producer authentication, reservations, signing or delivery checks.</summary>
	public static SubmissionReviewFileReport Check (string candidatePath, string expectedCandidateSha256,
		string packagePath, string policyPath, string templatePath, string observationsPath, string evidenceDirectory,
		string declarationsPath, string expectedDeclarationsSha256, SubmissionReviewMode mode,
		DateTimeOffset now, CancellationToken cancellationToken = default)
		{
		if (!Enum.IsDefined (mode)) throw new ArgumentOutOfRangeException (nameof (mode));
		if (expectedDeclarationsSha256?.Length != 64 || !expectedDeclarationsSha256.All (char.IsAsciiHexDigit))
			throw new ArgumentException ("Provide the independently reviewed gap declarations SHA-256.", nameof (expectedDeclarationsSha256));
		var inputs = new List<FileStream> ();
		try
			{
			FileStream Hold (string path)
				{
				cancellationToken.ThrowIfCancellationRequested ();
				var stream = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
				inputs.Add (stream);
				return stream;
				}
			byte[] Read (string path)
				{
				var input = Hold (path);
				if (input.Length > 16 * 1024 * 1024) throw new ArgumentException ("Review JSON inputs must not exceed 16 MiB.");
				var bytes = new byte[checked((int)input.Length)];
				input.ReadExactly (bytes);
				return bytes;
				}
			string Hash (byte[] bytes) => Convert.ToHexStringLower (SHA256.HashData (bytes));
			bool Equal (string left, string right) => left.Equals (right, StringComparison.OrdinalIgnoreCase);
			var declarationsBytes = Read (declarationsPath);
			var declarationsHash = Hash (declarationsBytes);
			if (!Equal (declarationsHash, expectedDeclarationsSha256))
				throw new InvalidDataException ("Gap declarations changed since independent review.");
			var declarations = SubmissionValidation.Read<SubmissionGapDeclarations> (declarationsBytes);
			if (declarations.SchemaVersion != 1 || declarations.Mode != mode)
				throw new ArgumentException ("Gap declarations must use schema 1 and the explicitly selected review mode.");
			var candidateBytes = Read (candidatePath);
			var policyBytes = Read (policyPath);
			var observationBytes = Read (observationsPath);
			Hold (packagePath);
			Hold (templatePath);
			var validation = SubmissionValidation.CheckFiles (candidatePath, expectedCandidateSha256, packagePath, policyPath,
				templatePath, observationsPath, evidenceDirectory, now, cancellationToken);
			if (validation.Issues.Count != 0 || validation.Package?.PackageChecksPassed != true)
				return new (declarationsHash, validation, null);
			var candidate = SubmissionValidation.Read<SubmissionCandidate> (candidateBytes);
			if (!Equal (Hash (candidateBytes), validation.CandidateSha256) || !Equal (Hash (policyBytes), candidate.Identity.PolicySha256) ||
				!Equal (Hash (observationBytes), validation.ObservationsSha256))
				throw new InvalidDataException ("Review input bytes differ from the validated candidate snapshot.");
			var declaredIdentity = declarations.Identity;
			if (!Equal (declaredIdentity.PackageSha256, candidate.Identity.PackageSha256) ||
				!Equal (declaredIdentity.SourceCommit, candidate.Identity.SourceCommit) ||
				!Equal (declaredIdentity.PolicySha256, candidate.Identity.PolicySha256) ||
				!Equal (declaredIdentity.TemplateSha256, candidate.Identity.TemplateSha256))
				throw new InvalidDataException ("Gap declarations belong to another candidate, policy or form revision.");
			var policy = SubmissionValidation.Read<SubmissionEvidencePolicy> (policyBytes);
			var observations = SubmissionValidation.Read<SubmissionEvidenceDocument> (observationBytes);
			var assessment = SubmissionReviewAssessment.Assess (candidate.Identity, policy.Requirements, observations.Observations,
				evidenceDirectory, mode, declarations.Declarations, now, cancellationToken);
			// References are checked again by Assess. An intervening file change must not produce a different review.
			if (!validation.Evidence!.Issues.SequenceEqual (assessment.Evidence.Issues.Where (issue => issue.Code != "invalid-outcome")))
				throw new InvalidDataException ("Retained evidence changed during review assessment.");
			return new (declarationsHash, validation, assessment);
			}
		finally
			{
			foreach (var input in inputs) input.Dispose ();
			}
		}
	}