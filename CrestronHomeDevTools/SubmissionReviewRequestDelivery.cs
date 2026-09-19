// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

/// <summary>Receipt produced by submission prepare-review-request. Preparation does not authorize delivery.</summary>
public sealed record SubmissionReviewRequestReceipt (int SchemaVersion, string State, SubmissionReviewMode ReviewMode,
	SubmissionVerificationStatus VerificationStatus, SubmissionVerificationStatus EvidenceVerificationStatus,
	string SourceCommit, string CandidateSha256, string ReviewReceiptSha256, string DeclarationsSha256,
	string DispositionSha256, string BundleSha256, string PackageFileName, string PackageSha256,
	string AttachmentFileName, string AttachmentSha256, SubmissionReviewAttachmentKind AttachmentKind,
	string RequestFormReportSha256, string BundleReportSha256, DateTimeOffset EvidenceRevalidatedUtc,
	bool SignatureApplied, bool VisualReviewRequired, bool ProducerAuthenticationRequired,
	bool DeliveryAuthorized, bool SubmissionReady, bool DeliveryAttempted);

/// <summary>Deliver a prepared unsigned request using independently approved pins, fresh archived-evidence checks,
/// and the durable delivery journal. The caller must authenticate the evidence producer and approver independently.
/// This does not apply a signature or infer any decision by Crestron.</summary>
public static class SubmissionReviewRequestDelivery
	{
	private static readonly JsonSerializerOptions Options = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		AllowDuplicateProperties = false,
		RespectNullableAnnotations = true,
		RespectRequiredConstructorParameters = true,
		Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
		};

	/// <summary>Revalidate before both upload and email. Confirmation of an upload is retained when email is blocked.
	/// No external request is automatically repeated following an uncertain result.</summary>
	public static Task<SubmissionReviewDeliveryReceipt> ExecuteAsync (string requestDirectory,
		SubmissionReviewDeliveryPlan plan, string approvalPath, string expectedApprovalSha256,
		string privateJournalDirectory, string privateScratchDirectory, ISubmissionReviewDeliveryTransport transport,
		TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
		{
		_ = SubmissionDelivery.ReviewPlanDigest (plan);
		RequireDirectory (requestDirectory);
		RequireDirectory (privateScratchDirectory);
		var clock = timeProvider ?? TimeProvider.System;
		return SubmissionDelivery.ExecuteReviewAuthorizedAsync (privateJournalDirectory, plan,
			Path.Combine (requestDirectory, "delivery", plan.PackageFileName),
			Path.Combine (requestDirectory, "delivery", plan.AttachmentFileName), transport,
			(_, token) =>
				{
				Check (requestDirectory, plan, privateScratchDirectory, clock.GetUtcNow (), token);
				return Task.FromResult (SubmissionReviewApproval.Verify (plan, approvalPath, expectedApprovalSha256, clock.GetUtcNow ()));
				}, clock, cancellationToken);
		}

	/// <summary>Check the exact retained request and reassess its archive at the supplied current time. This grants no
	/// delivery permission and does not treat matching hashes as producer authentication or visual form review.</summary>
	public static SubmissionReviewBundleReport Check (string requestDirectory, SubmissionReviewDeliveryPlan plan,
		string privateScratchDirectory, DateTimeOffset now, CancellationToken cancellationToken = default)
		{
		_ = SubmissionDelivery.ReviewPlanDigest (plan);
		RequireDirectory (requestDirectory);
		RequireDirectory (privateScratchDirectory);
		cancellationToken.ThrowIfCancellationRequested ();
		if (now == default || plan.ReviewMode != SubmissionReviewMode.DeclaredGaps ||
			plan.AttachmentKind is not (SubmissionReviewAttachmentKind.UnsignedSelfTest or SubmissionReviewAttachmentKind.DisclosureOnly))
			throw new InvalidDataException ("This route requires a prepared unsigned request with disclosed omissions and the current time.");
		byte[] Read (string name, string pin, int limit = 8 * 1024 * 1024) => ReadPinned (Path.Combine (requestDirectory, name), pin, limit);
		var receipt = JsonSerializer.Deserialize<SubmissionReviewRequestReceipt> (Read ("request-receipt.json", plan.ReviewSha256), Options)
			?? throw new InvalidDataException ("Missing prepared request receipt.");
		if (receipt.SchemaVersion != 1 || receipt.State != "UnsignedRequestWithDeclaredGapsPrepared" ||
			receipt.ReviewMode != plan.ReviewMode || receipt.VerificationStatus != plan.VerificationStatus ||
			receipt.AttachmentKind != plan.AttachmentKind || receipt.CandidateSha256 != plan.CandidateSha256 ||
			receipt.DeclarationsSha256 != plan.DeclarationsSha256 || receipt.PackageSha256 != plan.PackageSha256 ||
			receipt.AttachmentSha256 != plan.AttachmentSha256 || receipt.PackageFileName != plan.PackageFileName ||
			receipt.AttachmentFileName != plan.AttachmentFileName || receipt.SignatureApplied || receipt.DeliveryAuthorized ||
			receipt.SubmissionReady || receipt.DeliveryAttempted || !receipt.VisualReviewRequired || !receipt.ProducerAuthenticationRequired ||
			receipt.EvidenceRevalidatedUtc == default || receipt.EvidenceRevalidatedUtc > now ||
			!Enum.IsDefined (receipt.EvidenceVerificationStatus) || receipt.EvidenceVerificationStatus == SubmissionVerificationStatus.NeedsCorrection ||
			receipt.SourceCommit.Length != 40 || !receipt.SourceCommit.All (Uri.IsHexDigit))
			throw new InvalidDataException ("The prepared request does not match the exact approved disposition and artifacts.");
		if (System.Text.Encoding.ASCII.GetString (ReadBounded (Path.Combine (requestDirectory, "COMPLETE"), 128)).Trim () != plan.ReviewSha256)
			throw new InvalidDataException ("Request preparation did not finish with this receipt.");
		// The original receipt pins the inventory, official form mapping and any Android provenance retained at preparation.
		using var original = JsonDocument.Parse (Read ("review-receipt.json", receipt.ReviewReceiptSha256));
		foreach (var item in new[] { ("candidateSha256", receipt.CandidateSha256), ("declarationsSha256", receipt.DeclarationsSha256),
			("sourceCommit", receipt.SourceCommit), ("verificationStatus", receipt.EvidenceVerificationStatus.ToString ()) })
			if (original.RootElement.GetProperty (item.Item1).GetString () != item.Item2)
				throw new InvalidDataException ("The original review identifies a different candidate or assessment.");
		foreach (var item in new[] { ("inventory.json", "inventorySha256"), ("mapping.json", "mappingSha256") })
			Read (item.Item1, original.RootElement.GetProperty (item.Item2).GetString ()!);
		bool hasAudit = original.RootElement.TryGetProperty ("androidAuditSha256", out var audit);
		bool hasPins = original.RootElement.TryGetProperty ("androidPinsSha256", out var pins);
		if (hasAudit != hasPins) throw new InvalidDataException ("Incomplete retained Android provenance.");
		if (hasAudit)
			{
			Read ("android-audit.json", audit.GetString ()!, 64 * 1024 * 1024);
			Read ("android-pins.json", pins.GetString ()!, 4 * 1024 * 1024);
			}
		Read ("declarations.json", receipt.DeclarationsSha256, 16 * 1024 * 1024);
		Read ("request-form-report.json", receipt.RequestFormReportSha256);
		Read ("bundle-report.json", receipt.BundleReportSha256);
		var disposition = JsonSerializer.Deserialize<Disposition> (Read ("disposition.json", receipt.DispositionSha256), Options)
			?? throw new InvalidDataException ("Missing document disposition.");
		string[] required = plan.AttachmentKind == SubmissionReviewAttachmentKind.UnsignedSelfTest
			? ["signature"] : ["officialSelfTestForm", "signature"];
		if (disposition.SchemaVersion != 1 || disposition.ReviewReceiptSha256 != receipt.ReviewReceiptSha256 ||
			disposition.CandidateSha256 != plan.CandidateSha256 || disposition.DeclarationsSha256 != plan.DeclarationsSha256 ||
			disposition.ReviewMode != plan.ReviewMode || disposition.AttachmentKind != plan.AttachmentKind ||
			disposition.Omissions == null || disposition.Omissions.Length != required.Length ||
			!disposition.Omissions.Select (item => item.Id).Order ().SequenceEqual (required.Order ()) ||
			disposition.Omissions.Any (item => string.IsNullOrWhiteSpace (item.Reason) || item.Reason.Length > 8000 || item.Reason.Any (char.IsControl)))
			throw new InvalidDataException ("Every omitted form or signature requires its exact reviewed explanation.");
		// The public summary is reviewed independently; the full detailed declarations remain bound in the PDF and archive.
		Read (Path.Combine ("delivery", plan.PackageFileName), plan.PackageSha256, 64 * 1024 * 1024);
		Read (Path.Combine ("delivery", plan.AttachmentFileName), plan.AttachmentSha256, 64 * 1024 * 1024);
		var current = SubmissionBundle.CheckReview (Path.Combine (requestDirectory, "evidence.zip"), receipt.BundleSha256,
			plan.CandidateSha256, privateScratchDirectory, receipt.DeclarationsSha256, plan.ReviewMode, now, cancellationToken);
		if (!current.ReadyForReview || current.Review.Assessment?.VerificationStatus != receipt.EvidenceVerificationStatus ||
			current.Review.Validation.Package?.Sha256?.ToLowerInvariant () != plan.PackageSha256)
			throw new InvalidDataException ("Current archived evidence no longer matches the reviewed request.");
		return current;
		}

	private sealed record Omission (string Id, string Reason);
	private sealed record Disposition (int SchemaVersion, string ReviewReceiptSha256, string CandidateSha256,
		string DeclarationsSha256, SubmissionReviewMode ReviewMode, SubmissionReviewAttachmentKind AttachmentKind, Omission[] Omissions);

	private static void RequireDirectory (string path)
		{
		if (!Path.IsPathFullyQualified (path) || !Directory.Exists (path))
			throw new DirectoryNotFoundException ("Provide existing absolute private directories.");
		RejectLinks (path);
		}
	private static void RejectLinks (string path)
		{
		for (string? current = path; current != null; current = Path.GetDirectoryName (current))
			if ((File.GetAttributes (current) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("Retained request paths cannot traverse redirected links.");
		}
	private static byte[] ReadPinned (string path, string pin, int limit)
		{
		var data = ReadBounded (path, limit);
		if (Convert.ToHexStringLower (SHA256.HashData (data)) != pin)
			throw new InvalidDataException ("Retained request bytes differ from the approved packet.");
		return data;
		}
	private static byte[] ReadBounded (string path, int limit)
		{
		RejectLinks (path);
		using var input = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (input.Length is <= 0 || input.Length > limit) throw new InvalidDataException ("Retained request file exceeds its size limit.");
		byte[] data = new byte[(int)input.Length];
		input.ReadExactly (data);
		if (input.ReadByte () != -1) throw new InvalidDataException ("Retained request file changed while reading.");
		return data;
		}
	}