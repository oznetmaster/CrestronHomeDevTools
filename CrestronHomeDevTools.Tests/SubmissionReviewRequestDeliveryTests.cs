// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed partial class SubmissionReviewFilesTests
	{
	private SubmissionReviewDeliveryPlan PrepareRequest (SubmissionReviewAttachmentKind kind = SubmissionReviewAttachmentKind.UnsignedSelfTest)
		{
		CreateReviewBundle ();
		File.Move (PathFor ("review.zip"), PathFor ("evidence.zip"));
		Write ("inventory.json", new { synthetic = true });
		Write ("mapping.json", new { synthetic = true });
		Write ("review-receipt.json", new { candidateSha256 = _candidatePin, declarationsSha256 = _declarationsPin,
			sourceCommit = new string ('b', 40), verificationStatus = "GapsDeclared", inventorySha256 = Digest ("inventory.json"), mappingSha256 = Digest ("mapping.json") });
		var omissions = kind == SubmissionReviewAttachmentKind.UnsignedSelfTest
			? new[] { new { id = "signature", reason = "No signature is provided in this synthetic request." } }
			: [new { id = "signature", reason = "No signature is provided in this synthetic request." }, new { id = "officialSelfTestForm", reason = "Form omitted in this synthetic request." }];
		Write ("disposition.json", new { schemaVersion = 1, reviewReceiptSha256 = Digest ("review-receipt.json"), candidateSha256 = _candidatePin,
			declarationsSha256 = _declarationsPin, reviewMode = "DeclaredGaps", attachmentKind = kind.ToString (), omissions });
		Write ("request-form-report.json", new { synthetic = true });
		Write ("bundle-report.json", new { synthetic = true });
		Directory.CreateDirectory (PathFor ("delivery"));
		Directory.CreateDirectory (PathFor ("journal"));
		Directory.CreateDirectory (PathFor ("scratch"));
		File.Copy (PathFor (PACKAGE), PathFor ("delivery/" + PACKAGE));
		File.WriteAllText (PathFor ("delivery/request.pdf"), "%PDF-1.7 synthetic unsigned request - never send");
		var receipt = new SubmissionReviewRequestReceipt (1, "UnsignedRequestWithDeclaredGapsPrepared", SubmissionReviewMode.DeclaredGaps,
			SubmissionVerificationStatus.GapsDeclared, SubmissionVerificationStatus.GapsDeclared, new ('b', 40), _candidatePin,
			Digest ("review-receipt.json"), _declarationsPin, Digest ("disposition.json"), Digest ("evidence.zip"), PACKAGE, Digest (PACKAGE),
			"request.pdf", Digest ("delivery/request.pdf"), kind, Digest ("request-form-report.json"), Digest ("bundle-report.json"), Now,
			false, true, true, false, false, false);
		Write ("request-receipt.json", receipt);
		File.WriteAllText (PathFor ("COMPLETE"), Digest ("request-receipt.json") + "\n");
		return ApproveRequest (new (_candidatePin, Digest ("request-receipt.json"), new ('0', 64), Digest (PACKAGE), Digest ("delivery/request.pdf"),
			PACKAGE, "request.pdf", "sender@example.test", "recipient@example.test", SubmissionReviewMode.DeclaredGaps,
			SubmissionVerificationStatus.GapsDeclared, kind, _declarationsPin, "Controls and endurance not tested; equipment unavailable.", "Signature omitted; synthetic test only."));
		}
	private SubmissionReviewDeliveryPlan ApproveRequest (SubmissionReviewDeliveryPlan plan)
		{
		var preview = SubmissionReviewApproval.Preview (plan);
		Write ("approval.json", new SubmissionReviewApprovalDocument (1, preview.PacketSha256, preview.CorrespondenceSha256, Now.AddHours (1), true, true, true));
		return plan with { AuthorizationSha256 = Digest ("approval.json") };
		}
	private Task<SubmissionReviewDeliveryReceipt> DeliverRequest (SubmissionReviewDeliveryPlan plan, RequestTransport transport) =>
		SubmissionReviewRequestDelivery.ExecuteAsync (_root, plan, PathFor ("approval.json"), plan.AuthorizationSha256,
			PathFor ("journal"), PathFor ("scratch"), transport, new RequestClock ());
	private sealed class RequestClock : TimeProvider { public override DateTimeOffset GetUtcNow () => Now; }
	private sealed class RequestTransport : ISubmissionReviewDeliveryTransport
		{
		internal int Uploads, Sends;
		internal Action? AfterUpload;
		public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken token)
			{
			Uploads++;
			AfterUpload?.Invoke ();
			return Task.FromResult (new SubmissionUploadReceipt ("https://example.test/synthetic", "simulated upload"));
			}
		public Task<SubmissionMailReceipt> SendReviewAsync (SubmissionReviewDeliveryPlan plan, SubmissionUploadReceipt upload, Stream attachment, string id, CancellationToken token)
			{
			Sends++;
			return Task.FromResult (new SubmissionMailReceipt ("simulated mail"));
			}
		}

	[TestCase (SubmissionReviewAttachmentKind.UnsignedSelfTest)]
	[TestCase (SubmissionReviewAttachmentKind.DisclosureOnly)]
	public async Task PreparedRequestReassessesFailedEvidenceAndDeliversOnce (SubmissionReviewAttachmentKind kind)
		{
		var plan = PrepareRequest (kind);
		var report = SubmissionReviewRequestDelivery.Check (_root, plan, PathFor ("scratch"), Now);
		Assert.That (report.Review.Validation.ValidationChecksPassed, Is.False);
		var transport = new RequestTransport ();
		var result = await DeliverRequest (plan, transport);
		Assert.That (result.Delivery.State, Is.EqualTo (SubmissionDeliveryState.Submitted));
		Assert.That (result.VerificationStatus, Is.EqualTo (SubmissionVerificationStatus.GapsDeclared));
		Assert.That (await DeliverRequest (plan, transport), Is.EqualTo (result));
		Assert.That ((transport.Uploads, transport.Sends), Is.EqualTo ((1, 1)));
		}

	[TestCase ("evidence.zip")]
	[TestCase ("approval.json")]
	[TestCase ("request-receipt.json")]
	[TestCase ("review-receipt.json")]
	[TestCase ("disposition.json")]
	[TestCase ("inventory.json")]
	[TestCase ("mapping.json")]
	[TestCase ("declarations.json")]
	[TestCase ("request-form-report.json")]
	[TestCase ("bundle-report.json")]
	[TestCase ("delivery/request.pdf")]
	[TestCase ("COMPLETE")]
	public void ChangedRequestBeforeSendRetainsUploadAndBlocksEmail (string file)
		{
		var plan = PrepareRequest ();
		var transport = new RequestTransport { AfterUpload = () => File.AppendAllText (PathFor (file), "changed") };
		Assert.ThrowsAsync<InvalidDataException> (async () => await DeliverRequest (plan, transport));
		Assert.That ((transport.Uploads, transport.Sends), Is.EqualTo ((1, 0)));
		Assert.That (SubmissionDelivery.ReadReview (PathFor ("journal"), plan)!.Delivery.State, Is.EqualTo (SubmissionDeliveryState.Uploaded));
		}

	[TestCase ("signatureApplied")]
	[TestCase ("deliveryAuthorized")]
	[TestCase ("submissionReady")]
	[TestCase ("deliveryAttempted")]
	[TestCase ("schemaVersion")]
	[TestCase ("evidenceVerificationStatus")]
	public void ApprovalCannotAuthorizeAnInconsistentPreparedReceipt (string field)
		{
		var plan = PrepareRequest ();
		var json = System.Text.Json.Nodes.JsonNode.Parse (File.ReadAllText (PathFor ("request-receipt.json")))!;
		if (field == "schemaVersion") json[field] = 2;
		else if (field == "evidenceVerificationStatus") json[field] = "CompleteAgainstInterpretedRequirements";
		else json[field] = true;
		File.WriteAllText (PathFor ("request-receipt.json"), json.ToJsonString ());
		File.WriteAllText (PathFor ("COMPLETE"), Digest ("request-receipt.json"));
		plan = ApproveRequest (plan with { ReviewSha256 = Digest ("request-receipt.json") });
		var transport = new RequestTransport ();
		Assert.ThrowsAsync<InvalidDataException> (async () => await DeliverRequest (plan, transport));
		Assert.That ((transport.Uploads, transport.Sends), Is.EqualTo ((0, 0)));
		}
	}