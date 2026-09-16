// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json.Nodes;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionDeliveryTests
	{
	private string _root = null!;
	private string _package = null!;
	private string _form = null!;
	private SubmissionDeliveryPlan _plan = null!;
	private FakeTransport _transport = null!;
	private static string Hash (byte[] value) => Convert.ToHexString (SHA256.HashData (value)).ToLowerInvariant ();
	private static readonly SubmissionUploadReceipt Upload = new ("https://uploader.example.test/download?id=synthetic", "upload-receipt");
	private static readonly SubmissionMailReceipt Mail = new ("mail-receipt");

	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "delivery-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		_package = Path.Combine (_root, "Example_Platform_Test_IP.pkg");
		_form = Path.Combine (_root, "self-test.pdf");
		File.WriteAllText (_package, "Synthetic package; never sent to an external service");
		File.WriteAllText (_form, "Synthetic form; not a signature or attestation");
		_plan = new (new ('a', 64), new ('b', 64), new ('c', 64), Hash (File.ReadAllBytes (_package)), Hash (File.ReadAllBytes (_form)),
			Path.GetFileName (_package), Path.GetFileName (_form), "sender@example.test", "recipient@example.test");
		_transport = new ();
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);
	private Task<SubmissionDeliveryReceipt> Execute (CancellationToken token = default) =>
		SubmissionDelivery.ExecuteAsync (_root, _plan, _package, _form, _transport, token);

	[Test]
	public async Task CompletedDeliveryReturnsReceiptWithoutRepeatingOrNeedingSourceFiles ()
		{
		var first = await Execute ();
		File.Delete (_package);
		File.Delete (_form);
		var again = await Execute ();
		Assert.That (first.State, Is.EqualTo (SubmissionDeliveryState.Submitted));
		Assert.That (again, Is.EqualTo (first));
		Assert.That ((_transport.UploadCalls, _transport.SendCalls), Is.EqualTo ((1, 1)));
		Assert.That (first.Mail, Is.EqualTo (Mail));
		}

	[TestCase (SubmissionDeliveryStep.Upload)]
	[TestCase (SubmissionDeliveryStep.Send)]
	public async Task AmbiguousProviderFailureStopsAllAutomaticReplay (SubmissionDeliveryStep step)
		{
		_transport.Fail = step;
		Assert.ThrowsAsync<IOException> (async () => await Execute ());
		var receipt = SubmissionDelivery.Read (_root, _plan)!;
		Assert.That (receipt.State, Is.EqualTo (SubmissionDeliveryState.OutcomeUnknown));
		Assert.That (receipt.PendingStep, Is.EqualTo (step));
		_transport.Fail = null;
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Execute ());
		Assert.That (_transport.UploadCalls, Is.EqualTo (1));
		Assert.That (_transport.SendCalls, Is.EqualTo (step == SubmissionDeliveryStep.Send ? 1 : 0));
		SubmissionDelivery.Reconcile (_root, _plan, step, true, "Synthetic provider lookup confirms acceptance",
			step == SubmissionDeliveryStep.Upload ? Upload : null, step == SubmissionDeliveryStep.Send ? Mail : null);
		Assert.That ((await Execute ()).State, Is.EqualTo (SubmissionDeliveryState.Submitted));
		Assert.That ((_transport.UploadCalls, _transport.SendCalls), Is.EqualTo ((1, 1)));
		}

	[TestCase (SubmissionDeliveryStep.Upload)]
	[TestCase (SubmissionDeliveryStep.Send)]
	public async Task ConfirmedNonDeliveryAllowsOnlyTheOutstandingStep (SubmissionDeliveryStep step)
		{
		_transport.Fail = step;
		Assert.ThrowsAsync<IOException> (async () => await Execute ());
		Assert.Throws<InvalidOperationException> (() => SubmissionDelivery.Reconcile (_root, _plan,
			step == SubmissionDeliveryStep.Upload ? SubmissionDeliveryStep.Send : SubmissionDeliveryStep.Upload, false, "wrong step"));
		Assert.Throws<ArgumentException> (() => SubmissionDelivery.Reconcile (_root, _plan, step, false, ""));
		SubmissionDelivery.Reconcile (_root, _plan, step, false, "Provider lookup explicitly confirms no accepted request");
		_transport.Fail = null;
		var completed = await Execute ();
		Assert.That (completed.Reconciliations!.Single ().Performed, Is.False);
		Assert.That ((_transport.UploadCalls, _transport.SendCalls), Is.EqualTo (step == SubmissionDeliveryStep.Upload ? (2, 1) : (1, 2)));
		}

	[TestCase (SubmissionDeliveryStep.Upload)]
	[TestCase (SubmissionDeliveryStep.Send)]
	public void PersistedPendingIntentFromTerminatedProcessCannotReplay (SubmissionDeliveryStep step)
		{
		_transport.Fail = step;
		Assert.ThrowsAsync<IOException> (async () => await Execute ());
		EditReceipt (json => json["state"] = step == SubmissionDeliveryStep.Upload ? "UploadPending" : "SendPending");
		_transport.Fail = null;
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Execute ());
		Assert.That (_transport.UploadCalls, Is.EqualTo (1));
		}

	[Test]
	public async Task CancellationAfterConfirmedUploadResumesAtSendOnly ()
		{
		using var cancel = new CancellationTokenSource ();
		_transport.BeforeUpload = () => { cancel.Cancel (); return Task.CompletedTask; };
		Assert.ThrowsAsync<OperationCanceledException> (async () => await Execute (cancel.Token));
		Assert.That (SubmissionDelivery.Read (_root, _plan)!.State, Is.EqualTo (SubmissionDeliveryState.Uploaded));
		Assert.That ((await Execute ()).State, Is.EqualTo (SubmissionDeliveryState.Submitted));
		Assert.That ((_transport.UploadCalls, _transport.SendCalls), Is.EqualTo ((1, 1)));
		}

	[Test]
	public async Task ConcurrentDeliveryCannotEnterProviderWhileFirstOwnsJournal ()
		{
		var entered = new TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously);
		_transport.BeforeUpload = async () => { entered.SetResult (); await release.Task; };
		var first = Execute ();
		try
			{
			await entered.Task.WaitAsync (TimeSpan.FromSeconds (5));
			Assert.ThrowsAsync<IOException> (async () => await Execute ());
			Assert.That (_transport.UploadCalls, Is.EqualTo (1));
			}
		finally { release.TrySetResult (); await first; }
		}

	[Test]
	public async Task ModifiedAuthorizationCannotCreateSecondJournalForSameDelivery ()
		{
		await Execute ();
		_plan = _plan with { AuthorizationSha256 = new ('d', 64) };
		Assert.ThrowsAsync<InvalidDataException> (async () => await Execute ());
		Assert.That (_transport.SendCalls, Is.EqualTo (1));
		}

	[Test]
	public void AlteredFileFailsBeforeAnyExternalRequest ()
		{
		File.AppendAllText (_form, " changed");
		Assert.ThrowsAsync<InvalidDataException> (async () => await Execute ());
		Assert.That ((_transport.UploadCalls, _transport.SendCalls), Is.EqualTo ((0, 0)));
		Assert.That (SubmissionDelivery.Read (_root, _plan), Is.Null);
		}

	[Test]
	public async Task SourceFileChangesAfterVerificationDoNotChangeSentBytes ()
		{
		_transport.BeforeUpload = () => { File.WriteAllText (_form, "Changed externally"); return Task.CompletedTask; };
		await Execute ();
		Assert.That (Hash (_transport.SentForm!), Is.EqualTo (_plan.SignedFormSha256));
		}

	[Test]
	public void MissingProviderReceiptIsAnUnknownOutcome ()
		{
		_transport.UploadResult = new ("http://unsafe.example.test/", "");
		Assert.ThrowsAsync<InvalidDataException> (async () => await Execute ());
		Assert.That (SubmissionDelivery.Read (_root, _plan)!.State, Is.EqualTo (SubmissionDeliveryState.OutcomeUnknown));
		Assert.That (_transport.SendCalls, Is.Zero);
		}

	[Test]
	public async Task CorruptSubmittedJournalCannotClaimSuccess ()
		{
		await Execute ();
		EditReceipt (json => json["mail"] = null);
		Assert.ThrowsAsync<InvalidDataException> (async () => await Execute ());
		Assert.That (_transport.SendCalls, Is.EqualTo (1));
		}

	private void EditReceipt (Action<JsonNode> change)
		{
		var path = Directory.GetFiles (_root, "*.json").Single ();
		var json = JsonNode.Parse (File.ReadAllText (path))!;
		change (json);
		File.WriteAllText (path, json.ToJsonString ());
		}

	private sealed class FakeTransport : ISubmissionDeliveryTransport
		{
		internal int UploadCalls, SendCalls;
		internal SubmissionDeliveryStep? Fail;
		internal SubmissionUploadReceipt UploadResult = Upload;
		internal Func<Task>? BeforeUpload;
		internal byte[]? SentForm;
		public async Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken cancellationToken)
			{
			UploadCalls++;
			if (BeforeUpload != null) await BeforeUpload ();
			if (Fail == SubmissionDeliveryStep.Upload) throw new IOException ("Synthetic ambiguous upload");
			return UploadResult;
			}
		public async Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload, Stream signedForm, string messageId, CancellationToken cancellationToken)
			{
			SendCalls++;
			using var data = new MemoryStream ();
			await signedForm.CopyToAsync (data, cancellationToken);
			SentForm = data.ToArray ();
			if (Fail == SubmissionDeliveryStep.Send) throw new IOException ("Synthetic ambiguous send");
			return Mail;
			}
		}
	}