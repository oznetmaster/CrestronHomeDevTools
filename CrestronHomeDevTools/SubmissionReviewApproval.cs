// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

/// <summary>The independently approved packet and correspondence. Its file digest must originate with the approver,
/// not the evidence worker. These assertions do not constitute a Crestron decision or authenticate themselves.</summary>
public sealed record SubmissionReviewApprovalDocument (int SchemaVersion, string PacketSha256, string CorrespondenceSha256,
	DateTimeOffset ExpiresUtc, bool VisualReviewCompleted, bool ProducerAuthenticationConfirmed, bool DeliveryAuthorized);

public sealed record SubmissionReviewApprovalPreview (int SchemaVersion, string PacketSha256, string CorrespondenceSha256,
	SubmissionReviewCorrespondence Correspondence, SubmissionReviewMode ReviewMode, SubmissionVerificationStatus VerificationStatus,
	SubmissionReviewAttachmentKind AttachmentKind, bool DeliveryAuthorized);

/// <summary>Bind independent final approval to the exact review packet, omissions and actual generated correspondence.
/// This verifies approval only; the delivery coordinator must also revalidate current artifacts and evidence before each step.</summary>
public static class SubmissionReviewApproval
	{
	private static readonly JsonSerializerOptions Options = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		AllowDuplicateProperties = false,
		RespectNullableAnnotations = true,
		RespectRequiredConstructorParameters = true,
		Converters = { new JsonStringEnumConverter (allowIntegerValues: false) },
		WriteIndented = true
		};

	/// <summary>Preview without approving. Neutralize only the approval-file digest to avoid a circular hash:
/// the approved packet covers all other plan fields and correspondence; final delivery also binds the actual approval digest.</summary>
	public static SubmissionReviewApprovalPreview Preview (SubmissionReviewDeliveryPlan plan)
		{
		_ = SubmissionDelivery.ReviewPlanDigest (plan);
		var correspondence = SubmissionDelivery.ReviewCorrespondence (plan);
		var packet = new { Plan = plan with { AuthorizationSha256 = new string ('0', 64) }, Correspondence = correspondence };
		return new (1, Hash (JsonSerializer.SerializeToUtf8Bytes (packet, Options)),
			Hash (JsonSerializer.SerializeToUtf8Bytes (correspondence, Options)), correspondence,
			plan.ReviewMode, plan.VerificationStatus, plan.AttachmentKind, false);
		}

	/// <summary>Read and verify the same bounded bytes against a separately trusted pin. Matching hashes and boolean
/// assertions are not proof of approver identity; the caller must protect the approval channel and pin independently.</summary>
	public static SubmissionDeliveryAuthorization Verify (SubmissionReviewDeliveryPlan plan, string approvalPath,
		string expectedApprovalSha256, DateTimeOffset now)
		{
		var preview = Preview (plan);
		if (now == default || expectedApprovalSha256 != plan.AuthorizationSha256 || !Path.IsPathFullyQualified (approvalPath))
			throw new InvalidDataException ("Supply the current time, absolute approval path and separately trusted approval pin for this plan.");
		if ((File.GetAttributes (approvalPath) & FileAttributes.ReparsePoint) != 0)
			throw new InvalidDataException ("Approval files cannot be redirected links.");
		using var input = new FileStream (approvalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (input.Length is <= 0 or > 1024 * 1024)
			throw new InvalidDataException ("The approval document must fit within 1 MiB.");
		byte[] data = new byte[(int)input.Length];
		input.ReadExactly (data);
		if (input.ReadByte () != -1 || Hash (data) != expectedApprovalSha256)
			throw new InvalidDataException ("The approval differs from the independently trusted bytes.");
		var approval = JsonSerializer.Deserialize<SubmissionReviewApprovalDocument> (data, Options)
			?? throw new InvalidDataException ("Missing approval document.");
		using var json = JsonDocument.Parse (data);
		string expiry = json.RootElement.GetProperty ("expiresUtc").GetString ()!;
		if (!(expiry.EndsWith ('Z') || expiry.EndsWith ("+00:00", StringComparison.Ordinal)))
			throw new InvalidDataException ("Approval expiry must specify UTC explicitly.");
		if (approval.SchemaVersion != 1 || !approval.VisualReviewCompleted || !approval.ProducerAuthenticationConfirmed ||
			!approval.DeliveryAuthorized || approval.ExpiresUtc <= now || approval.PacketSha256 != preview.PacketSha256 ||
			approval.CorrespondenceSha256 != preview.CorrespondenceSha256)
			throw new InvalidDataException ("Current explicit approval must cover the exact packet, visual review, evidence provenance and correspondence.");
		return new (SubmissionDelivery.ReviewPlanDigest (plan), approval.ExpiresUtc);
		}

	private static string Hash (byte[] bytes) => Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant ();
	}