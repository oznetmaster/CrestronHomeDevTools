// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionReviewDeliveryTests
	{
	private string _root = null!, _package = null!, _attachment = null!;
	private SubmissionReviewDeliveryPlan _plan = null!;
	private Transport _transport = null!;
	private static readonly SubmissionUploadReceipt Upload = new ("https://example.test/synthetic", "synthetic upload receipt");
	private static readonly SubmissionMailReceipt Mail = new ("synthetic mail receipt");
	private static string Hash (byte[] bytes) => Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant ();

	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "review-delivery-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		_package = Path.Combine (_root, "Example_Device_IP.pkg");
		_attachment = Path.Combine (_root, "disclosures.pdf");
		File.WriteAllText (_package, "synthetic package - never transmit");
		File.WriteAllText (_attachment, "synthetic unsigned disclosure - never transmit");
		_plan = new (new ('a', 64), new ('b', 64), new ('c', 64), Hash (File.ReadAllBytes (_package)), Hash (File.ReadAllBytes (_attachment)),
			Path.GetFileName (_package), Path.GetFileName (_attachment), "sender@example.test", "recipient@example.test",
			SubmissionReviewMode.DeclaredGaps, SubmissionVerificationStatus.GapsDeclared, SubmissionReviewAttachmentKind.UnsignedSelfTest,
			new ('d', 64), "Endurance was not performed; equipment was unavailable.", "The developer declined to sign the incomplete self-test form.");
		_transport = new ();
		}

	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);

	private Task<SubmissionReviewDeliveryReceipt> Execute (Func<SubmissionDeliveryStep, CancellationToken, Task<SubmissionDeliveryAuthorization>>? approve = null) =>
		SubmissionDelivery.ExecuteReviewAuthorizedAsync (_root, _plan, _package, _attachment, _transport,
			approve ?? ((_, _) => Task.FromResult (new SubmissionDeliveryAuthorization (SubmissionDelivery.ReviewPlanDigest (_plan), DateTimeOffset.UtcNow.AddMinutes (2)))));

	[TestCase (SubmissionReviewAttachmentKind.SignedSelfTest)]
	[TestCase (SubmissionReviewAttachmentKind.UnsignedSelfTest)]
	[TestCase (SubmissionReviewAttachmentKind.DisclosureOnly)]
	public async Task DispositionSurvivesDeliveryWithoutBeingChangedToComplete (SubmissionReviewAttachmentKind kind)
		{
		_plan = _plan with { AttachmentKind = kind };
		var approvals = new List<SubmissionDeliveryStep> ();
		var result = await Execute ((step, _) =>
			{
			approvals.Add (step);
			return Task.FromResult (new SubmissionDeliveryAuthorization (SubmissionDelivery.ReviewPlanDigest (_plan), DateTimeOffset.UtcNow.AddMinutes (2)));
			});
		Assert.That (approvals, Is.EqualTo (new[] { SubmissionDeliveryStep.Upload, SubmissionDeliveryStep.Send }));
		Assert.That (result.Delivery.State, Is.EqualTo (SubmissionDeliveryState.Submitted));
		Assert.That (_transport.SentPlan, Is.EqualTo (_plan));
		Assert.That (_transport.SentPlan!.VerificationStatus, Is.EqualTo (SubmissionVerificationStatus.GapsDeclared));
		Assert.That (Hash (_transport.SentBytes!), Is.EqualTo (_plan.AttachmentSha256));
		File.Delete (_attachment);
		File.Delete (_package);
		Assert.That (await Execute ((_, _) => throw new AssertionException ("Completed delivery must not reauthorize or repeat")), Is.EqualTo (result));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((1, 1)));
		}

	[TestCase (SubmissionDeliveryStep.Upload)]
	[TestCase (SubmissionDeliveryStep.Send)]
	public async Task UnknownOutcomeRequiresReconciliationBeforeContinuing (SubmissionDeliveryStep step)
		{
		_transport.Fail = step;
		Assert.ThrowsAsync<IOException> (async () => await Execute ());
		Assert.That (SubmissionDelivery.ReadReview (_root, _plan)!.Delivery.State, Is.EqualTo (SubmissionDeliveryState.OutcomeUnknown));
		_transport.Fail = null;
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Execute ());
		SubmissionDelivery.ReconcileReview (_root, _plan, step, true, "Synthetic provider lookup confirms the accepted request",
			step == SubmissionDeliveryStep.Upload ? Upload : null, step == SubmissionDeliveryStep.Send ? Mail : null);
		Assert.That ((await Execute ()).Delivery.State, Is.EqualTo (SubmissionDeliveryState.Submitted));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((1, 1)));
		}

	[TestCase ("summary")]
	[TestCase ("declarations")]
	[TestCase ("omissions")]
	[TestCase ("signature")]
	[TestCase ("mode")]
	public void ChangingReviewInvalidatesApprovalBeforeAnyProviderCall (string change)
		{
		string approved = SubmissionDelivery.ReviewPlanDigest (_plan);
		_plan = Change (change);
		Assert.That (SubmissionDelivery.ReviewPlanDigest (_plan), Is.Not.EqualTo (approved));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Execute ((_, _) =>
			Task.FromResult (new SubmissionDeliveryAuthorization (approved, DateTimeOffset.UtcNow.AddMinutes (2)))));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((0, 0)));
		}

	private SubmissionReviewDeliveryPlan Change (string change) => change switch
		{
		"summary" => _plan with { GapSummary = "The endurance test failed; the original failed outcome is retained." },
		"declarations" => _plan with { DeclarationsSha256 = new ('e', 64) },
		"omissions" => _plan with { DocumentOmissions = "The official form and signature are unavailable." },
		"signature" => _plan with { AttachmentKind = SubmissionReviewAttachmentKind.DisclosureOnly },
		_ => _plan with { ReviewMode = SubmissionReviewMode.Complete, VerificationStatus = SubmissionVerificationStatus.CompleteAgainstInterpretedRequirements,
			AttachmentKind = SubmissionReviewAttachmentKind.SignedSelfTest, DeclarationsSha256 = null, GapSummary = null, DocumentOmissions = null }
		};

	[Test]
	public async Task NewApprovalOrModeCannotCreateAnotherJournalForIdenticalOutboundBytes ()
		{
		await Execute ();
		_plan = Change ("mode");
		Assert.ThrowsAsync<InvalidDataException> (async () => await Execute ());
		var legacy = new SubmissionDeliveryPlan (_plan.CandidateSha256, _plan.ReviewSha256, _plan.AuthorizationSha256,
			_plan.PackageSha256, _plan.AttachmentSha256, _plan.PackageFileName, _plan.AttachmentFileName, _plan.Sender, _plan.Recipient);
		Assert.Throws<InvalidDataException> (() => SubmissionDelivery.Read (_root, legacy));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((1, 1)));
		}

	[Test]
	public async Task ExpiredSendApprovalPreservesConfirmedUploadAndNeverSends ()
		{
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Execute ((step, _) => Task.FromResult (
			new SubmissionDeliveryAuthorization (SubmissionDelivery.ReviewPlanDigest (_plan),
				DateTimeOffset.UtcNow.AddMinutes (step == SubmissionDeliveryStep.Upload ? 2 : -2)))));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((1, 0)));
		Assert.That (SubmissionDelivery.ReadReview (_root, _plan)!.Delivery.State, Is.EqualTo (SubmissionDeliveryState.Uploaded));
		Assert.That ((await Execute ()).Delivery.State, Is.EqualTo (SubmissionDeliveryState.Submitted));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((1, 1)));
		}

	[TestCase ("complete")]
	[TestCase ("needs-correction")]
	[TestCase ("missing-reason")]
	[TestCase ("unknown-kind")]
	[TestCase ("missing-pin")]
	[TestCase ("header-injection")]
	public void InvalidOrConcealedOmissionsCannotBeDelivered (string change)
		{
		_plan = change switch
			{
			"complete" => _plan with { ReviewMode = SubmissionReviewMode.Complete },
			"needs-correction" => _plan with { VerificationStatus = SubmissionVerificationStatus.NeedsCorrection },
			"missing-reason" => _plan with { DocumentOmissions = null },
			"unknown-kind" => _plan with { AttachmentKind = (SubmissionReviewAttachmentKind)99 },
			"missing-pin" => _plan with { DeclarationsSha256 = null },
			_ => _plan with { GapSummary = "test\r\nBcc: other@example.test" }
			};
		Assert.ThrowsAsync<ArgumentException> (async () => await Execute ());
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((0, 0)));
		}

	[Test]
	public void AttachmentTamperingIsRejectedBeforeAuthorizationOrUpload ()
		{
		File.AppendAllText (_attachment, "changed");
		Assert.ThrowsAsync<InvalidDataException> (async () => await Execute ((_, _) => throw new AssertionException ("No approval before byte verification")));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((0, 0)));
		}

	private sealed class Transport : ISubmissionReviewDeliveryTransport
		{
		internal int Uploads, Sends;
		internal SubmissionDeliveryStep? Fail;
		internal SubmissionReviewDeliveryPlan? SentPlan;
		internal byte[]? SentBytes;
		public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken cancellationToken)
			{
			Uploads++;
			if (Fail == SubmissionDeliveryStep.Upload) throw new IOException ("Synthetic ambiguous upload");
			return Task.FromResult (Upload);
			}
		public async Task<SubmissionMailReceipt> SendReviewAsync (SubmissionReviewDeliveryPlan plan, SubmissionUploadReceipt upload,
			Stream attachment, string messageId, CancellationToken cancellationToken)
			{
			Sends++;
			SentPlan = plan;
			using var buffer = new MemoryStream ();
			await attachment.CopyToAsync (buffer, cancellationToken);
			SentBytes = buffer.ToArray ();
			if (Fail == SubmissionDeliveryStep.Send) throw new IOException ("Synthetic ambiguous mail");
			return Mail;
			}
		}
	}