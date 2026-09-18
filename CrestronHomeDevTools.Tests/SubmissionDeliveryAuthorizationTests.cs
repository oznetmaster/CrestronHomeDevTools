// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionDeliveryAuthorizationTests
	{
	private string _root = null!, _package = null!, _form = null!;
	private SubmissionDeliveryPlan _plan = null!;
	private Transport _transport = null!;
	private Clock _clock = null!;
	private static string Hash (byte[] bytes) => Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant ();
	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "authorized-delivery-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		_package = Path.Combine (_root, "example.pkg"); _form = Path.Combine (_root, "signed.pdf");
		File.WriteAllText (_package, "Synthetic package, no external delivery");
		File.WriteAllText (_form, "Synthetic form, no real signature");
		_plan = new (new ('a', 64), new ('b', 64), new ('c', 64), Hash (File.ReadAllBytes (_package)), Hash (File.ReadAllBytes (_form)),
			"example.pkg", "signed.pdf", "sender@example.test", "recipient@example.test");
		_transport = new (); _clock = new ();
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);
	private SubmissionDeliveryAuthorization Approval () => new (SubmissionDelivery.PlanDigest (_plan), _clock.Now.AddMinutes (1));
	private Task<SubmissionDeliveryReceipt> Execute (Func<SubmissionDeliveryStep, CancellationToken, Task<SubmissionDeliveryAuthorization>> check, CancellationToken token = default) =>
		SubmissionDelivery.ExecuteAuthorizedAsync (_root, _plan, _package, _form, _transport, check, _clock, token);
	[TestCase ("expired")]
	[TestCase ("exact-expiry")]
	[TestCase ("different-plan")]
	[TestCase ("missing")]
	public void InvalidApprovalCannotAttemptUpload (string failure)
		{
		var approval = failure switch
			{
			"expired" => Approval () with { ExpiresUtc = _clock.Now.AddTicks (-1) },
			"exact-expiry" => Approval () with { ExpiresUtc = _clock.Now },
			"different-plan" => Approval () with { PlanSha256 = new ('d', 64) },
			_ => null
			};
		Assert.ThrowsAsync<InvalidOperationException> (() => Execute ((_, _) => Task.FromResult (approval!)));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((0, 0)));
		Assert.That (SubmissionDelivery.Read (_root, _plan)!.State, Is.EqualTo (SubmissionDeliveryState.Prepared));
		}
	[Test]
	public void ExpiryDuringUploadPreservesReceiptWithoutSending ()
		{
		var approval = Approval ();
		_transport.AfterUpload = () => _clock.Now = approval.ExpiresUtc;
		var steps = new List<SubmissionDeliveryStep> ();
		Assert.ThrowsAsync<InvalidOperationException> (() => Execute ((step, _) => { steps.Add (step); return Task.FromResult (approval); }));
		Assert.That (steps, Is.EqualTo (new[] { SubmissionDeliveryStep.Upload, SubmissionDeliveryStep.Send }));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((1, 0)));
		var receipt = SubmissionDelivery.Read (_root, _plan)!;
		Assert.That (receipt.State, Is.EqualTo (SubmissionDeliveryState.Uploaded));
		Assert.That (receipt.Upload!.ProviderReceipt, Is.EqualTo ("synthetic-upload"));
		Assert.That (receipt.PendingStep, Is.Null);
		}
	[Test]
	public async Task TemporarilyUnavailableReviewResumesOnlyEmailWithSameValidApproval ()
		{
		var approval = Approval ();
		Assert.ThrowsAsync<IOException> (() => Execute ((step, _) => step == SubmissionDeliveryStep.Send
			? throw new IOException ("Synthetic unavailable evidence store") : Task.FromResult (approval)));
		Assert.That (SubmissionDelivery.Read (_root, _plan)!.State, Is.EqualTo (SubmissionDeliveryState.Uploaded));
		var steps = new List<SubmissionDeliveryStep> ();
		await Execute ((step, _) => { steps.Add (step); return Task.FromResult (approval); });
		Assert.That (steps, Is.EqualTo (new[] { SubmissionDeliveryStep.Send }));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((1, 1)));
		}
	[TestCase (SubmissionDeliveryStep.Upload)]
	[TestCase (SubmissionDeliveryStep.Send)]
	public void CancellationDuringRevalidationCannotStartThatStep (SubmissionDeliveryStep cancelAt)
		{
		using var cancel = new CancellationTokenSource ();
		Assert.CatchAsync<OperationCanceledException> (() => Execute ((step, _) =>
			{ if (step == cancelAt) cancel.Cancel (); return Task.FromResult (Approval ()); }, cancel.Token));
		Assert.That (_transport.Uploads, Is.EqualTo (cancelAt == SubmissionDeliveryStep.Upload ? 0 : 1));
		Assert.That (_transport.Sends, Is.Zero);
		Assert.That (SubmissionDelivery.Read (_root, _plan)!.PendingStep, Is.Null);
		}
	[Test]
	public async Task CompletedReceiptNeedsNeitherNewApprovalNorSourceFiles ()
		{
		await Execute ((_, _) => Task.FromResult (Approval ()));
		File.Delete (_package); File.Delete (_form);
		var receipt = await Execute ((_, _) => throw new InvalidOperationException ("Must not authorize a new request"));
		Assert.That (receipt.State, Is.EqualTo (SubmissionDeliveryState.Submitted));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((1, 1)));
		}
	[Test]
	public void UnknownUploadCannotBeBypassedWithFreshRevalidation ()
		{
		_transport.AfterUpload = () => throw new IOException ("Synthetic uncertain upload");
		Assert.ThrowsAsync<IOException> (() => Execute ((_, _) => Task.FromResult (Approval ())));
		Assert.That (SubmissionDelivery.Read (_root, _plan)!.State, Is.EqualTo (SubmissionDeliveryState.OutcomeUnknown));
		Assert.ThrowsAsync<InvalidOperationException> (() => Execute ((_, _) => throw new AssertionException ("Must not reauthorize an uncertain attempt")));
		Assert.That ((_transport.Uploads, _transport.Sends), Is.EqualTo ((1, 0)));
		}
	private sealed class Clock : TimeProvider
		{
		internal DateTimeOffset Now = new (2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow () => Now;
		}
	private sealed class Transport : ISubmissionDeliveryTransport
		{
		internal int Uploads, Sends;
		internal Action? AfterUpload;
		public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken token)
			{
			Uploads++; AfterUpload?.Invoke ();
			return Task.FromResult (new SubmissionUploadReceipt ("https://uploader.example.test/synthetic", "synthetic-upload"));
			}
		public Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload, Stream form, string messageId, CancellationToken token)
			{ Sends++; return Task.FromResult (new SubmissionMailReceipt ("synthetic-mail")); }
		}
	}