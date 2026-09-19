// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using MailKit.Net.Smtp;

using MimeKit;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionSmtpMailerTests
	{
	private string _root = null!;
	private SubmissionDeliveryPlan _plan = null!;
	private static readonly byte[] Form = "synthetic form, not a signed document"u8.ToArray ();
	private static readonly SubmissionUploadReceipt Upload = new ("https://upload.example.test/download?file=private", "confirmed upload");
	private static string Hash (byte[] bytes) => Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant ();
	private string MessageId => "<crestron-" + SubmissionDelivery.PlanDigest (_plan) + "@submission.local>";
	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "smtp-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		_plan = new (new ('a', 64), new ('b', 64), new ('c', 64), new ('d', 64), Hash (Form),
			"Example_Test_IP.pkg", "self-test.pdf", "sender@example.test", "recipient@example.test");
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);
	private SubmissionSmtpMailer Create (Session session, TimeSpan? timeout = null) => new ("smtp.example.test", 465,
		_plan.Sender, new NetworkCredential ("synthetic-user", "synthetic-password"), _root, timeout ?? TimeSpan.FromSeconds (5), () => session);

	[TestCase (false, "queued as synthetic-id")]
	[TestCase (true, "")]
	public async Task SendsExactFormAndRecipientAndRetainsAcceptanceEvenWhenCloseFails (bool disposeFails, string response)
		{
		var session = new Session { DisposeFails = disposeFails, Response = response };
		var result = await Create (session).SendAsync (_plan, Upload, new MemoryStream (Form), MessageId);
		Assert.That (session.Sends, Is.EqualTo (1));
		Assert.That (session.Disposed, Is.True);
		using var sent = MimeMessage.Load (new MemoryStream (session.SentBytes!));
		Assert.That (sent.From.Mailboxes.Single ().Address, Is.EqualTo (_plan.Sender));
		Assert.That (sent.To.Mailboxes.Single ().Address, Is.EqualTo (_plan.Recipient));
		Assert.That (sent.Cc.Concat (sent.Bcc), Is.Empty);
		Assert.That (sent.MessageId, Is.EqualTo (MessageId[1..^1]));
		Assert.That (sent.Subject, Is.EqualTo ("Driver Submission Package"));
		Assert.That (sent.TextBody, Does.Contain (Upload.DownloadUrl));
		var attachment = (MimePart)sent.Attachments.Single ();
		Assert.That (attachment.FileName, Is.EqualTo (_plan.SignedFormFileName));
		using var content = new MemoryStream ();
		attachment.Content!.DecodeTo (content);
		Assert.That (content.ToArray (), Is.EqualTo (Form));
		string attempt = Directory.GetDirectories (_root).Single ();
		Assert.That (File.Exists (Path.Combine (attempt, "accepted.json")), Is.True);
		Assert.That (File.Exists (Path.Combine (attempt, "failed.json")), Is.False);
		Assert.That (File.ReadAllText (Path.Combine (attempt, "message.eml")), Does.Not.Contain ("synthetic-password"));
		Assert.That (result.ProviderReceipt, Does.Contain ("\"Accepted\":true"));
		}

	[TestCase ("connection", false, 0)]
	[TestCase ("send", true, 1)]
	[TestCase ("timeout", true, 1)]
	public void FailuresDoNotResendAndPreserveUncertainOutcome (string mode, bool attempted, int sends)
		{
		var session = new Session { Failure = mode, DisposeFails = true };
		var error = Assert.ThrowsAsync<InvalidDataException> (() => Create (session, TimeSpan.FromSeconds (1)).SendAsync (_plan, Upload, new MemoryStream (Form), MessageId));
		Assert.That (error!.Message, Does.Not.Contain ("synthetic-password"));
		Assert.That (session.Sends, Is.EqualTo (sends));
		using var failure = JsonDocument.Parse (File.ReadAllText (Path.Combine (Directory.GetDirectories (_root).Single (), "failed.json")));
		Assert.That (failure.RootElement.GetProperty ("SendAttempted").GetBoolean (), Is.EqualTo (attempted));
		Assert.That (failure.RootElement.GetProperty ("Outcome").GetString (), Is.EqualTo (attempted ? "RequiresReconciliation" : "NotSent"));
		}

	[Test]
	public void ServerRejectionRetainsPrivateStatusAndRedactsThePassword ()
		{
		var session = new Session { Failure = "rejected" };
		var error = Assert.ThrowsAsync<InvalidDataException> (() => Create (session).SendAsync (_plan, Upload, new MemoryStream (Form), MessageId));
		Assert.That (session.Sends, Is.EqualTo (1));
		Assert.That (error!.Message, Does.Not.Contain ("Policy refused"));
		string saved = File.ReadAllText (Path.Combine (Directory.GetDirectories (_root).Single (), "failed.json"));
		Assert.That (saved, Does.Not.Contain ("synthetic-password"));
		using var failure = JsonDocument.Parse (saved);
		var rejection = failure.RootElement.GetProperty ("Rejection");
		Assert.That (rejection.GetProperty ("StatusCode").GetInt32 (), Is.EqualTo (553));
		Assert.That (rejection.GetProperty ("ErrorCode").GetString (), Is.EqualTo ("SenderNotAccepted"));
		Assert.That (rejection.GetProperty ("Reply").GetString (), Does.Contain ("[redacted]"));
		}

	[TestCase ("form")]
	[TestCase ("sender")]
	[TestCase ("message-id")]
	[TestCase ("link")]
	public void InvalidApprovedInputsNeverConnect (string mode)
		{
		var session = new Session ();
		var mailer = Create (session);
		var plan = mode == "sender" ? _plan with { Sender = "other@example.test" } : _plan;
		var upload = mode == "link" ? Upload with { DownloadUrl = "http://upload.example.test/private" } : Upload;
		Assert.ThrowsAsync<InvalidDataException> (() => mailer.SendAsync (plan, upload,
			new MemoryStream (mode == "form" ? "different"u8.ToArray () : Form), mode == "message-id" ? "<different@example.test>" : MessageId));
		Assert.That (session.Connects, Is.Zero);
		Assert.That (Directory.GetDirectories (_root), Is.Empty);
		}

	[Test]
	public void PlaintextPortIsNotSupported ()
		{
		Assert.Throws<ArgumentException> (() => new SubmissionSmtpMailer ("smtp.example.test", 25, _plan.Sender,
			new NetworkCredential ("test", "test"), _root, TimeSpan.FromSeconds (5)));
		}

	[TestCase (SubmissionReviewAttachmentKind.SignedSelfTest, "signed self-test form")]
	[TestCase (SubmissionReviewAttachmentKind.UnsignedSelfTest, "UNSIGNED self-test form")]
	[TestCase (SubmissionReviewAttachmentKind.DisclosureOnly, "disclosure report only")]
	public async Task ReviewMailDisclosesGapsAndActualDocumentStatus (SubmissionReviewAttachmentKind kind, string expected)
		{
		var plan = ReviewPlan (kind);
		var session = new Session ();
		string digest = SubmissionDelivery.ReviewPlanDigest (plan);
		await Create (session).SendReviewAsync (plan, Upload, new MemoryStream (Form), "<crestron-" + digest + "@submission.local>");
		using var sent = MimeMessage.Load (new MemoryStream (session.SentBytes!));
		var approved = SubmissionDelivery.ReviewCorrespondence (plan);
		Assert.That (sent.Subject, Is.EqualTo ("Driver Submission Package"));
		Assert.That (sent.TextBody!.Replace ("\r\n", "\n"), Is.EqualTo (
			(approved.Body + "\r\nConfirmed package download link:\r\n" + Upload.DownloadUrl + "\r\n").Replace ("\r\n", "\n")));
		Assert.That (sent.TextBody, Does.Contain ("Request for review with declared gaps"));
		Assert.That (sent.TextBody, Does.Contain (expected));
		Assert.That (sent.TextBody, Does.Contain (plan.GapSummary));
		Assert.That (sent.TextBody, Does.Contain (plan.DocumentOmissions));
		Assert.That (sent.TextBody, Does.Contain ("Only Crestron can decide"));
		Assert.That (sent.TextBody, Does.Not.Contain ("Our verification is complete"));
		Assert.That (sent.To.Mailboxes.Single ().Address, Is.EqualTo (plan.Recipient));
		Assert.That (sent.Cc.Concat (sent.Bcc), Is.Empty);
		var attachment = (MimePart)sent.Attachments.Single ();
		using var bytes = new MemoryStream ();
		attachment.Content!.DecodeTo (bytes);
		Assert.That (attachment.FileName, Is.EqualTo (plan.AttachmentFileName));
		Assert.That (bytes.ToArray (), Is.EqualTo (Form));
		}

	[Test]
	public async Task CompleteReviewMailLimitsItsClaimToInterpretedRequirements ()
		{
		var plan = ReviewPlan (SubmissionReviewAttachmentKind.SignedSelfTest) with
			{
			ReviewMode = SubmissionReviewMode.Complete, VerificationStatus = SubmissionVerificationStatus.CompleteAgainstInterpretedRequirements,
			DeclarationsSha256 = null, GapSummary = null, DocumentOmissions = null
			};
		var session = new Session ();
		await Create (session).SendReviewAsync (plan, Upload, new MemoryStream (Form),
			"<crestron-" + SubmissionDelivery.ReviewPlanDigest (plan) + "@submission.local>");
		using var sent = MimeMessage.Load (new MemoryStream (session.SentBytes!));
		Assert.That (sent.TextBody, Does.Contain ("complete against our interpretation of Crestron's published submission requirements"));
		Assert.That (sent.TextBody, Does.Contain ("Only Crestron can decide acceptance, publication or certification"));
		Assert.That (sent.TextBody, Does.Not.Contain ("Request for review with declared gaps"));
		}

	[TestCase ("attachment")]
	[TestCase ("disclosure")]
	public void ChangedReviewCannotConnectToMailServer (string change)
		{
		var plan = ReviewPlan (SubmissionReviewAttachmentKind.UnsignedSelfTest);
		string messageId = "<crestron-" + SubmissionDelivery.ReviewPlanDigest (plan) + "@submission.local>";
		var session = new Session ();
		if (change == "disclosure") plan = plan with { DocumentOmissions = "Different reviewed omission" };
		Assert.ThrowsAsync<InvalidDataException> (() => Create (session).SendReviewAsync (plan, Upload,
			new MemoryStream (change == "attachment" ? "different"u8.ToArray () : Form), messageId));
		Assert.That (session.Connects, Is.Zero);
		}

	private SubmissionReviewDeliveryPlan ReviewPlan (SubmissionReviewAttachmentKind kind) =>
		new (_plan.CandidateSha256, _plan.ReviewSha256, _plan.AuthorizationSha256, _plan.PackageSha256,
			_plan.SignedFormSha256, _plan.PackageFileName, "review-disclosures.pdf", _plan.Sender, _plan.Recipient,
			SubmissionReviewMode.DeclaredGaps, SubmissionVerificationStatus.GapsDeclared, kind, new ('e', 64),
			"Endurance was untested because equipment was unavailable; a recovery check failed.",
			"The document or signature omission is deliberately disclosed in this synthetic fixture.");

	[Test]
	public void DeliveryJournalPreservesUploadAndBlocksRetryAfterLostMailAcknowledgement ()
		{
		byte[] package = "synthetic package"u8.ToArray ();
		_plan = _plan with { PackageSha256 = Hash (package) };
		string packagePath = Path.Combine (_root, _plan.PackageFileName);
		string formPath = Path.Combine (_root, _plan.SignedFormFileName);
		File.WriteAllBytes (packagePath, package);
		File.WriteAllBytes (formPath, Form);
		var session = new Session { Failure = "send" };
		var transport = new Transport (Create (session));
		string journal = Path.Combine (_root, "journal");
		Directory.CreateDirectory (journal);
		Assert.ThrowsAsync<InvalidDataException> (() => SubmissionDelivery.ExecuteAsync (journal, _plan, packagePath, formPath, transport));
		var receipt = SubmissionDelivery.Read (journal, _plan)!;
		Assert.That (receipt.State, Is.EqualTo (SubmissionDeliveryState.OutcomeUnknown));
		Assert.That (receipt.Upload, Is.EqualTo (Upload));
		session.Failure = "";
		Assert.ThrowsAsync<InvalidOperationException> (() => SubmissionDelivery.ExecuteAsync (journal, _plan, packagePath, formPath, transport));
		Assert.That (session.Sends, Is.EqualTo (1));
		Assert.That (transport.Uploads, Is.EqualTo (1));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task ReviewJournalAndSmtpPreserveDispositionAndNeverReplayAnUncertainSend (bool loseAcknowledgement)
		{
		byte[] package = "synthetic package"u8.ToArray ();
		var plan = ReviewPlan (SubmissionReviewAttachmentKind.UnsignedSelfTest) with { PackageSha256 = Hash (package) };
		string packagePath = Path.Combine (_root, plan.PackageFileName), formPath = Path.Combine (_root, plan.AttachmentFileName);
		File.WriteAllBytes (packagePath, package);
		File.WriteAllBytes (formPath, Form);
		var session = new Session { Failure = loseAcknowledgement ? "send" : "" };
		var transport = new Transport (Create (session));
		string journal = Path.Combine (_root, "review-journal");
		Directory.CreateDirectory (journal);
		Task<SubmissionReviewDeliveryReceipt> Execute () => SubmissionDelivery.ExecuteReviewAuthorizedAsync (journal, plan, packagePath, formPath,
			transport, (_, _) => Task.FromResult (new SubmissionDeliveryAuthorization (SubmissionDelivery.ReviewPlanDigest (plan), DateTimeOffset.UtcNow.AddMinutes (2))));
		if (loseAcknowledgement)
			{
			Assert.ThrowsAsync<InvalidDataException> (async () => await Execute ());
			session.Failure = "";
			Assert.ThrowsAsync<InvalidOperationException> (async () => await Execute ());
			}
		else
			{
			await Execute ();
			await Execute ();
			}
		var receipt = SubmissionDelivery.ReadReview (journal, plan)!;
		Assert.That (receipt.VerificationStatus, Is.EqualTo (SubmissionVerificationStatus.GapsDeclared));
		Assert.That (receipt.AttachmentKind, Is.EqualTo (SubmissionReviewAttachmentKind.UnsignedSelfTest));
		Assert.That (receipt.DeclarationsSha256, Is.EqualTo (plan.DeclarationsSha256));
		Assert.That (receipt.Delivery.State, Is.EqualTo (loseAcknowledgement ? SubmissionDeliveryState.OutcomeUnknown : SubmissionDeliveryState.Submitted));
		Assert.That (receipt.Delivery.Upload, Is.EqualTo (Upload));
		Assert.That ((transport.Uploads, session.Sends), Is.EqualTo ((1, 1)));
		}

	private sealed class Transport (SubmissionSmtpMailer mailer) : ISubmissionDeliveryTransport, ISubmissionReviewDeliveryTransport
		{
		public int Uploads;
		public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken token)
			{
			Uploads++;
			return Task.FromResult (Upload);
			}
		public Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload,
			Stream form, string messageId, CancellationToken token) => mailer.SendAsync (plan, upload, form, messageId, token);
		public Task<SubmissionMailReceipt> SendReviewAsync (SubmissionReviewDeliveryPlan plan, SubmissionUploadReceipt upload,
			Stream form, string messageId, CancellationToken token) => mailer.SendReviewAsync (plan, upload, form, messageId, token);
		}

	private sealed class Session : ISubmissionSmtpSession
		{
		public int Connects, Sends;
		public bool Disposed, DisposeFails;
		public string Failure = "", Response = "queued as synthetic-id";
		public byte[]? SentBytes;
		public Task ConnectAsync (string host, int port, NetworkCredential credential, CancellationToken token)
			{
			Connects++;
			if (Failure == "connection")
				{
				throw new IOException ("synthetic-password");
				}
			return Task.CompletedTask;
			}
		public async Task<string> SendAsync (MimeMessage message, CancellationToken token)
			{
			Sends++;
			if (Failure == "rejected")
				{
				throw new SmtpCommandException (SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxNameNotAllowed, "Policy refused synthetic-password");
				}
			if (Failure == "send")
				{
				throw new IOException ("synthetic-password");
				}
			if (Failure == "timeout")
				{
				await Task.Delay (Timeout.Infinite, token);
				}
			using var saved = new MemoryStream ();
			await message.WriteToAsync (saved, token);
			SentBytes = saved.ToArray ();
			return Response;
			}
		public void Dispose ()
			{
			Disposed = true;
			if (DisposeFails)
				{
				throw new IOException ("synthetic cleanup failure");
				}
			}
		}
	}