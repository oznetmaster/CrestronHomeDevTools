// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

namespace CrestronHomeDevTools;

public static partial class SubmissionReviewRequestDelivery
	{
	// A signed declared-gap packet uses the same approval, provider and journal
	// boundaries as an unsigned request. It retains its independent signing chain.
	private static SubmissionReviewBundleReport CheckSigned (string directory, SubmissionReviewDeliveryPlan plan,
		string scratch, DateTimeOffset now, CancellationToken cancellationToken)
		{
		if (now == default || plan.ReviewMode != SubmissionReviewMode.DeclaredGaps || !string.IsNullOrEmpty (plan.DocumentOmissions))
			throw new InvalidDataException ("Signed review delivery requires declared-gap mode, no document omissions and the current time.");
		byte[] Read (string name, string pin, int limit = 8 * 1024 * 1024) => ReadPinned (Path.Combine (directory, name), pin, limit);
		using var receiptDocument = JsonDocument.Parse (Read ("signed-review-receipt.json", plan.ReviewSha256));
		var receipt = receiptDocument.RootElement;
		static string Text (JsonElement item, string key) => item.GetProperty (key).GetString () ?? throw new InvalidDataException ("Missing signed review identity.");
		static void Match (JsonElement item, string key, string expected)
			{
			if (Text (item, key) != expected) throw new InvalidDataException ("The signed review differs from the reviewed packet.");
			}
		if (receipt.GetProperty ("schemaVersion").GetInt32 () != 1 ||
			Text (receipt, "state") != "SignedReviewPrepared" || !receipt.GetProperty ("signatureApplied").GetBoolean () ||
			!receipt.GetProperty ("visualReviewRequired").GetBoolean () || receipt.GetProperty ("deliveryAuthorized").GetBoolean () ||
			receipt.GetProperty ("submissionReady").GetBoolean () || receipt.GetProperty ("deliveryAttempted").GetBoolean ())
			throw new InvalidDataException ("A completed, separately approved signed review is required.");
		foreach (var item in new[] { ("candidateSha256", plan.CandidateSha256), ("packageSha256", plan.PackageSha256),
			("signedFormSha256", plan.AttachmentSha256), ("packageFileName", plan.PackageFileName),
			("signedFormFileName", plan.AttachmentFileName), ("reviewMode", plan.ReviewMode.ToString ()),
			("verificationStatus", plan.VerificationStatus.ToString ()), ("declarationsSha256", plan.DeclarationsSha256) })
			Match (receipt, item.Item1, item.Item2!);
		var revalidated = receipt.GetProperty ("evidenceRevalidatedUtc").GetDateTimeOffset ();
		string commit = Text (receipt, "sourceCommit");
		if (revalidated == default || revalidated > now || commit.Length != 40 || !commit.All (Uri.IsHexDigit) ||
			System.Text.Encoding.ASCII.GetString (ReadBounded (Path.Combine (directory, "COMPLETE"), 128)).Trim () != plan.ReviewSha256)
			throw new InvalidDataException ("Signed review preparation did not finish with the approved identity.");
		using var originalDocument = JsonDocument.Parse (Read ("review-receipt.json", Text (receipt, "reviewReceiptSha256")));
		var original = originalDocument.RootElement;
		Match (original, "state", "UnsignedReviewWithDeclaredGapsPrepared");
		if (!original.GetProperty ("signingCopy").GetBoolean ()) throw new InvalidDataException ("The original form was not prepared for signing.");
		foreach (string key in new[] { "candidateSha256", "sourceCommit", "bundleSha256", "reviewMode", "verificationStatus", "declarationsSha256" })
			Match (original, key, Text (receipt, key));
		Read ("inventory.json", Text (original, "inventorySha256"));
		Read ("mapping.json", Text (original, "mappingSha256"));
		Read ("self-test.review.pdf", Text (original, "formSha256"), 64 * 1024 * 1024);
		using var formDocument = JsonDocument.Parse (Read ("form-report.json", Text (original, "formReportSha256")));
		var form = formDocument.RootElement;
		Match (form, "formSha256", Text (original, "formSha256"));
		using var signingDocument = JsonDocument.Parse (Read ("signing-report.json", Text (receipt, "signingReportSha256")));
		var signing = signingDocument.RootElement;
		if (!signing.GetProperty ("signatureApplied").GetBoolean ()) throw new InvalidDataException ("The signature has not been applied.");
		Match (signing, "unsignedFormSha256", Text (original, "formSha256"));
		foreach (string key in new[] { "candidateSha256", "packageSha256", "signedFormSha256", "authorizationSha256", "reviewMode", "verificationStatus", "declarationsSha256" })
			Match (signing, key, Text (receipt, key));
		foreach (string key in new[] { "reviewMode", "verificationStatus", "declarationsSha256" })
			{
			Match (form, key, Text (receipt, key));
			Match (form.GetProperty ("identity"), key, Text (receipt, key));
			}
		bool hasAudit = original.TryGetProperty ("androidAuditSha256", out var audit);
		bool hasPins = original.TryGetProperty ("androidPinsSha256", out var pins);
		if (hasAudit != hasPins) throw new InvalidDataException ("Incomplete retained Android provenance.");
		if (hasAudit)
			{
			Read ("android-audit.json", audit.GetString ()!, 64 * 1024 * 1024);
			Read ("android-pins.json", pins.GetString ()!, 4 * 1024 * 1024);
			}
		Read ("declarations.json", plan.DeclarationsSha256!, 16 * 1024 * 1024);
		Read ("validation-report.json", Text (receipt, "validationReportSha256"));
		Read (Path.Combine ("delivery", plan.PackageFileName), plan.PackageSha256, 64 * 1024 * 1024);
		Read (Path.Combine ("delivery", plan.AttachmentFileName), plan.AttachmentSha256, 64 * 1024 * 1024);
		var current = SubmissionBundle.CheckReview (Path.Combine (directory, "evidence.zip"), Text (receipt, "bundleSha256"),
			plan.CandidateSha256, scratch, plan.DeclarationsSha256!, plan.ReviewMode, now, cancellationToken);
		if (!current.ReadyForReview || current.Review.Assessment?.VerificationStatus != plan.VerificationStatus ||
			current.Review.Validation.Package?.Sha256?.ToLowerInvariant () != plan.PackageSha256 ||
			current.Review.Validation.ObservationsSha256 != Text (form.GetProperty ("identity"), "observationsSha256"))
			throw new InvalidDataException ("Current evidence no longer matches the signed review.");
		return current;
		}
	}