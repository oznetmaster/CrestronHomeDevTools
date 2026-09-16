// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

public sealed record SubmissionCandidate (
	int SchemaVersion, SubmissionEvidenceIdentity Identity, SubmissionPackageRequirements PackageRequirements);
public sealed record SubmissionEvidencePolicy (int SchemaVersion, IReadOnlyList<SubmissionRequirement> Requirements);
public sealed record SubmissionEvidenceDocument (int SchemaVersion, IReadOnlyList<SubmissionObservation> Observations);
public sealed record SubmissionValidationIssue (string Code, string Message);
public sealed record SubmissionValidationReport (
	string CandidateSha256, string ObservationsSha256, SubmissionPackageReport? Package,
	SubmissionEvidenceReport? Evidence, IReadOnlyList<SubmissionValidationIssue> Issues)
	{
	public bool ValidationChecksPassed => Issues.Count == 0 && Package?.PackageChecksPassed == true && Evidence?.EvidenceChecksPassed == true;
	}

/// <summary>Offline validation of candidate-bound input files. Does not authenticate evidence, sign forms or authorize delivery.</summary>
public static class SubmissionValidation
	{
	private const int MAX_JSON_BYTES = 16 * 1024 * 1024;
	private static readonly JsonSerializerOptions JsonOptions = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		RespectRequiredConstructorParameters = true,
		RespectNullableAnnotations = true,
		Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
		};

	/// <summary>The expected candidate digest must come from the trusted release workflow, not from the evidence producer.</summary>
	public static SubmissionValidationReport CheckFiles (
		string candidatePath, string expectedCandidateSha256, string packagePath, string policyPath,
		string templatePath, string observationsPath, string evidenceDirectory, DateTimeOffset now,
		CancellationToken cancellationToken = default)
		{
		if (expectedCandidateSha256?.Length != 64 || !expectedCandidateSha256.All (char.IsAsciiHexDigit))
			throw new ArgumentException ("Provide the candidate SHA-256 pinned by the trusted release workflow.", nameof (expectedCandidateSha256));
		cancellationToken.ThrowIfCancellationRequested ();
		// Hold all inputs open while checking them; hash and parse the same bytes.
		using var candidateInput = Open (candidatePath);
		var candidateBytes = ReadJsonBytes (candidateInput);
		var candidateDigest = Hash (candidateBytes);
		var issues = new List<SubmissionValidationIssue> ();
		if (!SameHash (candidateDigest, expectedCandidateSha256))
			return new (candidateDigest, "", null, null, [new ("candidate-digest", "The candidate declaration differs from the trusted release workflow's digest.")]);
		var candidate = Read<SubmissionCandidate> (candidateBytes);
		if (candidate.SchemaVersion != 1)
			throw new ArgumentException ("Unsupported submission candidate schema version.");
		using var packageInput = Open (packagePath);
		var package = SubmissionPackage.Inspect (packageInput, Path.GetFileName (packagePath), candidate.PackageRequirements);
		if (!SameHash (package.Sha256, candidate.Identity.PackageSha256))
			issues.Add (new ("package-digest", "The actual package differs from the release candidate's package digest."));
		using var policyInput = Open (policyPath);
		var policyBytes = ReadJsonBytes (policyInput);
		if (!SameHash (Hash (policyBytes), candidate.Identity.PolicySha256))
			issues.Add (new ("policy-digest", "The policy differs from the release candidate's approved policy digest."));
		using var templateInput = Open (templatePath);
		if (!SameHash (Convert.ToHexString (SHA256.HashData (templateInput)), candidate.Identity.TemplateSha256))
			issues.Add (new ("template-digest", "The official form differs from the release candidate's template digest."));
		using var observationsInput = Open (observationsPath);
		var observationBytes = ReadJsonBytes (observationsInput);
		var observationsDigest = Hash (observationBytes);
		if (issues.Count != 0)
			return new (candidateDigest, observationsDigest, package, null, issues.AsReadOnly ());
		var policy = Read<SubmissionEvidencePolicy> (policyBytes);
		var observations = Read<SubmissionEvidenceDocument> (observationBytes);
		if (policy.SchemaVersion != 1 || observations.SchemaVersion != 1)
			throw new ArgumentException ("Unsupported evidence policy or observation schema version.");
		var evidence = SubmissionEvidence.Evaluate (candidate.Identity, policy.Requirements, observations.Observations,
			evidenceDirectory, now, cancellationToken);
		return new (candidateDigest, observationsDigest, package, evidence, issues.AsReadOnly ());
		}

	private static FileStream Open (string path) => new (path, FileMode.Open, FileAccess.Read, FileShare.Read);
	internal static T ReadFile<T> (string path)
		{
		using var input = Open (path);
		return Read<T> (ReadJsonBytes (input));
		}
	private static string Hash (byte[] bytes) => Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant ();
	private static bool SameHash (string left, string? right) => left.Equals (right, StringComparison.OrdinalIgnoreCase);
	private static byte[] ReadJsonBytes (FileStream input)
		{
		if (input.Length > MAX_JSON_BYTES)
			throw new ArgumentException ("Submission JSON inputs must not exceed 16 MiB.");
		var bytes = new byte[checked((int)input.Length)];
		input.ReadExactly (bytes);
		return bytes;
		}

	private static T Read<T> (byte[] bytes)
		{
		using var document = JsonDocument.Parse (bytes);
		RejectDuplicateProperties (document.RootElement);
		return JsonSerializer.Deserialize<T> (bytes, JsonOptions) ?? throw new JsonException ("A submission document must not be null.");
		}

	private static void RejectDuplicateProperties (JsonElement element)
		{
		if (element.ValueKind == JsonValueKind.Object)
			{
			var names = new HashSet<string> (StringComparer.Ordinal);
			foreach (var property in element.EnumerateObject ())
				{
				if (!names.Add (property.Name))
					throw new JsonException ("Duplicate submission JSON property.");
				RejectDuplicateProperties (property.Value);
				}
			}
		else if (element.ValueKind == JsonValueKind.Array)
			foreach (var item in element.EnumerateArray ())
				RejectDuplicateProperties (item);
		}
	}