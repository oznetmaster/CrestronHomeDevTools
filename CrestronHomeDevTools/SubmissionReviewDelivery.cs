// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

namespace CrestronHomeDevTools;

public enum SubmissionReviewAttachmentKind { SignedSelfTest, UnsignedSelfTest, DisclosureOnly }

/// <summary>An exact outbound packet. The trusted coordinator must reconcile all gaps and document omissions
/// against the full interpreted requirements before authorizing this plan. An attachment name never proves a signature.</summary>
public sealed record SubmissionReviewDeliveryPlan (string CandidateSha256, string ReviewSha256, string AuthorizationSha256,
	string PackageSha256, string AttachmentSha256, string PackageFileName, string AttachmentFileName, string Sender, string Recipient,
	SubmissionReviewMode ReviewMode, SubmissionVerificationStatus VerificationStatus, SubmissionReviewAttachmentKind AttachmentKind,
	string? DeclarationsSha256, string? GapSummary, string? DocumentOmissions)
	{
	/// <summary>Optional independently reviewed correspondence. Its body must contain exactly one
	/// {{PACKAGE_DOWNLOAD_URL}} token. The caller must review all disclosures; changing this text invalidates approval.</summary>
	[System.Text.Json.Serialization.JsonIgnore (Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public SubmissionReviewCorrespondence? CorrespondenceOverride { get; init; }
	}

/// <summary>Reviewable correspondence. The confirmed provider download URL is appended only after upload.</summary>
public sealed record SubmissionReviewCorrespondence (string Subject, string Body);

/// <summary>Keep reviewed verification and document disposition separate from provider delivery state.
/// This record contains no Crestron decision; one requires separately retained actual correspondence.</summary>
public sealed record SubmissionReviewDeliveryReceipt (SubmissionReviewMode ReviewMode, SubmissionVerificationStatus VerificationStatus,
	SubmissionReviewAttachmentKind AttachmentKind, string? DeclarationsSha256, SubmissionDeliveryReceipt Delivery);

/// <summary>Send once, without automatic retries. The stream can be an unsigned form or disclosure report;
/// implementations must preserve the exact plan's signature and verification distinctions.</summary>
public interface ISubmissionReviewDeliveryTransport
	{
	Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken cancellationToken);
	Task<SubmissionMailReceipt> SendReviewAsync (SubmissionReviewDeliveryPlan plan, SubmissionUploadReceipt upload,
		Stream attachment, string messageId, CancellationToken cancellationToken);
	}

public static partial class SubmissionDelivery
	{
	/// <summary>Read delivery state only. Submitted means a provider-confirmed upload and send, never Crestron acceptance.</summary>
	public static SubmissionReviewDeliveryReceipt? ReadReview (string privateJournalDirectory, SubmissionReviewDeliveryPlan plan)
		{
		var descriptor = DescribeReview (plan);
		using var journal = new Journal (privateJournalDirectory, descriptor.Digest, descriptor.Key);
		var receipt = journal.Read ();
		return receipt == null ? null : ReviewReceipt (plan, receipt);
		}

	/// <summary>Authorize the exact verification mode, declarations, omissions, document bytes and correspondence before
/// each external step. The callback must independently verify the reviewed packet and current approver authority.</summary>
	public static async Task<SubmissionReviewDeliveryReceipt> ExecuteReviewAuthorizedAsync (string privateJournalDirectory,
		SubmissionReviewDeliveryPlan plan, string packagePath, string attachmentPath, ISubmissionReviewDeliveryTransport transport,
		Func<SubmissionDeliveryStep, CancellationToken, Task<SubmissionDeliveryAuthorization>> revalidate,
		TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (transport);
		var receipt = await ExecuteAuthorizedCoreAsync (privateJournalDirectory, DescribeReview (plan), packagePath, attachmentPath,
			transport.UploadAsync, (upload, attachment, messageId, token) => transport.SendReviewAsync (plan, upload, attachment, messageId, token),
			revalidate, timeProvider, cancellationToken).ConfigureAwait (false);
		return ReviewReceipt (plan, receipt);
		}

	/// <summary>Resolve only a provider-confirmed outcome; a timeout or missing email is not proof of non-delivery.</summary>
	public static SubmissionReviewDeliveryReceipt ReconcileReview (string privateJournalDirectory, SubmissionReviewDeliveryPlan plan,
		SubmissionDeliveryStep step, bool performed, string evidence, SubmissionUploadReceipt? upload = null, SubmissionMailReceipt? mail = null)
		=> ReviewReceipt (plan, ReconcileCore (privateJournalDirectory, DescribeReview (plan), step, performed, evidence, upload, mail));

	private static SubmissionReviewDeliveryReceipt ReviewReceipt (SubmissionReviewDeliveryPlan plan, SubmissionDeliveryReceipt receipt) =>
		new (plan.ReviewMode, plan.VerificationStatus, plan.AttachmentKind, plan.DeclarationsSha256, receipt);

	/// <summary>Includes the actual generated correspondence, not just a template version. A wording, gap or attachment
/// change invalidates approval. No raw evidence or private file paths belong in public correspondence fields.</summary>
	public static string ReviewPlanDigest (SubmissionReviewDeliveryPlan plan)
		{
		ValidateReviewPlan (plan);
		return Hash (JsonSerializer.SerializeToUtf8Bytes (new { Plan = plan, Correspondence = ReviewCorrespondenceCore (plan) }, JsonOptions));
		}

	public static SubmissionReviewCorrespondence ReviewCorrespondence (SubmissionReviewDeliveryPlan plan)
		{
		ValidateReviewPlan (plan);
		return ReviewCorrespondenceCore (plan);
		}

	private static SubmissionReviewCorrespondence ReviewCorrespondenceCore (SubmissionReviewDeliveryPlan plan)
		{
		if (plan.CorrespondenceOverride is { } reviewed) return reviewed;
		string verification = plan.VerificationStatus == SubmissionVerificationStatus.GapsDeclared
			? "Request for review with declared gaps. This submission does not meet all requirements as we interpret Crestron's published submission requirements.\r\n\r\n" +
				"Declared gaps: " + plan.GapSummary + "\r\nDeclaration SHA-256: " + plan.DeclarationsSha256 + "\r\n"
			: "Our verification is complete against our interpretation of Crestron's published submission requirements.\r\n";
		string attachment = plan.AttachmentKind switch
			{
			SubmissionReviewAttachmentKind.SignedSelfTest => "The attachment contains the signed self-test form and its reviewed evidence summary.",
			SubmissionReviewAttachmentKind.UnsignedSelfTest => "The attachment contains an UNSIGNED self-test form and its disclosure report. No signature is supplied.",
			_ => "The attachment is a disclosure report only. The required official self-test form and signature are NOT supplied."
			};
		return new ("Driver Submission Package", verification + "\r\n" + attachment + "\r\n" +
			(plan.DocumentOmissions == null ? "" : "Document/signature omissions and reasons: " + plan.DocumentOmissions + "\r\n") +
			"\r\nOnly Crestron can decide acceptance, publication or certification; none is implied by this request.\r\n\r\n" +
			"Package: " + plan.PackageFileName + "\r\nPackage SHA-256: " + plan.PackageSha256 + "\r\n" +
			"Attachment: " + plan.AttachmentFileName + "\r\nAttachment SHA-256: " + plan.AttachmentSha256 + "\r\n");
		}

	private static void ValidateReviewPlan (SubmissionReviewDeliveryPlan plan)
		{
		ArgumentNullException.ThrowIfNull (plan);
		if (plan.CorrespondenceOverride is { } correspondence)
			{
			const string token = "{{PACKAGE_DOWNLOAD_URL}}";
			if (correspondence.Subject != "Driver Submission Package" || string.IsNullOrWhiteSpace (correspondence.Body) ||
				correspondence.Body.Length > 32000 || correspondence.Body.Any (c => char.IsControl (c) && c is not ('\r' or '\n' or '\t')) ||
				correspondence.Body.IndexOf (token, StringComparison.Ordinal) < 0 ||
				correspondence.Body.IndexOf (token, StringComparison.Ordinal) != correspondence.Body.LastIndexOf (token, StringComparison.Ordinal))
				throw new ArgumentException ("Reviewed correspondence requires the submission subject and one package download URL token.");
			}
		// Reuse only common byte/name/address syntax validation, not the old signed-form semantics or plan digest.
		_ = PlanDigest (new (plan.CandidateSha256, plan.ReviewSha256, plan.AuthorizationSha256, plan.PackageSha256,
			plan.AttachmentSha256, plan.PackageFileName, plan.AttachmentFileName, plan.Sender, plan.Recipient));
		if (!Enum.IsDefined (plan.ReviewMode) || !Enum.IsDefined (plan.AttachmentKind) || !Enum.IsDefined (plan.VerificationStatus) ||
			plan.VerificationStatus == SubmissionVerificationStatus.NeedsCorrection)
			throw new ArgumentException ("An internally consistent, reviewed submission disposition is required.");
		foreach (var value in new[] { plan.GapSummary, plan.DocumentOmissions })
			if (value != null && (string.IsNullOrWhiteSpace (value) || value.Length > 16000 || value.Any (char.IsControl)))
				throw new ArgumentException ("Use nonempty public disclosure text up to 16000 characters, without control characters.");
		if (plan.ReviewMode == SubmissionReviewMode.Complete)
			{
			if (plan.VerificationStatus != SubmissionVerificationStatus.CompleteAgainstInterpretedRequirements ||
				plan.AttachmentKind != SubmissionReviewAttachmentKind.SignedSelfTest || plan.DeclarationsSha256 != null ||
				plan.GapSummary != null || plan.DocumentOmissions != null)
				throw new ArgumentException ("Complete mode cannot conceal test, document or signature omissions.");
			}
		else
			{
			if (plan.VerificationStatus != SubmissionVerificationStatus.GapsDeclared || plan.GapSummary == null ||
				plan.DeclarationsSha256?.Length != 64 || !plan.DeclarationsSha256.All (c => char.IsAsciiDigit (c) || c is >= 'a' and <= 'f'))
				throw new ArgumentException ("Declared-gap delivery requires its exact disclosure pin, summary and gap status.");
			if (plan.AttachmentKind != SubmissionReviewAttachmentKind.SignedSelfTest && plan.DocumentOmissions == null)
				throw new ArgumentException ("Explain the missing signature or official form explicitly.");
			}
		}

	private static DeliveryDescriptor DescribeReview (SubmissionReviewDeliveryPlan plan) => new (ReviewPlanDigest (plan),
		// Shared with legacy delivery: changing mode, disclosures or approvals must not replay the same outbound bytes.
		Hash (JsonSerializer.SerializeToUtf8Bytes (new[] { plan.PackageSha256, plan.AttachmentSha256, plan.Sender.ToLowerInvariant (), plan.Recipient.ToLowerInvariant () })),
		plan.PackageFileName, plan.PackageSha256, plan.AttachmentFileName, plan.AttachmentSha256);
	}
